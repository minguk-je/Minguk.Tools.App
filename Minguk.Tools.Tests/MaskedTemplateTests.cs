using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using DevExpress.Mvvm;

using Minguk.Tools.Markup.Regions;
using Minguk.Tools.Vision.Matching;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 본보기 마스크 - 다각형 안만 견주는지, 알파 PNG 로 오가는지, 구역에 저장되는지, 미리보기에서 꼭짓점을 고치는지.
/// </summary>
/// <remarks>
/// 사용자(2026-09-23) "Adorner 안에 폴리곤 식의 구역을 자유롭게 지정해서 이부분만 매칭 해라". 둥근 아이콘을 본보기로 떴는데 둘레(네모 귀퉁이)의 배경이
/// 화면마다 다르면, 마스크 없이는 귀퉁이가 닮음을 깎는다. 마스크를 씌우면 둥근 안쪽만 보아 닮음이 1 에 가깝다 - 그것을 잰다.
/// </remarks>
internal static partial class Program
{
    private static void TestMaskedTemplate()
    {
        // ── 칠하기 ──
        var square = PolygonMask.Rasterize([new Point(0.25, 0.25), new Point(0.75, 0.25), new Point(0.75, 0.75), new Point(0.25, 0.75)], 8, 8);
        var inside = Enumerable.Range(2, 4).SelectMany(y => Enumerable.Range(2, 4).Select(x => square[(y * 8) + x])).All(v => v == 255);
        var outside = square.Where((_, i) => i % 8 is < 2 or > 5 || i / 8 is < 2 or > 5).All(v => v == 0);

        Check("마스크: 네모 다각형은 안쪽 255·바깥 0 으로 칠한다", inside && outside, $"안 {inside} · 밖 {outside}");

        var triangle = PolygonMask.Rasterize([new Point(0, 0), new Point(1, 0), new Point(0, 1)], 40, 40);
        var triangleArea = triangle.Sum(v => v) / 255.0 / (40 * 40);

        Check("마스크: 삼각형(절반)은 넓이 절반", Math.Abs(triangleArea - 0.5) < 0.01, $"{triangleArea:0.000}");

        // ── 알파 → 마스크 ──
        var opaque = GrayImage.From(DrawSolid(8, 6, Colors.White), useAlpha: true);
        var transparent = GrayImage.From(BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 16), useAlpha: true);

        Check("마스크: 알파가 모두 255 면 마스크 없음(옛 본보기 그대로)", opaque.Mask is null, "");
        Check("마스크: 알파가 모두 0 이면 마스크 없음(알파를 안 채운 그림)", transparent.Mask is null, "");

        // ── 둥근 아이콘 - 둘레 배경이 다른 화면에서 찾기 ──
        var sceneA = DrawIconScene(noiseSeed: 1);
        var sceneB = DrawIconScene(noiseSeed: 2);
        const double ix = IconLeft / 1920.0, iy = IconTop / 1080.0;
        var crop = Crop(sceneA, ix, iy, IconSide / 1920.0, IconSide / 1080.0);
        var circle = Enumerable.Range(0, 32)
            .Select(i => new Point(0.5 + (0.42 * Math.Cos(i * Math.PI / 16)), 0.5 + (0.42 * Math.Sin(i * Math.PI / 16))))
            .ToArray();

        // 영역 이미지 저장과 같은 길 - 알파를 입혀 PNG 로 쓰고, 스크립트처럼 파일에서 읽어 알파를 마스크로.
        var masked = PolygonMask.Apply(crop, circle);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(masked));

        using var memory = new MemoryStream();
        png.Save(memory);
        memory.Position = 0;

        var loaded = new BitmapImage();
        loaded.BeginInit();
        loaded.CacheOption = BitmapCacheOption.OnLoad;
        loaded.StreamSource = memory;
        loaded.EndInit();
        loaded.Freeze();

        var maskedNeedle = GrayImage.From(loaded, useAlpha: true);
        var plainNeedle = GrayImage.From(crop);

        Check("마스크: 알파 PNG 를 읽으면 마스크가 산다(투명한 귀퉁이 = 0)",
              maskedNeedle.Mask is { } m && m[0] == 0 && m[(IconSide / 2 * IconSide) + (IconSide / 2)] == 255,
              maskedNeedle.Mask is null ? "마스크 없음" : $"귀퉁이 {maskedNeedle.Mask[0]} · 가운데 {maskedNeedle.Mask[(IconSide / 2 * IconSide) + (IconSide / 2)]} · 넓이 {maskedNeedle.MaskedArea:0}px");

        var haystack = GrayImage.From(sceneB);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var maskedHit = TemplateMatch.Find(haystack, maskedNeedle);
        watch.Stop();
        var plainHit = TemplateMatch.Find(haystack, plainNeedle);

        var offX = maskedHit is null ? double.NaN : Math.Abs(maskedHit.Value.X - ix) * 1920;
        var offY = maskedHit is null ? double.NaN : Math.Abs(maskedHit.Value.Y - iy) * 1080;
        var plainScore = plainHit is { } p && Math.Abs(p.X - ix) * 1920 <= 4 ? p.Score : plainHit?.Score ?? 0;

        Check("마스크: 둘레 배경이 달라도 둥근 안쪽만 견줘 찾는다(4px 안, 닮음 0.95 넘게, 1.5초 안)",
              maskedHit is not null && maskedHit.Value.Score > 0.95 && offX <= 4 && offY <= 4 && watch.ElapsedMilliseconds <= 1500,
              maskedHit is null ? $"못 찾음 ({watch.ElapsedMilliseconds}ms)" : $"닮음 {maskedHit.Value.Score:0.000} · 어긋남 {offX:0}·{offY:0}px · {watch.ElapsedMilliseconds}ms");

        Check("마스크: 마스크 없이는 귀퉁이 배경 탓에 닮음이 낮다(마스크가 0.15 넘게 높다)",
              maskedHit is not null && maskedHit.Value.Score - plainScore > 0.15,
              $"마스크 {maskedHit?.Score:0.000} · 없이 {plainScore:0.000}");

        // 없는 그림 - 마스크를 씌워도 닮음이 낮아야 한다.
        var stranger = GrayImage.From(PolygonMask.Apply(DrawNoise(IconSide, IconSide), circle), useAlpha: true);
        var wrong = TemplateMatch.Find(haystack, stranger);

        Check("마스크: 화면에 없는 그림은 마스크를 씌워도 닮음이 낮다(0.8 아래)", wrong is null || wrong.Value.Score < 0.8, wrong is null ? "못 찾음" : $"닮음 {wrong.Value.Score:0.000}");

        // 몇 픽셀짜리 마스크는 어디에나 맞는다 - 안 찾는다.
        var tinyMask = new byte[IconSide * IconSide];
        for (var i = 0; i < 6; i++) tinyMask[(40 * IconSide) + 40 + i] = 255;

        Check("마스크: 마스크 안이 너무 작으면(12px 미만) 안 찾는다",
              TemplateMatch.Find(haystack, new GrayImage(IconSide, IconSide, plainNeedle.Pixels, tinyMask)) is null, "");

        // ── 구역에 저장 ──
        var folder = Path.Combine(Path.GetTempPath(), "MingukMaskTest_" + Guid.NewGuid().ToString("N"));

        try
        {
            var book = new RegionBook(folder);
            var region = new NamedRegion { Name = "아이콘", X = 0.1, Y = 0.1, Width = 0.2, Height = 0.2 };
            var plainRegion = new NamedRegion { Name = "네모", X = 0.5, Y = 0.5, Width = 0.2, Height = 0.2 };

            region.Cells[0].Mask = PolygonMask.DefaultShape();
            book.Put(region);
            book.Put(plainRegion);
            book.Save();

            var json = File.ReadAllText(book.Path);
            var back = RegionBook.Load(folder);
            var restored = back.Find("아이콘")!.Cells[0].Mask;
            var plainBack = back.Find("네모")!.Cells[0];

            Check("마스크: 구역에 저장하고 다시 읽으면 꼭짓점이 그대로(8개)",
                  restored.Count == 8 && restored.SequenceEqual(region.Cells[0].Mask), $"{restored.Count}개");
            Check("마스크: 마스크 없는 구역은 파일에 mask 를 안 적는다(옛 파일과 같은 모양)",
                  json.Split("\"mask\"").Length == 2 && !plainBack.HasMask, $"mask 키 {json.Split("\"mask\"").Length - 1}개");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 미리보기 캔버스에서 마스크 꼭짓점을 옮기고·늘리고·빼는지. 실제 커서 없이 캔버스 함수를 부른다(끌기 이벤트는 --region-drag 몫).
    /// </summary>
    private static void TestRegionMaskEditing()
    {
        const int size = 400;

        var image = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var region = new NamedRegion { Name = "영역1", X = 0.25, Y = 0.25, Width = 0.5, Height = 0.5 };
        var cell = region.Cells[0];
        var edits = new List<CellEdit>();

        cell.Mask = PolygonMask.DefaultShape();

        var canvas = new RegionCanvas
        {
            Width = size,
            Height = size,
            Source = image,
            Regions = new System.Collections.ObjectModel.ObservableCollection<NamedRegion> { region },
            Zoom = 1,
            IsEditing = true,
            CellEditCommand = new DelegateCommand<CellEdit>(e =>
            {
                edits.Add(e);
                e.Cell.Rect = e.Rect;
                e.Cell.Angle = e.Angle;
                if (e.Mask is not null) e.Cell.Mask = e.Mask;
            })
        };

        canvas.Measure(new Size(size, size));
        canvas.Arrange(new Rect(0, 0, size, size));
        canvas.UpdateLayout();

        canvas.SelectedRegion = region;
        canvas.SelectedCell = cell;
        canvas.UpdateLayout();

        var item = canvas.CellItems.Single();

        Check("마스크 편집: 칸 항목이 마스크를 그린다(테두리·밖 어둡게)",
              item.DisplayMask.Count == 8 && item.MaskOutline is not null && item.MaskShade is not null,
              $"꼭짓점 {item.DisplayMask.Count} · 테두리 {item.MaskOutline is not null} · 어둡게 {item.MaskShade is not null}");

        // 어도너를 층 없이 만들어 손잡이 수를 본다 - 꼭짓점 8 + 변 가운데 8.
        var adorner = new RegionCellAdorner(item);
        adorner.Measure(new Size(200, 200));
        adorner.Arrange(new Rect(0, 0, 200, 200));

        Check("마스크 편집: 고른 칸 어도너에 꼭짓점·변 가운데 손잡이가 놓인다(8·8)",
              adorner.MaskEditor.VertexHandleCount == 8 && adorner.MaskEditor.MidpointHandleCount == 8,
              $"꼭짓점 {adorner.MaskEditor.VertexHandleCount} · 변 가운데 {adorner.MaskEditor.MidpointHandleCount}");

        // 눈으로 볼 그림 - 캔버스(마스크 테두리·밖 어둡게) 위에 어도너(손잡이)를 칸 자리에 겹쳐 찍는다.
        {
            var shot = new DrawingVisual();

            using (var dc = shot.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(60, 90, 130)), null, new Rect(0, 0, size, size));
                dc.DrawRectangle(new VisualBrush(canvas) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, size, size) }, null, new Rect(0, 0, size, size));
                dc.DrawRectangle(new VisualBrush(adorner) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(-20, -20, 240, 240) },
                                 null, new Rect(System.Windows.Controls.Canvas.GetLeft(item) - 20, System.Windows.Controls.Canvas.GetTop(item) - 20, 240, 240));
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(shot);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var path = Path.Combine(Path.GetTempPath(), "minguk-region-mask.png");
            using (var file = File.Create(path)) encoder.Save(file);

            Console.WriteLine($"[INFO] 마스크 편집 화면을 찍었다: {path}");
        }

        // 칸은 200x200(캔버스 400 의 절반) - 꼭짓점 0 을 칸 안 (20, 30) 으로. 칸 밖으로 끌면 가장자리에 멈춘다.
        canvas.BeginMaskDrag(item);
        canvas.MoveMaskVertex(item, 0, new Point(20, 30));
        canvas.MoveMaskVertex(item, 1, new Point(-50, 500));
        canvas.EndMaskDrag(item);

        var saved = edits.LastOrDefault();

        Check("마스크 편집: 꼭짓점을 옮기면 칸 기준 0~1 로 저장되고(0.1, 0.15), 칸 밖은 가장자리에 멈춘다(0, 1)",
              saved is { Completed: true, Mask: not null } && cell.Mask[0] == new Point(0.1, 0.15) && cell.Mask[1] == new Point(0, 1),
              $"0번 {cell.Mask[0]} · 1번 {cell.Mask[1]} · 마지막 올림 {(saved?.Completed == true ? "놓음" : "끄는 중")}");

        Check("마스크 편집: 꼭짓점만 고쳐도 칸 상자·각도는 그대로",
              cell.Rect == new Rect(0, 0, 1, 1) && Math.Abs(cell.Angle) < 0.01, $"{cell.Rect} · {cell.Angle}°");

        // 변 가운데를 끌면 그 자리에 꼭짓점이 는다.
        canvas.BeginMaskDrag(item);
        var added = canvas.InsertMaskVertex(item, 2);
        canvas.MoveMaskVertex(item, added, new Point(100, 100));
        canvas.EndMaskDrag(item);

        Check("마스크 편집: 변 가운데를 끌면 꼭짓점이 하나 늘어 그 자리로(9개, 3번이 가운데)",
              added == 3 && cell.Mask.Count == 9 && cell.Mask[3] == new Point(0.5, 0.5), $"넣은 번호 {added} · {cell.Mask.Count}개 · 3번 {(cell.Mask.Count > 3 ? cell.Mask[3] : default)}");

        // 오른쪽 버튼으로 빼기 - 셋은 남는다.
        var removed = 0;
        while (canvas.RemoveMaskVertex(item, 0)) removed++;

        Check("마스크 편집: 꼭짓점을 빼도 셋은 남는다", cell.Mask.Count == 3 && removed == 6, $"뺀 것 {removed} · 남은 것 {cell.Mask.Count}");
    }

    private const int IconLeft = 1200;
    private const int IconTop = 700;
    private const int IconSide = 80;

    /// <summary>메뉴 카드 화면 위에 둥근 아이콘 하나. 아이콘 둘레의 네모(80x80)는 <paramref name="noiseSeed"/> 마다 다른 잡티 - 아이콘 안쪽 무늬는 늘 같다.</summary>
    private static BitmapSource DrawIconScene(int noiseSeed)
    {
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(DrawCards(1920, 1080, 1.0), new Rect(0, 0, 1920, 1080));
            dc.DrawImage(DrawNoiseSeeded(IconSide, IconSide, noiseSeed), new Rect(IconLeft, IconTop, IconSide, IconSide));

            var center = new Point(IconLeft + (IconSide / 2.0), IconTop + (IconSide / 2.0));

            dc.PushClip(new EllipseGeometry(center, 36, 36));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 60, 140)), null, new Rect(IconLeft, IconTop, IconSide, IconSide));

            var random = new Random(5);
            for (var n = 0; n < 30; n++)
            {
                var brush = new SolidColorBrush(Color.FromRgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
                dc.DrawEllipse(brush, null, new Point(IconLeft + (random.NextDouble() * IconSide), IconTop + (random.NextDouble() * IconSide)), 5, 5);
            }

            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }

    private static BitmapSource DrawNoiseSeeded(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new byte[width * height * 4];

        random.NextBytes(pixels);

        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();

        return bitmap;
    }
}
