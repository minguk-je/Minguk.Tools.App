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

        // 영역마다 제 엔진(regions.json 의 ocrEngine) - 앱과 같다. 비었으면 PP-OCRv5(GPU).
        var engines = new Dictionary<OcrEngineKind, IOcrEngine>();

        IOcrEngine ocr(NamedRegion region)
        {
            var kind = region.OcrEngine ?? OcrEngineKind.PaddleGpu;

            if (!engines.TryGetValue(kind, out var engine)) engines[kind] = engine = OcrEngineFactory.Create(kind, out _);

            return engine;
        }

        // ── 720p 필드 화면 - 대상 없음. 영역 "자리" 만 본다 - 모두 PP-OCRv5 로 읽는다 ──
        // 이 화면은 대화창에 올리며 줄어든 것이라 글자가 작다. 빠른 엔진(Windows·v6 tiny)은 여기서 체력·마나·지역을 틀리지만
        // 실제 캡처(1920×1080, 사용자 로그)에서는 맞는다 - 엔진은 아래 1080p 로 본다.
        var screen = "720p 필드";
        var read = ReadHud(_ => ocr(new NamedRegion()), regions, Path.Combine(folder, "aion2-field-720p.png"), "field");

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

        // ── 1080p 대상 잡은 화면 - 실제 캡처 크기. 영역마다 제 엔진(regions.json 의 ocrEngine)으로 ──
        screen = "1080p 대상";
        read = ReadHud(ocr, regions, Path.Combine(folder, "aion2-target-1080p.png"), "target");

        Expect("지역", r => Squash(r.Text).Contains("고원동부"), "아르타미아 고원 동부");
        Expect("퀘스트", r => Squash(r.Text).Contains("악몽을") && r.Text.Contains("45"), "[LV45] 악몽을 보는 데바");
        Expect("게이지", r => r.Numbers.FirstOrDefault() == 94, "94.4% → 94");
        Expect("체력", r => r.Numbers.SequenceEqual([10129, 10129]), "[10129, 10129]");
        Expect("마나", r => r.Numbers.SequenceEqual([4002, 4002]), "[4002, 4002]");
        Expect("레벨", r => r.Numbers.SequenceEqual([46]), "[46]");
        Expect("재화", r => r.Numbers.Skip(r.Numbers.Length - 3).SequenceEqual([2212096, 64076, 16751]), "끝 셋 [2212096, 64076, 16751]");
        Expect("대상", r => Squash(r.Text).Contains("칼니프"), "46 고원 칼니프 - Windows OCR 이라 앞은 깨져도 이름 끝은 읽는다");
        Expect("대상거리", r => r.Numbers.FirstOrDefault() == 14, "14m → 14");

        foreach (var engine in engines.Values) engine.Dispose();

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

    /// <summary>
    /// <c>--hud-bench</c> - 1080p 대상 화면의 영역마다 OCR 엔진별로 걸린 시간과 맞게 읽었는지를 잰다.
    /// </summary>
    /// <remarks>
    /// 사용자 로그(2026-09-24) - 게임을 켜 둔 채 PP-OCRv5(GPU) 로 영역 하나에 0.9~1초가 걸려 사냥 한 바퀴가 2초쯤 됐다. 게임과 DirectML 이 3GB 카드를 나눠 쓰는 탓으로 본다.
    /// 여기서는 게임 없이 재므로 GPU 쪽이 실제보다 빠르게 나온다 - 엔진끼리 견주는 데 쓴다.
    /// </remarks>
    private static int BenchHudOcr()
    {
        var folder = FindHudData();

        if (folder is null) { Console.WriteLine("HudData 폴더를 못 찾았다"); return 1; }

        var regions = LoadPreset(Path.Combine(folder, HudPreset));
        var screen = new BitmapImage();
        screen.BeginInit();
        screen.CacheOption = BitmapCacheOption.OnLoad;
        screen.UriSource = new Uri(Path.Combine(folder, "aion2-target-1080p.png"));
        screen.EndInit();
        screen.Freeze();

        static string Squash(string text) => text.Replace(" ", string.Empty);

        var expected = new (string Name, Func<(string Text, int[] Numbers), bool> Ok)[]
        {
            ("체력", r => r.Numbers.SequenceEqual([10129, 10129])),
            ("마나", r => r.Numbers.SequenceEqual([4002, 4002])),
            ("레벨", r => r.Numbers.SequenceEqual([46])),
            ("게이지", r => r.Numbers.FirstOrDefault() == 94),
            ("대상", r => Squash(r.Text).Contains("고원칼니프")),
            ("대상거리", r => r.Numbers.FirstOrDefault() == 14),
            ("지역", r => Squash(r.Text).Contains("고원동부")),
            ("퀘스트", r => Squash(r.Text).Contains("악몽을"))
        };

        // 조각은 미리 잘라 둔다 - 엔진 시간만 잰다.
        var crops = expected.ToDictionary(e => e.Name, e =>
        {
            var region = regions.First(r => r.Name == e.Name);
            return RegionTargets.Of(region, null).Select(t => RegionPreprocess.Apply(CropRatio(screen, t.Box), region)).ToList();
        });

        const int rounds = 10;

        Console.WriteLine($"영역마다 {rounds}번 읽은 평균(ms) · O=맞게 읽음 X=틀림");
        Console.WriteLine($"{"엔진",-22}" + string.Concat(expected.Select(e => $"{e.Name,9}")) + $"{"한 바퀴",9}");

        foreach (var kind in new[] { OcrEngineKind.PaddleGpu, OcrEngineKind.PaddleCpu, OcrEngineKind.Windows, OcrEngineKind.PaddleV6TinyGpu, OcrEngineKind.PaddleV6SmallGpu })
        {
            IOcrEngine engine;

            try
            {
                engine = OcrEngineFactory.Create(kind, out var fallback);
                if (fallback is not null) Console.WriteLine($"  ({kind}: {fallback})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{kind,-22} 못 열었다 - {ex.Message}");
                continue;
            }

            using (engine)
            {
                // 예열 - 첫 호출은 모델 올리기·그래프 준비라 뺀다.
                foreach (var crop in crops.Values.SelectMany(c => c)) engine.RecognizeAsync(crop).GetAwaiter().GetResult();

                var cells = new List<string>();
                double total = 0;

                foreach (var (name, ok) in expected)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    List<string> texts = [];

                    for (var i = 0; i < rounds; i++)
                        texts = [.. crops[name].Select(c => engine.RecognizeAsync(c).GetAwaiter().GetResult().Text)];

                    watch.Stop();

                    var ms = watch.Elapsed.TotalMilliseconds / rounds;
                    total += ms;
                    cells.Add($"{ms,7:0}{(ok(RegionTargets.Combine(texts)) ? "O" : "X"),2}");
                }

                Console.WriteLine($"{engine.Name,-22}" + string.Concat(cells) + $"{total,9:0}");

                // 틀린 것은 무엇으로 읽었는지 - 「대상」 은 대상이 없는 화면(720p)에서 비어 나오는지도(있나 확인에 쓸 수 있는지).
                foreach (var (name, ok) in expected)
                {
                    var got = RegionTargets.Combine([.. crops[name].Select(c => engine.RecognizeAsync(c).GetAwaiter().GetResult().Text)]);
                    if (!ok(got)) Console.WriteLine($"    {name}: 「{got.Text}」");
                }

                var noTarget = new BitmapImage();
                noTarget.BeginInit();
                noTarget.CacheOption = BitmapCacheOption.OnLoad;
                noTarget.UriSource = new Uri(Path.Combine(folder, "aion2-field-720p.png"));
                noTarget.EndInit();
                noTarget.Freeze();

                var targetRegion = regions.First(r => r.Name == "대상");
                var empty = engine.RecognizeAsync(RegionPreprocess.Apply(CropRatio(noTarget, RegionTargets.Of(targetRegion, null)[0].Box), targetRegion)).GetAwaiter().GetResult().Text.Trim();

                Console.WriteLine($"    대상 없는 화면의 「대상」: 「{empty}」{(empty.Length == 0 ? " - 비어 나온다" : "")}");
            }
        }

        return 0;
    }

    /// <summary>화면 한 장을 영역마다 잘라 읽는다. MINGUK_HUD_DUMP 를 켜면 잘린 조각을 %TEMP%\hud-{tag}-영역-구역.png 로 남긴다(영역을 맞출 때 눈으로 본다).</summary>
    private static Dictionary<string, (string Text, int[] Numbers)> ReadHud(Func<NamedRegion, IOcrEngine> ocr, IReadOnlyList<NamedRegion> regions, string imagePath, string tag)
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

                    return ocr(region).RecognizeAsync(crop).GetAwaiter().GetResult().Text;
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
    private static int ApplyHudRegions(string projectFolder) => ApplyHudRegions(projectFolder, enginesOnly: false);

    /// <param name="enginesOnly">
    /// <c>--apply-engines</c> - 이미 있는 영역의 <b>글자 읽기 엔진만</b> 목록 것으로 바꾼다(자리·손질은 사용자가 고쳤을 수 있어 그대로). 없는 영역은 더한다.
    /// </param>
    private static int ApplyHudRegions(string projectFolder, bool enginesOnly)
    {
        if (enginesOnly)
        {
            var presetFolder = FindHudData();

            if (presetFolder is null || !Directory.Exists(projectFolder)) { Console.WriteLine("HudData 폴더나 프로젝트 폴더가 없다"); return 1; }

            var projectBook = RegionBook.Load(projectFolder);
            var changed = new List<string>();

            foreach (var preset in LoadPreset(Path.Combine(presetFolder, HudPreset)))
            {
                if (projectBook.Find(preset.Name) is not { } existing) { projectBook.Put(preset); changed.Add($"{preset.Name}(더함)"); continue; }
                if (existing.OcrEngineName == preset.OcrEngineName) continue;

                changed.Add($"{preset.Name}: {existing.OcrEngineName ?? "기본"} → {preset.OcrEngineName ?? "기본"}");
                existing.OcrEngineName = preset.OcrEngineName;
            }

            projectBook.Save();

            Console.WriteLine(changed.Count > 0 ? "바꿈: " + string.Join(" · ", changed) : "바꿀 것 없음");
            Console.WriteLine($"→ {projectBook.Path}");

            return 0;
        }

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
