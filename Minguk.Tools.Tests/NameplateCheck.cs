using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Ocr;

namespace Minguk.Tools.Tests;

/// <summary>
/// 데이터셋의 라벨 위 이름표 자리를 잘라 OCR 에 넣어 본다. 언어별로, 원본과 전처리 각각 무엇이 읽히는지.
/// </summary>
/// <remarks>
/// 실시간에서 이름표가 빈 글로 나올 때 "코드 경로가 안 도는지" 와 "OCR 이 그 글자를 못 읽는지" 를
/// 가르는 도구다. 잘라낸 그림을 파일로도 남겨 눈으로 볼 수 있게 한다.
/// <c>--nameplate-check [--root=폴더] [--count=3] [--out=폴더]</c>
/// </remarks>
internal static class NameplateCheck
{
    public static int Run(string? root, int count, string? outDirectory)
    {
        var dataset = new LabelDataset(root ?? LabelDataset.ConfiguredRoot);
        var items = dataset.EnumerateItems().Where(i => i.HasLabel).Take(count).ToList();
        var output = outDirectory ?? Path.Combine(Path.GetTempPath(), "minguk-nameplate");

        Directory.CreateDirectory(output);

        var engines = WindowsOcrEngine.AvailableLanguages
            .Select(tag => (Tag: tag, Engine: OcrEngineFactory.TryCreate(tag)))
            .Where(e => e.Engine is not null)
            .ToList();

        Console.WriteLine($"데이터셋 {dataset.Root} · 그림 {items.Count}장 · 언어 {string.Join(", ", engines.Select(e => e.Tag))} · 잘라낸 그림 → {output}");

        foreach (var item in items)
        {
            var image = LoadImage(item.ImagePath);
            var labels = LabelFile.Load(item.LabelPath, out _);

            for (var i = 0; i < labels.Count; i++)
            {
                var region = NameplateRegion.Above(labels[i]);
                if (region.IsEmpty) continue;

                var rect = new Int32Rect(
                    (int)(region.X * image.PixelWidth), (int)(region.Y * image.PixelHeight),
                    Math.Max(1, (int)(region.Width * image.PixelWidth)), Math.Max(1, (int)(region.Height * image.PixelHeight)));

                var crop = new CroppedBitmap(image, rect);
                crop.Freeze();

                var file = Path.Combine(output, $"{Path.GetFileNameWithoutExtension(item.ImagePath)}-{i}.png");
                Save(crop, file);

                Console.WriteLine($"{item.Name} #{i} {rect.Width}x{rect.Height}px");

                // 원본과 제품 전처리(NameplateInk)를 나란히. 전처리를 손보면 여기서 바로 비교된다.
                var variants = new (string Name, BitmapSource Image)[]
                {
                    ("원본", crop),
                    ("전처리", NameplateInk.Prepare(crop)),
                };

                foreach (var (name, variant) in variants)
                {
                    if (name == "전처리") Save(variant, Path.Combine(output, $"{Path.GetFileNameWithoutExtension(item.ImagePath)}-{i}-{name}.png"));

                    var results = engines.Select(e =>
                    {
                        var read = e.Engine!.RecognizeAsync(variant).GetAwaiter().GetResult();
                        return $"{e.Tag}=[{read.Text.Replace(Environment.NewLine, " / ")}] {read.Elapsed.TotalMilliseconds:0}ms";
                    });

                    Console.WriteLine($"    {name,-6} {string.Join("  ", results)}");
                }
            }
        }

        foreach (var e in engines) e.Engine!.Dispose();

        return 0;
    }

    private static BitmapSource LoadImage(string path)
    {
        using var stream = File.OpenRead(path);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
