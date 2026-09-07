using System.Globalization;
using System.Windows.Data;
using DevExpress.Xpf.Grid;

namespace Minguk.Base.Converters;

public class RowNumberConverter : System.Windows.Markup.MarkupExtension, IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is GridControl gridControl)
        {
            var rowHandle = (int)values[1];
            if (rowHandle == DataControlBase.NewItemRowHandle || rowHandle == DataControlBase.AutoFilterRowHandle)
                return string.Empty;

            // var result = gridControl.GetListIndexByRowHandle(rowHandle);
            // return rowHandle >= 0 ? $"{result + 1}" : "";
            var result = rowHandle;
            return rowHandle >= 0 ? $"{result + 1}" : "";
        }

        return null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return this;
    }
}
