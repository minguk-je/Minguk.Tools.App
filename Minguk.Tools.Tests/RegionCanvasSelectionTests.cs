using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using DevExpress.Mvvm;

using Minguk.Tools.Markup.Regions;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 자리를 클릭하면 자리가, 다시 클릭해야 그 안의 칸이 골라지는지 - 실제 마우스(커서)·창 없이, 라우티드 이벤트로 직접 눌러 본다.
/// </summary>
/// <remarks>
/// <b>왜</b>(사용자, 2026-09-18 "마우스 클릭하니까 처음부터 구역이 선택되네" · "처음 클릭하면 영역이 선택되고 또 클릭하면 구역이 선택되어야") -
/// 새 자리는 자리와 같은 크기의 「전체」 칸으로 시작한다(<see cref="NamedRegion.Cells"/>). 칸은 자리 위 형제(z 순서가 위)라 그냥 두면
/// 자리 어디를 눌러도 늘 칸부터 잡혀, 자리를 고르거나 손잡이로 옮길 수가 없었다. 자리가 이미 고른 것이 아니면 칸은 히트 테스트에서 빠진다
/// (<see cref="RegionCellItem.IsHitTestVisible"/> - <c>RegionCanvas.PlaceCells</c>) - 그래서 첫 클릭은 자리로 간다.
///
/// <b>뚫기는 손잡이를 실제로 눌렀다 떼야 난다</b>(사용자, 2026-09-18 "선택 후에 클릭하면 바로 구역이 선택되는데 - MouseUp 을 또 클릭인지
/// 마우스 드래그인지로"). <see cref="RegionCanvas.Press"/>(터널 단계)는 후보만 적어 두고, 실제로 뚫는 것은 옮기기 손잡이(<see cref="RegionMoveThumb"/>)의
/// <c>DragCompleted</c> → <see cref="RegionCanvas.EndDrag"/> 다 - 끄는 동안 안 움직였을 때만. 그래서 여기서는 손잡이를 찾아 그 위에서 직접
/// 눌렀다 떼서(<see cref="Click"/>) 진짜 클릭과 같은 경로(Press → BeginDrag → EndDrag)를 태운다.
///
/// <b>어떻게 재는가</b> - <c>--canvas-drag</c>·<c>--region-drag</c> 처럼 실제 커서나 진짜 창을 쓰지 않는다(<see cref="UIElement.InputHitTest"/> 는
/// 화면에 붙은(<c>PresentationSource</c>) 요소가 아니면 늘 null 이라 못 쓴다). 대신 실제로 어느 쪽이 클릭을 받을지는 <b>테스트가 직접 판단</b>한다
/// (칸이 <c>IsHitTestVisible</c> 이면 칸, 아니면 자리 - 실제 WPF z-순서 히트 테스트와 같은 규칙) - 그렇게 고른 항목 안의 옮기기 손잡이에
/// <c>MouseLeftButtonDown</c>/<c>Up</c> 라우티드 이벤트를 <c>RaiseEvent</c> 로 직접 태운다. 라우팅은 시각 트리만 따라가므로(창 연결이 필요 없다)
/// <see cref="RegionItemBase.OnPreviewMouseLeftButtonDown"/> 도 Thumb 의 자체 처리도 실제 클릭과 똑같이 탄다.
/// </remarks>
internal static partial class Program
{
    private static void TestRegionCanvasSelection()
    {
        const int size = 400;

        var image = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);

        var region = new NamedRegion { Name = "영역1", X = 0.2, Y = 0.2, Width = 0.4, Height = 0.4 };
        var region2 = new NamedRegion { Name = "영역2", X = 0.6, Y = 0.6, Width = 0.3, Height = 0.3 };
        var regions = new System.Collections.ObjectModel.ObservableCollection<NamedRegion> { region, region2 };

        var canvas = new RegionCanvas
        {
            Width = size,
            Height = size,
            Source = image,
            Regions = regions,
            Zoom = 1,
            IsEditing = true,
            EditCommand = new DelegateCommand<RegionEdit>(e => e.Region.Rect = e.Rect),
            CellEditCommand = new DelegateCommand<CellEdit>(e => { e.Cell.Rect = e.Rect; e.Cell.Angle = e.Angle; })
        };

        canvas.Measure(new Size(size, size));
        canvas.Arrange(new Rect(0, 0, size, size));
        canvas.UpdateLayout();

        var regionItem = canvas.Items.Single(i => ReferenceEquals(i.Region, region));
        var cellItem = canvas.CellItems.Single(c => ReferenceEquals(c.Region, region));

        // ① 아무것도 안 골랐을 때 - 칸(형제, z 순서가 위)이 히트 테스트에서 빠져 있어야 클릭이 자리로 간다.
        Check("클릭 전에는 칸이 히트 테스트에서 빠져 있다(자리가 대신 받는다)", !cellItem.IsHitTestVisible, $"칸 IsHitTestVisible={cellItem.IsHitTestVisible}");

        Click(regionItem, cellItem);
        Check("첫 클릭 - 자리가 골라진다(칸은 아직)", ReferenceEquals(canvas.SelectedRegion, region) && canvas.SelectedCell is null,
              $"자리 {(ReferenceEquals(canvas.SelectedRegion, region) ? "골라짐" : "null")} · 칸 {(canvas.SelectedCell?.Name ?? "null")}");

        // ② 자리를 고른 뒤 - 아직 "뚫지" 않았으니 칸은 여전히 히트 테스트에서 빠져 있어야 한다(두 번째 클릭도 자리로).
        Check("첫 클릭 뒤에도 칸은 아직 히트 테스트에서 빠져 있다(두 번째 클릭도 자리로)", !cellItem.IsHitTestVisible, $"칸 IsHitTestVisible={cellItem.IsHitTestVisible}");

        // ③ 이미 고른 자리를 또 누르면(끌지 않고 그대로 뗀다) "뚫린다" - 그 뒤로는 칸이 히트 테스트를 받는다.
        //    (2026-09-18 고침 - 누르는 순간(Press)이 아니라 뗄 때(EndDrag, 안 끌었으면)라 여기서도 손잡이를 실제로 눌렀다 뗀다.)
        Click(regionItem, cellItem);
        Check("이미 고른 자리를 또 누르면(끌지 않고) 뚫려 칸이 히트 테스트를 받는다", cellItem.IsHitTestVisible, $"칸 IsHitTestVisible={cellItem.IsHitTestVisible}");

        Click(regionItem, cellItem);
        Check("뚫린 뒤 세 번째 클릭 - 이제 칸(전체)이 골라진다", ReferenceEquals(canvas.SelectedRegion, region) && canvas.SelectedCell is not null,
              $"자리 {(ReferenceEquals(canvas.SelectedRegion, region) ? "골라짐" : "null")} · 칸 {(canvas.SelectedCell?.Name ?? "null")}");

        // ④ 다른 자리를 새로 고르면(목록에서 고른 것처럼, 클릭이 아니어도) 뚫린 상태가 풀려야 한다 - 그 자리를 눌러도 처음부터(자리 먼저).
        var region2Item = canvas.Items.Single(i => ReferenceEquals(i.Region, region2));
        var region2CellItem = canvas.CellItems.Single(c => ReferenceEquals(c.Region, region2));

        // 목록에서 다른 자리를 고르는 것과 같다 - VM 의 SelectedRegionNode 세터도 자리를 고르기 전에 SelectedCell 을 먼저 비운다.
        canvas.SelectedCell = null;
        canvas.SelectedRegion = region2;

        Check("다른 자리를 고르면(목록 등) 뚫림이 풀린다 - 그 자리의 칸도 아직 히트 테스트를 안 받는다",
              !region2CellItem.IsHitTestVisible, $"칸 IsHitTestVisible={region2CellItem.IsHitTestVisible}");

        // 목록으로 이미 고른 자리를 캔버스에서 누르면(파워포인트가 이미 고른 그룹을 다시 누르면 안의 것으로 들어가는 것과 같다) - 클릭이 처음이라도(끌지 않으면) 바로 뚫린다.
        // "몇 번째 클릭인가" 가 아니라 "이 자리가 이미 고른 것인가" 로 가르기 때문이다 - 자리는 어느 길로 골랐든 같다.
        Click(region2Item, region2CellItem);
        Check("목록으로 고른 자리를 캔버스에서 누르면 - 이미 고른 자리라 곧바로 뚫린다",
              region2CellItem.IsHitTestVisible, $"칸 IsHitTestVisible={region2CellItem.IsHitTestVisible}");

        // ⑤ 옮기려고 누른 채 끄는 것은 "또 클릭" 이 아니다 - 끄는 동안·끈 뒤에도 칸이 뚫리면 안 된다(이번에 고친 것 - 예전에는 누르는
        //    순간 바로 뚫려, 옮기려던 자리가 옮기던 중에 칸으로 바뀌었다). 이건 여기서 못 잰다 - RegionCanvas.DragMove 가 Thumb 변위가
        //    아니라 Mouse.GetPosition(this) 로 "지금 마우스 자리" 를 직접 읽어(끌기 시작점부터의 실제 이동량), 창 연결이 없는 여기서는
        //    커서를 옮겨도 늘 같은 값이라 "안 움직인 끌기" 가 돼 버린다(_dragMoved 가 안 켜진다) - 진짜 커서가 있어야 재는 값이다.
        //    이 갈림(끌었나 안 끌었나)은 실제 커서로 손잡이를 끄는 --region-drag 가 잰다.
    }

    /// <summary>진짜 클릭이라면 무엇이 받을지 - 칸이 히트 테스트에 있으면 칸(z 순서가 위), 아니면 자리.</summary>
    private static RegionItemBase RealTarget(RegionItem region, RegionCellItem cell) => cell.IsHitTestVisible ? cell : region;

    /// <summary>
    /// 진짜 클릭(끌지 않고 눌렀다 뗀다) - 항목 안의 옮기기 손잡이(<see cref="RegionMoveThumb"/>)를 찾아 누르고(Press → BeginDrag) 안 끌고 뗀다(EndDrag).
    /// </summary>
    /// <remarks>
    /// Thumb 자신의 <c>OnMouseLeftButtonDown</c> 은 <c>CaptureMouse()</c> 가 돼야 <c>DragStarted</c> 를 내는데, 캡처는 창 연결
    /// (<c>PresentationSource</c>)이 있어야 확실하다 - 여기서 재는 것은 <b>WPF Thumb 이 캡처를 잘하는지</b>가 아니라 <b>손잡이 이벤트를 받은
    /// <see cref="RegionCanvas"/> 가 뚫기를 옳게 정하는지</b>이므로, Thumb 의 DragStarted/DragCompleted 라우티드 이벤트를(Thumb 자신이
    /// 태우는 것과 똑같이) 직접 <c>RaiseEvent</c> 로 태운다.
    /// </remarks>
    private static void Click(RegionItem region, RegionCellItem cell)
    {
        var target = RealTarget(region, cell);
        var thumb = Descendants(target).OfType<RegionMoveThumb>().Single();

        Raise(target, UIElement.PreviewMouseLeftButtonDownEvent);

        thumb.RaiseEvent(new DragStartedEventArgs(0, 0));
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false));
    }

    private static void Raise(UIElement target, RoutedEvent routedEvent)
        => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent });

    // Descendants(DependencyObject) - RegionChromeTests.cs 것을 그대로 쓴다(같은 partial class Program).
}
