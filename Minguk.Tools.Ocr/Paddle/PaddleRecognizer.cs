using System;

using Minguk.Tools.Inference;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>
/// 글자 한 줄 읽기(korean PP-OCRv5 mobile rec). 높이 48, 폭은 320 이나 640 두 계단.
/// </summary>
/// <remarks>
/// 폭을 두 계단으로만 두는 것은 DirectML 커널을 다시 잡지 않게 하려는 것이다(<see cref="PaddleDetector"/> 와 같은 이유).
/// 비율을 지켜 높이 48 로 맞추고 모자란 오른쪽은 0 으로 채운다 - PaddleOCR <c>resize_norm_img</c> 와 같다.
/// 640 보다 긴 줄은 가로로 눌러 넣는다(글자가 좁아진다). 자리 하나에 그만큼 긴 한 줄은 드물다.
/// 정규화는 (x/255 − 0.5)/0.5, 채널은 BGR 그대로.
/// </remarks>
internal sealed class PaddleRecognizer : IDisposable
{
    public const int Height = 48;

    private static readonly int[] Widths = [320, 640];

    private readonly OnnxDmlEngine _engine;
    private readonly CtcDecoder _decoder;

    public PaddleRecognizer(string modelPath, string dictionaryPath, bool useGpu)
    {
        _decoder = CtcDecoder.Load(dictionaryPath);
        _engine = new OnnxDmlEngine(modelPath, useGpu: useGpu);
    }

    public CtcResult Read(BgraImage line)
    {
        var fitted = (int)Math.Ceiling(Height * (double)line.Width / line.Height);
        var slot = fitted <= Widths[0] ? Widths[0] : Widths[1];
        var width = Math.Clamp(fitted, 1, slot);
        var resized = line.Resize(width, Height);
        var plane = Height * slot;
        var tensor = new float[3 * plane];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = ((y * width) + x) * 4;
                var target = (y * slot) + x;

                for (var c = 0; c < 3; c++)
                    tensor[(c * plane) + target] = ((resized.Pixels[source + c] / 255f) - 0.5f) / 0.5f;
            }
        }

        using var outputs = _engine.Run(tensor, [1, 3, Height, slot]);

        var output = outputs[0];
        var shape = output.GetTensorTypeAndShape().Shape;   // [1, T, 11947]

        return _decoder.Decode(output.GetTensorDataAsSpan<float>(), (int)shape[1], (int)shape[2]);
    }

    public void Dispose() => _engine.Dispose();
}
