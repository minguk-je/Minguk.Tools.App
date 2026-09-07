using System.Windows;
using DevExpress.Xpf.Grid;

namespace Minguk.Base.Dependency;

public class GridColumnDependency : DependencyObject
{

    public static readonly DependencyProperty IsColumnAutoWidthProperty =
        DependencyProperty.RegisterAttached("IsColumnAutoWidth", typeof(bool?), typeof(GridColumnDependency),
            new FrameworkPropertyMetadata(IsColumnAutoWidth_PropertyChanged));

    public static void SetIsColumnAutoWidth(DependencyObject element, bool value)
    {
        element.SetValue(IsColumnAutoWidthProperty, value);
    }
    public static bool GetIsColumnAutoWidth(DependencyObject element)
    {
        return (bool)element.GetValue(IsColumnAutoWidthProperty);
    }

    private static void IsColumnAutoWidth_PropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        if (source is GridColumn gridColumn)
        {
            gridColumn.Width = new GridColumnWidth(gridColumn.ActualWidth, (bool)e.NewValue ? GridColumnUnitType.Auto : GridColumnUnitType.Pixel);
        }
    }
}
