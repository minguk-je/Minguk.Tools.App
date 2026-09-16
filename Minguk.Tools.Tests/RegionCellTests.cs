using System;
using System.IO;
using System.Linq;
using System.Windows;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 자리 안의 칸(<see cref="RegionCell"/>). 저장·옛 파일·이름 찾기·읽을 사각형·읽은 것 잇기.
/// </summary>
/// <remarks>
/// 칸은 자리 기준 0~1 이다. 이 환산이 어긋나면 화면에 그린 칸과 읽는 곳이 달라져 스크립트가 엉뚱한 숫자를 받는다.
/// </remarks>
internal static partial class Program
{
    private static void TestRegionCells()
    {
        var folder = Path.Combine(Path.GetTempPath(), "minguk-cells-" + Guid.NewGuid().ToString("N"));

        try
        {
            var ammo = new NamedRegion { Name = "탄약", Rect = new Rect(0.9, 0.8, 0.1, 0.05) };
            ammo.Cells.Clear();
            ammo.Cells.Add(new RegionCell { Name = "현재", Rect = new Rect(0, 0, 0.4, 1) });
            ammo.Cells.Add(new RegionCell { Name = "최대", Rect = new Rect(0.6, 0, 0.4, 1) });

            var book = new RegionBook(folder);
            book.Put(ammo);
            book.Put(new NamedRegion { Name = "체력", Rect = new Rect(0.1, 0.8, 0.1, 0.05) });
            book.Save();

            var read = RegionBook.Load(folder);
            var back = read.Find("탄약");

            Check("칸 둘을 저장했다 되읽는다", back is not null && back.Cells.Count == 2 && back.Cells[1].Name == "최대" && Near(back.Cells[1].X, 0.6),
                  back is null ? "없음" : string.Join(" · ", back.Cells.Select(c => $"{c.Name} {c.Rect}")));

            var health = read.Find("체력");
            Check("칸 없이 넣은 자리에는 칸1 이 붙는다", health is not null && health.Cells.Count == 1 && health.Cells[0].Name == NamedRegion.DefaultCellName
                  && Near(health.Cells[0].Width, 1) && Near(health.Cells[0].Height, 1), health?.Cells.Count.ToString() ?? "없음");

            // 칸을 적기 전의 파일 - 읽을 때 칸1 을 붙여 지금과 똑같이 읽힌다.
            File.WriteAllText(Path.Combine(folder, RegionBook.FileName),
                """[{ "name": "옛자리", "x": 0.1, "y": 0.2, "width": 0.3, "height": 0.04, "live": false, "note": "" }]""");

            var legacy = RegionBook.Load(folder).Find("옛자리");
            Check("칸이 없던 옛 파일은 칸1 하나", legacy is not null && legacy.Cells.Count == 1 && Near(legacy.Cells[0].X, 0) && Near(legacy.Cells[0].Width, 1),
                  legacy?.Cells.Count.ToString() ?? "없음");

            Check("자리.칸 으로 찾는다(대소문자 무시)", read.Resolve("탄약.현재") is { Region.Name: "탄약", Cell.Name: "현재" }
                  && read.Resolve(" 탄약 . 최대 ") is { Cell.Name: "최대" }, "");
            Check("자리만 부르면 칸은 없다", read.Resolve("탄약") is { Region.Name: "탄약", Cell: null }, "");
            Check("없는 자리·없는 칸은 못 찾는다", read.Resolve("없는자리") is null && read.Resolve("탄약.없는칸") is null, "");

            Check("이름에 점은 못 쓴다", !RegionBook.IsValidName("탄약.현재") && !RegionBook.IsValidName("  ") && RegionBook.IsValidName("탄약"), "");

            // 자리 (0.9, 0.8) 0.1×0.05 안의 칸 (0.6, 0) 0.4×1 → 화면 (0.96, 0.8) 0.04×0.05
            var max = back!.CellRect(back.Cells[1]);
            Check("칸의 화면 사각형", Near(max.X, 0.96) && Near(max.Y, 0.8) && Near(max.Width, 0.04) && Near(max.Height, 0.05), max.ToString());

            var all = RegionTargets.Of(back, null);
            var one = RegionTargets.Of(back, back.Cells[1]);
            Check("자리를 부르면 칸 순서대로, 칸을 부르면 그 칸만",
                  all.Count == 2 && all[0].Cell.Name == "현재" && Near(all[1].Box.X, 0.96) && one.Count == 1 && one[0].Cell.Name == "최대",
                  string.Join(" · ", all.Select(t => $"{t.Cell.Name} {t.Box}")));

            var joined = RegionTargets.Combine(["17", "", "24"]);
            Check("칸들 읽은 것 잇기 - 글은 띄어쓰기, 숫자는 차례로", joined.Text == "17 24" && joined.Numbers.SequenceEqual([17, 24]),
                  $"「{joined.Text}」 [{string.Join(", ", joined.Numbers)}]");

            var glued = RegionTargets.Combine(["30140", "HP 5 / 9"]);
            Check("한 칸 안의 숫자 덩어리는 숫자 아닌 글자에서 끊는다", glued.Numbers.SequenceEqual([30140, 5, 9]), string.Join(", ", glued.Numbers));

            TestRotatedCell(folder);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>돌린 칸 - 각도 저장, 감싸는 상자(픽셀 공간에서 돌린다), 역회전으로 똑바로 세우기.</summary>
    private static void TestRotatedCell(string folder)
    {
        // 100x100 화면을 통째로 덮는 자리, 그 가운데 40x6 픽셀 칸을 90도 돌린다 - 세로 막대 모양이 된다.
        var region = new NamedRegion { Name = "기울기", Rect = new Rect(0, 0, 1, 1) };
        region.Cells.Clear();
        region.Cells.Add(new RegionCell { Name = "세로", Rect = new Rect(0.3, 0.47, 0.4, 0.06), Angle = 90 });

        var book = new RegionBook(folder);
        book.Put(region);
        book.Save();

        var back = RegionBook.Load(folder).Find("기울기")!.Cells[0];
        Check("칸 각도를 저장했다 되읽는다", Near(back.Angle, 90), back.Angle.ToString("0.##"));

        var target = RegionTargets.Of(region, null)[0];
        var bounds = RegionTargets.Bounds(target, 100, 100);
        Check("90도 돌린 칸을 감싸는 상자는 세로로 길다", Near(bounds.X, 0.47) && Near(bounds.Y, 0.3) && Near(bounds.Width, 0.06) && Near(bounds.Height, 0.4), bounds.ToString());

        // 가로세로가 다른 화면(200x100)에서도 픽셀로 돌려야 찌그러지지 않는다: 칸 80x6 픽셀 → 감싸는 상자 6x80 픽셀 = 0.03 x 0.8
        var wide = RegionTargets.Bounds(target, 200, 100);
        Check("넓은 화면에서도 픽셀 공간에서 돈다", Near(wide.Width, 0.03) && Near(wide.Height, 0.8), wide.ToString());

        // 100x100 어두운 화면에 세로 흰 막대(열 48~52, 줄 30~70). 감싸는 상자를 잘라 세우면 40x6 가로 흰 띠가 된다.
        var frame = new byte[100 * 100 * 4];

        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 100; x++)
            {
                var o = ((y * 100) + x) * 4;
                var white = x is >= 48 and <= 52 && y is >= 30 and < 70;
                frame[o] = frame[o + 1] = frame[o + 2] = white ? (byte)240 : (byte)20;
                frame[o + 3] = 255;
            }

        var left = (int)Math.Floor(bounds.X * 100);
        var top = (int)Math.Floor(bounds.Y * 100);
        var right = (int)Math.Ceiling(bounds.Right * 100);
        var bottom = (int)Math.Ceiling(bounds.Bottom * 100);
        var full = System.Windows.Media.Imaging.BitmapSource.Create(100, 100, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, frame, 400);
        var crop = new System.Windows.Media.Imaging.CroppedBitmap(full, new Int32Rect(left, top, right - left, bottom - top));
        crop.Freeze();

        var upright = RegionTargets.Upright(crop, bounds, target, 100, 100);
        var pixels = Minguk.Tools.Vision.Ocr.Paddle.BgraImage.From(upright);
        var middle = Enumerable.Range(2, upright.PixelWidth - 4).Count(x => pixels.Pixels[((3 * upright.PixelWidth) + x) * 4] > 200);

        Check("돌린 칸을 똑바로 세우면 가로 띠가 된다", upright.PixelWidth == 40 && upright.PixelHeight == 6 && middle >= upright.PixelWidth - 6,
              $"{upright.PixelWidth}x{upright.PixelHeight} 가운데 줄 흰 칸 {middle}");

        var flat = RegionTargets.Of(new NamedRegion { Name = "평평", Rect = new Rect(0.1, 0.1, 0.2, 0.2) }, null)[0];
        Check("각도 0 이면 감싸는 상자는 칸 그대로", RegionTargets.Bounds(flat, 100, 100) == flat.Box, flat.Box.ToString());
    }
}
