using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Minguk.Tools.Markup;

/// <summary>
/// 글꼴 크기 → 상태 표시줄 높이(글꼴 크기 × 2, 반올림). 상태 줄이 담긴 글에 따라 높이가 바뀌지 않게 못 박는다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-24) "Adorner 클릭해서 뭔가를 하면 미리보기 화면이 내려갔다가 위로 올라가는 증상" - 실제 앱에서 재 보니 미리보기 패널 위쪽은 그대로인데
/// 높이가 892 ↔ 893 으로 1px 씩 0.3~1초마다 오르내렸다(확대 2.7배라 그림이 몇 px 씩 흔들렸다). 패널 아래의 상태 줄이 담긴 글(검출 상태·글자 읽기 상태는
/// 수시로, 영역을 놓으면 「영역1」… 문구)에 따라 줄 높이가 1px 달라지는 것으로 보았다 - 「」 같은 글자가 다른 글꼴로 그려지면 줄 높이가 바뀐다.
/// 픽셀로 못 박으면 글꼴을 키운 사용자에게 잘리므로 글꼴 크기에서 구한다 - 기본 12 에서 잰 높이가 24 였다.
/// </remarks>
public sealed class FontSizeToBarHeightConverter : MarkupExtension, IValueConverter
{
    private static readonly FontSizeToBarHeightConverter Instance = new();

    public override object ProvideValue(IServiceProvider serviceProvider) => Instance;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double size && size > 0 && !double.IsNaN(size) ? Math.Round(size * 2) : DependencyProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
