using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Ocr;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 아이온2 HUD 영역 - 실제 게임 화면(<c>HudData/aion2-field-720p.png</c>)을 영역 목록(<c>HudData/아이온2-HUD.regions.json</c>)대로 잘라 PP-OCR 로 읽는다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-24) "아이온2 HUD 영역 잡는 것도 해줘". 영역은 0~1 비율이라 해상도가 달라도 16:9 면 같은 자리다(미니맵 영역이 사용자가 1080p 에서 잡은 것과
/// 이 720p 화면에서 딱 맞았다). <c>--hud</c> 로 따로, <c>--vision</c> 에도 든다. 프로젝트에 넣기는 <c>--apply-regions=&lt;프로젝트 폴더&gt;</c>.
/// </remarks>
internal static partial class Program
{
    private const string HudPreset = "아이온2-HUD.regions.json";

    private static void TestAion2Hud()
    {
        var folder = FindHudData();

        if (folder is null)
        {
            Fail("아이온2 HUD: 검사 그림", "HudData 폴더를 못 찾았다");
            return;
        }

        var regions = LoadPreset(Path.Combine(folder, HudPreset));

        using var ocr = OcrEngineFactory.Create(out var fallback);

        if (fallback is not null) Console.WriteLine($"[INFO] 글자 읽기: {fallback}");

        // ── 720p 필드 화면 - 대상 없음 ──
        var screen = "720p 필드";
        var read = ReadHud(ocr, regions, Path.Combine(folder, "aion2-field-720p.png"), "field");

        static string Squash(string text) => text.Replace(" ", string.Empty);

        Expect("지역", r => Squash(r.Text).Contains("붉은가시왕관섬"), "붉은 가시 왕관섬");
        Expect("퀘스트", r => Squash(r.Text).Contains("악몽을") && r.Text.Contains("45"), "[LV45] 악몽을 보는 데바");
        Expect("퀘스트설명", r => Squash(r.Text).Contains("각성게이지"), "각성 게이지 100% 도달시 개방됩니다.");
        Expect("게이지", r => r.Text.Contains("93.3") || r.Numbers.SequenceEqual([93, 3]), "93.3%");
        Expect("체력", r => r.Numbers.SequenceEqual([9473, 9473]), "[9473, 9473]");
        Expect("마나", r => r.Numbers.SequenceEqual([3724, 3724]), "[3724, 3724]");
        Expect("레벨", r => r.Numbers.SequenceEqual([46]), "[46]");
        Expect("재화", r => r.Numbers.Skip(r.Numbers.Length - 3).SequenceEqual([2479369, 49849, 11751]), "끝 셋 [2479369, 49849, 11751]");
        Expect("시각", r => r.Numbers.SequenceEqual([6, 47, 8]) || r.Text.Contains("06:47:08"), "06:47:08");
        Expect("대상", r => r.Text.Length == 0, "대상이 없으면 빈 글");

        // ── 1080p 대상 잡은 화면 - 같은 영역(0~1 비율)이 해상도가 달라도 맞는지 ──
        screen = "1080p 대상";
        read = ReadHud(ocr, regions, Path.Combine(folder, "aion2-target-1080p.png"), "target");

        Expect("지역", r => Squash(r.Text).Contains("고원동부"), "아르타미아 고원 동부");
        Expect("퀘스트", r => Squash(r.Text).Contains("악몽을") && r.Text.Contains("45"), "[LV45] 악몽을 보는 데바");
        Expect("게이지", r => r.Numbers.FirstOrDefault() == 94, "94.4% → 94");
        Expect("체력", r => r.Numbers.SequenceEqual([10129, 10129]), "[10129, 10129]");
        Expect("마나", r => r.Numbers.SequenceEqual([4002, 4002]), "[4002, 4002]");
        Expect("레벨", r => r.Numbers.SequenceEqual([46]), "[46]");
        Expect("재화", r => r.Numbers.Skip(r.Numbers.Length - 3).SequenceEqual([2212096, 64076, 16751]), "끝 셋 [2212096, 64076, 16751]");
        Expect("대상", r => Squash(r.Text).Contains("고원칼니프") && r.Numbers.FirstOrDefault() == 46, "46 고원 칼니프");
        Expect("대상거리", r => r.Numbers.FirstOrDefault() == 14, "14m → 14");

        Check("숫자 뽑기: 천 단위 쉼표는 숫자 안으로(「9,473 / 9,473」 → 9473, 9473), 세 자리가 아니면 끊는다(「17,24」 → 17, 24)",
              RegionTargets.NumbersIn("9,473 / 9,473").SequenceEqual([9473, 9473]) && RegionTargets.NumbersIn("2,479,369").SequenceEqual([2479369])
              && RegionTargets.NumbersIn("17,24").SequenceEqual([17, 24]),
              string.Join(" | ", RegionTargets.NumbersIn("9,473 / 9,473")));

        void Expect(string name, Func<(string Text, int[] Numbers), bool> ok, string expected)
        {
            var got = read.TryGetValue(name, out var value) ? value : (string.Empty, []);

            Check($"아이온2 HUD({screen}): 「{name}」 을 읽는다(기대 {expected})", ok(got), $"「{got.Item1}」 [{string.Join(", ", got.Item2)}]");
        }
    }

    /// <summary>화면 한 장을 영역마다 잘라 읽는다. MINGUK_HUD_DUMP 를 켜면 잘린 조각을 %TEMP%\hud-{tag}-영역-구역.png 로 남긴다(영역을 맞출 때 눈으로 본다).</summary>
    private static Dictionary<string, (string Text, int[] Numbers)> ReadHud(IOcrEngine ocr, IReadOnlyList<NamedRegion> regions, string imagePath, string tag)
    {
        var screen = new BitmapImage();
        screen.BeginInit();
        screen.CacheOption = BitmapCacheOption.OnLoad;
        screen.UriSource = new Uri(imagePath);
        screen.EndInit();
        screen.Freeze();

        var read = new Dictionary<string, (string Text, int[] Numbers)>(StringComparer.Ordinal);

        foreach (var region in regions)
        {
            var texts = RegionTargets.Of(region, null)
                .Select(target =>
                {
                    var crop = RegionPreprocess.Apply(CropRatio(screen, target.Box), region);

                    if (Environment.GetEnvironmentVariable("MINGUK_HUD_DUMP") is { Length: > 0 })
                        SavePng(crop, Path.Combine(Path.GetTempPath(), $"hud-{tag}-{region.Name}-{target.Cell.Name}.png"));

                    return ocr.RecognizeAsync(crop).GetAwaiter().GetResult().Text;
                })
                .ToList();

            read[region.Name] = RegionTargets.Combine(texts);
            Console.WriteLine($"[INFO] 아이온2 HUD {tag} 「{region.Name}」 → 「{read[region.Name].Text}」 숫자 [{string.Join(", ", read[region.Name].Numbers)}]");
        }

        return read;
    }

    /// <summary>
    /// <c>--apply-regions=&lt;프로젝트 폴더&gt;</c> - 아이온2 HUD 영역을 그 프로젝트의 regions.json 에 더한다. 이미 있는 이름은 건드리지 않는다.
    /// </summary>
    private static int ApplyHudRegions(string projectFolder)
    {
        var folder = FindHudData();

        if (folder is null || !Directory.Exists(projectFolder))
        {
            Console.WriteLine(folder is null ? "HudData 폴더를 못 찾았다" : $"프로젝트 폴더가 없다: {projectFolder}");
            return 1;
        }

        var book = RegionBook.Load(projectFolder);
        var added = new List<string>();
        var kept = new List<string>();

        foreach (var region in LoadPreset(Path.Combine(folder, HudPreset)))
        {
            if (book.Find(region.Name) is not null) { kept.Add(region.Name); continue; }

            book.Put(region);
            added.Add(region.Name);
        }

        book.Save();

        Console.WriteLine($"더함: {(added.Count > 0 ? string.Join(", ", added) : "없음")}");
        Console.WriteLine($"이미 있어 그대로 둠: {(kept.Count > 0 ? string.Join(", ", kept) : "없음")}");
        Console.WriteLine($"→ {book.Path}");

        return 0;
    }

    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static List<NamedRegion> LoadPreset(string path)
    {
        var regions = JsonSerializer.Deserialize<List<NamedRegion>>(File.ReadAllText(path)) ?? [];

        foreach (var region in regions) region.EnsureCells();

        return regions;
    }

    /// <summary>그림의 0~1 자리를 자른다 - 허브와 같은 반올림(내림·올림).</summary>
    private static BitmapSource CropRatio(BitmapSource source, Rect ratio)
    {
        var left = (int)Math.Floor(ratio.X * source.PixelWidth);
        var top = (int)Math.Floor(ratio.Y * source.PixelHeight);
        var right = Math.Min(source.PixelWidth, (int)Math.Ceiling((ratio.X + ratio.Width) * source.PixelWidth));
        var bottom = Math.Min(source.PixelHeight, (int)Math.Ceiling((ratio.Y + ratio.Height) * source.PixelHeight));

        var crop = new CroppedBitmap(source, new Int32Rect(left, top, right - left, bottom - top));
        crop.Freeze();

        return crop;
    }

    private static string? FindHudData()
    {
        var here = AppContext.BaseDirectory;

        for (var i = 0; i < 8 && here is not null; i++)
        {
            foreach (var candidate in new[] { Path.Combine(here, "HudData"), Path.Combine(here, "Minguk.Tools.Tests", "HudData") })
                if (File.Exists(Path.Combine(candidate, HudPreset))) return candidate;

            here = Path.GetDirectoryName(here);
        }

        return null;
    }
}
