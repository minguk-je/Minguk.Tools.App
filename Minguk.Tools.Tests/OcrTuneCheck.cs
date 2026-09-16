using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Ocr;

namespace Minguk.Tools.Tests;

/// <summary>
/// 자리 하나를 여러 장·여러 손질로 읽어 어느 조합이 잘 읽는지 표로 낸다.
/// <c>--ocr-tune --dir=사진폴더 --region=x,y,w,h [--take=20] [--expect=6]</c>
/// </summary>
/// <remarks>
/// <b>왜</b> - "어쩌다 한 번 읽는다" 는 한 장으로는 못 고친다. 배경이 바뀌는 자리는 여러 장에서 몇 장이 읽히는지로 봐야
/// 손질을 고를 수 있다(실측 2026-09-16: 오버워치 탄약이 고정 문턱 손질로 10장 중 4장).
/// </remarks>
public static class OcrTuneCheck
{
    public static int Run(string[] args)
    {
        var dir = Program.ArgValue(args, "--dir=");
        var region = Program.ArgValue(args, "--region=");

        if (dir is null || region is null || !Directory.Exists(dir))
        {
            Console.WriteLine("--dir=<사진 폴더> --region=<x,y,w,h> 가 있어야 한다 (0~1 비율).");
            return 2;
        }

        var parts = region.Split(',');

        if (parts.Length != 4 || !parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            Console.WriteLine("--region 은 x,y,w,h 네 개여야 한다.");
            return 2;
        }

        var rect = new Rect(
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture));

        var take = int.TryParse(Program.ArgValue(args, "--take="), out var t) ? t : 20;
        var files = Directory.EnumerateFiles(dir, "*.png").OrderBy(f => f).Take(take).ToList();

        if (files.Count == 0)
        {
            Console.WriteLine($"{dir} 에 png 가 없다.");
            return 2;
        }

        // 언어도 같이 잰다 - 같은 숫자를 ko 는 읽고 en-US 는 못 읽는 자리가 있다(실측 2026-09-16).
        var languages = (Program.ArgValue(args, "--langs=") ?? "en-US,ko").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var engines = languages.Select(OcrEngineFactory.TryCreate).Where(e => e is not null).Select(e => e!).ToList();

        if (engines.Count == 0)
        {
            Console.WriteLine($"엔진을 못 만들었다: {string.Join(",", languages)}");
            return 2;
        }

        Console.WriteLine($"자리 {rect} · 사진 {files.Count}장 · 언어 {string.Join(" · ", engines.Select(e => e.Language))}");
        Console.WriteLine();

        var heights = new[] { 200, 300, 450 };
        var shears = new double[] { 0, 12 };
        var best = (Hits: -1, Label: string.Empty);

        foreach (var engine in engines)
        foreach (var preprocessor in OcrPreprocessors.All)
            foreach (var height in heights)
                foreach (var shear in shears)
                {
                    var options = new OcrPreprocessOptions(height, shear);
                    var hits = 0;
                    var samples = new List<string>();

                    foreach (var file in files)
                    {
                        var text = ReadOne(engine, file, rect, preprocessor, options);

                        if (text.Length > 0) hits++;
                        if (samples.Count < 4) samples.Add(text);
                    }

                    var label = $"{engine.Language,-6} {preprocessor.Name,-12} 높이 {height,3} {(shear == 0 ? "   " : "세움")}";

                    Console.WriteLine($"{label} → {hits,2}/{files.Count}  {string.Join(" ", samples.Select(s => $"[{s}]"))}");

                    if (hits > best.Hits) best = (hits, label);
                }

        Console.WriteLine();
        Console.WriteLine($"가장 잘 읽은 것: {best.Label} ({best.Hits}/{files.Count})");

        foreach (var engine in engines) engine.Dispose();

        return 0;
    }

    private static string ReadOne(IOcrEngine engine, string file, Rect rect, IOcrPreprocessor preprocessor, OcrPreprocessOptions options)
    {
        var image = new BitmapImage();

        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(file);
        image.EndInit();
        image.Freeze();

        var left = (int)Math.Round(rect.X * image.PixelWidth);
        var top = (int)Math.Round(rect.Y * image.PixelHeight);
        var width = Math.Max(1, (int)Math.Round(rect.Width * image.PixelWidth));
        var height = Math.Max(1, (int)Math.Round(rect.Height * image.PixelHeight));

        var crop = new CroppedBitmap(image, new Int32Rect(left, top, width, height));

        crop.Freeze();

        var prepared = preprocessor.Prepare(crop, options);

        return engine.RecognizeAsync(prepared).GetAwaiter().GetResult().Text.Replace(Environment.NewLine, " ").Trim();
    }
}
