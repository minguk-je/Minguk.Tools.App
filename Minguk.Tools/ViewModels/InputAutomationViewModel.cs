using System;
using System.Collections.Generic;
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

    /// <summary>스크립트에서 읽어 낸 계획. 실행할 때 이것으로 시퀀스를 만든다.</summary>
    private SequencePlan _plan = new();

    /// <summary>스크립트를 돌려 단계를 받아 오는 것. 언어를 바꾸면 갈아 끼운다.</summary>
    private IScriptEngine? _engine;

    /// <summary>타이핑이 멎기를 기다리는 타이머.</summary>
    private System.Threading.Timer? _debounce;

    /// <summary>돌고 있는 컴파일을 접는 데 쓴다.</summary>
    private CancellationTokenSource? _compileCts;

    /// <summary>스크립트가 통째로 들어가는 설정 키.</summary>
    private const string ScriptSettingKey = "Script";

    /// <summary>
    /// 언어마다 따로 둔 글. 언어를 바꾸면 쓰던 글을 여기 넣어 두고 그 언어의 글을 꺼낸다.
    /// </summary>
    /// <remarks>
    /// 하나만 들고 있으면 파이썬으로 바꿔 놓고 C# 글을 보게 되고, 그 상태로 저장하면 C# 이 든
    /// .py 가 나온다 - 실제로 "파이썬 골랐더니 스크립트는 C#" 이었다. 언어별로 기억하면
    /// 왔다 갔다 해도 각자 것이 그대로 있다. 설정 키는 <c>Script.CSharp</c> 처럼 언어를 붙인다.
    /// </remarks>
    private readonly Dictionary<ScriptLanguage, (string Text, string? Path, bool Dirty)> _scriptsByLanguage = new();

    /// <summary>지금 화면에 올라 있는 글이 어느 언어의 것인지. SetProperty 콜백은 예전 값을 안 알려 준다.</summary>
    private ScriptLanguage _shownLanguage;

    private static string ScriptKeyFor(ScriptLanguage language) => $"{ScriptSettingKey}.{language}";
    private static string ScriptPathKeyFor(ScriptLanguage language) => $"{ScriptPathSettingKey}.{language}";

    /// <summary>
    /// 마지막으로 열었던 파일 경로가 들어가는 설정 키.
    /// </summary>
    /// <remarks>
    /// 글 자체(<see cref="ScriptSettingKey"/>)도 따로 저장한다. 파일에 저장하지 않은 글이
    /// 다음에 열 때 사라지면 안 되기 때문이다. 경로는 "무엇을 보고 있었나" 를 되살리는 데 쓴다.
    /// </remarks>
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

        DoNewScriptCommand = new DelegateCommand(DoNewScript, () => IsIdle, false);
        DoOpenScriptCommand = new DelegateCommand(DoOpenScript, () => IsIdle, false);
        DoSaveScriptCommand = new DelegateCommand(DoSaveScript, () => true, false);
        DoSaveScriptAsCommand = new DelegateCommand(DoSaveScriptAs, () => true, false);

        // 도는 동안에도 눌린다. 한 바퀴 돌려 보고 지우고 다시 돌리는 것이 흔한 흐름이다.
        DoClearTestPadCommand = new DelegateCommand(() => TestPadText = string.Empty, () => true, false);

        DoRefreshWindowsCommand = new DelegateCommand(DoRefreshWindows, () => IsIdle, false);
        DoInstallDriverCommand = new DelegateCommand(DoInstallDriver, () => IsIdle && CanInstallDriver, false);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────
    // InitializeObservable() 은 구독할 이벤트가 없어 비워 둔다.
    // 글이 바뀔 때 다시 읽는 것은 ScriptText 의 SetProperty 콜백이 한다.

    /// <remarks>
    /// 편집기를 못 잡아도 화면은 돈다 - 줄 담기가 캐럿 자리 대신 끝에 붙을 뿐이다.
    /// 그래서 여기서 막지 않는다.
    /// </remarks>
    /// <remarks>
    /// 테마가 바뀔 때 편집기 색을 다시 재려고 구독한다. 정적 이벤트라 <see cref="ReleaseResources"/>
    /// 에서 반드시 풀어야 한다.
    /// </remarks>
    protected override void InitializeObservable()
        => DevExpress.Xpf.Core.LightweightThemeManager.CurrentThemeChanged += OnApplicationThemeChanged;

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

        SelectedScriptLanguage = Enum.TryParse<ScriptLanguage>(
            GetSetting(nameof(SelectedScriptLanguage), nameof(ScriptLanguage.CSharp)), out var language)
            ? language
            : ScriptLanguage.CSharp;

        // 언어를 넣어도 SetProperty 의 콜백은 값이 같으면 안 돈다. 엔진은 여기서 확실히 만든다.
        _engine ??= ScriptEngineFactory.Create(SelectedScriptLanguage);
        _shownLanguage = SelectedScriptLanguage;

        // 언어별로 저장해 둔 글을 전부 되읽는다. 지금 언어 것만 화면에 올린다.
        foreach (var each in ScriptLanguages)
        {
            var text = GetSetting(ScriptKeyFor(each), string.Empty);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var path = GetSetting(ScriptPathKeyFor(each), string.Empty);
            _scriptsByLanguage[each] = (text, string.IsNullOrEmpty(path) ? null : path, false);
        }

        if (_scriptsByLanguage.TryGetValue(SelectedScriptLanguage, out var shown))
        {
            ScriptText = shown.Text;
            ScriptFilePath = shown.Path;
        }
        else
        {
            // 언어별 저장이 생기기 전의 설정(Script / ScriptPath)은 그때 고른 언어의 것이다.
            ScriptFilePath = GetSetting(ScriptPathSettingKey, string.Empty) is { Length: > 0 } saved ? saved : null;
            RestoreScript();
        }

        // 되살린 글은 아직 아무것도 안 고친 상태다.
        IsScriptDirty = false;

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
            // 예전에는 자체 형식(글자 "..." / Enter)이었다. 그 글은 C# 으로 컴파일되지 않으므로
            // 한 번 읽어 옮겨 준다. 새로 쓰는 것은 전부 C# 이다.
            ScriptText = SequenceScript.TryParse(saved, out var legacyPlan, out var legacyErrors) && legacyErrors.Count == 0
                ? SequenceScript.ToCSharp(legacyPlan)
                : saved;

            if (!ReferenceEquals(ScriptText, saved))
                Logger.Info("예전 형식의 시퀀스를 C# 스크립트로 옮겼다.");

            return;
        }

        var legacy = GetSetting(LegacyStepsSettingKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var plan = SequencePlan.FromJson(legacy, out var error);

            if (error is not null) Logger.Warn($"예전에 저장된 시퀀스를 읽지 못했다: {error}");

            ScriptText = SequenceScript.ToCSharp(plan);
            Logger.Info("예전 JSON 시퀀스를 C# 스크립트로 옮겼다.");
            return;
        }

        ScriptText = _engine?.SampleSource ?? string.Empty;
    }

    protected override void OnLoaded()
    {
        ApplyBackend();
        UpdateSequenceText();
        RegisterHotkeys();

        ApplyEditorTheme();
        UpdateDriverNotice();

        RaisePropertyChanged(nameof(NeedsWindowTarget));

        if (NeedsWindowTarget) DoRefreshWindows();

        // 첫 준비가 유독 느리다(C# 은 첫 컴파일, 파이썬은 런타임 받기). 미리 치러 둔다.
        _ = PrepareEngineAsync();
    }

    protected override void SaveSettings()
    {
        SetSetting(nameof(SelectedInputBackend), SelectedInputBackend.ToString());
        SetSetting(nameof(SelectedScriptLanguage), SelectedScriptLanguage.ToString());

        // 화면의 글을 제 언어 칸에 넣고 언어별로 전부 저장한다.
        StashShownScript();

        foreach (var (language, script) in _scriptsByLanguage)
        {
            SetSetting(ScriptKeyFor(language), script.Text);
            SetSetting(ScriptPathKeyFor(language), script.Path ?? string.Empty);
        }

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

        DevExpress.Xpf.Core.LightweightThemeManager.CurrentThemeChanged -= OnApplicationThemeChanged;

        _editor = null;

        _debounce?.Dispose();
        _debounce = null;

        _compileCts?.Cancel();
        _compileCts?.Dispose();
        _compileCts = null;

        _engine?.Dispose();
        _engine = null;

        _windows?.Dispose();
        _windows = null;

        // 놓아 주지 않으면 앱이 살아 있는 동안 그 조합이 잠긴 채로 남는다.
        _hotkeys?.Dispose();
        _hotkeys = null;
    });
}
