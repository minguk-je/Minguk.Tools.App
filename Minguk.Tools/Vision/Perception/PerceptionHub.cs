using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Capture;
using Minguk.Tools.Vision.Inference;

namespace Minguk.Tools.Vision.Perception;

/// <summary>
/// <see cref="IPerceptionHub"/> 의 구현. 스냅샷은 참조 바꿔치기, 프레임은 잠금 아래 복사.
/// </summary>
public sealed class PerceptionHub : IPerceptionHub
{
    private readonly object _frameGate = new();
    private byte[]? _frame;
    private int _frameWidth;
    private int _frameHeight;

    private volatile DetectionSnapshot? _latest;
    private volatile CaptureTarget? _target;
    private volatile bool _capturing;
    private volatile bool _detecting;
    private volatile bool _wantsFrames;

    public bool IsCapturing => _capturing;

    public bool IsDetecting => _detecting;

    public CaptureTarget? Target => _target;

    public DetectionSnapshot? Latest => _latest;

    public bool WantsFrames
    {
        get => _wantsFrames;
        set => _wantsFrames = value;
    }

    public void PublishState(bool capturing, bool detecting, CaptureTarget? target)
    {
        _capturing = capturing;
        _detecting = detecting;
        _target = target;

        // 눈을 감으면 옛 결과는 버린다. 몇 초 전 몹을 지금 것으로 주면 안 된다.
        if (!capturing) _latest = null;
    }

    public void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0)
        => _latest = new DetectionSnapshot(found, names, frameWidth, frameHeight, _target, Environment.TickCount64) { FrameTicks = frameTicks };

    public void PublishFrame(byte[] bgra, int width, int height)
    {
        var needed = width * 4 * height;

        lock (_frameGate)
        {
            if (_frame is null || _frame.Length < needed) _frame = new byte[needed];

            Buffer.BlockCopy(bgra, 0, _frame, 0, needed);
            _frameWidth = width;
            _frameHeight = height;
        }
    }

    public bool TryCropFrame(Rect ratio, out BitmapSource? crop)
    {
        crop = null;

        lock (_frameGate)
        {
            if (_frame is null || _frameWidth == 0) return false;

            var width = _frameWidth;
            var height = _frameHeight;
            var stride = width * 4;

            var left = Math.Clamp((int)Math.Floor(ratio.X * width), 0, width - 1);
            var top = Math.Clamp((int)Math.Floor(ratio.Y * height), 0, height - 1);
            var right = Math.Clamp((int)Math.Ceiling(ratio.Right * width), left + 1, width);
            var bottom = Math.Clamp((int)Math.Ceiling(ratio.Bottom * height), top + 1, height);

            var w = right - left;
            var h = bottom - top;
            var pixels = new byte[w * 4 * h];

            for (var y = 0; y < h; y++)
                Buffer.BlockCopy(_frame, ((top + y) * stride) + (left * 4), pixels, y * w * 4, w * 4);

            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            bitmap.Freeze();
            crop = bitmap;

            return true;
        }
    }
}
