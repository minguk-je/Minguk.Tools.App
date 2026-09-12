using System;
using System.Globalization;
using System.IO;
using System.Linq;

using Minguk.Tools.Inference;

namespace Minguk.Tools.Tests;

/// <summary>
/// GPU(DirectML)와 CPU 가 <b>같은 답</b>을 내는지 본다.
/// <c>--onnx-agree --model=경로.onnx --image=경로.png [--size=640x640] [--fit=늘리기|비율]</c>
/// </summary>
/// <remarks>
/// <b>왜 이것이 있나</b> - 실행 공급자는 틀린 답을 <b>조용히</b> 낸다. 터지지도 느려지지도 않는다.
/// DirectML 이 <c>MatMul(행렬, 1차원 벡터)</c> 을 그 벡터를 무시한 채 행 합계로 계산하는 바람에
/// 잘 배운 D-FINE 이 192개 중 0개를 찾았다(점수 0.9 → 0.06). 헛것도 0개라 "덜 배웠나" 로 보였다(실측).
///
/// CPU 쪽 답을 잣대로 삼는다 - 느리지만 참조 구현이다. 둘이 어긋나면 모델을 탓하기 전에 여기부터 본다.
/// 고치는 법은 <c>도구/onnx-DirectML-고치기.py</c> 에 있다.
/// </remarks>
internal static class OnnxAgree
{
    /// <summary>이 정도 넘게 벌어지면 모델 탓이 아니라 실행 공급자 탓이다.</summary>
    private const double Tolerance = 0.02;

    /// <summary>이 점수 아래의 줄은 쓰레기다. 사각형을 견주지 않는다.</summary>
    private const float MeaningfulScore = 0.25f;

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

        var spec = new TensorSpec { Width = width, Height = height, Letterbox = letterbox };
        var tensor = OnnxRaw.BuildTensor(imagePath, spec);

        var (agrees, worst, detail) = Check(modelPath, tensor, spec);

        Console.WriteLine($"모델 {Path.GetFileName(modelPath)} · 입력 {width}x{height}");
        Console.WriteLine(detail);
        Console.WriteLine();
        Console.WriteLine(agrees
            ? $"== GPU 와 CPU 가 같은 답을 낸다 (가장 큰 차이 {worst:P1}) =="
            : $"== 어긋난다 (가장 큰 차이 {worst:P1}) - 도구/onnx-DirectML-고치기.py 를 돌려 보라 ==");

        return agrees ? 0 : 1;
    }

    /// <summary>
    /// 같은 텐서를 GPU 와 CPU 로 돌려 출력을 견준다. 상대 차이가 <see cref="Tolerance"/> 안이면 같다고 본다.
    /// </summary>
    /// <remarks>
    /// <b>사각형은 자신 있는 것만 견준다.</b> 검출 모델은 질의 300개를 늘 돌려주는데 뒤쪽은 점수가 0.03 쯤으로
    /// 고만고만해, 정렬이 조금만 달라도 사각형 줄이 통째로 밀린다. 그것까지 세면 멀쩡한 모델이 10% 어긋난 것으로
    /// 보인다(실측). 잣대는 "찾은 것이 같은가" 이지 "쓰레기까지 같은 순서인가" 가 아니다.
    /// </remarks>
    public static (bool Agrees, double Worst, string Detail) Check(string modelPath, float[] tensor, TensorSpec spec)
    {
        using var gpu = new OnnxDmlEngine(modelPath, 0, useGpu: true);
        using var cpu = new OnnxDmlEngine(modelPath, 0, useGpu: false);

        long[]? sizes = gpu.InputNames.Count == 2 ? [spec.Width, spec.Height] : null;

        using var gpuOut = gpu.Run(tensor, spec.Shape, sizes);
        using var cpuOut = cpu.Run(tensor, spec.Shape, sizes);

        // 어느 줄이 쓸모 있는지는 점수가 안다. 점수 출력이 없는 모델이면 전부 견준다.
        var scoreAt = gpu.OutputNames.ToList().IndexOf("scores");
        var confident = scoreAt >= 0 ? cpuOut[scoreAt].GetTensorDataAsSpan<float>().ToArray() : null;

        var worst = 0.0;
        var lines = new System.Collections.Generic.List<string>();

        for (var i = 0; i < gpu.OutputNames.Count; i++)
        {
            var info = gpuOut[i].GetTensorTypeAndShape();

            if (info.ElementDataType != Microsoft.ML.OnnxRuntime.Tensors.TensorElementType.Float) continue;

            var a = gpuOut[i].GetTensorDataAsSpan<float>();
            var b = cpuOut[i].GetTensorDataAsSpan<float>();
            var length = Math.Min(a.Length, b.Length);

            // 한 줄에 몇 개가 들었나(사각형이면 4). 점수 줄과 짝을 맞추는 데 쓴다.
            var stride = confident is null || confident.Length == 0 ? 1 : Math.Max(1, length / confident.Length);

            // 상대 차이로 본다. 사각형은 640 단위, 점수는 0~1 단위라 절대값으로는 견줄 수 없다.
            var scale = 1e-6f;
            var difference = 0f;
            var counted = 0;

            for (var k = 0; k < length; k++)
            {
                if (confident is { } scores && stride > 1 && scores[k / stride] < MeaningfulScore) continue;

                scale = Math.Max(scale, Math.Abs(b[k]));
                difference = Math.Max(difference, Math.Abs(a[k] - b[k]));
                counted++;
            }

            var relative = difference / scale;

            worst = Math.Max(worst, relative);
            lines.Add($"  {gpu.OutputNames[i],-8} 차이 {relative:P2}  GPU {a[0]:0.###} ↔ CPU {b[0]:0.###}" +
                      (stride > 1 ? $"  ({counted / stride}줄만 견줌)" : ""));
        }

        return (worst <= Tolerance, worst, string.Join(Environment.NewLine, lines));
    }
}
