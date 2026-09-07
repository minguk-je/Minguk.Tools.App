using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Binding = System.Windows.Data.Binding;

namespace Minguk.Base.Converters
{
    /// <summary>
    /// bool 을 Visible / <b>Hidden</b> 으로 바꾼다. (Collapsed 가 아니다)
    ///
    /// Collapsed 는 레이아웃에서 아예 빠지지만 Hidden 은 자리를 그대로 차지한다.
    /// 같은 칸에 컨트롤 두 개를 겹쳐 놓고 번갈아 보여줄 때, Collapsed 를 쓰면
    /// 어느 쪽이 보이느냐에 따라 칸 크기가 달라진다.
    /// Hidden 을 쓰면 둘 다 항상 측정되므로 크기가 '둘 중 큰 쪽'으로 고정되어 흔들리지 않는다.
    ///
    /// 예) 로그 그리드의 data 칸은 구문 강조 on/off 에 따라
    ///     avalonedit:TextEditor 와 dxe:TextEdit 을 번갈아 보여주는데,
    ///     둘의 줄 높이가 미묘하게 달라 행 높이가 어긋난다. 이 컨버터로 맞춘다.
    ///
    /// MarkupExtension 이라 리소스 선언 없이 바로 쓸 수 있다.
    ///   Visibility="{Binding IsX, Converter={converters:BooleanToVisibilityHiddenConverter}}"
    ///   Visibility="{Binding IsX, Converter={converters:BooleanToVisibilityHiddenConverter Inverse=True}}"
    /// </summary>
    public sealed class BooleanToVisibilityHiddenConverter : MarkupExtension, IValueConverter
    {
        /// <summary>참일 때 숨기고 거짓일 때 보인다.</summary>
        public bool Inverse { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var flag = value is bool b && b;

            return flag != Inverse ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;

        public override object ProvideValue(IServiceProvider serviceProvider) => this;
    }
}
