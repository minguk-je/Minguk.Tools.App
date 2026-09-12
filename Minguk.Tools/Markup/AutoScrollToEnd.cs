using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using DevExpress.Xpf.Editors;

namespace Minguk.Tools.Markup;

/// <summary>
/// 글이 늘어나면 <see cref="TextEdit"/> 를 맨 아래로 내려 준다. 출력·호출 칸처럼 계속 쌓이는 곳에 붙인다.
/// </summary>
/// <remarks>
/// <code>
/// &lt;dxe:TextEdit markup:AutoScrollToEnd.IsEnabled="True" EditValue="{Binding ..., Mode=OneWay}" /&gt;
/// </code>
///
/// <b>사람이 올려 본 동안에는 따라가지 않는다.</b> 로그가 초당 수십 줄 쌓이는 칸에서 무조건 맨 아래로
/// 끌어내리면, 위를 읽으려 할 때마다 글이 튀어 읽을 수가 없다. 그래서 <b>이미 바닥에 있을 때만</b> 따라간다 -
/// 위로 올리면 멈추고, 다시 바닥까지 내리면 도로 따라간다. 채팅 창이 하는 것과 같은 규칙이다.
///
/// 스크롤은 <b>글이 실제로 들어간 뒤</b>에 해야 한다. <c>EditValueChanged</c> 시점에는 속 TextBox 가 아직
/// 새 글로 자리를 다시 잡기 전이라, 그 자리에서 부르면 한 번 늦은 자리로 내려간다.
/// 그래서 디스패처의 <see cref="DispatcherPriority.Background"/> 로 미룬다.
/// </remarks>
public static class AutoScrollToEnd
{
    /// <summary>바닥에서 이만큼 안이면 "바닥에 있다" 로 본다(px). 한 줄 높이쯤.</summary>
    private const double BottomSlack = 16;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(AutoScrollToEnd),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEdit edit) return;

        // 켜고 끄기를 오갈 수 있으니 늘 먼저 뗀다 - 두 번 붙으면 두 번 스크롤한다.
        edit.EditValueChanged -= OnEditValueChanged;

        if (e.NewValue is true) edit.EditValueChanged += OnEditValueChanged;
    }

    private static void OnEditValueChanged(object sender, EditValueChangedEventArgs e)
    {
        if (sender is not TextEdit edit) return;

        // 값이 바뀐 지금이 아니라, 그 글로 자리를 다시 잡은 뒤에 내린다.
        edit.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ScrollIfAtBottom(edit)));
    }

    private static void ScrollIfAtBottom(TextEdit edit)
    {
        if (edit.EditCore is not TextBox box) return;

        // 아직 그릴 자리가 없으면(탭이 숨어 있는 등) 볼 것도 없다.
        if (box.ExtentHeight <= 0 || box.ViewportHeight <= 0) return;

        var atBottom = box.VerticalOffset + box.ViewportHeight >= box.ExtentHeight - BottomSlack;

        if (atBottom) box.ScrollToEnd();
    }
}
