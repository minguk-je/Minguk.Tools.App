using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Ocr.Paddle;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 프로젝트의 사진과 자리로 PP-OCRv5 가 몇 장 맞는지 잰다.
/// <c>--ocr-bench --project=프로젝트폴더 [--take=30] [--truth=정답.tsv] [--sheet=폴더] [--cpu]</c>
/// </summary>
/// <remarks>
/// <b>왜</b> - 그린 글자는 게임 글꼴·배경을 못 흉내 낸다. 엔진·값을 바꿀지는 실제 화면에서 몇 장 맞는지로 정한다.
/// 옛 길(Windows OCR + 자리 손질)을 걷어낼 때 이것으로 견줬다(2026-09-16, 오버워치 40장 × 3곳: 81/119 대 67/119).
///
/// <b>정답 파일</b> - 한 줄에 <c>자리이름⇥파일이름⇥정답</c>, <c>#</c> 은 주석. 띄어쓰기는 비교에서 뺀다. 정답이 빈 칸이면 "빈 글이 맞다".
/// 정답을 모를 때는 <c>--sheet</c> 로 자리 조각을 번호 붙여 한 장에 모아 보고 적는다.
/// </remarks>
internal static class OcrBench
{
    public static int Run(string[] args)
    {
        var project = Program.ArgValue(args, "--project=");

        if (project is null || !Directory.Exists(Path.Combine(project, "Images")))
        {
            Console.WriteLine("--project=<프로젝트 폴더> 가 있어야 한다 (Images 폴더와 regions.json 이 든 곳).");
            return 2;
        }

        var take = int.TryParse(Program.ArgValue(args, "--take="), out var t) ? t : 30;

        // "-오려낸" 은 예전 --ocr-crop 이 그림 옆에 떨군 조각이다 - 화면이 아니다.
        var all = Directory.EnumerateFiles(Path.Combine(project, "Images"), "*.png")
            .Where(f => !Path.GetFileName(f).Contains("-오려낸", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // 앞에서부터 자르면 한 녹화에 몰린다 - 전체에서 고르게 고른다.
        var files = all.Count <= take ? all : [.. Enumerable.Range(0, take).Select(i => all[i * all.Count / take])];
        var regions = RegionBook.Load(project).Regions.Where(r => r.IsUsable).ToList();
        var truth = LoadTruth(Program.ArgValue(args, "--truth="));
        var sheetFolder = Program.ArgValue(args, "--sheet=");

        if (files.Count == 0 || regions.Count == 0)
        {
            Console.WriteLine($"사진 {files.Count}장 · 자리 {regions.Count}곳 - 둘 다 있어야 한다.");
            return 2;
        }

        using var paddle = PaddleOcrEngine.Create(useGpu: !args.Contains("--cpu"));

        Console.WriteLine($"프로젝트 {project} · 사진 {files.Count}장 · 자리 {regions.Count}곳 · {paddle.Name} · 정답 {truth.Count}개");

        var images = files.Select(file => (Name: Path.GetFileName(file), Image: Load(file))).ToList();
        var total = new Score();

        foreach (var region in regions)
        {
            Console.WriteLine();
            Console.WriteLine($"[{region.Name}] {region.Rect}");
            Console.WriteLine("  번호 | 파일 | PP-OCRv5 | 정답");

            var score = new Score();
            var crops = new List<BitmapSource>();

            for (var i = 0; i < images.Count; i++)
            {
                var crop = Crop(images[i].Image, region.Rect);
                crops.Add(crop);

                var watch = Stopwatch.StartNew();
                // 띄어쓰기는 남긴다 - 스크립트는 띄어쓰기로도 숫자 덩어리를 가른다(「17 24」 → [17, 24]).
                var read = paddle.RecognizeAsync(crop).GetAwaiter().GetResult().Text.Replace(Environment.NewLine, " ").Trim();
                watch.Stop();

                var expected = truth.TryGetValue((region.Name, images[i].Name), out var e) ? Flat(e) : null;

                // 첫 장은 세션 준비가 섞여 속도에서 뺀다.
                score.Add(read, expected, i == 0 ? null : watch.Elapsed.TotalMilliseconds);
                Console.WriteLine($"  {i + 1,3} | {images[i].Name} | {read} | {expected ?? "?"}");
            }

            Console.WriteLine($"  → {score.Summary()}");
            total.Merge(score);

            if (sheetFolder is not null)
            {
                Directory.CreateDirectory(sheetFolder);
                var path = Path.Combine(sheetFolder, $"{region.Name}.png");
                SaveSheet(path, crops);
                Console.WriteLine($"  조각 모음: {path}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"전체 → {total.Summary()}");

        return 0;
    }

    private sealed class Score
    {
        public int Cases, Judged, Read, Right, NumbersRight, Timed;
        public double Milliseconds;

        public void Add(string text, string? expected, double? ms)
        {
            Cases++;
            if (ms is { } m) { Milliseconds += m; Timed++; }
            if (text.Length > 0) Read++;

            if (expected is null) return;

            Judged++;

            // 스크립트는 숫자 덩어리로 쓴다(ReadNumbersAt) - 구분선을 1 로 읽으면 글은 비슷해도 값이 틀린다.
            // 띄어쓰기를 지운 뒤 세면 「17 24」 가 「1724」 로 붙어 맞은 것을 틀렸다고 센다(실측 2026-09-16 - 처음에 이렇게 셌다).
            if (Numbers(text) == Numbers(expected)) NumbersRight++;
            if (Flat(text) == expected) Right++;
        }

        public void Merge(Score other)
        {
            Cases += other.Cases; Judged += other.Judged; Read += other.Read; Right += other.Right; NumbersRight += other.NumbersRight;
            Milliseconds += other.Milliseconds; Timed += other.Timed;
        }

        public string Summary()
            => $"읽음 {Read}/{Cases} · 맞음 {Right}/{Judged} · 숫자 맞음 {NumbersRight}/{Judged} · 평균 {(Timed == 0 ? 0 : Milliseconds / Timed):0}ms";

        /// <summary>숫자 덩어리만 순서대로 이은 것. 「30|40」 → 「30,40」, 「30140」 → 「30140」.</summary>
        private static string Numbers(string text)
            => string.Join(",", System.Text.RegularExpressions.Regex.Matches(text, "[0-9]+").Select(m => m.Value));
    }

    private static string Flat(string text) => new([.. text.Where(c => !char.IsWhiteSpace(c))]);

    private static Dictionary<(string Region, string File), string> LoadTruth(string? path)
    {
        var truth = new Dictionary<(string, string), string>();

        if (path is null) return truth;

        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split('\t');

            if (parts.Length >= 3) truth[(parts[0], parts[1])] = parts[2];
        }

        return truth;
    }

    private static BitmapSource Load(string path)
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

    /// <summary>화면(<c>CropFrame</c>)과 같은 반올림으로 자른다.</summary>
    private static BitmapSource Crop(BitmapSource image, Rect region)
    {
        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var left = Math.Clamp((int)Math.Floor(region.X * w), 0, w - 1);
        var top = Math.Clamp((int)Math.Floor(region.Y * h), 0, h - 1);
        var right = Math.Clamp((int)Math.Ceiling(region.Right * w), left + 1, w);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom * h), top + 1, h);

        var crop = new CroppedBitmap(image, new Int32Rect(left, top, right - left, bottom - top));
        crop.Freeze();

        return crop;
    }

    /// <summary>조각을 번호 붙여 세로로 모은 한 장. 2배로 키워 눈으로 정답을 적기 쉽게.</summary>
    private static void SaveSheet(string path, IReadOnlyList<BitmapSource> crops)
    {
        const int scale = 2, label = 48, pad = 3;

        var rowHeight = (crops.Max(c => c.PixelHeight) * scale) + (2 * pad);
        var width = label + (crops.Max(c => c.PixelWidth) * scale) + pad;
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, rowHeight * crops.Count));

            for (var i = 0; i < crops.Count; i++)
            {
                var y = i * rowHeight;
                var number = new FormattedText($"{i + 1}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                               new Typeface("Segoe UI"), 16, Brushes.Black, 1.0);

                dc.DrawText(number, new Point(6, y + pad));
                dc.DrawImage(crops[i], new Rect(label, y + pad, crops[i].PixelWidth * scale, crops[i].PixelHeight * scale));
            }
        }

        var bitmap = new RenderTargetBitmap(width, rowHeight * crops.Count, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
