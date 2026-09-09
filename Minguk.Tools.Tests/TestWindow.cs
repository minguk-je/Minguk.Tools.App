using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;

namespace Minguk.Tools.Tests;

/// <summary>입력을 받아 줄 대상 창. XAML 없이 코드로만 만든다.</summary>
internal sealed class TestWindow : Window
{
    public TextBox Input { get; }

    public Button Target { get; }

    public ScrollViewer Scroller { get; }

    /// <summary>버튼이 실제로 눌린 횟수.</summary>
    public int ClickCount { get; set; }

    /// <summary>
    /// 창에 도착한 마우스 메시지 수. WPF 가 처리했는지와 무관하게 센다.
    /// "메시지가 안 왔다" 와 "왔는데 WPF 가 무시했다" 를 가르려고 둔다 -
    /// 앞은 어댑터 문제이고 뒤는 대상이 WPF 라서 생기는 성질이다.
    /// </summary>
    public int MouseDownMessages { get; private set; }

    public int WheelMessages { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(CountMessages);
    }

    private IntPtr CountMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_MOUSEWHEEL = 0x020A;

        if (message == WM_LBUTTONDOWN) MouseDownMessages++;
        if (message == WM_MOUSEWHEEL) WheelMessages++;

        return IntPtr.Zero;
    }

    public TestWindow()
    {
        Title = "입력 어댑터 검증 대상 창";
        Width = 520;
        Height = 420;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Input = new TextBox
        {
            AcceptsReturn = true,
            Height = 90,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 15
        };

        Target = new Button
        {
            Content = "클릭 대상",
            Height = 44,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Target.Click += (_, _) => ClickCount++;

        var tall = new StackPanel();
        for (var i = 1; i <= 60; i++)
        {
            tall.Children.Add(new TextBlock { Text = $"스크롤 확인용 {i}번째 줄", Margin = new Thickness(4) });
        }

        Scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 170,
            Content = tall
        };

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock
        {
            Text = "이 창에 입력을 쏘고 결과를 되읽는다.",
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(Input);
        root.Children.Add(Target);
        root.Children.Add(Scroller);

        Content = root;
    }
}
