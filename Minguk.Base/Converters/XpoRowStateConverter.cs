using System.Globalization;
using System.Windows.Data;
using DevExpress.Xpo;
using Binding = System.Windows.Data.Binding;

namespace Minguk.Base.Converters;

public sealed class XpoRowStateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PersistentBase pb)
            return Binding.DoNothing;   // PersistentBase가 아니면 트리거 미적용

        if (pb.IsDeleted)
            return "Deleted";

        Session? session = pb.Session;
        if (session == null)
            return Binding.DoNothing;

        if (session.IsNewObject(pb))
            return "Added";

        if (session.IsObjectToSave(pb))   // 신규가 아닌데 저장 대상이면 수정됨
            return "Modified";

        return "Unchanged";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
