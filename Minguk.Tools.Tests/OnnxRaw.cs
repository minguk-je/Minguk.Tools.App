using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Inference;

namespace Minguk.Tools.Tests;

/// <summary>
/// 모델이 내놓는 숫자를 <b>날것으로</b> 본다. 해석기·문턱·좌표 되돌리기를 다 건너뛴다.
/// <c>--onnx-raw --model=경로.onnx --image=경로.png [--size=640x640] [--fit=늘리기|비율] [--cpu]</c>
/// </summary>
/// <remarks>
/// <b>왜 필요했나</b> - 학습한 D-FINE 이 파이썬에서는 0.9 로 찾는데 우리 앱에서는 192개 중 0개였다(실측).
/// 사이에 낀 것이 넷이다: 넣는 방식(레터박스냐), 채널 순서, 실행 공급자(DirectML), 해석기.
/// 점수 다섯 개만 찍어 보면 어디가 어긋났는지 한 번에 갈린다 - <c>--cpu</c> 와 견주면 공급자 탓인지도 나온다.
/// </remarks>
internal static class OnnxRaw
{
    public static int Run(string[] args)
    {
        var modelPath = Program.ArgValue(args, "--model=");
        var imagePath = Program.ArgValue(args, "--image=");

        if (modelPath is null || !File.Exists(modelPath) || imagePath is null || !File.Exists(imagePath))
        {
            Console.WriteLine("--model=<경로.onnx> --image=<경로.png> 가 있어야 한다.");
            return 1;
        }

        var size = (Program.ArgValue(args, "--size=") ?? "640x640").Split('x');
        var width = int.Parse(size[0], CultureInfo.InvariantCulture);
        var height = int.Parse(size.Length > 1 ? size[1] : size[0], CultureInfo.InvariantCulture);
        var letterbox = (Program.ArgValue(args, "--fit=") ?? "늘리기") is "비율" or "letterbox" or "레터박스";
        var useGpu = !args.Contains("--cpu");

        using var engine = new OnnxDmlEngine(modelPath, 0, useGpu);

        var spec = new TensorSpec { Width = width, Height = height, Letterbox = letterbox };
        var tensor = BuildTensor(imagePath, spec);

        Console.WriteLine($"모델 {Path.GetFileName(modelPath)} · {(useGpu ? "DirectML" : "CPU")} · 입력 {width}x{height} {(letterbox ? "비율 지켜 여백" : "늘려 맞춤")}");
        Console.WriteLine($"텐서: 최소 {tensor.Min():0.000} 최대 {tensor.Max():0.000} 평균 {tensor.Average():0.000}");
        Console.WriteLine();

        long[]? sizes = engine.InputNames.Count == 2 ? [width, height] : null;

        using var outputs = engine.Run(tensor, spec.Shape, sizes);

        for (var i = 0; i < engine.OutputNames.Count; i++)
        {
            var value = outputs[i];
            var info = value.GetTensorTypeAndShape();
            var head = info.ElementDataType == Microsoft.ML.OnnxRuntime.Tensors.TensorElementType.Int64
                ? string.Join(", ", value.GetTensorDataAsSpan<long>()[..Math.Min(8, (int)info.ElementCount)].ToArray())
                : string.Join(", ", value.GetTensorDataAsSpan<float>()[..Math.Min(8, (int)info.ElementCount)].ToArray().Select(v => v.ToString("0.###")));

            Console.WriteLine($"{engine.OutputNames[i],-8} [{string.Join(",", info.Shape)}] {info.ElementDataType}");
            Console.WriteLine($"         앞 8개: {head}");
        }

        return 0;
    }

    /// <summary>그림을 명세대로 텐서에 담는다. 최근접 표본이면 충분하다 - 여기서 보는 것은 값의 자릿수다.</summary>
    internal static float[] BuildTensor(string imagePath, TensorSpec spec)
    {
        var frame = BitmapFrame.Create(new Uri(imagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var sourceWidth = converted.PixelWidth;
        var sourceHeight = converted.PixelHeight;
        var stride = sourceWidth * 4;
        var pixels = new byte[stride * sourceHeight];

        converted.CopyPixels(pixels, stride, 0);

        var scale = Math.Min(spec.Width / (double)sourceWidth, spec.Height / (double)sourceHeight);
        var padX = (spec.Width - (sourceWidth * scale)) / 2;
        var padY = (spec.Height - (sourceHeight * scale)) / 2;

        var tensor = new float[spec.ElementCount];
        var plane = spec.Width * spec.Height;

        for (var y = 0; y < spec.Height; y++)
        {
            for (var x = 0; x < spec.Width; x++)
            {
                var sourceX = spec.Letterbox ? (int)((x + 0.5 - padX) / scale) : (int)((x + 0.5) * sourceWidth / spec.Width);
                var sourceY = spec.Letterbox ? (int)((y + 0.5 - padY) / scale) : (int)((y + 0.5) * sourceHeight / spec.Height);

                float r = spec.PadColor.X, g = spec.PadColor.Y, b = spec.PadColor.Z;

                if (sourceX >= 0 && sourceY >= 0 && sourceX < sourceWidth && sourceY < sourceHeight)
                {
                    var at = ((sourceY * sourceWidth) + sourceX) * 4;
                    (r, g, b) = (pixels[at + 2] / 255f, pixels[at + 1] / 255f, pixels[at] / 255f);
                }

                var index = (y * spec.Width) + x;

                tensor[index] = r;
                tensor[plane + index] = g;
                tensor[(plane * 2) + index] = b;
            }
        }

        return tensor;
    }
}
