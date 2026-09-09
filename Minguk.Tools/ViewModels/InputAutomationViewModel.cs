using System;
using System.Threading;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using ICSharpCode.AvalonEdit;
using Minguk.Image;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Hotkeys;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 입력 시퀀스를 짜서 대상 창에 보내는 화면.
///
/// 무엇을 하는 화면인가
///   글자·클릭·이동·휠을 <b>스크립트로 적어</b> 한 바퀴 돌리거나 반복한다.
///   형식은 Input/Sequencing/SequenceScript 에 적혀 있다.
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

    /// <summary>편집기. 줄을 캐럿 자리에 끼우려면 필요하다.</summary>
    private TextEditor? _editor;

    /// <summary>스크립트에서 읽어 낸 계획. 실행할 때 이것으로 시퀀스를 만든다.</summary>
    private SequencePlan _plan = new();

    /// <summary>스크립트가 통째로 들어가는 설정 키.</summary>
    private const string ScriptSettingKey = "Script";

    /// <summary>
    /// 스크립트를 쓰기 전, 단계 목록을 JSON 으로 넣던 키.
    /// </summary>
    /// <remarks>
    /// 예전에 저장해 둔 것을 한 번 읽어 글로 옮겨 주려고 남겨 둔다.
    /// 옮기고 나면 <see cref="ScriptSettingKey"/> 만 쓴다.
    /// </remarks>
    private const string LegacyStepsSettingKey = "Steps";

    public InputAutomationViewModel()
    {
        Caption = "입력 자동화";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/hardwarenetwork/16x16/keyboard.png");

        DoRunOnceCommand = new DelegateCommand(DoRunOnce, () => IsIdle, false);
        DoStartLoopCommand = new DelegateCommand(DoStartLoop, () => IsIdle, false);
        DoStopCommand = new DelegateCommand(DoStop, () => IsRunning, false);

        DoAddStepCommand = new DelegateCommand<SequenceStepKind>(DoAddStep, _ => IsIdle, false);
        DoResetStepsCommand = new DelegateCommand(DoResetSteps, () => IsIdle, false);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────
    // InitializeObservable() 은 구독할 이벤트가 없어 비워 둔다.
    // 글이 바뀔 때 다시 읽는 것은 ScriptText 의 SetProperty 콜백이 한다.

    /// <remarks>
    /// 편집기를 못 잡아도 화면은 돈다 - 줄 담기가 캐럿 자리 대신 끝에 붙을 뿐이다.
    /// 그래서 여기서 막지 않는다.
    /// </remarks>
    protected override void InitializeControls()
    {
        _editor = FindControl<TextEditor>("EditorObjectService");

        if (_editor is null) Logger.Warn("스크립트 편집기를 찾지 못했다. 줄 담기는 글 끝에 붙는다.");
    }

    protected override void RestoreSettings()
    {
        SelectedInputBackend = Enum.TryParse<InputBackend>(
            GetSetting(nameof(SelectedInputBackend), nameof(InputBackend.SendInput)), out var backend)
            ? backend
            : InputBackend.SendInput;

        RestoreScript();

        HoldTimeMs = GetSetting(nameof(HoldTimeMs), 30);
        IntervalMs = GetSetting(nameof(IntervalMs), 60);
        JitterMs = GetSetting(nameof(JitterMs), 0);
        // 손으로 대상 창을 앞으로 가져오려면 3초는 빠듯하다.
        StartDelaySeconds = GetSetting(nameof(StartDelaySeconds), 5);
        MaxLoops = GetSetting(nameof(MaxLoops), 10);
    }

    /// <summary>
    /// 저장해 둔 스크립트를 되읽는다. 없으면 예전 JSON 을 글로 옮기고, 그것도 없으면 기본값.
    /// </summary>
    /// <remarks>
    /// 설정 문자열 하나에 글을 통째로 넣는다. 단계마다 설정 키를 만들면 개수가 줄었을 때
    /// 남는 키를 지워야 하는데, 그 뒤처리를 어디선가 빠뜨리면 예전 단계가 되살아난다.
    /// </remarks>
    private void RestoreScript()
    {
        var saved = GetSetting(ScriptSettingKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(saved))
        {
            ScriptText = saved;
            return;
        }

        var legacy = GetSetting(LegacyStepsSettingKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var plan = SequencePlan.FromJson(legacy, out var error);

            if (error is not null) Logger.Warn($"예전에 저장된 시퀀스를 읽지 못했다: {error}");

            ScriptText = SequenceScript.ToText(plan);
            Logger.Info("예전 JSON 시퀀스를 스크립트로 옮겼다.");
            return;
        }

        ScriptText = SequenceScript.ToText(SequencePlan.CreateDefault());
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
        SetSetting(ScriptSettingKey, ScriptText ?? string.Empty);

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

        _editor = null;

        // 놓아 주지 않으면 앱이 살아 있는 동안 그 조합이 잠긴 채로 남는다.
        _hotkeys?.Dispose();
        _hotkeys = null;
    });
}
