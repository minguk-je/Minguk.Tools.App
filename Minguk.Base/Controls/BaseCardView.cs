using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;

namespace Minguk.Base.Controls;

public class BaseCardView : CardView
{
    public BaseCardView()
    {
        this.AllowEditing = false;
        this.AllowFilterEditor = DevExpress.Utils.DefaultBoolean.True;
        this.AllowLeaveFocusOnTab = true;
        this.AllowLeaveInvalidEditor = true;

        this.CardAlignment = Alignment.Near;
        this.CardLayout = CardLayout.Rows;

        this.ColumnFilterPopupMode = ColumnFilterPopupMode.ExcelSmart;
        this.EditorButtonShowMode = EditorButtonShowMode.ShowForFocusedRow;
        this.EditorShowMode = DevExpress.Xpf.Core.EditorShowMode.Default;
        this.EnableImmediatePosting = true;
        this.IsSynchronizedWithCurrentItem = true;
        this.ItemsSourceErrorInfoShowMode = ItemsSourceErrorInfoShowMode.RowAndCell;
        this.NavigationStyle = GridViewNavigationStyle.Cell;

        this.PrintFixedTotalSummary = false;
        this.PrintTotalSummary = false;

        this.SeparatorThickness = 0;
        this.ShowCardExpandButton = true;
        this.ShowColumnHeaders = false;
        this.ShowFixedTotalSummary = false;
        this.ShowFocusedRectangle = true;
        this.ShowGroupPanel = false;
        this.ShowGroupedColumns = false;
        this.ShowSearchPanelFindButton = true;
        this.ShowSearchPanelMode = ShowSearchPanelMode.Never;
        this.ShowSelectionRectangle = true;
        this.ShowTotalSummary = false;
    }
}
