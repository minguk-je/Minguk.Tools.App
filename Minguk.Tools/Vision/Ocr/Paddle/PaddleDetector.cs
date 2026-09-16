using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Inference;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>
/// 글자 줄 찾기(PP-OCRv5 mobile det). 조각을 고정 칸에 얹어 넣고, 찾은 사각형을 조각 픽셀 좌표로 돌려준다.
/// </summary>
/// <remarks>
/// <b>칸이 늘 같다</b>(<see cref="Side"/>) - DirectML 은 입력 모양이 바뀔 때마다 커널을 다시 잡아 한 번에 수백 ms 가 튄다.
/// 자리마다 크기가 달라도 모양을 하나로 묶는다.
///
/// <b>둘레에 여백</b>(<see cref="Margin"/>) - 글자가 칸 끝에 닿으면 확률맵이 끊긴다.
///
/// <b>키우기 한도</b>(<see cref="MaxUpscale"/>) - 작은 자리를 칸 가득 키우면 글자가 학습 때보다 훨씬 커진다. 값은 벤치(--ocr-bench)로 본다.
///
/// <b>채널은 BGR</b> - PaddleOCR 은 OpenCV(BGR) 그대로 정규화한다. Bgra32 순서를 그대로 옮기면 된다.
/// </remarks>
internal sealed class PaddleDetector : IDisposable
{
    public const int Side = 640;
    public const int Margin = 16;
    public const double MaxUpscale = 4;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    private readonly OnnxDmlEngine _engine;
    private readonly float[] _tensor = new float[3 * Side * Side];

    public PaddleDetector(string modelPath, bool useGpu) => _engine = new OnnxDmlEngine(modelPath, useGpu: useGpu);

    public IReadOnlyList<DbBox> Detect(BgraImage image)
    {
        var room = Side - (2 * Margin);
        var scale = Math.Min(Math.Min((double)room / image.Width, (double)room / image.Height), MaxUpscale);
        var width = Math.Clamp((int)Math.Round(image.Width * scale), 1, room);
        var height = Math.Clamp((int)Math.Round(image.Height * scale), 1, room);
        var resized = image.Resize(width, height);
        var plane = Side * Side;

        // 칸의 나머지는 0 - 정규화 뒤 0 은 평균 색이다.
        Array.Clear(_tensor);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = ((y * width) + x) * 4;
                var target = ((y + Margin) * Side) + x + Margin;

                for (var c = 0; c < 3; c++)
                    _tensor[(c * plane) + target] = ((resized.Pixels[source + c] / 255f) - Mean[c]) / Std[c];
            }
        }

        using var outputs = _engine.Run(_tensor, [1, 3, Side, Side]);

        var output = outputs[0];
        var shape = output.GetTensorTypeAndShape().Shape;   // [1, 1, H, W]
        var mapHeight = (int)shape[2];
        var mapWidth = (int)shape[3];
        var toSide = (double)Side / mapWidth;

        return [.. DbPostProcess.Extract(output.GetTensorDataAsSpan<float>(), mapWidth, mapHeight)
            .Select(box => new DbBox(
                Math.Clamp(((box.Left * toSide) - Margin) / scale, 0, image.Width),
                Math.Clamp(((box.Top * toSide) - Margin) / scale, 0, image.Height),
                Math.Clamp(((box.Right * toSide) - Margin) / scale, 0, image.Width),
                Math.Clamp(((box.Bottom * toSide) - Margin) / scale, 0, image.Height),
                box.Score))
            .Where(box => box.Width >= 1 && box.Height >= 1)];
    }

    public void Dispose() => _engine.Dispose();
}
