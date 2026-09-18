using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Minguk.Tools.Markup.Regions;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 영역 어도너 모양 - 확대해도 이름표·치수가 화면에서 같은 크기인지.
/// </summary>
/// <remarks>
/// <b>왜 이 검사가 있나</b>(2026-09-18, 사용자 "전체 어도너가 커져서") - 템플릿 안에 <c>&lt;ScaleTransform ScaleX="{Binding InverseZoom}" /&gt;</c> 로 적으면
/// 그 바인딩은 <b>조용히 죽는다</b>. ScaleTransform 은 Freezable 이라 DataContext 를 물려받을 길이 없다. 빌드는 통과하고 화면만 틀어진다 -
/// 로그에 "Cannot find governing FrameworkElement ... target element is 'ScaleTransform'" 이 쌓이는 것이 유일한 자취였다.
/// 여기서는 템플릿을 실제로 씌워 <b>붙은 Transform 의 배율이 InverseZoom 인지</b>를 본다.
/// </remarks>
internal static partial class Program
{
    private static void TestRegionChrome()
    {
        // 확대 2배 - 역수 0.5 면 이름표는 절반 크기로 그려져야 화면에서 같다.
        const double inverseZoom = 0.5;

        var region = new NamedRegion { Name = "탄약", X = 0.1, Y = 0.1, Width = 0.2, Height = 0.1 };

        region.Cells.Add(new RegionCell { Name = "현재", X = 0, Y = 0, Width = 1, Height = 1 });

        var item = new RegionItem { Region = region, InverseZoom = inverseZoom, Width = 200, Height = 100 };
        var cell = new RegionCellItem { Region = region, Cell = region.Cells[0], InverseZoom = inverseZoom, Width = 100, Height = 50 };

        foreach (var (control, what) in new (FrameworkElement, string)[] { (item, "자리"), (cell, "칸") })
        {
            // 화면 밖에서 템플릿을 실제로 씌운다 - 씌우지 않으면 템플릿 안은 만들어지지도 않는다.
            var host = new Border { Width = 400, Height = 300, Child = control };

            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));
            host.UpdateLayout();

            var labels = Descendants(control).OfType<Border>().Where(b => b.Child is TextBlock).ToList();
            var scaled = labels.Where(b => Scale(b.LayoutTransform) is { } s && Math.Abs(s - inverseZoom) < 0.001).ToList();

            Check($"어도너 {what} 이름표는 배율 역수만큼 되돌린다(확대해도 화면에서 같은 크기)",
                  labels.Count > 0 && scaled.Count == labels.Count,
                  labels.Count == 0 ? "이름표를 못 찾음" : $"이름표 {labels.Count}개 · 배율 {string.Join(", ", labels.Select(b => Scale(b.LayoutTransform)?.ToString("0.##") ?? "없음"))}");
        }
    }

    /// <summary>가로·세로가 같은 배율이면 그 값. 아니면 null.</summary>
    private static double? Scale(Transform? transform)
        => transform switch
        {
            ScaleTransform s when Math.Abs(s.ScaleX - s.ScaleY) < 0.001 => s.ScaleX,
            TransformGroup g => g.Children.OfType<ScaleTransform>().Select(s => (double?)s.ScaleX).FirstOrDefault(),
            _ => null
        };

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            yield return child;

            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
