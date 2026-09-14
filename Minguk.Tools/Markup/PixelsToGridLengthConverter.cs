using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Minguk.Tools.Markup;

/// <summary>
/// 픽셀(double)을 <see cref="GridLength"/> 픽셀 값으로. 매개변수는 더할 여백(px).
/// </summary>
/// <remarks>
/// 도킹 패널(<c>dxdo:LayoutPanel</c>)의 <c>ItemHeight</c> 는 star·픽셀만 받고 Auto 가 없다. 내용 높이에 맞추려면
/// 내용의 <c>ActualHeight</c> 를 이것으로 묶는다 - 라벨링 화면의 학습 패널이 그렇다(190px 로 못 박았더니 모델 줄 아래가 비었다).
/// 값이 아직 0 이면(첫 배치 전) 그대로 두게 <see cref="DependencyProperty.UnsetValue"/> 를 준다.
/// </remarks>
public sealed class PixelsToGridLengthConverter : MarkupExtension, IValueConverter
{
    private static readonly PixelsToGridLengthConverter Instance = new();

    public override object ProvideValue(IServiceProvider serviceProvider) => Instance;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double pixels || pixels <= 0 || double.IsNaN(pixels)) return DependencyProperty.UnsetValue;

        var extra = parameter is null ? 0 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);

        return new GridLength(Math.Ceiling(pixels + extra), GridUnitType.Pixel);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GridLength { IsAbsolute: true } length ? length.Value : DependencyProperty.UnsetValue;
}
