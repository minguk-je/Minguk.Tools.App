using System;
using System.Threading;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using Minguk.Image;
using Minguk.Tools.Input;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 입력 시퀀스를 짜서 대상 창에 보내는 화면.
///
/// 무엇을 하는 화면인가
///   글자·클릭·이동·휠을 순서대로 묶어 한 바퀴 돌리거나 반복한다.
///   보내는 경로(SendInput · PostMessage · Interception)를 골라 같은 시퀀스를 다르게 흘릴 수 있다.
///
/// 이 화면의 특별한 점
///   보낸 입력이 <b>포커스를 가진 창</b>으로 간다. 이 앱이 앞에 있으면 이 화면의 입력란에
///   글자가 들어간다. 그래서 시작 전 대기(StartDelaySeconds)를 두어 그 사이에 대상 창을
///   앞으로 가져오게 하고, 도는 동안에는 설정을 잠근다.
/// </summary>
public partial class InputAutomationViewModel : DocumentViewModelBase
{
    public static InputAutomationViewModel Create() => ViewModelSource.Create(() => new InputAutomationViewModel());

    private IInputAdapter? _adapter;
    private InputService? _service;
    private CancellationTokenSource? _cts;

    public InputAutomationViewModel()
    {
        Caption = "입력 자동화";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/hardwarenetwork/16x16/keyboard.png");

        DoRunOnceCommand = new DelegateCommand(DoRunOnce, () => IsIdle, false);
        DoStartLoopCommand = new DelegateCommand(DoStartLoop, () => IsIdle, false);
        DoStopCommand = new DelegateCommand(DoStop, () => IsRunning, false);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────
    // InitializeControls() / InitializeObservable() 는 잡을 컨트롤도 구독할 이벤트도 없어 비워 둔다.
    // 옵션이 바뀔 때 순서 문구를 갱신하는 것은 각 프로퍼티의 SetProperty 콜백이 한다.

    protected override void RestoreSettings()
    {
        SelectedInputBackend = Enum.TryParse<InputBackend>(
            GetSetting(nameof(SelectedInputBackend), nameof(InputBackend.SendInput)), out var backend)
            ? backend
            : InputBackend.SendInput;

        Text = GetSetting(nameof(Text), "안녕하세요");
        IncludeEnter = GetSetting(nameof(IncludeEnter), true);
        IncludeClick = GetSetting(nameof(IncludeClick), false);
        IncludeMove = GetSetting(nameof(IncludeMove), false);
        IncludeScroll = GetSetting(nameof(IncludeScroll), false);
        ScrollNotches = GetSetting(nameof(ScrollNotches), -1);

        HoldTimeMs = GetSetting(nameof(HoldTimeMs), 30);
        IntervalMs = GetSetting(nameof(IntervalMs), 60);
        JitterMs = GetSetting(nameof(JitterMs), 0);
        StartDelaySeconds = GetSetting(nameof(StartDelaySeconds), 3);
        MaxLoops = GetSetting(nameof(MaxLoops), 10);

        var screen = VirtualScreen.GetBounds();
        MoveX = GetSetting(nameof(MoveX), screen.Left + screen.Width / 2);
        MoveY = GetSetting(nameof(MoveY), screen.Top + screen.Height / 2);
    }

    protected override void OnLoaded()
    {
        ApplyBackend();
        UpdateSequenceText();
    }

    protected override void SaveSettings()
    {
        SetSetting(nameof(SelectedInputBackend), SelectedInputBackend.ToString());
        SetSetting(nameof(Text), Text);
        SetSetting(nameof(IncludeEnter), IncludeEnter);
        SetSetting(nameof(IncludeClick), IncludeClick);
        SetSetting(nameof(IncludeMove), IncludeMove);
        SetSetting(nameof(IncludeScroll), IncludeScroll);
        SetSetting(nameof(ScrollNotches), ScrollNotches);

        SetSetting(nameof(HoldTimeMs), HoldTimeMs);
        SetSetting(nameof(IntervalMs), IntervalMs);
        SetSetting(nameof(JitterMs), JitterMs);
        SetSetting(nameof(StartDelaySeconds), StartDelaySeconds);
        SetSetting(nameof(MaxLoops), MaxLoops);
        SetSetting(nameof(MoveX), MoveX);
        SetSetting(nameof(MoveY), MoveY);
    }

    protected override void ReleaseResources() => Guard(() =>
    {
        // 화면을 닫을 때 돌고 있으면 멈추고, 어댑터가 든 자원(드라이버 컨텍스트)을 놓아 준다.
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _adapter?.Dispose();
        _adapter = null;
        _service = null;
    });
}
