using System.Threading.Tasks;

using DevExpress.Mvvm.POCO;

using Minguk.Base.Utilities;
using Minguk.Image;
using Minguk.Tools.Input;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 편집 화면. 창을 잡아 몹 찾기·글자 읽기를 켜 놓고 <b>보면서</b> 스크립트를 쓰고 한 번씩 돌려 본다.
/// </summary>
/// <remarks>
/// 캡처 화면(담기)과 플레이 화면(반복 실행) 사이의 개발 자리다. 잡는 일은 <see cref="CaptureViewModelBase"/>,
/// 보는 일은 <see cref="RecognizingCaptureViewModelBase"/>, 스크립트 문서는 <see cref="ScriptWorkbench"/>,
/// 돌리는 일은 <see cref="ScriptPlayer"/> 가 한다. 이 파일은 그 넷을 잇는다.
///
/// 스크립트가 보내는 입력은 미리보기의 입력 전달과 <b>같은 어댑터</b>(고른 경로)로 나간다. 두 벌이면
/// 미리보기 클릭은 되는데 스크립트 클릭은 안 나가는 일이 생긴다.
/// </remarks>
public partial class ScriptStudioViewModel : RecognizingCaptureViewModelBase
{
    public static ScriptStudioViewModel Create() => ViewModelSource.Create(() => new ScriptStudioViewModel());

    /// <summary>스크립트 문서. XAML 은 <c>Script.Text</c> 처럼 한 단계 들어가 묶는다.</summary>
    public ScriptWorkbench Script { get; }

    /// <summary>돌리는 것. 한 번 · 반복 · 중지 · 진행.</summary>
    public ScriptPlayer Player { get; }

    public ScriptStudioViewModel()
    {
        Caption = "편집";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/document-edit.png");

        Script = new ScriptWorkbench(new ScriptWorkbenchHost
        {
            GetSetting = (key, fallback) => GetSetting(key, fallback),
            SetSetting = (key, value) => SetSetting(key, value),
            OnUi = RunOnUi,
            OpenDialog = () => OpenFileDialogService,
            SaveDialog = () => SaveFileDialogService
        });

        Player = new ScriptPlayer(ResolveRun);

        // 도는 동안 글을 잠근다. 도중에 바뀌면 무엇이 나갔는지 알 수 없다.
        Player.RunningChanged += (_, _) => Script.IsLocked = Player.IsRunning;
    }

    /// <summary>UI 스레드에서 돌린다. 검증 하네스처럼 서비스가 없는 자리에서도 배선은 돌아야 한다.</summary>
    private static void RunOnUi(System.Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    /// <summary>
    /// 돌릴 것을 모은다. 틀린 줄이 있으면 안 돌린다 - 반쪽짜리가 나가는 것보다 안 나가는 것이 낫다.
    /// </summary>
    private ScriptRunContext? ResolveRun()
    {
        if (Script.HasError)
        {
            MessengerUtility.SendMainMessage("스크립트에 고칠 줄이 있습니다.");
            return null;
        }

        if (_inputRouter is null) return null;

        var service = new InputService(_inputRouter.InputAdapter);

        return new ScriptRunContext(Script.Plan, service, ActivateTargetAsync);
    }

    /// <summary>보내기 직전에 대상 창을 앞으로. 끌어올렸으면 포그라운드 전환이 반영될 때까지 잠깐 기다린다.</summary>
    private async Task ActivateTargetAsync()
    {
        if (_inputRouter?.TryFocusTargetWindow() == true)
            await Task.Delay(ActivationSettleDelayMs);
    }

    protected override void RestoreSettings()
    {
        base.RestoreSettings();

        Script.Restore();
        Player.Restore((key, fallback) => GetSetting(key, fallback));
    }

    protected override void SaveSettings()
    {
        base.SaveSettings();

        Script.Save();
        Player.Save((key, value) => SetSetting(key, value));
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        Script.ApplyEditorTheme();

        // 첫 준비가 유독 느리다(C# 은 첫 컴파일, 파이썬은 런타임 받기). 미리 치러 둔다.
        _ = Script.PrepareAsync();
    }

    protected override void ReleaseResources()
    {
        Player.Stop();
        Script.Dispose();

        base.ReleaseResources();
    }
}
