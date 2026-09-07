using System.Windows.Controls;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Grid;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Minguk.Base.Controls;

public class BaseTableView : TableView
{
    public BaseTableView()
    {
        this.AllowBestFit = true;
        this.AllowCascadeUpdate = false;
        this.AllowEditing = false;
        this.AllowFilterEditor = DevExpress.Utils.DefaultBoolean.False;
        this.AllowFixedColumnMenu = true;
        this.AllowLeaveFocusOnTab = true;
        this.AllowLeaveInvalidEditor = true;
        this.AllowPerPixelScrolling = false;
        this.AllowScrollAnimation = false;
        this.AllowSortedDataDragDrop = false;

        this.AutoWidth = false;
        this.BestFitArea = BestFitArea.All;
        this.BestFitMaxRowCount = 1000;
        this.BestFitMode = DevExpress.Xpf.Core.BestFitMode.VisibleRows;
        this.BestFitModeOnSourceChange = DevExpress.Xpf.Core.BestFitMode.VisibleRows;
        this.CheckBoxSelectorColumnWidth = 50;
        this.ColumnFilterPopupMode = ColumnFilterPopupMode.ExcelSmart;
        this.EditFormShowMode = EditFormShowMode.None;
        this.EditorButtonShowMode = EditorButtonShowMode.ShowForFocusedRow;
        this.EditorShowMode = DevExpress.Xpf.Core.EditorShowMode.Default;
        this.EnableImmediatePosting = true;
        this.IsSynchronizedWithCurrentItem = true;
        this.ItemsSourceErrorInfoShowMode = ItemsSourceErrorInfoShowMode.RowAndCell;
        this.NavigationStyle = GridViewNavigationStyle.Cell;
        this.NewItemRowPosition = NewItemRowPosition.None;
        this.PrintFixedTotalSummary = false;
        this.PrintTotalSummary = false;
        this.RowMinHeight = 24;
        this.ShowAutoFilterRow = false;
        this.ShowCheckBoxSelectorColumn = true;
        this.ShowCriteriaInAutoFilterRow = true;
        this.ShowDataNavigator = false;
        this.ShowEditFormOnDoubleClick = false;
        this.ShowEditFormOnEnterKey = false;
        this.ShowEditFormOnF2Key = false;
        this.ShowEditFormUpdateCancelButtons = true;
        this.ShowFixedTotalSummary = false;
        this.ShowFocusedRectangle = true;
        this.ShowGroupPanel = true;
        this.ShowGroupedColumns = true;
        this.ShowIndicator = true;
        this.ShowSearchPanelFindButton = true;
        this.ShowSearchPanelMode = ShowSearchPanelMode.Always;
        this.ShowSelectionRectangle = true;
        this.RetainSelectionOnClickOutsideCheckBoxSelector = false;
        this.ShowTotalSummary = false;
        this.UseEvenRowBackground = true;
        this.UseAnimationWhenExpanding = false;

        this.SnapsToDevicePixels = true;
        this.UseLayoutRounding = true;

        this.ScrollBarAnnotationMode = DevExpress.Xpf.Grid.ScrollBarAnnotationMode.All;
        this.SearchPanelParseMode = SearchPanelParseMode.Mixed;
        //this.SearchPanelNullText = "검색어 입력 (여러 단어=OR, +단어=AND, -단어=NOT, \"단어 \"=일치, 컬럼이름:단어)";

        this.ShowSearchPanelNavigationButtons = true;
        this.SearchPanelAllowFilter = true;
        this.ShowSearchPanelResultInfo = true;

        this.SearchPanelPosition = SearchPanelPosition.Default;
        this.SearchPanelHorizontalAlignment = HorizontalAlignment.Stretch;

        this.HorizontalScrollbarVisibility = ScrollBarVisibility.Auto;
        this.VerticalScrollbarVisibility = ScrollBarVisibility.Auto;
    }

    /// <summary>
    /// 컬럼 폭을 내용에 맞추되 각 컬럼에 <paramref name="addWidth"/> 만큼 여유를 더한다.
    ///
    /// 이름을 <c>BestFitColumns</c> 로 두면 안 된다.
    /// C# 오버로드 해석은 파생 타입에 적용 가능한 후보가 있으면 기반 타입의 메서드를 후보에서 제거한다.
    /// 선택 인자 덕분에 <c>BestFitColumns()</c> 도 이 메서드에 적용 가능해지므로,
    /// 인자 없이 부른 모든 호출이 DevExpress 원본 <see cref="TableView.BestFitColumns()"/> 대신
    /// 여기로 흘러들어온다. 실제로 그 때문에 로그 화면 컬럼 폭이 전부 뭉개졌던 이력이 있다.
    ///
    /// 또한 <c>CalcColumnBestFitWidth</c> 는 CellTemplate 안이 AvalonEdit 같은
    /// ScrollViewer 기반 컨트롤인 컬럼의 폭을 제대로 재지 못한다.
    /// 그런 컬럼이 있는 그리드에서는 원본 <c>BestFitColumns()</c> 를 쓸 것.
    /// </summary>
    public void BestFitColumnsWithPadding(int addWidth = 4)
    {
        BestFitColumnsCore(addWidth, maxWidth: int.MaxValue, retry: true);
    }

    public void BestFitColumnsWithMaxWidth(int addWidth = 4, int maxWidth = 500)
    {
        BestFitColumnsCore(addWidth, maxWidth, retry: true);
    }

    private void BestFitColumnsCore(int addWidth, int maxWidth, bool retry)
    {
        var anyApplied = false;

        foreach (var column in Columns)
        {
            var w = CalcColumnBestFitWidth(column) + addWidth;
            if (w <= addWidth)
                continue;

            column.Width = Math.Min(w, maxWidth);
            anyApplied = true;
        }

        // 레이아웃(측정/배치)이 아직 완료되지 않은 시점에 호출되면 CalcColumnBestFitWidth가 0을 돌려줌
        // → 렌더링이 끝난 뒤(ContextIdle) 한 번 더 시도
        if (anyApplied == false && retry)
        {
            Dispatcher.BeginInvoke(
                new Action(() => BestFitColumnsCore(addWidth, maxWidth, retry: false)),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }
}
