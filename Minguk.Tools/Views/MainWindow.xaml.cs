using System;

using DevExpress.Xpf.Core;

using Minguk.Tools.Helper;

namespace Minguk.Tools.Views;

public partial class MainWindow : ThemedWindow
{
    public MainWindow()
    {
        StartupTrace.Mark("  MainWindow 인스턴스화 시작");

        InitializeComponent();

        StartupTrace.Mark("  MainWindow.InitializeComponent (XAML 구성)");
    }

    /// <summary>
    /// 창 핸들은 만들어졌지만 아직 아무것도 그려지지 않은 시점.
    /// 저장된 위치·크기를 여기서 넣어야 첫 프레임부터 제자리에 뜬다.
    ///
    /// 예전에는 스플래시가 닫힌 뒤(= 창이 이미 보인 뒤)에 옮겼다. 그래서
    /// XAML 기본 크기로 한 번 그려진 다음 저장된 자리로 튀어가고,
    /// 그 과정에서 도킹·아코디언 배치가 통째로 두 번 계산됐다.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        StartupTrace.Mark("MainWindow 생성");

        UserPreferencesHelper.ApplyWindowPlacement(this);

        base.OnSourceInitialized(e);
    }

    private bool _activated;

    /// <summary>
    /// 첫 프레임이 그려진 직후 창을 앞으로 끌어온다.
    ///
    /// 스플래시가 별도 최상위 창이라 그것이 닫힐 때 포커스가 본 창으로 넘어오지 않는다.
    /// 그대로 두면 앱이 다른 창 뒤에서 떠서, 시작한 줄도 모르게 된다.
    /// 한 번만 한다 — 이후에는 사용자가 정한 z 순서를 건드리지 않는다.
    /// </summary>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_activated)
            return;

        _activated = true;

        StartupTrace.Mark("첫 화면 표시");
        StartupTrace.Dump();

        if (WindowState == System.Windows.WindowState.Minimized)
            WindowState = System.Windows.WindowState.Normal;

        Activate();
    }
}
