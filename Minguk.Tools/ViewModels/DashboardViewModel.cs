using System;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using Minguk.Image;
using Minguk.Base.Utilities;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 샘플 화면 1. 새 화면의 최소 골격이자, 베이스 생명주기를 어떻게 채우는지 보여 주는 예다.
///
/// 새 화면 만드는 순서:
///   1) DocumentViewModelBase 를 상속하고 Create() 팩터리를 둔다 (POCO 프록시가 필요하다)
///   2) 생성자에서 Caption / CaptionImage 와 커맨드를 만든다
///   3) 필요한 단계만 override 한다 — 여기서는 RestoreSettings / OnLoaded / SaveSettings
///   4) View 의 DataContext 를 {dxmvvm:ViewModelSource Type=...} 로 물린다
///   5) App.xaml.cs 에 AddTransient&lt;View&gt;, MainMenu 에 항목 추가
///
/// 생성자에서 하는 일과 Initialize 단계에서 하는 일을 섞지 말 것.
/// 생성자는 XAML 이 바인딩할 대상(커맨드·컬렉션)을 만드는 자리고,
/// 화면이 실제로 떠 있어야 되는 일(컨트롤 참조·구독·조회)은 전부 Initialize 단계다.
/// </summary>
public partial class DashboardViewModel : DocumentViewModelBase
{
    public static DashboardViewModel Create() => ViewModelSource.Create(() => new DashboardViewModel());

    public DashboardViewModel()
    {
        Caption = "대시보드";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/business/16x16/business_report.png");

        DoRefreshCommand = new DelegateCommand(DoRefresh, false);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────
    // InitializeControls() / InitializeObservable() 는 이 화면에 잡을 컨트롤도, 구독할 이벤트도
    // 없어서 비워 두었다. 그리드나 에디터가 붙는 화면에서 채우면 된다.

    protected override void RestoreSettings()
    {
        RefreshCount = GetSetting(nameof(RefreshCount), 0);
    }

    protected override void OnLoaded()
    {
        DoRefresh();
    }

    protected override void SaveSettings()
    {
        SetSetting(nameof(RefreshCount), RefreshCount);
    }

    // ── 동작 ─────────────────────────────────────────────────────────────

    private void DoRefresh() => Guard(() =>
    {
        Logger.Trace(string.Empty);

        // TODO: 실제 파이프라인 지표로 교체할 것.
        TotalFrames = 0;
        SkippedFrames = 0;
        AverageLatencyMs = 0d;
        LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        RefreshCount++;

        // 상태바에 메시지를 흘린다. 대상을 지정하지 않으면 MainViewModel 이 받아 표시한다.
        MessengerUtility.SendMainMessage($"대시보드를 새로고침했습니다. (누적 {RefreshCount}회)");
    });
}
