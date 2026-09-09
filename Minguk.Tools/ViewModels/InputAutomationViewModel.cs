using System;
using System.Threading;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using Minguk.Image;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Hotkeys;
using Minguk.Tools.Input.Sequencing;

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
    private IGlobalHotkeyAdapter? _hotkeys;

    /// <summary>단계 목록이 통째로 들어가는 설정 키.</summary>
    private const string StepsSettingKey = "Steps";

    public InputAutomationViewModel()
    {
        Caption = "입력 자동화";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/hardwarenetwork/16x16/keyboard.png");

        DoRunOnceCommand = new DelegateCommand(DoRunOnce, () => IsIdle, false);
        DoStartLoopCommand = new DelegateCommand(DoStartLoop, () => IsIdle, false);
        DoStopCommand = new DelegateCommand(DoStop, () => IsRunning, false);

        DoAddStepCommand = new DelegateCommand<SequenceStepKind>(DoAddStep, _ => IsIdle, false);
        DoRemoveStepCommand = new DelegateCommand(DoRemoveStep, () => SelectedStep is not null, false);
        DoDuplicateStepCommand = new DelegateCommand(DoDuplicateStep, () => SelectedStep is not null, false);
        DoMoveStepUpCommand = new DelegateCommand(DoMoveStepUp, () => SelectedStep is not null, false);
        DoMoveStepDownCommand = new DelegateCommand(DoMoveStepDown, () => SelectedStep is not null, false);
        DoResetStepsCommand = new DelegateCommand(DoResetSteps, () => IsIdle, false);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────
    // InitializeControls() 는 잡을 컨트롤이 없어 비워 둔다.
    // 타이밍 값이 바뀔 때 순서 문구를 갱신하는 것은 각 프로퍼티의 SetProperty 콜백이 한다.

    /// <remarks>
    /// RestoreSettings 보다 먼저 돈다. 그래서 복구하며 담는 줄도 이 구독을 탄다.
    /// </remarks>
    protected override void InitializeObservable()
    {
        Steps.CollectionChanged += OnStepsChanged;
    }

    protected override void RestoreSettings()
    {
        SelectedInputBackend = Enum.TryParse<InputBackend>(
            GetSetting(nameof(SelectedInputBackend), nameof(InputBackend.SendInput)), out var backend)
            ? backend
            : InputBackend.SendInput;

        RestoreSteps();

        HoldTimeMs = GetSetting(nameof(HoldTimeMs), 30);
        IntervalMs = GetSetting(nameof(IntervalMs), 60);
        JitterMs = GetSetting(nameof(JitterMs), 0);
        // 손으로 대상 창을 앞으로 가져오려면 3초는 빠듯하다.
        StartDelaySeconds = GetSetting(nameof(StartDelaySeconds), 5);
        MaxLoops = GetSetting(nameof(MaxLoops), 10);
    }

    /// <summary>
    /// 저장해 둔 단계들을 되읽는다.
    /// </summary>
    /// <remarks>
    /// 설정 문자열 하나에 JSON 으로 넣는다. 단계마다 설정 키를 만들면 개수가 줄었을 때
    /// 남는 키를 지워야 하는데, 그 뒤처리를 어디선가 빠뜨리면 예전 단계가 되살아난다.
    /// </remarks>
    private void RestoreSteps()
    {
        var plan = SequencePlan.FromJson(GetSetting(StepsSettingKey, string.Empty), out var error);

        if (error is not null)
            Logger.Warn($"저장된 시퀀스를 읽지 못해 기본값으로 되돌린다: {error}");

        foreach (var step in plan.Steps) Steps.Add(step);

        SelectedStep = Steps.Count > 0 ? Steps[0] : null;
    }

    protected override void OnLoaded()
    {
        ApplyBackend();
        UpdateSequenceText();
        RegisterHotkeys();
    }

    protected override void SaveSettings()
    {
        SetSetting(nameof(SelectedInputBackend), SelectedInputBackend.ToString());
        SetSetting(StepsSettingKey, new SequencePlan { Steps = [.. Steps] }.ToJson());

        SetSetting(nameof(HoldTimeMs), HoldTimeMs);
        SetSetting(nameof(IntervalMs), IntervalMs);
        SetSetting(nameof(JitterMs), JitterMs);
        SetSetting(nameof(StartDelaySeconds), StartDelaySeconds);
        SetSetting(nameof(MaxLoops), MaxLoops);
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

        Steps.CollectionChanged -= OnStepsChanged;

        foreach (var step in Steps) step.PropertyChanged -= OnStepEdited;

        // 놓아 주지 않으면 앱이 살아 있는 동안 그 조합이 잠긴 채로 남는다.
        _hotkeys?.Dispose();
        _hotkeys = null;
    });
}
