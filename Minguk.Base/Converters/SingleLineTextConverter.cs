using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using Binding = System.Windows.Data.Binding;

namespace Minguk.Base.Converters
{
    /// <summary>
    /// 줄바꿈과 탭을 공백으로 바꿔 한 줄로 만든다. 그리드 셀 표시 전용.
    ///
    /// 로그 payload 에는 예외 스택트레이스처럼 원문에 개행이 들어 있는 값이 섞여 있다.
    /// 자동 줄바꿈을 꺼도(TextWrapping=NoWrap) 원문의 개행까지 막지는 못해서
    /// 한 건이 열 줄 넘게 차지하고, 그러면 한 화면에 몇 건 못 보여 훑어보기가 안 된다.
    ///
    /// AvalonEdit 쪽은 AvalonEditBehaviour.IsSingleLine 이 같은 일을 하므로 이 컨버터를 쓰지 않는다.
    /// 여기는 구문 강조를 껐을 때 보이는 dxe:TextEdit 용이다.
    ///
    /// 표시만 바꾸는 것이라 원본 값은 그대로다. 내보내기나 상세 보기에는 영향이 없다.
    ///
    /// MarkupExtension 이라 리소스 선언 없이 바로 쓸 수 있다.
    ///   EditValue="{Binding Value, Mode=OneWay, Converter={converters:SingleLineTextConverter}}"
    /// </summary>
    public sealed class SingleLineTextConverter : MarkupExtension, IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string text || text.Length == 0)
                return value;

            if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0 && text.IndexOf('\t') < 0)
                return text;

            return text.Replace("\r\n", " ", StringComparison.Ordinal)
                       .Replace('\r', ' ')
                       .Replace('\n', ' ')
                       .Replace('\t', ' ');
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;

        public override object ProvideValue(IServiceProvider serviceProvider) => this;
    }
}
