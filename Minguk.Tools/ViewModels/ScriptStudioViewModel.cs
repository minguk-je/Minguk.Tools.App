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
        BuildCommand = new DelegateCommand(DoBuild, () => Script.IsProject && !Script.IsLocked, false);
        SplitVerticalCommand = new DelegateCommand(() => SetSplit(System.Windows.Controls.Orientation.Vertical), false);
        SplitHorizontalCommand = new DelegateCommand(() => SetSplit(System.Windows.Controls.Orientation.Horizontal), false);
        SwapPanesCommand = new DelegateCommand(SwapPanes, false);
        GoToErrorCommand = new DelegateCommand<object?>(GoToError, false);

        Script.Project.ProjectChanged += (_, _) =>
        {
            SaveAsCommand.RaiseCanExecuteChanged();
            CloseDocumentCommand.RaiseCanExecuteChanged();
            BuildCommand.RaiseCanExecuteChanged();
        };

        Player.RunningChanged += (_, _) => BuildCommand.RaiseCanExecuteChanged();
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

    /// <summary>미리보기 위 · 스크립트 아래. VS XAML 디자이너의 "가로 분할" 자리.</summary>
    public DelegateCommand SplitVerticalCommand { get; }

    /// <summary>미리보기와 스크립트를 나란히(기본).</summary>
    public DelegateCommand SplitHorizontalCommand { get; }

    /// <summary>미리보기와 스크립트의 자리를 맞바꾼다.</summary>
    public DelegateCommand SwapPanesCommand { get; }

    /// <summary>오류 목록 더블 클릭 - 그 파일을 열고 그 줄로.</summary>
    public DelegateCommand<object?> GoToErrorCommand { get; }

    /// <summary>Ctrl+Shift+B. 프로젝트를 .NET DLL(.mtsx)로 빌드한다 - 플레이어가 소스 없이 실행한다.</summary>
    public DelegateCommand BuildCommand { get; }

    /// <summary>
    /// 프로젝트를 IL 로 빌드해 프로젝트 폴더의 <c>bin\&lt;이름&gt;.mtsx</c> 로 쓴다. 리소스가 있으면 옆에 같이 복사한다.
    /// </summary>
    /// <remarks>
    /// 소스가 아니라 IL 이라 텍스트로는 못 본다(작정하면 디컴파일러로는 봄). 일반 사용자는 이 파일을 플레이어에서 실행만 한다.
    /// 진입점 이름이 우리 것(<see cref="CompiledScriptBuilder.EntryTypeName"/>)이라 Roslyn 을 올려도 예전 파일이 그대로 돈다.
    /// </remarks>
    private async void DoBuild()
    {
        try
        {
            if (Script.Project.ToUnit() is not { } unit || string.IsNullOrEmpty(unit.EntryPath))
            {
                StatusText = "빌드하려면 프로젝트를 열고 시작 파일을 정하세요 (솔루션 탐색기에서 .csx 오른쪽 → 시작 파일로 설정).";
                return;
            }

            if (Script.Engine is not ICompiledScriptEngine builder)
            {
                StatusText = "프로젝트는 C# 만 빌드합니다.";
                return;
            }

            var project = Script.Project.Project!;
            var name = project.Name;

            StatusText = $"'{name}' 을(를) 빌드하는 중...";
            Live.Console.Print($"빌드 시작: {name}");

            var (bytes, errors) = await builder.BuildAsync(unit, name);

            if (bytes is null || errors.Count > 0)
            {
                StatusText = $"빌드 실패: {(errors.Count > 0 ? errors[0].ToString() : "알 수 없는 오류")}";
                foreach (var error in System.Linq.Enumerable.Take(errors, 20)) Live.Console.Print($"빌드 오류: {error}");
                return;
            }

            // 완성품은 프로젝트 폴더의 bin 에 둔다(사격장/bin/사격장.mtsx) - 모델·영역·리소스가 이미 프로젝트 폴더에 있어 따로 복사할 것이 없다.
            // bin 인 이유: 솔루션 탐색기가 bin 을 안 봐서 .mtsx 가 프로젝트 파일 목록에 안 끼어든다.
            var outputDirectory = System.IO.Path.Combine(project.Directory, "bin");
            System.IO.Directory.CreateDirectory(outputDirectory);

            var outputPath = System.IO.Path.Combine(outputDirectory, name + ScriptFiles.CompiledExtension);
            await System.IO.File.WriteAllBytesAsync(outputPath, bytes);

            var resourceNote = CopyResources(project.Directory, outputDirectory);

            // 스크립트가 참조한 바깥 DLL(참조 항목·#r) - IL 은 이름으로만 가리켜 옆에 없으면 플레이에서 로드하다 실패한다.
            var references = CompiledScriptBuilder.CopyReferences(unit, outputDirectory);
            if (references.Count > 0) resourceNote += $" · 참조 DLL {references.Count}개 함께 복사({string.Join(", ", references)})";

            StatusText = $"빌드 완료: {outputPath} ({bytes.Length / 1024.0:0.#} KB){resourceNote}";
            Live.Console.Print($"빌드 완료: {outputPath} ({bytes.Length:N0}바이트){resourceNote}");
            Minguk.Base.Utilities.MessengerUtility.SendMainMessage($"'{name}' 빌드 완료 - 플레이 메뉴의 목록에서 골라 돌립니다 ({outputDirectory}).");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "빌드에 실패했다");
            StatusText = $"빌드 중 오류: {ex.Message}";
        }
    }

    /// <summary>프로젝트의 <c>Resources</c> 폴더를 빌드 결과물 옆으로 복사한다. 리소스는 소스가 아니라 파일이라 IL 에 못 넣는다.</summary>
    private static string CopyResources(string? projectDirectory, string outputDirectory)
    {
        if (string.IsNullOrEmpty(projectDirectory)) return string.Empty;

        var source = System.IO.Path.Combine(projectDirectory, "Resources");
        if (!System.IO.Directory.Exists(source)) return string.Empty;

        var target = System.IO.Path.Combine(outputDirectory, "Resources");
        var count = 0;

        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(source, file);
            var destination = System.IO.Path.Combine(target, relative);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            System.IO.File.Copy(file, destination, overwrite: true);
            count++;
        }

        return count > 0 ? $" · 리소스 {count}개 함께 복사(같이 옮기세요)" : string.Empty;
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

        Script.LineRequest = new EditorLineRequest(error.Line);
    });

    // ── 도킹 배치 ────────────────────────────────────────────────────────

    private DevExpress.Xpf.Docking.DockLayoutManager? _dock;
    private DevExpress.Xpf.Bars.BarManager? _bars;
    private string? _defaultLayout;

    private const string DockLayoutKey = "DockLayout";
    private const string BarLayoutKey = "BarLayout";

    /// <summary>
    /// 배치 형식이 바뀌면 올린다 - 옛 배치를 새 화면에 되살리면 없는 창을 찾거나 새 창이 사라진다.
    /// 3: 솔루션 탐색기 왼쪽 · 미리보기|문서 좌우 · 도구 모음 세 줄(2026-09-13). 옛 배치를 그대로 살리면 새 기본이 안 보인다.
    /// </summary>
    private const int DockLayoutVersion = 3;

    // ── 미리보기 | 문서 나누기 ──────────────────────────────────────────

    /// <summary>
    /// 미리보기와 문서 탭을 담은 그룹. 방향과 순서를 여기서 바꾼다.
    /// </summary>
    /// <remarks>도킹 참조는 서비스에서 바로 찾는다 - 부모 주입 없이 띄운 하네스(<c>--script-screen</c>)에서도 이 명령이 돌게.</remarks>
    private LayoutGroup? DesignSplitGroup => (_dock ??= FindControl<DockLayoutManager>("DockObjectService"))?.GetItem("DesignSplitGroup") as LayoutGroup;

    private void SetSplit(System.Windows.Controls.Orientation orientation) => Guard(() =>
    {
        if (DesignSplitGroup is not { } group) return;

        group.Orientation = orientation;
        StatusText = orientation == System.Windows.Controls.Orientation.Vertical ? "미리보기를 위, 스크립트를 아래에 두었습니다." : "미리보기와 스크립트를 나란히 두었습니다.";
    });

    /// <summary>
    /// 미리보기를 그룹의 반대쪽 끝으로 옮긴다. 문서 그룹은 둘(프로젝트·한 파일짜리)이라 "앞이면 맨 뒤로, 아니면 맨 앞으로" 다.
    /// </summary>
    private void SwapPanes() => Guard(() =>
    {
        if (DesignSplitGroup is not { } group || _dock?.GetItem("PreviewPanel") is not { } preview) return;

        // 미리보기가 떠 있거나 닫혀 있으면 그룹 안에 없다 - 먼저 되돌린다.
        if (!group.Items.Contains(preview)) _dock.DockController.Restore(preview);

        var index = group.Items.IndexOf(preview);
        if (index < 0) return;

        group.Remove(preview);
        group.Insert(index == 0 ? group.Items.Count : 0, preview);
        StatusText = index == 0 ? "미리보기를 뒤(아래·오른쪽)로 옮겼습니다." : "미리보기를 앞(위·왼쪽)으로 옮겼습니다.";
    });

    // ── 도구 모음 배치 ────────────────────────────────────────────────────

    /// <summary>
    /// 도구 모음을 끌어 옮긴 자리. 이름(x:Name)으로 되찾으므로 도구 모음마다 이름이 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 도구 모음은 <c>BarManager.Bars</c> 에 있어야 한다. 독립 <c>ToolBarControl</c> 을 컨테이너에 넣어 두면 관리자의 Bars 가
    /// 비어 배치 XML 이 빈 껍데기다(실측, <c>--script-screen</c> 이 이름 다섯 개가 들어 있는지 본다).
    /// </remarks>
    private void RestoreBarLayout()
    {
        if (_bars is not { } manager) return;

        var saved = GetSetting(BarLayoutKey, string.Empty);
        if (string.IsNullOrEmpty(saved)) return;

        try
        {
            using var stream = new System.IO.MemoryStream(Convert.FromBase64String(saved));
            manager.RestoreLayoutFromStream(stream);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "도구 모음 배치를 되살리지 못했다 - 처음 배치로 시작한다");
        }
    }

    private void SaveBarLayout()
    {
        if (_bars is not { } manager) return;

        try
        {
            using var stream = new System.IO.MemoryStream();
            manager.SaveLayoutToStream(stream);
            SetSetting(BarLayoutKey, Convert.ToBase64String(stream.ToArray()));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "도구 모음 배치를 저장하지 못했다");
        }
    }

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
        SetSetting(BarLayoutKey, string.Empty);
        StatusText = "창 레이아웃을 처음대로 되돌렸습니다. 도구 모음 자리는 다시 열 때 처음대로 갑니다.";
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
        RestoreBarLayout();

        Script.Restore();
        Player.Restore((key, fallback) => GetSetting(key, fallback));
    }

    protected override void SaveSettings()
    {
        base.SaveSettings();

        SaveDockLayout();
        SaveBarLayout();
        Script.Save();
        Player.Save((key, value) => SetSetting(key, value));
    }

    protected override void InitializeControls()
    {
        base.InitializeControls();

        _editor = FindControl<ScriptEditor>("EditorObjectService");
        _dock = FindControl<DevExpress.Xpf.Docking.DockLayoutManager>("DockObjectService");
        _bars = FindControl<DevExpress.Xpf.Bars.BarManager>("BarManagerObjectService");
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
