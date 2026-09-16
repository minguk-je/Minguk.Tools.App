using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DevExpress.Data.Linq.Helpers;
using DevExpress.Mvvm;
using DevExpress.Xpf.Grid;

namespace Minguk.Base.Dependency;

public class GridControlDependency
{
    public static readonly DependencyProperty MaxRowIndicatorWidthProperty =
        DependencyProperty.RegisterAttached("MaxRowIndicatorWidth", typeof(double), typeof(GridControlDependency), null);

    public static readonly DependencyProperty IsRowNumberProperty =
         DependencyProperty.RegisterAttached("IsRowNumber", typeof(bool), typeof(GridControlDependency),
                new FrameworkPropertyMetadata(IsRowNumber_PropertyChanged));

    public static readonly DependencyProperty IsColumnAutoWidthProperty =
         DependencyProperty.RegisterAttached("IsColumnAutoWidth", typeof(bool), typeof(GridControlDependency),
                new FrameworkPropertyMetadata(IsColumnAutoWidth_PropertyChanged));

    public static double GetMaxRowIndicatorWidth(DependencyObject obj)
    {
        return (double)obj.GetValue(MaxRowIndicatorWidthProperty);
    }

    public static void SetMaxRowIndicatorWidth(DependencyObject obj, double value)
    {
        obj.SetValue(MaxRowIndicatorWidthProperty, value);
    }

    public static void SetIsRowNumber(UIElement element, bool value)
    {
        element.SetValue(IsRowNumberProperty, value);
    }

    public static bool GetIsRowNumber(UIElement element)
    {
        return (bool)element.GetValue(IsRowNumberProperty);
    }

    public static void SetIsColumnAutoWidth(UIElement element, bool value)
    {
        element.SetValue(IsColumnAutoWidthProperty, value);
    }

    public static bool GetIsColumnAutoWidth(UIElement element)
    {
        return (bool)element.GetValue(IsColumnAutoWidthProperty);
    }

    private static void IsRowNumber_PropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        //if (ViewModelBase.IsInDesignMode)
        //    return;

        if (source is GridControl grid)
        {
            if ((bool)e.NewValue)
            {
                grid.CustomUnboundColumnData += OnCustomUnboundColumnData;
                grid.PropertyChanged += OnRowNumberEnabledPropertyChanged;
            }
            else
            {
                grid.CustomUnboundColumnData -= OnCustomUnboundColumnData;
                grid.PropertyChanged -= OnRowNumberEnabledPropertyChanged;
            }
        }
    }

    public static void OnCustomUnboundColumnData(object? sender, GridColumnDataEventArgs e)
    {
        if (sender is GridControl grid && e.Column.FieldName == "RowNumber")
        {
            var rowHandle = grid.GetRowHandleByListIndex(e.ListSourceRowIndex);
            e.Value = rowHandle + 1;
        }
    }

    public static void OnRowNumberEnabledPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "VisibleRowCount")
        {
            if (sender is GridControl gridControl && (gridControl.View is TableView || gridControl.View is TreeListView))
            {
                //InitRowIndicatorWidth(gridControl);

                gridControl.Dispatcher.BeginInvoke((Action)(() => { InitRowIndicatorWidth(gridControl); }),
                    System.Windows.Threading.DispatcherPriority.Render);
            }
        }
    }

    /// <summary>
    /// 행 인디케이터(행번호 칸) 폭을 지금 행 수에 맞춰 다시 잰다.
    ///
    /// 예전에는 커질 때만 갱신했다(<c>if (width &gt; maxWidth)</c>).
    /// 그래서 많이 조회한 뒤 적게 조회하면 인디케이터가 넓은 채로 남았다.
    /// 조회할 때마다 다시 계산하도록 바꿨으므로 행 수가 줄면 폭도 줄어든다.
    ///
    /// 폭이 실제로 달라졌을 때만 값을 쓴다. 같은 값을 다시 넣으면
    /// 스크롤로 VisibleRowCount 가 흔들릴 때마다 레이아웃이 다시 도는 셈이 된다.
    /// </summary>
    public static void InitRowIndicatorWidth(GridControl gridControl)
    {
        var rowCount = GetRowCount(gridControl);

        if (rowCount == null)
            return;

        // "A" 는 행 편집 상태(A/U/D) 글자가 들어갈 자리를 미리 잡아 두는 것이다.
        double width = MeasureString(gridControl, rowCount.Value.ToString() + "A").Width;
        double current = (double)gridControl.GetValue(MaxRowIndicatorWidthProperty);

        if (!width.Equals(current))
            gridControl.SetValue(MaxRowIndicatorWidthProperty, width);
    }

    /// <summary>인디케이터 폭 계산에 쓸 행 수. 원본 종류에 따라 세는 방법이 다르다.</summary>
    private static int? GetRowCount(GridControl gridControl)
    {
        if (gridControl.ItemsSource is IList list)
            return list.Count;

        if (gridControl.ItemsSource is IQueryable queryable)
            return queryable.Count();

        if (gridControl.ItemsSource is DataTable dataTable)
            return dataTable.Rows.Count;

        if (gridControl.ItemsSource is IListSource)
            return gridControl.VisibleRowCount;

        return null;
    }

    public static System.Windows.Size MeasureString(GridControl gridControl, string textToFormat)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(gridControl).PixelsPerDip;
        var fontSize = System.Windows.Application.Current?.MainWindow == null ? 10 : System.Windows.Application.Current.MainWindow.FontSize;
        var fontFamily = System.Windows.Application.Current?.MainWindow == null ? "Consolas" : System.Windows.Application.Current.MainWindow.FontFamily.ToString();

        FormattedText formattedText = new FormattedText(
            textToFormat,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily(fontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize,
            System.Windows.Media.Brushes.Black,
            pixelsPerDip);

        return new System.Windows.Size(formattedText.Width + textToFormat.Length, formattedText.Height);
    }

    private static void IsColumnAutoWidth_PropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        if (ViewModelBase.IsInDesignMode)
            return;

        if (source is not GridControl grid)
            return;

        ApplyColumnAutoWidth(grid, (bool)e.NewValue);

        // XAML 에서는 이 속성이 열보다 먼저 들어와 그때는 열이 0개다 - 켜도 아무 열도 Auto 가 안 되어 머리글 폭에 머물렀다(환경설정 그리드, 2026-09-17).
        // 뜬 뒤 한 박자 늦게 한 번 더 건다(그때의 값으로). Loaded 안에서 바로 걸면 그리드가 뒤이어 열 폭을 Pixel 로 되돌렸다(실측, --environment-screen).
        if (!grid.IsLoaded)
        {
            void OnLoaded(object sender, RoutedEventArgs args)
            {
                grid.Loaded -= OnLoaded;
                grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    () => ApplyColumnAutoWidth(grid, GetIsColumnAutoWidth(grid)));
            }

            grid.Loaded += OnLoaded;
        }
    }

    /// <summary>
    /// 컬럼 폭 단위를 Auto / Pixel 로 다시 적용한다.
    ///
    /// 첨부 프로퍼티 <see cref="IsColumnAutoWidthProperty"/> 의 변경 콜백에서만 부르면
    /// 값이 이미 같을 때(예: 액션 버튼을 두 번째로 누를 때) PropertyChanged 가 뜨지 않아
    /// 아무 일도 일어나지 않는다. 그래서 ViewModel 이 바인딩을 거치지 않고
    /// 직접 부를 수 있도록 본체를 밖으로 뺀다.
    /// </summary>
    public static void ApplyColumnAutoWidth(GridControl? grid, bool isColumnAutoWidth)
    {
        if (ViewModelBase.IsInDesignMode)
            return;

        if (grid == null)
            return;

        foreach (GridColumn gridColumn in grid.Columns)
        {
            if (isColumnAutoWidth)
            {
                var columnOverride = gridColumn.GetValue(GridColumnDependency.IsColumnAutoWidthProperty);
                if (columnOverride != null)
                {
                    if ((bool)columnOverride == false)
                        gridColumn.Width = new GridColumnWidth(gridColumn.ActualWidth, GridColumnUnitType.Pixel);
                    else
                        gridColumn.Width = new GridColumnWidth(gridColumn.ActualWidth, GridColumnUnitType.Auto);
                }
                else
                {
                    gridColumn.Width = new GridColumnWidth(gridColumn.ActualWidth, GridColumnUnitType.Auto);
                }
            }
            else
            {
                gridColumn.Width = new GridColumnWidth(gridColumn.ActualWidth, GridColumnUnitType.Pixel);
            }
        }
    }
}
