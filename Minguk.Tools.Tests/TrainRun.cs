using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 화면 없이 학습시킨다. <c>--train --size=480x270 [--epochs=20]</c>.
/// </summary>
/// <remarks>
/// <b>왜</b> - 크기를 바꿔 견주려면 라벨링 화면을 띄워 눌러야 했다. 몇 분씩 걸리는 일을 앱을 켜 둔 채로 못 하고,
/// 잰 값도 사람이 옮겨 적어야 했다. 여기서 돌리면 앱을 안 건드리고, 끝난 뒤 검출 시간까지 이어서 잰다.
///
/// <b>모델을 덮어쓴다</b> - 학습은 데이터셋 폴더의 detector.zip 을 갈아 끼운다. 돌리기 전에 detector.zip ·
/// detector.json 을 <c>detector.(확장자).bak</c> 로 복사해 둔다.
/// </remarks>
internal static class TrainRun
{
    public static int Run(string[] args)
    {
        var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);
        var size = ParseSize(Program.ArgValue(args, "--size=") ?? "480x270");
        var epochs = int.TryParse(Program.ArgValue(args, "--epochs="), out var e) ? e : 20;

        Console.WriteLine($"데이터셋: {dataset.Root}");
        Console.WriteLine($"크기 {size.Width}x{size.Height} · {epochs} 바퀴");

        if (LibTorchRuntime.Installed is not { } flavor)
        {
            Console.WriteLine("libtorch 가 없다. 앱에서 학습을 한 번 눌러 받아야 한다.");
            return 1;
        }

        LibTorchRuntime.Load(flavor);
        Console.WriteLine($"libtorch {flavor} · CUDA {TorchSharp.torch.cuda.is_available()}");

        BackUp(DetectorTrainer.ModelPathFor(dataset));
        BackUp(Path.ChangeExtension(DetectorTrainer.ModelPathFor(dataset), ".json"));

        var progress = new Progress<string>(Console.WriteLine);
        var result = DetectorTrainer.TrainAsync(dataset, epochs, progress, CancellationToken.None, size.Width, size.Height)
            .GetAwaiter().GetResult();

        Console.WriteLine();
        Console.WriteLine($"학습 끝: {result.InputSize} · 그림 {result.Images}장 · 사각형 {result.Boxes}개 · {result.Elapsed.TotalMinutes:0.0}분 · GPU {result.UsedGpu}");

        Measure(dataset, result.ModelPath);
        return 0;
    }

    /// <summary>학습한 모델로 한 장에 몇 ms 가 걸리는지. 실시간 반복문의 한 바퀴가 이 값에 걸린다.</summary>
    private static void Measure(LabelDataset dataset, string modelPath)
    {
        var image = dataset.EnumerateItems().Select(i => i.ImagePath).FirstOrDefault(File.Exists);

        if (image is null)
        {
            Console.WriteLine("잴 그림이 없다.");
            return;
        }

        using var model = Minguk.Tools.Vision.Inference.DetectorFactory.Create(modelPath);
        var classes = dataset.LoadClasses();

        // 첫 장은 준비가 섞여 느리다. 버리고 다섯 번 잰다.
        model.Detect(image, classes);

        var times = new long[5];

        for (var i = 0; i < times.Length; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var found = model.Detect(image, classes);
            watch.Stop();
            times[i] = watch.ElapsedMilliseconds;

            if (i == 0) Console.WriteLine($"찾은 것 {found.Count}개: {string.Join(", ", found.Take(3).Select(d => d.Describe))}");
        }

        Console.WriteLine($"추론 한 장: {times.Average():0} ms (잰 값 {string.Join(", ", times)})");
    }

    private static void BackUp(string path)
    {
        if (!File.Exists(path)) return;

        var backup = path + ".bak";
        File.Copy(path, backup, overwrite: true);
        Console.WriteLine($"덮어쓰기 전에 남김: {backup}");
    }

    private static (int Width, int Height) ParseSize(string text)
    {
        var parts = text.Split('x', StringSplitOptions.TrimEntries);

        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : (480, 270);
    }
}
