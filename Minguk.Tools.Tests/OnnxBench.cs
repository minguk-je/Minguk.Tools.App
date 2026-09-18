using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Minguk.Tools.Inference;

namespace Minguk.Tools.Tests;

/// <summary>
/// ONNX 검출기가 이 PC 에서 한 장에 몇 ms 인지만 잰다(설계 0단계). <c>--onnx-bench --model=경로 [--runs=30]</c>.
/// </summary>
/// <remarks>
/// <b>왜 속도만</b> - 지금 쓰는 AutoFormerV2 가 4070 랩톱에서 330ms 다. 카드를 바꿔도 2배밖에 안 줄었으니
/// 모델·실행 방식을 갈아야 하는데, 그 길이 정말 빠른지부터 확인해야 나머지 단계(디코더·학습 경로)가 뜻이 있다.
/// 목표는 30ms 다(docs/검출-속도-설계.md).
///
/// <b>전처리는 여기서 CPU 로 한다</b> - 실제 앱은 <see cref="FramePreprocessor"/> 가 GPU 셰이더로 하지만,
/// 그건 D3D 텍스처가 있어야 한다. 여기서 재는 것은 추론 시간이라 입력은 아무 값이나 채워도 같다 -
/// 대신 값을 0 으로 두지 않는다(0 만 든 텐서는 커널이 일찍 끝나 실제보다 빨라 보일 수 있다).
/// </remarks>
internal static class OnnxBench
{
    /// <summary>"640x640" → 텐서 규약. 크기를 모델에서 못 읽을 때 쓴다.</summary>
    private static TensorSpec FromArgument(string text)
    {
        var parts = text.Split('x', StringSplitOptions.TrimEntries);

        var width = parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ? w : 640;
        var height = parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ? h : 640;

        return new TensorSpec { Width = width, Height = height };
    }

    public static int Run(string[] args)
    {
        var modelPath = Program.ArgValue(args, "--model=");
        var runs = int.TryParse(Program.ArgValue(args, "--runs="), out var n) ? n : 30;

        if (modelPath is null || !File.Exists(modelPath))
        {
            Console.WriteLine("쓸 모델이 없다: --model=<경로.onnx>");
            return 1;
        }

        Console.WriteLine($"모델: {modelPath} ({new FileInfo(modelPath).Length / 1_048_576.0:N1} MB)");

        var opening = Stopwatch.StartNew();
        using var engine = new OnnxDmlEngine(modelPath, 0, useGpu: !args.Contains("--cpu"));
        opening.Stop();

        Console.WriteLine($"세션 만들기: {opening.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"입력 {string.Join(", ", engine.InputNames)} / 출력 {string.Join(", ", engine.OutputNames)}");

        // 모델이 크기를 고정해 두지 않았으면(동적 차원) 사람이 정해 준다 - RT-DETR·D-FINE 은 그렇게 내보내진다.
        var spec = engine.TryDeriveInputSpec() ?? FromArgument(Program.ArgValue(args, "--size=") ?? "640x640");

        Console.WriteLine($"입력 크기: {spec.Width}x{spec.Height} ({spec.Layout})");

        // 0 만 든 텐서는 실제보다 빨라 보일 수 있다. 회색 화면쯤 되는 값으로 채운다.
        var tensor = new float[spec.ElementCount];
        var random = new Random(7);

        for (var i = 0; i < tensor.Length; i++) tensor[i] = 0.3f + ((float)random.NextDouble() * 0.4f);

        // 입력이 둘인 내보내기(D-FINE)는 "원본 크기" 를 같이 받는다. 여기서는 입력 칸 크기를 그대로 준다.
        long[]? sizes = engine.InputNames.Count == 2 ? [spec.Width, spec.Height] : null;

        // 첫 몇 장은 커널을 올리고 메모리를 잡느라 느리다. 버린다.
        for (var i = 0; i < 3; i++) engine.Run(tensor, spec.Shape, sizes).Dispose();

        var times = new double[runs];

        for (var i = 0; i < runs; i++)
        {
            using var outputs = engine.Run(tensor, spec.Shape, sizes);
            times[i] = engine.LastInferenceMs;

            if (i == 0)
            {
                var first = outputs[0].GetTensorTypeAndShape();
                Console.WriteLine($"출력 모양: [{string.Join(", ", first.Shape)}]");
            }
        }

        Array.Sort(times);

        var median = times[times.Length / 2];

        Console.WriteLine();
        Console.WriteLine($"추론 한 장: 가운뎃값 {median:N1} ms  최소 {times[0]:N1}  최대 {times[^1]:N1}  →  {1000 / median:N1} fps");
        Console.WriteLine(median <= 30
            ? "== 목표(30ms) 안에 든다 - 다음 단계로 갈 값어치가 있다 =="
            : $"== 목표(30ms)를 {median - 30:N0} ms 넘는다 - 더 작은 모델이나 다른 실행 공급자를 봐야 한다 ==");

        return 0;
    }
}
