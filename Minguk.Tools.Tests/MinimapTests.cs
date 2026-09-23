using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Minimap;

namespace Minguk.Tools.Tests;

/// <summary>
/// 미니맵으로 방향 읽기 - 실제 게임 화면 다섯 장(<c>MinimapData/</c>)으로.
/// </summary>
/// <remarks>
/// 절대 방위의 정답은 모르지만 <b>장면 사이의 관계가 고정</b>이라 좋은 정답지다(2026-09-23 실측):
/// 던전 셋은 서로 0°(그중 하나는 <b>마우스만</b> 돌린 것 - 화살표가 몸 방향이라 안 돌아야 한다),
/// 걸은 뒤는 던전 대비 −56°, 필드는 던전 대비 −31°. 목표 마커는 걸으면서 105 → 118px 로 멀어진다.
/// 여기에 본보기를 알려진 각도로 돌려 넣는 합성 검사를 더해 재는 눈금 자체를 본다.
/// </remarks>
internal static partial class Program
{
    private static void TestMinimap()
    {
        var spec = new MinimapSpec();
        var folder = FindMinimapData();

        if (folder is null)
        {
            Fail("미니맵: 검사 그림", "MinimapData 폴더를 못 찾았다");
            return;
        }

        var field = ArrowOf(folder, "field.png", spec);
        var dungeonA = ArrowOf(folder, "dungeon-a.png", spec);
        var dungeonB = ArrowOf(folder, "dungeon-b.png", spec);
        var turned = ArrowOf(folder, "dungeon-b-turned.png", spec);
        var walked = ArrowOf(folder, "dungeon-b-walked.png", spec);

        Check("미니맵: 다섯 장 모두에서 화살표를 뽑는다(40~90px)",
              new[] { field, dungeonA, dungeonB, turned, walked }.All(a => a is not null && a.Mask.Count is >= 40 and <= 90),
              string.Join(" · ", new[] { ("필드", field), ("던전A", dungeonA), ("던전B", dungeonB), ("돌린뒤", turned), ("걸은뒤", walked) }
                  .Select(p => $"{p.Item1} {p.Item2?.Mask.Count.ToString() ?? "못 찾음"}px")));

        if (field is null || dungeonA is null || dungeonB is null || turned is null || walked is null) return;

        // 던전 세 장은 서로 같은 방향이어야 한다 - 그중 하나는 마우스만 돌린 것이라 0° 가 몸 방향임을 뒷받침한다.
        var betweenDungeons = MinimapReader.BestRotation(dungeonA.Mask, dungeonB.Mask);
        var afterTurning = MinimapReader.BestRotation(dungeonB.Mask, turned.Mask);

        Check("미니맵: 다른 던전 두 장이 같은 방향(0° ±3)",
              Math.Abs(MinimapReader.Difference(0, betweenDungeons)) <= 3,
              $"{betweenDungeons:0}도");

        Check("미니맵: 마우스만 돌리면 화살표는 안 돈다(0° ±3) - 몸 방향이다",
              Math.Abs(MinimapReader.Difference(0, afterTurning)) <= 3,
              $"{afterTurning:0}도");

        // 걸으면 돈다 - 실측 −56°.
        var afterWalking = MinimapReader.BestRotation(dungeonB.Mask, walked.Mask);

        Check("미니맵: 걸으면 화살표가 돈다(−56° ±3)",
              Math.Abs(MinimapReader.Difference(-56, afterWalking)) <= 3,
              $"{MinimapReader.Difference(0, afterWalking):0}도");

        var fieldVersusDungeon = MinimapReader.BestRotation(dungeonB.Mask, field.Mask);

        Check("미니맵: 필드와 던전의 차이(−31° ±4)",
              Math.Abs(MinimapReader.Difference(-31, fieldVersusDungeon)) <= 4,
              $"{MinimapReader.Difference(0, fieldVersusDungeon):0}도");

        // 모양만으로 머리 가리기 - 부챗살 거리합이 가장 무거운 쪽. 익힐 때 기준각을 정하는 데만 쓴다.
        // 뚜렷한 장면(여유 1.1배 이상)에서는 맞지만 아슬아슬한 장면은 못 믿는다 - 그래서 여유를 같이 준다.
        Check("미니맵: 머리가 뚜렷하면 모양만으로 가린다(필드 55·던전 90, ±10)",
              field.HeadMargin >= 1.1 && dungeonB.HeadMargin >= 1.1
              && Math.Abs(MinimapReader.Difference(55, field.HeadAngle)) <= 10
              && Math.Abs(MinimapReader.Difference(90, dungeonB.HeadAngle)) <= 10,
              $"필드 {field.HeadAngle:0}도(여유 {field.HeadMargin:0.00}배) · 던전 {dungeonB.HeadAngle:0}도(여유 {dungeonB.HeadMargin:0.00}배)");

        // 걸은 뒤 장면은 머리와 꼬리의 무게가 거의 같다(1.05배) - 판별이 20° 어긋난다. 그것을 여유로 미리 알려야 한다.
        Check("미니맵: 뚜렷하지 않으면 여유가 낮게 나온다(걸은 뒤 1.1 미만)",
              walked.HeadMargin < 1.1,
              $"걸은 뒤 {walked.HeadAngle:0}도 · 여유 {walked.HeadMargin:0.00}배 (실제 35도 - 못 믿는 장면)");

        // 뚜렷한 두 장 사이에서는 판별과 회전 대조가 대체로 맞아야 한다 - 판별 정밀도는 ±10° 쯤이다.
        var byHead = MinimapReader.Difference(dungeonB.HeadAngle, field.HeadAngle);

        Check("미니맵: 머리 판별과 회전 대조가 비슷한 값을 준다(10° 안)",
              Math.Abs(byHead - MinimapReader.Difference(0, fieldVersusDungeon)) <= 10,
              $"머리 {byHead:0}도 · 대조 {MinimapReader.Difference(0, fieldVersusDungeon):0}도");

        // 눈금 자체 - 본보기를 알려진 각도로 돌려 넣고 그 각이 나오는지.
        var offs = new[] { 7.0, 33.0, 90.0, 156.0, 271.0 };
        var worst = 0.0;

        foreach (var degree in offs)
        {
            var spun = Spin(dungeonB.Mask, degree);
            var measured = MinimapReader.BestRotation(dungeonB.Mask, spun);

            worst = Math.Max(worst, Math.Abs(MinimapReader.Difference(degree, measured)));
        }

        // 합성은 마스크를 돌려 만들므로 반올림으로 픽셀이 겹쳐 사라진다(모양이 성겨진다) - 실제 그림끼리는 1~2° 다.
        Check("미니맵: 돌려 넣은 각을 그대로 되읽는다(±4)", worst <= 4, $"가장 큰 어긋남 {worst:0.0}도");

        // 목표 마커 - 걸으면 남서쪽 목표에서 멀어진다(105 → 118px).
        var beforeWalk = MarkersOf(folder, "dungeon-b-turned.png", spec, turned);
        var afterWalk = MarkersOf(folder, "dungeon-b-walked.png", spec, walked);

        Check("미니맵: 목표 마커의 방위와 거리(237°·105px → 234°·118px, ±4°·±6px)",
              beforeWalk.Count > 0 && afterWalk.Count > 0
              && Math.Abs(MinimapReader.Difference(237, beforeWalk[0].Bearing)) <= 4
              && Math.Abs(beforeWalk[0].Distance - 105) <= 6
              && Math.Abs(MinimapReader.Difference(234, afterWalk[0].Bearing)) <= 4
              && Math.Abs(afterWalk[0].Distance - 118) <= 6,
              beforeWalk.Count == 0 || afterWalk.Count == 0
                  ? $"마커 {beforeWalk.Count}개 · {afterWalk.Count}개"
                  : $"{beforeWalk[0].Bearing:0}도 {beforeWalk[0].Distance:0}px → {afterWalk[0].Bearing:0}도 {afterWalk[0].Distance:0}px");

        // 같은 자리에서 두 개가 보이던 장면 - 가까운 것부터 준다.
        var two = MarkersOf(folder, "dungeon-a.png", spec, dungeonA);

        Check("미니맵: 마커 둘을 가까운 순서로(88°·32px, 90°·65px)",
              two.Count >= 2 && two[0].Distance < two[1].Distance && Math.Abs(two[0].Distance - 32) <= 6,
              string.Join(" · ", two.Take(3).Select(m => $"{m.Bearing:0}도 {m.Distance:0}px {m.Pixels}px")));

        // 사용자가 실제로 익힌 영역(오버워치/미니맵, 2026-09-23) - 미니맵에 게임 시계(☀)가 겹쳐 있다.
        var live = ArrowOf(folder, "dungeon-clock.png", spec);

        Check("미니맵: 사람이 그린 실제 영역에서도 화살표가 한가운데로 잡힌다",
              live is not null && Math.Abs(live.CenterX - 124) <= 3 && Math.Abs(live.CenterY - 82) <= 3
              && live.Mask.Count is >= 40 and <= 90 && live.HeadMargin >= 1.1,
              live is null ? "못 찾음" : $"{live.Mask.Count}px · 중심({live.CenterX:0},{live.CenterY:0}) · 머리 {live.HeadAngle:0}도 · 여유 {live.HeadMargin:0.00}배");

        if (live is not null)
        {
            // 시계 옆 해 아이콘은 목표 마커와 색이 거의 같다(평균 RGB 222,181,64 대 232,182,81) - 색으로는 못 거른다.
            var withClock = MarkersOf(folder, "dungeon-clock.png", spec, live);
            var masked = new MinimapSpec { Ignore = [[0.69, 0.87, 0.14, 0.13]] };
            var withoutClock = MarkersOf(folder, "dungeon-clock.png", masked, live);

            Check("미니맵: 목표가 없는데 시계(☀)가 마커로 잡힌다 - 그래서 ignore 가 필요하다",
                  withClock.Count == 1,
                  string.Join(" · ", withClock.Select(m => $"{m.Bearing:0}도 {m.Distance:0}px {m.Pixels}px")));

            Check("미니맵: ignore 구역이 그 시계를 목표에서 뺀다",
                  withoutClock.Count == 0,
                  $"남은 마커 {withoutClock.Count}개");
        }

        // 빠른가 - 조준 스레드 옆에서 프레임마다 돌 수 있어야 한다.
        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 50; i++) MinimapReader.BestRotation(dungeonB.Mask, walked.Mask);

        watch.Stop();

        Check("미니맵: 한 번 재는 데 2ms 안", watch.Elapsed.TotalMilliseconds / 50 <= 2,
              $"{watch.Elapsed.TotalMilliseconds / 50:0.00}ms");
    }

    /// <summary>마스크를 그만큼 돌린다 - 눈금 검사용.</summary>
    private static (short X, short Y)[] Spin(System.Collections.Generic.IReadOnlyList<(short X, short Y)> mask, double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        return mask
            .Select(p => ((short)Math.Round((p.X * cos) - (p.Y * sin)), (short)Math.Round((p.X * sin) + (p.Y * cos))))
            .Distinct()
            .ToArray();
    }

    private static MinimapArrow? ArrowOf(string folder, string name, MinimapSpec spec)
    {
        var (pixels, width, height) = LoadBgra(Path.Combine(folder, name));

        return MinimapReader.FindArrow(pixels, width, height, spec);
    }

    private static System.Collections.Generic.IReadOnlyList<MinimapMarker> MarkersOf(string folder, string name, MinimapSpec spec, MinimapArrow arrow)
    {
        var (pixels, width, height) = LoadBgra(Path.Combine(folder, name));

        return MinimapReader.Markers(pixels, width, height, spec, arrow.CenterX, arrow.CenterY);
    }

    private static (byte[] Pixels, int Width, int Height) LoadBgra(string path)
    {
        var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource source = frame.Format == PixelFormats.Bgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var pixels = new byte[width * height * 4];

        source.CopyPixels(pixels, width * 4, 0);

        return (pixels, width, height);
    }

    /// <summary>검사 그림은 소스 폴더에 둔다 - 출력으로 복사하지 않아 빌드가 가볍다.</summary>
    private static string? FindMinimapData()
    {
        var here = AppContext.BaseDirectory;

        for (var i = 0; i < 8 && here is not null; i++)
        {
            var candidate = Path.Combine(here, "MinimapData");

            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.png").Length > 0) return candidate;

            candidate = Path.Combine(here, "Minguk.Tools.Tests", "MinimapData");

            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.png").Length > 0) return candidate;

            here = Path.GetDirectoryName(here.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }
}
