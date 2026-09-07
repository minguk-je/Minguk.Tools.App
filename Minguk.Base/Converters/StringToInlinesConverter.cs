using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Xml;

namespace Minguk.Base.Converters;

public class StringToInlinesConverter : MarkupExtension, IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var textBlock = values[0] as TextBlock;
        if (textBlock == null) return null;

        //textBlock.ClearValue(TextBlock.TextProperty);
        textBlock.Inlines.Clear();

        const string @namespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        string? formattedText;
        if (values[1] == DependencyProperty.UnsetValue)
            formattedText = string.Empty;
        else
            formattedText = System.Convert.ToString(values[1]);

        formattedText = $@"<Span xml:space=""preserve"" xmlns=""{@namespace}"">{formattedText}</Span>";

        StringReader stringReader = new StringReader(formattedText);
        XmlReader xmlReader = XmlReader.Create(stringReader);
        Span span = (Span)XamlReader.Load(xmlReader);
        textBlock.Inlines.Add(span);

        return formattedText;
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
