using System.Windows.Controls;

using DevExpress.Utils;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;

namespace Minguk.Base.Controls;

public class BaseTreeListView : TreeListView
{
    public BaseTreeListView()
    {
        this.AllowBestFit = true;
        this.AllowCascadeUpdate = true;
        this.AllowEditing = false;
        this.AllowFilterEditor = DefaultBoolean.True;
        this.AllowIndeterminateCheckState = true;
        this.AllowLeaveFocusOnTab = true;
        this.AllowLeaveInvalidEditor = true;
        this.AllowPerPixelScrolling = false;
        this.AllowRecursiveNodeChecking = true;
        this.AllowRecursiveNodeSummaryCalculation = false;
        this.AllowScrollAnimation = true;

        this.AutoExpandAllNodes = true;
        this.BestFitArea = BestFitArea.All;
        this.BestFitMaxRowCount = 1000;
        this.BestFitMode = BestFitMode.AllRows;
        this.BestFitModeOnSourceChange = BestFitMode.AllRows;
        this.ColumnFilterPopupMode = ColumnFilterPopupMode.Default;
        this.EditFormShowMode = EditFormShowMode.None;
        this.EditorButtonShowMode = EditorButtonShowMode.ShowOnlyInEditor;
        this.EditorShowMode = DevExpress.Xpf.Core.EditorShowMode.MouseDown;
        this.EnableImmediatePosting = true;
        this.FetchSublevelChildrenOnExpand = true;
        this.IsSynchronizedWithCurrentItem = true;
        this.ItemsSourceErrorInfoShowMode = ItemsSourceErrorInfoShowMode.RowAndCell;
        this.NavigationStyle = GridViewNavigationStyle.Cell;
        this.PrintFixedTotalSummary = false;
        this.PrintTotalSummary = false;
        this.RowIndent = 16;
        this.RowMinHeight = 22;
        this.ScrollBarAnnotationMode = DevExpress.Xpf.Grid.ScrollBarAnnotationMode.FocusedRow & DevExpress.Xpf.Grid.ScrollBarAnnotationMode.Selected;
        this.SearchPanelHorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        this.ShowAutoFilterRow = false;
        this.ShowCheckboxes = false;
        this.ShowColumnHeaders = true; // 기본은 보인다(사용자, 2026-09-16). 열이 하나뿐인 트리는 쓰는 화면의 XAML 에서 끈다(솔루션 탐색기).
        this.ShowCriteriaInAutoFilterRow = true;
        this.ShowDataNavigator = false;
        this.ShowEditFormOnDoubleClick = false;
        this.ShowEditFormOnEnterKey = false;
        this.ShowEditFormOnF2Key = false;
        this.ShowEditFormUpdateCancelButtons = false;
        this.ShowExpandButtons = true;
        this.ShowFixedTotalSummary = false;
        this.ShowFocusedRectangle = false;
        this.ShowHorizontalLines = false;
        this.ShowIndicator = false;
        this.ShowNodeImages = true;
        this.ShowSearchPanelFindButton = true;
        this.ShowSearchPanelMode = ShowSearchPanelMode.Always;
        this.ShowSelectionRectangle = false;
        this.ShowTotalSummary = false;
        this.ShowVerticalLines = false;
        this.SnapsToDevicePixels = true;
        this.SwitchToCompactModeWidth = 10;
        this.UseEvenRowBackground = true;
        this.UseLayoutRounding = true;

        this.VerticalScrollbarVisibility = ScrollBarVisibility.Auto;
        this.HorizontalScrollbarVisibility = ScrollBarVisibility.Auto;
    }

    /// <summary>컬럼 폭을 내용에 맞추되 각 컬럼에 <paramref name="addWidth"/> 만큼 여유를 더한다 - 이름·이유는 <see cref="BaseTableView.BestFitColumnsWithPadding"/> 과 같다.</summary>
    public void BestFitColumnsWithPadding(int addWidth = 4)
    {
        BestFitColumnsCore(addWidth, retriesLeft: 10);
    }

    /// <summary>
    /// 안 고른 탭 안(예: 영역 패널이 맨 앞 탭이 아닐 때)은 재도 CalcColumnBestFitWidth 가 계속 0 을 준다 - 실제로
    /// 화면에 그려질 때까지(탭을 고르는 등) 알 수 없다. 한 번만 재시도하면 그새 그려지지 않으면 영영 안 맞았다
    /// (사용자, 2026-09-18 여러 번 "너무 빽빽해" - 처음 뜬 탭이 아니면 이 길을 탔다). ContextIdle 마다 몇 번 더 본다.
    /// </summary>
    private void BestFitColumnsCore(int addWidth, int retriesLeft)
    {
        if (DataControl is not GridControl grid) return;

        var anyApplied = false;

        foreach (var column in grid.Columns)
        {
            var w = CalcColumnBestFitWidth(column) + addWidth;
            if (w <= addWidth)
                continue;

            column.Width = w;
            anyApplied = true;
        }

        if (anyApplied == false && retriesLeft > 0)
        {
            Dispatcher.BeginInvoke(
                new System.Action(() => BestFitColumnsCore(addWidth, retriesLeft - 1)),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }
}
