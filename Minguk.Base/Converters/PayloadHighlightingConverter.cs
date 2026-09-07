using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Minguk.Base.Converters
{
    /// <summary>
    /// 셀 값의 형태를 보고 그에 맞는 구문 강조 정의를 골라 준다. 맞는 것이 없으면 null 을 넘겨 강조를 끈다.
    ///
    /// 시계열데이터의 payload 에는 네 종류가 섞여 있다.
    ///   JSON            {"AMRID":"1", ...}                     → JSON 정의
    ///   프로토콜 프레임  00000496Cmd=11&amp;AId=1&amp;Count=4"Status=1  → 프레임 정의
    ///   스택트레이스     System.Net.Http...\n   at ...           → 강조 없음
    ///   평문            ECU Program On                         → 강조 없음
    ///
    /// 원래는 ViewModel 의 단일 값(Json.Dark / Json.Light)이 모든 셀에 그대로 걸려 있어서
    /// JSON 이 아닌 값에도 JSON 색이 입혀졌다. 색이 의미를 갖지 않으면 읽기를 방해한다.
    /// 스택트레이스와 평문은 강조할 문법이 없으므로 일부러 끈다.
    ///
    /// 값 순서.
    ///   values[0] : 셀 값(문자열)
    ///   values[1] : JSON 용 정의   (ViewModel 이 테마에 맞춰 고른 것)
    ///   values[2] : 프레임용 정의  (없으면 생략 가능)
    ///
    /// AvalonEdit 타입을 직접 참조하지 않고 그대로 통과시키기만 하므로
    /// 이 어셈블리가 AvalonEdit 에 의존하지 않는다. 종류를 늘리려면 값을 하나 더 받고
    /// <see cref="Detect"/> 에 분기를 추가하면 된다.
    ///
    ///   &lt;MultiBinding Converter="{converters:PayloadHighlightingConverter}"&gt;
    ///       &lt;Binding Path="Value" Mode="OneWay" /&gt;
    ///       &lt;Binding Path="View.DataContext.EditorHighlighting" Mode="OneWay" /&gt;
    ///       &lt;Binding Path="View.DataContext.FrameHighlighting" Mode="OneWay" /&gt;
    ///   &lt;/MultiBinding&gt;
    /// </summary>
    public sealed class PayloadHighlightingConverter : MarkupExtension, IMultiValueConverter
    {
        private enum Kind
        {
            None,
            Json,
            Frame,
        }

        public object? Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return null;

            if (values[0] is not string text || text.Length == 0)
                return null;

            return Detect(text) switch
            {
                Kind.Json  => Pick(values, 1),
                Kind.Frame => Pick(values, 2),
                _          => null,
            };
        }

        private static object? Pick(object?[] values, int index)
        {
            if (index >= values.Length)
                return null;

            var definition = values[index];

            return definition == DependencyProperty.UnsetValue ? null : definition;
        }

        /// <summary>
        /// 파싱은 하지 않는다. 셀마다 JSON 을 파싱하면 스크롤이 무거워진다.
        /// 첫 글자와 공백 유무, '=' 개수만 본다.
        /// </summary>
        private static Kind Detect(string text)
        {
            var i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;

            if (i >= text.Length)
                return Kind.None;

            // 여는 괄호로 시작하면 JSON 으로 본다.
            if (text[i] is '{' or '[')
                return Kind.Json;

            // 프레임은 공백이 전혀 없고 키=값이 여러 번 나온다.
            // 스택트레이스와 평문은 공백(개행 포함)이 있으므로 여기서 걸러진다.
            var equals = 0;
            for (var k = i; k < text.Length; k++)
            {
                var c = text[k];

                if (char.IsWhiteSpace(c))
                    return Kind.None;

                if (c == '=')
                    equals++;
            }

            return equals >= 3 ? Kind.Frame : Kind.None;
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        public override object ProvideValue(IServiceProvider serviceProvider) => this;
    }
}
