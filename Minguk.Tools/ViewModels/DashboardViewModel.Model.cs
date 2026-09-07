using DevExpress.Mvvm;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 화면이 들고 있는 상태와 커맨드 선언만 모은 쪽.
///
/// ViewModel 을 partial 로 쪼개는 규칙(TamsTools 와 동일):
///   XxxViewModel.cs        생명주기 — Create / 생성자 / Initialize* / Save / Release
///   XxxViewModel.Model.cs  상태     — 바인딩 프로퍼티, 커맨드 선언, 컨트롤 참조
///   XxxViewModel.Code.cs   동작     — 커맨드가 실제로 하는 일 (길어질 때만 만든다)
///
/// XAML 을 열지 않아도 이 파일만 보면 화면이 뭘 바인딩하는지 알 수 있게 두는 것이 목적이다.
/// </summary>
public partial class DashboardViewModel
{
    // ── 커맨드 ───────────────────────────────────────────────────────────
    // OnInitializedCommand / OnClosingCommand / DoCloseCommand 는 베이스가 준다. 다시 선언하지 않는다.

    public DelegateCommand DoRefreshCommand { get; private set; } = null!;

    // ── 바인딩 프로퍼티 ──────────────────────────────────────────────────

    public int TotalFrames { get => GetProperty(() => TotalFrames); set => SetProperty(() => TotalFrames, value); }

    public int SkippedFrames { get => GetProperty(() => SkippedFrames); set => SetProperty(() => SkippedFrames, value); }

    public double AverageLatencyMs { get => GetProperty(() => AverageLatencyMs); set => SetProperty(() => AverageLatencyMs, value); }

    public string? LastUpdated { get => GetProperty(() => LastUpdated); set => SetProperty(() => LastUpdated, value); }

    /// <summary>새로고침을 몇 번 했는지. 화면을 닫아도 남는다(RestoreSettings/SaveSettings 예시).</summary>
    public int RefreshCount { get => GetProperty(() => RefreshCount); set => SetProperty(() => RefreshCount, value); }
}
