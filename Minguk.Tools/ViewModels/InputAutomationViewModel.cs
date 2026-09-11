using System;
using System.Threading;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using ICSharpCode.AvalonEdit;
using Minguk.Image;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Adapters;
using Minguk.Tools.Input.Hotkeys;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Sequencing;
using Minguk.Tools.Input.Targets;

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

    /// <summary>대상 창을 찾아 주는 것. PostMessage 경로에서만 쓴다.</summary>
    private IWindowTargetAdapter? _windows;

    /// <summary>
    /// 스크립트 문서 - 언어, 글, 파일, 계획, 틀린 줄, 편집기 색. XAML 은 <c>Script.Text</c> 처럼 묶는다.
    /// </summary>
    /// <remarks>
    /// 예전에는 이 화면이 전부 제 손으로 했다(언어별 글 기억, 디바운스 컴파일, 파일 열고 저장).
    /// 스크립트·플레이 화면이 같은 일을 하게 되면서 <see cref="ScriptWorkbench"/> 로 옮겼다 - 세 벌이면
    /// 밑줄·완성 같은 것이 한쪽에만 붙는다. 설정 키(<c>Script.CSharp</c> 등)는 그대로라 저장된 글이 이어진다.
    /// </remarks>
    public ScriptWorkbench Script { get; }

    /// <summary>언어별로 나누기 전, 스크립트가 통째로 들어가던 설정 키. 한 번 읽어 옮기는 데만 쓴다.</summary>
    private const string ScriptSettingKey = "Script";

    /// <summary>언어별로 나누기 전의 파일 경로 키. 한 번 읽어 옮기는 데만 쓴다.</summary>
    private const string ScriptPathSettingKey = "ScriptPath";

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

        Script = new ScriptWorkbench(new ScriptWorkbenchHost
        {
            GetSetting = (key, fallback) => GetSetting(key, fallback),
            SetSetting = (key, value) => SetSetting(key, value),
            OnUi = RunOnUi,
            OpenDialog = () => OpenFileDialogService,
            SaveDialog = () => SaveFileDialogService
        });

        // 계획이 새로 나오면 순서 미리보기를 다시 그린다.
        Script.PlanChanged += (_, _) => UpdateSequenceText();

        // 도는 동안에도 눌린다. 한 바퀴 돌려 보고 지우고 다시 돌리는 것이 흔한 흐름이다.
        DoClearTestPadCommand = new DelegateCommand(() => TestPadText = string.Empty, () => true, false);

        DoRefreshWindowsCommand = new DelegateCommand(DoRefreshWindows, () => IsIdle, false);
        DoInstallDriverCommand = new DelegateCommand(DoInstallDriver, () => IsIdle && CanInstallDriver, false);
    }

    /// <summary>UI 스레드에서 돌린다. 검증 하네스처럼 서비스가 없는 자리에서도 배선은 돌아야 한다.</summary>
    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────
    // InitializeObservable() 은 구독할 이벤트가 없어 비워 둔다. 테마는 Script 가 스스로 듣는다.

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

        Script.Restore();
        MigrateLegacyScript();

        HoldTimeMs = GetSetting(nameof(HoldTimeMs), 30);
        IntervalMs = GetSetting(nameof(IntervalMs), 60);
        JitterMs = GetSetting(nameof(JitterMs), 0);
        // 손으로 대상 창을 앞으로 가져오려면 3초는 빠듯하다.
        StartDelaySeconds = GetSetting(nameof(StartDelaySeconds), 5);
        MaxLoops = GetSetting(nameof(MaxLoops), 10);
    }

    /// <summary>
    /// 언어별 저장이 생기기 전의 설정(Script / ScriptPath / Steps)을 한 번 읽어 옮긴다.
    /// </summary>
    /// <remarks>
    /// 지금 언어의 글이 이미 언어별 키에 있으면 할 일이 없다. 없을 때만 옛 키를 본다 -
    /// 자체 형식(글자 "..." / Enter)이면 C# 으로, 그보다 전의 JSON 단계 목록이면 그것도 C# 으로.
    /// </remarks>
    private void MigrateLegacyScript()
    {
        if (HasSetting($"{ScriptSettingKey}.{Script.SelectedLanguage}")) return;

        var savedPath = GetSetting(ScriptPathSettingKey, string.Empty);
        var saved = GetSetting(ScriptSettingKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(saved))
        {
            var moved = SequenceScript.TryParse(saved, out var legacyPlan, out var legacyErrors) && legacyErrors.Count == 0
                ? SequenceScript.ToCSharp(legacyPlan)
                : saved;

            if (!ReferenceEquals(moved, saved)) Logger.Info("예전 형식의 시퀀스를 C# 스크립트로 옮겼다.");

            Script.Text = moved;
            Script.FilePath = savedPath.Length > 0 ? savedPath : null;
            Script.IsDirty = false;
            return;
        }

        var legacy = GetSetting(LegacyStepsSettingKey, string.Empty);

        if (string.IsNullOrWhiteSpace(legacy)) return;

        var plan = SequencePlan.FromJson(legacy, out var error);

        if (error is not null) Logger.Warn($"예전에 저장된 시퀀스를 읽지 못했다: {error}");

        Script.Text = SequenceScript.ToCSharp(plan);
        Script.IsDirty = false;
        Logger.Info("예전 JSON 시퀀스를 C# 스크립트로 옮겼다.");
    }

    protected override void OnLoaded()
    {
        ApplyBackend();
        UpdateSequenceText();
        RegisterHotkeys();

        Script.ApplyEditorTheme();
        UpdateDriverNotice();

        RaisePropertyChanged(nameof(NeedsWindowTarget));

        if (NeedsWindowTarget) DoRefreshWindows();

        // 첫 준비가 유독 느리다(C# 은 첫 컴파일, 파이썬은 런타임 받기). 미리 치러 둔다.
        _ = Script.PrepareAsync();
    }

    protected override void SaveSettings()
    {
        SetSetting(nameof(SelectedInputBackend), SelectedInputBackend.ToString());

        Script.Save();

        // 옛 키는 비운다. 남겨 두면 언어별 키가 없는 언어로 바꿨을 때 엉뚱한 언어의 글이 되살아난다.
        SetSetting(ScriptSettingKey, string.Empty);
        SetSetting(ScriptPathSettingKey, string.Empty);

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

        Script.Dispose();

        _windows?.Dispose();
        _windows = null;

        // 놓아 주지 않으면 앱이 살아 있는 동안 그 조합이 잠긴 채로 남는다.
        _hotkeys?.Dispose();
        _hotkeys = null;
    });
}
