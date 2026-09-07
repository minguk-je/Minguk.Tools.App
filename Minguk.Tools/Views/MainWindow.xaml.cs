using System;

using DevExpress.Xpf.Core;

using Minguk.Tools.Helper;

namespace Minguk.Tools.Views;

public partial class MainWindow : ThemedWindow
{
    public MainWindow()
    {
        InitializeComponent();
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
        UserPreferencesHelper.ApplyWindowPlacement(this);

        base.OnSourceInitialized(e);
    }
}
