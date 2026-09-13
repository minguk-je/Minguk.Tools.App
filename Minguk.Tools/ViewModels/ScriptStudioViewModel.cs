using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using DevExpress.Xpf.Docking;

using Minguk.Image;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Hotkeys;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;
using Minguk.Tools.Markup;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 스크립트 화면. 창을 잡아 몹 찾기·글자 읽기를 켜 놓고 <b>보면서</b> 스크립트를 쓰고 한 번씩 돌려 본다.
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

    /// <summary>실시간 실행에 필요한 것들 - 출력 칸, 비상 정지, API 에 빌려 줄 것.</summary>
    public LiveScriptSession Live { get; }

    /// <summary>F5. 멈춰 있으면 계속, 아니면 처음부터.</summary>
    public DelegateCommand RunOrContinueCommand { get; }

    /// <summary>F10. 멈춰 있으면 다음 줄, 아니면 첫 줄에서 멈추게 시작.</summary>
    public DelegateCommand StepCommand { get; }

    /// <summary>캐럿이 있는 줄의 중단점을 켜고 끈다. 편집기 여백을 눌러도 된다.</summary>
    public DelegateCommand ToggleBreakpointCommand { get; }

    private readonly List<HotkeyClaim> _hotkeyClaims = [];
    private ScriptEditor? _editor;

    public ScriptStudioViewModel()
    {
        Caption = "스크립트";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/document-edit.png");

        Script = new ScriptWorkbench(new ScriptWorkbenchHost
        {
            GetSetting = (key, fallback) => GetSetting(key, fallback),
            SetSetting = (key, value) => SetSetting(key, value),
            OnUi = RunOnUi,
            OpenDialog = () => OpenFileDialogService,
            SaveDialog = () => SaveFileDialogService,
            Ask = (message, button) => MessageBoxService?.ShowMessage(message, "스크립트", button, MessageIcon.Question) ?? MessageResult.Yes,
            IsLive = true
        });

        Live = new LiveScriptSession(
            () => _inputRouter is null ? null : new InputService(_inputRouter.InputAdapter),
            () => _inputRouter?.InputAdapter.RequiresForegroundTarget ?? true,
            () => SelectedTarget,
            OcrEngineForScripts,
            () => RegionBook,
            ActivateTargetAsync,
            RunOnUi,
            message => RunOnUi(() => StatusText = message));

        Player = new ScriptPlayer(() => Live.Resolve(Script, Player));

        // 도는 동안 글을 잠근다. 도중에 바뀌면 무엇이 나갔는지 알 수 없다.
        Player.RunningChanged += (_, _) => Script.IsLocked = Player.IsRunning;

        RunOrContinueCommand = new DelegateCommand(RunOrContinue, false);
        StepCommand = new DelegateCommand(Step, false);
        ToggleBreakpointCommand = new DelegateCommand(ToggleBreakpoint, false);

        SaveCommand = new DelegateCommand(() => Guard(() => { if (Script.IsProject) Script.Project.ActiveDocument?.Save(); else Script.SaveCommand.Execute(null); }), false);
        SaveAsCommand = new DelegateCommand(() => Script.SaveAsCommand.Execute(null), () => !Script.IsProject, false);
        SaveAllCommand = new DelegateCommand(() => Guard(() => { if (Script.IsProject) Script.Project.SaveAll(); else Script.SaveCommand.Execute(null); }), false);
        CloseDocumentCommand = new DelegateCommand(() => Guard(() => Script.Project.CloseDocument(Script.Project.ActiveDocument)), () => Script.IsProject, false);
        ShowToolWindowCommand = new DelegateCommand<string>(ShowToolWindow, false);
        ResetLayoutCommand = new DelegateCommand(ResetLayout, false);
        GoToErrorCommand = new DelegateCommand<object?>(GoToError, false);

        Script.Project.ProjectChanged += (_, _) =>
        {
            SaveAsCommand.RaiseCanExecuteChanged();
            CloseDocumentCommand.RaiseCanExecuteChanged();
        };
    }

    // ── VS 메뉴 ──────────────────────────────────────────────────────────

    /// <summary>Ctrl+S. 프로젝트면 지금 탭, 아니면 한 파일짜리.</summary>
    public DelegateCommand SaveCommand { get; }

    public DelegateCommand SaveAsCommand { get; }

    /// <summary>Ctrl+Shift+S.</summary>
    public DelegateCommand SaveAllCommand { get; }

    /// <summary>Ctrl+F4.</summary>
    public DelegateCommand CloseDocumentCommand { get; }

    /// <summary>보기 메뉴 - 닫은 도구 창을 다시 열고 앞으로. 인자는 창 이름(XAML 의 x:Name).</summary>
    public DelegateCommand<string> ShowToolWindowCommand { get; }

    /// <summary>창 > 창 레이아웃 다시 설정.</summary>
    public DelegateCommand ResetLayoutCommand { get; }

    /// <summary>오류 목록 더블 클릭 - 그 파일을 열고 그 줄로.</summary>
    public DelegateCommand<object?> GoToErrorCommand { get; }

    /// <summary>한 파일짜리 편집기에 "이 줄로" 요청.</summary>
    public EditorLineRequest? SingleLineRequest
    {
        get => GetProperty(() => SingleLineRequest);
        set => SetProperty(() => SingleLineRequest, value);
    }

    /// <summary>한 파일짜리 편집기의 캐럿 줄·열. 상태 표시줄이 본다.</summary>
    public int SingleCaretLine { get => GetProperty(() => SingleCaretLine); set => SetProperty(() => SingleCaretLine, value); }

    public int SingleCaretColumn { get => GetProperty(() => SingleCaretColumn); set => SetProperty(() => SingleCaretColumn, value); }

    /// <summary>
    /// F9 - VS 처럼 캐럿 줄의 중단점을 켜고 끈다. 프로젝트면 지금 탭의 파일에, 아니면 한 파일짜리 편집기에.
    /// </summary>
    private void ToggleBreakpoint()
    {
        if (Script.IsProject)
        {
            if (Script.Project.ActiveDocument is not { } doc) return;

            var line = Math.Max(1, doc.CaretLine);
            if (!doc.Breakpoints.Remove(line)) doc.Breakpoints.Add(line);
            return;
        }

        _editor?.ToggleBreakpointAtCaret();
    }

    private void GoToError(object? row) => Guard(() =>
    {
        if (row is not ScriptError error || error.Line <= 0) return;

        if (Script.IsProject && error.File is { } file && System.IO.File.Exists(file))
        {
            Script.Project.OpenFile(file).GoToLine(error.Line);
            return;
        }

        SingleLineRequest = new EditorLineRequest(error.Line);
    });

    // ── 도킹 배치 ────────────────────────────────────────────────────────

    private DevExpress.Xpf.Docking.DockLayoutManager? _dock;
    private string? _defaultLayout;

    private const string DockLayoutKey = "DockLayout";

    /// <summary>
    /// 배치 형식이 바뀌면 올린다 - 옛 배치를 새 화면에 되살리면 없는 창을 찾거나 새 창이 사라진다.
    /// </summary>
    private const int DockLayoutVersion = 1;

    private void ShowToolWindow(string? name) => Guard(() =>
    {
        if (_dock is null || string.IsNullOrEmpty(name)) return;

        if (_dock.GetItem(name) is not { } item) return;

        // 닫은 창이면 되살리고, 자동 숨김이면 펼치고, 그다음 앞으로.
        if (item.IsClosed) _dock.DockController.Restore(item);
        _dock.DockController.Activate(item);
    });

    private void ResetLayout() => Guard(() =>
    {
        if (_dock is null || _defaultLayout is null) return;

        RestoreLayout(_defaultLayout);
        SetSetting(DockLayoutKey, string.Empty);
        StatusText = "창 레이아웃을 처음대로 되돌렸습니다.";
    });

    private static string SaveLayout(DevExpress.Xpf.Docking.DockLayoutManager dock)
    {
        using var stream = new System.IO.MemoryStream();
        dock.SaveLayoutToStream(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    private void RestoreLayout(string base64)
    {
        using var stream = new System.IO.MemoryStream(Convert.FromBase64String(base64));
        _dock!.RestoreLayoutFromStream(stream);
    }

    /// <summary>
    /// 지난번 배치를 되살린다. 처음 배치(XAML 그대로)는 먼저 떠 둔다 - "창 레이아웃 다시 설정" 이 그것으로 돌아간다.
    /// </summary>
    /// <remarks>
    /// 문서 탭이 열리기 전(RestoreSettings 앞)에 한다. 탭은 컬렉션에서 만들어지는 것이라 배치에 적혀 있으면 되살릴 때 헷갈린다.
    /// 되살리다 터지면 처음 배치로 간다 - 배치 하나 때문에 화면이 안 뜨면 안 된다.
    /// </remarks>
    private void RestoreDockLayout()
    {
        if (_dock is null) return;

        try
        {
            _defaultLayout = SaveLayout(_dock);

            var saved = GetSetting(DockLayoutKey, string.Empty);
            if (GetSetting(DockLayoutKey + "Version", 0) == DockLayoutVersion && !string.IsNullOrEmpty(saved))
                RestoreLayout(saved);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "창 배치를 되살리지 못했다 - 처음 배치로 시작한다");

            try { if (_defaultLayout is not null) RestoreLayout(_defaultLayout); }
            catch (Exception) { }
        }
    }

    private void SaveDockLayout()
    {
        if (_dock is null) return;

        try
        {
            SetSetting(DockLayoutKey, SaveLayout(_dock));
            SetSetting(DockLayoutKey + "Version", DockLayoutVersion);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "창 배치를 저장하지 못했다");
        }
    }

    /// <summary>F5 - 멈춰 있으면 계속, 쉬고 있으면 처음부터. 도는 중이면 아무것도 안 한다.</summary>
    private void RunOrContinue()
    {
        Logger.Debug($"실행/계속 요청: 멈춤={Live.Debug.IsPaused}, 쉬는 중={Player.IsIdle}, 앞 창={ForegroundWindow.Describe()}");

        if (Live.Debug.IsPaused) Live.Debug.Continue();
        else if (Player.IsIdle) Player.RunOnce();
    }

    /// <summary>F6 - 대기 중이든 도는 중이든 멈춘다. 비상 정지(Pause)는 도는 동안만 걸리므로 대기 중에는 이것뿐이다.</summary>
    private void StopByHotkey()
    {
        Logger.Debug($"중지 요청(F6): 쉬는 중={Player.IsIdle}");
        Player.Stop();
    }

    /// <summary>F10 - 멈춰 있으면 다음 줄, 쉬고 있으면 첫 줄에서 멈추게 시작.</summary>
    private void Step()
    {
        if (Live.Debug.IsPaused)
        {
            Live.Debug.StepNext();
            return;
        }

        if (!Player.IsIdle) return;

        if (!Script.Engine.SupportsStepping)
        {
            StatusText = "C# 스크립트는 한 줄씩 밟을 수 없습니다(Roslyn 스크립트에는 디버거가 없다). 호출 로그로 보거나 JavaScript·Python 을 쓰세요.";
            return;
        }

        Live.Debug.Mode = ScriptStepMode.Step;
        Player.RunOnce();
    }

    /// <summary>
    /// F5·F10 을 전역으로 쥔다. 게임이 앞에 있어야 입력이 들어가므로 앱 밖에서 누를 수단이 있어야 한다.
    /// 플레이·입력 자동화 화면도 F5 를 쥔다 - 공용 단축키라 같이 열려 있어도 되고, 마지막에 본 화면이 받는다.
    /// </summary>
    private void RegisterStudioHotkeys() => Guard(() =>
    {
        (string Label, Key Key, System.Action Action)[] bindings =
        [
            ("F5 실행/계속", Key.F5, RunOrContinue),
            ("F6 중지", Key.F6, StopByHotkey),
            ("F10 한 줄", Key.F10, Step)
        ];

        var live = new List<string>();
        var failed = new List<string>();

        foreach (var (label, key, action) in bindings)
        {
            if (SharedHotkeysFactory.Default.Claim(key, ModifierKeys.None, label, action, out _) is { } claim)
            {
                _hotkeyClaims.Add(claim);
                live.Add(label);
            }
            else failed.Add(label);
        }

        StatusText = failed.Count == 0
            ? $"단축키: {string.Join(" · ", live)} · 도는 동안 Pause 비상 정지 · 마지막에 본 화면이 받습니다"
            : $"단축키: {string.Join(" · ", live)}  (등록 실패: {string.Join(", ", failed)} - 다른 프로그램이 쥐고 있습니다)";
    });

    /// <summary>탭이 앞으로 왔다. 이제 F5 는 여기가 받는다.</summary>
    protected override void OnActivated()
    {
        base.OnActivated();
        foreach (var claim in _hotkeyClaims) claim.Activate();
    }

    /// <summary>UI 스레드에서 돌린다. 검증 하네스처럼 서비스가 없는 자리에서도 배선은 돌아야 한다.</summary>
    private static void RunOnUi(System.Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
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

        // 문서 탭이 열리기 전에 창 배치부터.
        RestoreDockLayout();

        Script.Restore();
        Player.Restore((key, fallback) => GetSetting(key, fallback));
    }

    protected override void SaveSettings()
    {
        base.SaveSettings();

        SaveDockLayout();
        Script.Save();
        Player.Save((key, value) => SetSetting(key, value));
    }

    protected override void InitializeControls()
    {
        base.InitializeControls();

        _editor = FindControl<ScriptEditor>("EditorObjectService");
        _dock = FindControl<DevExpress.Xpf.Docking.DockLayoutManager>("DockObjectService");
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        RegisterStudioHotkeys();
        Script.ApplyEditorTheme();

        // 첫 준비가 유독 느리다(C# 은 첫 컴파일, 파이썬은 런타임 받기). 미리 치러 둔다.
        _ = Script.PrepareAsync();
    }

    protected override void ReleaseResources()
    {
        Player.Stop();

        // 놓아 주지 않으면 앱이 살아 있는 동안 그 키가 잠긴 채로 남는다.
        foreach (var claim in _hotkeyClaims) claim.Dispose();
        _hotkeyClaims.Clear();
        _editor = null;
        _dock = null;

        Live.Dispose();
        Script.Dispose();

        base.ReleaseResources();
    }
}
