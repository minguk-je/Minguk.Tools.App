using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace Minguk.Tools.Markup;

/// <summary>
/// 편집기가 내준 숫자(Decimal 등)를 double 로 되돌린다.
/// </summary>
/// <remarks>
/// DevExpress SpinEdit 는 값을 Decimal 로 내준다. double 속성에 그대로 묶으면 되돌리기(ConvertBack)가 실패해
/// 칸은 바뀌는데 값은 안 들어간다 - 조용히, 로그에만 남는다(실측: 미리보기 확대). SpinEdit 컨트롤은 EditValueType 으로 막지만
/// 도구 모음의 SpinEditSettings 에는 그 속성이 없어 이것을 붙인다.
/// </remarks>
public sealed class ToDoubleConverter : MarkupExtension, IValueConverter
{
    private static readonly ToDoubleConverter Instance = new();

    public override object ProvideValue(IServiceProvider serviceProvider) => Instance;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? null : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
}
