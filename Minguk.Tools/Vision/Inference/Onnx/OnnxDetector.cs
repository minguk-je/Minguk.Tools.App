using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Vision.Inference.Onnx;

/// <summary>
/// ONNX Runtime + DirectML 로 도는 검출기. 같은 카드(GTX 1060)에서 지금 것이 660ms, 이쪽이 6~37ms 였다(실측).
/// </summary>
/// <remarks>
/// <b>지금은 그림 파일을 받는다</b> - 화면 쪽 경로가 그렇다(프레임 → PNG → 읽기). 제 모습은 GPU 텍스처를
/// <see cref="FramePreprocessor"/> 로 바로 텐서로 만드는 것이고, 설계 3단계에서 그 길을 낸다. 그때도 이 클래스는
/// 그대로 두고 텐서를 받는 입구만 늘린다.
///
/// <b>전처리는 여기서 손으로 한다</b> - 비율을 지켜 줄이고(레터박스), 남는 자리를 회색으로 채우고, 0~1 로 만든다.
/// 셰이더가 하는 것과 같은 계산이라 <see cref="LetterboxMap"/> 한 군데를 같이 본다.
/// </remarks>
public sealed class OnnxDetector : IDetector, ITensorDetector
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static readonly IDetectionDecoder[] Decoders = [new DetrDecoder()];

    private readonly OnnxDmlEngine _engine;
    private readonly IDetectionDecoder _decoder;
    private readonly TensorSpec _spec;
    private readonly float[] _tensor;

    private bool _disposed;

    public OnnxDetector(string modelPath, DetectorManifest manifest)
    {
        _engine = new OnnxDmlEngine(modelPath);
        Manifest = manifest;
        ModelPath = modelPath;

        // 모델이 크기를 박아 두었으면 그것이 답이다. 동적 차원(RT-DETR·D-FINE)이면 쪽지의 크기를 쓴다.
        _spec = _engine.TryDeriveInputSpec()
                ?? new TensorSpec { Width = manifest.InputWidth, Height = manifest.InputHeight };

        _decoder = Decoders.FirstOrDefault(d => d.CanDecode(_engine.OutputNames))
                   ?? throw new NotSupportedException(
                       $"이 모델의 출력을 풀 줄 모른다: {string.Join(", ", _engine.OutputNames)}. " +
                       "지금은 DETR 계열(logits · pred_boxes)만 안다.");

        _tensor = new float[_spec.ElementCount];

        Logger.Info($"ONNX 검출기: {Path.GetFileName(modelPath)} · 입력 {_spec.Width}x{_spec.Height} · 해석 {_decoder.Name}");
    }

    public string ModelPath { get; }

    public DetectorManifest Manifest { get; }

    /// <summary>이 모델이 받고 싶어 하는 텐서 모양. 캡처 쪽이 이 명세로 셰이더를 돌린다.</summary>
    public TensorSpec InputSpec => _spec;

    /// <summary>마지막 추론에 든 시간(ms). 전처리·해석은 안 든다.</summary>
    public double LastInferenceMs => _engine.LastInferenceMs;

    public IReadOnlyList<Detection> Detect(string imagePath, LabelClasses classes, float minimumScore = 0.5f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var (pixels, width, height) = ReadBgra(imagePath);
        var map = LetterboxMap.For(width, height, _spec.Width, _spec.Height, _spec.Letterbox);

        Fill(pixels, width, height, map);

        return RunAndDecode(_tensor, map, classes, minimumScore);
    }

    /// <summary>
    /// 캡처가 셰이더로 만들어 둔 텐서로 바로 찾는다. 그림 파일도, CPU 리드백도 안 거친다.
    /// </summary>
    public IReadOnlyList<Detection> Detect(float[] tensor, LetterboxMap map, LabelClasses classes, float minimumScore = 0.5f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (tensor.Length != _spec.ElementCount)
            throw new ArgumentException($"텐서 크기가 다르다: {tensor.Length} ≠ {_spec.ElementCount} ({_spec.Width}x{_spec.Height})", nameof(tensor));

        return RunAndDecode(tensor, map, classes, minimumScore);
    }

    private IReadOnlyList<Detection> RunAndDecode(float[] tensor, LetterboxMap map, LabelClasses classes, float minimumScore)
    {
        using var outputs = _engine.Run(tensor, _spec.Shape);

        var values = new Dictionary<string, (float[] Values, long[] Shape)>();

        for (var i = 0; i < _engine.OutputNames.Count; i++)
        {
            var output = outputs[i];
            values[_engine.OutputNames[i]] = (output.GetTensorDataAsSpan<float>().ToArray(), output.GetTensorTypeAndShape().Shape);
        }

        return _decoder.Decode(values, map, classes, minimumScore);
    }

    /// <summary>그림을 BGRA 한 줄 배열로 읽는다. 화면 캡처와 같은 채널 순서라 뒤에서 헷갈릴 일이 없다.</summary>
    private static (byte[] Pixels, int Width, int Height) ReadBgra(string imagePath)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException($"그림이 없다: {imagePath}", imagePath);

        var frame = BitmapFrame.Create(new Uri(imagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        converted.CopyPixels(pixels, stride, 0);

        return (pixels, width, height);
    }

    /// <summary>
    /// 원본을 배율만큼 줄여 입력 텐서에 채운다. 남는 자리는 여백 색으로.
    /// </summary>
    /// <remarks>
    /// 이중선형으로 섞는다. 가장 가까운 픽셀만 집으면 60px 짜리 봇이 640 칸으로 줄 때 가장자리가 튀어
    /// 점수가 눈에 띄게 떨어진다(작은 물체일수록 심하다).
    /// </remarks>
    private void Fill(byte[] pixels, int sourceWidth, int sourceHeight, LetterboxMap map)
    {
        var plane = _spec.Width * _spec.Height;
        var nchw = _spec.Layout == TensorLayout.Nchw;
        var scale = map.IsStretched ? 0 : map.Scale;

        for (var y = 0; y < _spec.Height; y++)
        {
            for (var x = 0; x < _spec.Width; x++)
            {
                double sourceX, sourceY;

                if (map.IsStretched)
                {
                    sourceX = (x + 0.5) * sourceWidth / _spec.Width;
                    sourceY = (y + 0.5) * sourceHeight / _spec.Height;
                }
                else
                {
                    sourceX = (x + 0.5 - map.PadX) / scale;
                    sourceY = (y + 0.5 - map.PadY) / scale;
                }

                float r, g, b;

                if (sourceX < 0 || sourceY < 0 || sourceX >= sourceWidth || sourceY >= sourceHeight)
                {
                    r = _spec.PadColor.X;
                    g = _spec.PadColor.Y;
                    b = _spec.PadColor.Z;
                }
                else
                {
                    (r, g, b) = Sample(pixels, sourceWidth, sourceHeight, sourceX - 0.5, sourceY - 0.5);
                }

                r = (r - _spec.Mean.X) / _spec.Std.X;
                g = (g - _spec.Mean.Y) / _spec.Std.Y;
                b = (b - _spec.Mean.Z) / _spec.Std.Z;

                var index = (y * _spec.Width) + x;

                if (nchw)
                {
                    _tensor[index] = r;
                    _tensor[plane + index] = g;
                    _tensor[(plane * 2) + index] = b;
                }
                else
                {
                    _tensor[(index * 3) + 0] = r;
                    _tensor[(index * 3) + 1] = g;
                    _tensor[(index * 3) + 2] = b;
                }
            }
        }
    }

    /// <summary>이중선형 표본. 0~1 로 돌려준다.</summary>
    private static (float R, float G, float B) Sample(byte[] pixels, int width, int height, double x, double y)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = (float)(x - x0);
        var fy = (float)(y - y0);

        var x1 = Math.Clamp(x0 + 1, 0, width - 1);
        var y1 = Math.Clamp(y0 + 1, 0, height - 1);

        x0 = Math.Clamp(x0, 0, width - 1);
        y0 = Math.Clamp(y0, 0, height - 1);

        var a = At(pixels, width, x0, y0);
        var b = At(pixels, width, x1, y0);
        var c = At(pixels, width, x0, y1);
        var d = At(pixels, width, x1, y1);

        return (Mix(a.R, b.R, c.R, d.R, fx, fy), Mix(a.G, b.G, c.G, d.G, fx, fy), Mix(a.B, b.B, c.B, d.B, fx, fy));
    }

    private static (float R, float G, float B) At(byte[] pixels, int width, int x, int y)
    {
        var at = ((y * width) + x) * 4;

        // BGRA 순서다.
        return (pixels[at + 2] / 255f, pixels[at + 1] / 255f, pixels[at] / 255f);
    }

    private static float Mix(float a, float b, float c, float d, float fx, float fy)
        => ((a * (1 - fx)) + (b * fx)) * (1 - fy) + (((c * (1 - fx)) + (d * fx)) * fy);

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _engine.Dispose();
    }
}
