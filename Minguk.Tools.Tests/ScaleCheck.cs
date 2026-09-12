using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 실시간 경로(WPF 로 줄인 PNG)와 파일 경로(원본을 ML.NET 이 줄임)가 같은 답을 내는지 견준다.
/// </summary>
/// <remarks>
/// 캡처 모니터는 프레임을 <c>TransformedBitmap</c> 으로 모델 크기까지 줄여 PNG 로 넘기고,
/// 되찾기 검사는 원본 1080p 를 ML.NET(SkiaSharp) 이 줄인다. 줄이는 방식이 다르면 작은 몹의
/// 윤곽이 달라져 점수가 깎일 수 있다 - 되찾기 97% 인 모델이 실시간에서 흔들릴 때 거리 탓인지
/// 경로 탓인지 갈라야 한다. 같은 그림을 두 길로 넣어 찾은 개수와 점수를 나란히 본다.
///
/// <c>--scale-check[=<폴더>] [--side=640]</c>. libtorch 와 모델이 있어야 돈다.
/// </remarks>
internal static class ScaleCheck
{
    public static int Run(string? root, int longestSide)
    {
        var dataset = new LabelDataset(root ?? LabelDataset.ConfiguredRoot);

        if (LibTorchRuntime.Installed is not { } flavor)
        {
            Console.WriteLine("libtorch 가 없다.");
            return 1;
        }

        LibTorchRuntime.Load(flavor);

        var modelPath = DetectorTrainer.ModelPathFor(dataset);
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"학습한 모델이 없다: {modelPath}");
            return 1;
        }

        using var model = DetectorFactory.Create(modelPath);
        var classes = dataset.LoadClasses();
        var items = dataset.EnumerateItems().Where(i => i.HasLabel).ToList();

        Console.WriteLine($"모델 {model.Manifest.Describe} · 그림 {items.Count}장 · 실시간 경로는 긴 변 {longestSide}px 로 줄인 PNG");
        Console.WriteLine();

        var temp = Path.Combine(Path.GetTempPath(), "minguk-scale-" + Guid.NewGuid().ToString("N") + ".png");
        var totalOriginal = new DetectionMatch.Result(0, 0, 0);
        var totalScaled = new DetectionMatch.Result(0, 0, 0);
        var scoresOriginal = new List<double>();
        var scoresScaled = new List<double>();

        try
        {
            foreach (var item in items)
            {
                var labels = LabelFile.Load(item.LabelPath, out _);

                var original = model.Detect(item.ImagePath, classes, 0.5f);
                WriteScaled(item.ImagePath, temp, longestSide);
                var scaled = model.Detect(temp, classes, 0.5f);

                var a = DetectionMatch.Match(labels, original);
                var b = DetectionMatch.Match(labels, scaled);

                totalOriginal = new DetectionMatch.Result(totalOriginal.Found + a.Found, totalOriginal.Labels + a.Labels, totalOriginal.Extra + a.Extra);
                totalScaled = new DetectionMatch.Result(totalScaled.Found + b.Found, totalScaled.Labels + b.Labels, totalScaled.Extra + b.Extra);
                scoresOriginal.AddRange(original.Select(d => (double)d.Score));
                scoresScaled.AddRange(scaled.Select(d => (double)d.Score));

                var mark = a.Found == b.Found ? "같음" : b.Found < a.Found ? "실시간이 덜 찾음" : "실시간이 더 찾음";
                Console.WriteLine($"{item.Name}  원본 {a.Describe} ({Top(original)})  실시간 {b.Describe} ({Top(scaled)})  {mark}");
            }
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception) { }
        }

        Console.WriteLine();
        Console.WriteLine($"== 원본 경로: {totalOriginal.Found}/{totalOriginal.Labels} · 헛것 {totalOriginal.Extra} · 평균 점수 {Mean(scoresOriginal):P0}");
        Console.WriteLine($"== 실시간 경로: {totalScaled.Found}/{totalScaled.Labels} · 헛것 {totalScaled.Extra} · 평균 점수 {Mean(scoresScaled):P0}");

        return 0;
    }

    /// <summary>캡처 모니터가 하는 것과 같은 방법으로 줄인다(TransformedBitmap → PNG).</summary>
    private static void WriteScaled(string source, string target, int longestSide)
    {
        BitmapSource bitmap;

        using (var stream = File.OpenRead(source))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            bitmap = image;
        }

        var scale = Math.Min(1d, (double)longestSide / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        var scaled = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));

        using var output = File.Create(target);
        encoder.Save(output);
    }

    private static string Top(IReadOnlyList<Detection> found)
        => found.Count == 0 ? "-" : string.Join(" ", found.OrderByDescending(d => d.Score).Take(3).Select(d => $"{d.Score:P0}"));

    private static double Mean(List<double> values) => values.Count == 0 ? 0 : values.Average();
}
