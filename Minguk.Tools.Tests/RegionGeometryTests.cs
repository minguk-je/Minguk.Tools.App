using System.Windows;

using Minguk.Tools.Markup.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 미리보기 캔버스 위에서 자리를 놓고 끄는 계산(<see cref="RegionGeometry"/>). 마우스 없이 숫자로 본다.
/// </summary>
/// <remarks>
/// 0~1 ↔ 캔버스 픽셀 환산이 어긋나면 손잡이로 고친 자리가 저장할 때 다른 곳으로 간다 - 화면에는 그럴싸하게 보여
/// 스크립트가 엉뚱한 글을 읽고 나서야 안다.
/// </remarks>
internal static partial class Program
{
    /// <summary>0.1 × 800 같은 셈은 마지막 자리가 흔들린다. 픽셀 셈은 이 정도면 같은 것이다.</summary>
    private static bool NearPx(double a, double b) => System.Math.Abs(a - b) < 1e-6;

    private static void TestRegionGeometry()
    {
        // 컨트롤 800x600 에 16:9 그림 - 위아래에 띠가 남고 그림은 800x450 이 (0,75) 에 놓인다.
        var area = RegionGeometry.ImageArea(new Size(800, 600), new Size(1920, 1080));

        Check("그림이 놓인 자리 - 가로를 채우고 세로 가운데",
              NearPx(area.X, 0) && NearPx(area.Y, 75) && NearPx(area.Width, 800) && NearPx(area.Height, 450),
              area.ToString());

        var ratio = new Rect(0.5, 0.2, 0.1, 0.1);
        var canvas = RegionGeometry.ToCanvas(ratio, area);

        // 세로는 띠(75) 만큼 내려앉는다: 75 + 0.2 × 450 = 165.
        Check("0~1 → 캔버스", NearPx(canvas.X, 400) && NearPx(canvas.Y, 165) && NearPx(canvas.Width, 80) && NearPx(canvas.Height, 45), canvas.ToString());

        var back = RegionGeometry.ToRatio(canvas, area);

        Check("캔버스 → 0~1 이 되돌아온다", NearPx(back.X, 0.5) && NearPx(back.Y, 0.2) && NearPx(back.Width, 0.1) && NearPx(back.Height, 0.1), back.ToString());

        var minimum = new Size(3.2, 1.8);

        var wider = RegionGeometry.Resize(canvas, HorizontalAlignment.Right, VerticalAlignment.Stretch, 40, 99, minimum, area);

        Check("오른쪽 변만 끈다 - 세로는 그대로", NearPx(wider.X, 400) && NearPx(wider.Width, 120) && NearPx(wider.Y, 165) && NearPx(wider.Height, 45), wider.ToString());

        var fromTop = RegionGeometry.Resize(canvas, HorizontalAlignment.Left, VerticalAlignment.Top, -10, -20, minimum, area);

        Check("왼쪽 위 모서리 - 반대편은 그대로", NearPx(fromTop.Right, 480) && NearPx(fromTop.Bottom, 210) && NearPx(fromTop.X, 390) && NearPx(fromTop.Y, 145), fromTop.ToString());

        var collapsed = RegionGeometry.Resize(canvas, HorizontalAlignment.Right, VerticalAlignment.Stretch, -500, 0, minimum, area);

        Check("반대편을 넘겨 끌면 최소 크기에서 멈춘다(뒤집히지 않는다)", NearPx(collapsed.X, 400) && NearPx(collapsed.Width, minimum.Width), collapsed.ToString());

        var outside = RegionGeometry.Resize(canvas, HorizontalAlignment.Right, VerticalAlignment.Bottom, 9999, 9999, minimum, area);

        Check("그림 밖으로는 안 늘어난다", NearPx(outside.Right, area.Right) && NearPx(outside.Bottom, area.Bottom), outside.ToString());

        var moved = RegionGeometry.Move(canvas, 30, -10, area);

        Check("옮기기 - 크기는 그대로", NearPx(moved.X, 430) && NearPx(moved.Y, 155) && NearPx(moved.Width, 80) && NearPx(moved.Height, 45), moved.ToString());

        var pinned = RegionGeometry.Move(canvas, -9999, 9999, area);

        Check("가장자리에서 멈춘다 - 잘리지 않는다", NearPx(pinned.X, area.Left) && NearPx(pinned.Bottom, area.Bottom) && NearPx(pinned.Width, 80), pinned.ToString());

        Check("그림이 없으면 자리가 없다", RegionGeometry.ImageArea(new Size(800, 600), Size.Empty).IsEmpty, "");

        TestCellGeometry();
    }

    /// <summary>자리 안의 칸 - 자리 기준 0~1 ↔ 캔버스, 돌린 칸 늘리기(반대편 변은 제자리), 회전 손잡이 각도.</summary>
    private static void TestCellGeometry()
    {
        var region = new Rect(100, 50, 200, 100);
        var cell = RegionGeometry.CellToCanvas(new Rect(0.5, 0.25, 0.25, 0.5), region);

        Check("칸 0~1 → 캔버스(자리 안)", NearPx(cell.X, 200) && NearPx(cell.Y, 75) && NearPx(cell.Width, 50) && NearPx(cell.Height, 50), cell.ToString());

        var back = RegionGeometry.CellToRatio(cell, region);
        Check("칸 캔버스 → 0~1 이 되돌아온다", NearPx(back.X, 0.5) && NearPx(back.Y, 0.25) && NearPx(back.Width, 0.25) && NearPx(back.Height, 0.5), back.ToString());

        var outside = RegionGeometry.CellToRatio(new Rect(250, 100, 100, 100), region);
        Check("자리 밖으로 나간 칸은 자리 안으로 접는다", NearPx(outside.Right, 1) && NearPx(outside.Bottom, 1), outside.ToString());

        var minimum = new Size(4, 4);

        // 돌리지 않은 칸 - 오른쪽 변을 끌면 너비만 는다.
        var flat = RegionGeometry.ResizeRotated(new Rect(0, 0, 40, 10), 0, HorizontalAlignment.Right, VerticalAlignment.Stretch, 6, 3, minimum);
        Check("각도 0 이면 늘리기는 축 그대로", NearPx(flat.X, 0) && NearPx(flat.Width, 46) && NearPx(flat.Height, 10), flat.ToString());

        // 90도 돈 칸의 '오른쪽' 변은 화면 아래를 향한다 - 화면에서 아래로 10 끌면 너비가 10 늘고, 가운데는 아래로 5 간다(왼쪽 변은 제자리).
        var turned = RegionGeometry.ResizeRotated(new Rect(0, 0, 40, 10), 90, HorizontalAlignment.Right, VerticalAlignment.Stretch, 0, 10, minimum);
        var center = new Point(turned.X + (turned.Width / 2), turned.Y + (turned.Height / 2));
        Check("돌린 칸은 돌린 방향으로 늘고 반대편 변은 제자리", NearPx(turned.Width, 50) && NearPx(turned.Height, 10) && NearPx(center.X, 20) && NearPx(center.Y, 10),
              $"{turned} 가운데 {center}");

        var crushed = RegionGeometry.ResizeRotated(new Rect(0, 0, 40, 10), 30, HorizontalAlignment.Left, VerticalAlignment.Stretch, 999, 0, minimum);
        Check("돌린 칸도 최소 크기에서 멈춘다", NearPx(crushed.Width, minimum.Width), crushed.ToString());

        var quarter = RegionGeometry.Rotate(0, new Point(0, 0), new Point(0, -10), new Point(10, 0));
        var back90 = RegionGeometry.Rotate(350, new Point(0, 0), new Point(0, -10), new Point(-10, 0));
        Check("회전 손잡이 - 위에서 오른쪽으로 끌면 +90, 각도는 0~360 안", NearPx(quarter, 90) && NearPx(back90, 260), $"{quarter} / {back90}");
    }
}
