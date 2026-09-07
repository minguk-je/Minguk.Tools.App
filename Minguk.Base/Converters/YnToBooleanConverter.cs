using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Minguk.Base.Converters
{
    public class YnToBooleanConverter : IValueConverter
    {
        public object Convert(object? value, Type t, object? p, CultureInfo c) => (value as string) == "Y";
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => (value is bool b && b) ? "Y" : "N";
    }
}
