using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using DevExpress.Xpf.Docking;
using DevExpress.Xpf.Grid;

using NamedRegion = Minguk.Tools.Vision.Regions.NamedRegion;
using RegionCell = Minguk.Tools.Vision.Regions.RegionCell;
using ScriptProject = Minguk.Tools.Input.Scripting.Projects.ScriptProject;

using Minguk.Image;
using Minguk.Tools.Helper;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Hotkeys;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;
using Minguk.Tools.Markup;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 스크립트 화면. 창을 잡아 검출·글자 읽기를 켜 놓고 <b>보면서</b> 스크립트를 쓰고 한 번씩 돌려 본다.
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

    /// <summary>도움말 패널의 줄 - 기본 사용법 · 함수 전부 · 검출의 속성(<see cref="ScriptHelp"/>).</summary>
    public IReadOnlyList<ScriptHelpRow> HelpRows => ScriptHelp.Rows;

    /// <summary>도움말에서 고른 줄. 아래 칸에 설명·예시가 뜬다.</summary>
    public ScriptHelpRow? SelectedHelpRow { get => GetProperty(() => SelectedHelpRow); set => SetProperty(() => SelectedHelpRow, value); }

    /// <summary>돌리는 것. 한 번 · 반복 · 중지 · 진행.</summary>
    /// <summary>
    /// 이 화면이 만든 것(Player·Script·Live)의 구독. 바탕의 Disposables 는 ReleaseResources <b>앞</b>에서 끊기는데, 여기는 <b>끝</b>에서 끊는다 -
    /// 닫으며 부르는 <c>Player.Stop()</c> 이 실행 끝남(글 잠금 풀기·제 프로젝트로 돌아가기)을 그대로 거치게.
    /// </summary>
    private readonly System.Reactive.Disposables.CompositeDisposable _ownEvents = new();

    public ScriptPlayer Player { get; }

    /// <summary>실시간 실행에 필요한 것들 - 출력 칸, 비상 정지, API 에 빌려 줄 것.</summary>
    public LiveScriptSession Live { get; }

    /// <summary>F5. 멈춰 있으면 계속, 아니면 처음부터.</summary>
    public DelegateCommand RunOrContinueCommand { get; }

    /// <summary>F10. 멈춰 있으면 다음 줄, 아니면 첫 줄에서 멈추게 시작.</summary>
    public DelegateCommand StepCommand { get; }

    /// <summary>캐럿이 있는 줄의 중단점을 켜고 끈다. 편집기 여백을 눌러도 된다.</summary>
    public DelegateCommand ToggleBreakpointCommand { get; }

    /// <summary>
    /// 실행 일시정지(디버그 도구 줄) - 도는 스크립트만 멈춘다. 다시 누르거나 F5 로 계속. 캡처는 캡처 도구 모음의 「캡처 일시정지」(<see cref="CaptureViewModelBase.IsCapturePaused"/>)가 따로 든다.
    /// </summary>
    /// <remarks>
    /// 스크립트는 다음 API 호출에서 멈추고 누르고 있던 키를 뗀다(<see cref="ScriptPauseGate"/>). 실행이 끝나면 저절로 풀린다.
    /// 예전에는 캡처까지 한 단추로 멈췄는데 실행 옆에 있어 실행용으로만 읽혔다(사용자, 2026-09-17) - 둘로 나눴다.
    /// </remarks>
    public bool IsPaused { get => GetProperty(() => IsPaused); set => SetProperty(() => IsPaused, value, OnIsPausedChanged); }

    /// <summary>실행 일시정지를 누를 수 있는가 - 스크립트가 돌 때(멈춰 있으면 풀 수 있게 늘).</summary>
    public bool CanPause => IsPaused || Player.IsRunning;

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
            message => RunOnUi(() => StatusText = message),
            OcrEngineForScripts,
            RunProjectFromSourceAsync);

        Player = new ScriptPlayer(() => Live.Resolve(Script, Player));

        // 도는 중에는 실행 단추를 끈다(사용자, 2026-09-18) - 멈춰 있을 때(실행 일시정지·중단점)만 "계속" 으로 산다.
        RunOrContinueCommand = new DelegateCommand(RunOrContinue, () => Player.IsIdle || IsPaused || Live.Debug.IsPaused, false);
        _ownEvents.Add(RxEvents.PropertyChanged(Live.Debug, nameof(Live.Debug.IsPaused)).Listen(_ => RunOrContinueCommand.RaiseCanExecuteChanged()));
        StepCommand = new DelegateCommand(Step, false);
        ToggleBreakpointCommand = new DelegateCommand(ToggleBreakpoint, false);

        SaveCommand = new DelegateCommand(() => Guard(() => { if (Script.IsProject) Script.Project.ActiveDocument?.Save(); else Script.SaveCommand.Execute(null); }), false);
        SaveAsCommand = new DelegateCommand(() => Script.SaveAsCommand.Execute(null), () => !Script.IsProject, false);
        SaveAllCommand = new DelegateCommand(() => Guard(() => { if (Script.IsProject) Script.Project.SaveAll(); else Script.SaveCommand.Execute(null); }), false);
        CloseDocumentCommand = new DelegateCommand(() => Guard(() => Script.Project.CloseDocument(Script.Project.ActiveDocument)), () => Script.IsProject, false);
        ShowToolWindowCommand = new DelegateCommand<string>(ShowToolWindow, false);
        ResetLayoutCommand = new DelegateCommand(ResetLayout, false);
        BuildCommand = new DelegateCommand(DoBuild, () => Script.IsProject && !Script.IsLocked, false);
        BuildAllCommand = new DelegateCommand(DoBuildAll, () => Script.IsProject && !Script.IsLocked, false);
        BuildProjectNodeCommand = new DelegateCommand(DoBuildSelectedProjectNode, () => Script.IsProject && !Script.IsLocked, false);
        SplitVerticalCommand = new DelegateCommand(() => SetSplit(System.Windows.Controls.Orientation.Vertical), false);
        SplitHorizontalCommand = new DelegateCommand(() => SetSplit(System.Windows.Controls.Orientation.Horizontal), false);
        SwapPanesCommand = new DelegateCommand(SwapPanes, false);
        GoToErrorCommand = new DelegateCommand<object?>(GoToError, false);

        // 아래 구독은 화면이 만든 것(Player·Script·Live)이라 수명이 같다 - 생성자에서 걸고 ReleaseResources 끝에서 푼다(_ownEvents).
        _ownEvents.Add(RxEvents.From(h => Script.Project.ProjectChanged += h, h => Script.Project.ProjectChanged -= h).Listen(_ =>
        {
            SaveAsCommand.RaiseCanExecuteChanged();
            CloseDocumentCommand.RaiseCanExecuteChanged();
            BuildCommand.RaiseCanExecuteChanged();
            BuildAllCommand.RaiseCanExecuteChanged();
            BuildProjectNodeCommand.RaiseCanExecuteChanged();
        }));

        // 실행이 켜지고 꺼질 때 - 순서가 있다: 글 잠금이 먼저(빌드 단추가 그것을 본다), 그다음 단추 상태, 일시정지 맞추기, 끝났으면 제 프로젝트로 돌아가기.
        _ownEvents.Add(RxEvents.From(h => Player.RunningChanged += h, h => Player.RunningChanged -= h).Listen(_ =>
        {
            // 도는 동안 글을 잠근다. 도중에 바뀌면 무엇이 나갔는지 알 수 없다.
            Script.IsLocked = Player.IsRunning;

            BuildCommand.RaiseCanExecuteChanged();
            BuildAllCommand.RaiseCanExecuteChanged();
            BuildProjectNodeCommand.RaiseCanExecuteChanged();
            RunOrContinueCommand.RaiseCanExecuteChanged();

            // 실행 일시정지 단추를 켜고 끄고, 끝났으면 일시정지를 푼다.
            SyncPause();
            ReturnToHomeProjectWhenStopped(Player.IsRunning);
        }));
    }

    // ── 일시정지 ─────────────────────────────────────────────────────────

    private async void OnIsPausedChanged() => await GuardAsync(async () =>
    {
        RaisePropertyChanged(nameof(CanPause));
        RunOrContinueCommand.RaiseCanExecuteChanged();

        if (IsPaused)
        {
            if (!Player.IsRunning)
            {
                IsPaused = false;
                return;
            }

            Live.PauseGate.Pause();
            StatusText = "실행 일시정지 - 스크립트는 다음 호출에서 멈춥니다. 계속은 [실행 일시정지] 를 다시 누르거나 F5.";
            return;
        }

        if (!Live.PauseGate.IsPaused) return;

        // 단추를 누르느라 앱이 앞에 왔다 - 게임을 앞으로 돌려놓고 연다. 안 그러면 다음 입력이 "대상 창이 앞에 없다" 로 막힌다.
        if (Player.IsRunning) await ActivateTargetAsync();

        Live.PauseGate.Resume();
        StatusText = Player.IsRunning ? "계속 - 멈춘 호출부터 이어 갑니다." : "계속";
    });

    private void SyncPause()
    {
        RaisePropertyChanged(nameof(CanPause));

        if (IsPaused && !Player.IsRunning) IsPaused = false;
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

    /// <summary>스크립트 화면은 도킹 배치가 있어 미리보기를 나눌 수 있다 - PreviewForwardBar 의 세 버튼을 켠다.</summary>
    public override bool SupportsPreviewSplit => true;

    /// <summary>미리보기 위 · 스크립트 아래. VS XAML 디자이너의 "가로 분할" 자리.</summary>
    public override ICommand SplitVerticalCommand { get; }

    /// <summary>미리보기와 스크립트를 나란히(기본).</summary>
    public override ICommand SplitHorizontalCommand { get; }

    /// <summary>미리보기와 스크립트의 자리를 맞바꾼다.</summary>
    public override ICommand SwapPanesCommand { get; }

    /// <summary>오류 목록 더블 클릭 - 그 파일을 열고 그 줄로.</summary>
    public DelegateCommand<object?> GoToErrorCommand { get; }

    /// <summary>Ctrl+Shift+B. 프로젝트를 .NET DLL(.mtsx)로 빌드한다 - 플레이어가 소스 없이 실행한다.</summary>
    public DelegateCommand BuildCommand { get; }

    /// <summary>솔루션 탐색기의 솔루션 줄 메뉴 · 위 도구 줄의 「전체 빌드」 - 솔루션의 프로젝트를 모두 저장하고 빌드한다.</summary>
    public DelegateCommand BuildAllCommand { get; }

    /// <summary>솔루션 탐색기의 프로젝트 줄(지금 연 것·다른 프로젝트 모두) 메뉴 「빌드」 - 고른 그 프로젝트만.</summary>
    public DelegateCommand BuildProjectNodeCommand { get; }

    /// <summary>
    /// 프로젝트를 IL 로 빌드해 프로젝트 폴더의 <c>bin\&lt;이름&gt;.mtsx</c> 로 쓴다. 리소스가 있으면 옆에 같이 복사한다.
    /// </summary>
    /// <remarks>
    /// 소스가 아니라 IL 이라 텍스트로는 못 본다(작정하면 디컴파일러로는 봄). 일반 사용자는 이 파일을 플레이어에서 실행만 한다.
    /// 진입점 이름이 우리 것(<see cref="CompiledScriptBuilder.EntryTypeName"/>)이라 Roslyn 을 올려도 예전 파일이 그대로 돈다.
    /// </remarks>
    private async void DoBuild()
    {
        if (Script.Project.Project is not { } project)
        {
            StatusText = "빌드하려면 프로젝트를 열고 시작 파일을 정하세요 (솔루션 탐색기에서 .csx 오른쪽 → 시작 파일로 설정).";
            return;
        }

        // 열어 둔 프로젝트는 저장 안 한 탭까지 포함해 굳힌다(Script.Project.ToUnit) - 디스크만 보는 다른 프로젝트 빌드와 다르다.
        await BuildProjectAsync(project, Script.Project.ToUnit());
    }

    /// <summary>
    /// 솔루션 탐색기에서 프로젝트 줄(지금 연 것이든 다른 프로젝트든)을 골라 「빌드」 - 고른 그 프로젝트 하나만 빌드한다.
    /// </summary>
    private async void DoBuildSelectedProjectNode()
    {
        var node = Script.Project.SelectedNode;

        if (node is not { Kind: ScriptNodeKind.Project })
        {
            StatusText = "빌드하려면 솔루션 탐색기에서 프로젝트 줄을 고르세요.";
            return;
        }

        if (node.Id == ScriptProjectWorkspace.RootId)
        {
            DoBuild();
            return;
        }

        var path = node.Id[ScriptProjectWorkspace.OtherProjectPrefix.Length..];

        ScriptProject other;

        try { other = ScriptProject.Load(path); }
        catch (Exception ex)
        {
            StatusText = $"프로젝트를 못 읽었습니다: {ex.Message}";
            return;
        }

        await BuildProjectAsync(other, other.ToUnit(new Dictionary<string, string>()));
    }

    /// <summary>
    /// 솔루션 탐색기의 솔루션 줄 「전체 빌드」 · 위 도구 줄의 「전체 빌드」 - 지금 열려 있으면 저장부터 하고,
    /// 솔루션의 프로젝트를 이름 순으로 하나씩 빌드한다. 시작 파일이 없는(공유) 프로젝트는 건너뛴다.
    /// </summary>
    private async void DoBuildAll()
    {
        if (Script.Project.CurrentSolution() is not { } solution)
        {
            StatusText = "전체 빌드는 솔루션 안의 프로젝트에서만 됩니다.";
            return;
        }

        Guard(() => Script.Project.SaveAll());

        Live.Console.Print($"전체 빌드 시작: 솔루션 '{solution.Name}' ({solution.Projects.Count}개 프로젝트)");

        var built = 0;

        foreach (var entry in solution.Projects.OrderBy(e => Minguk.Tools.Projects.Solution.NameOf(e), StringComparer.CurrentCultureIgnoreCase))
        {
            var filePath = solution.FullPath(entry.Path);

            ScriptProject project;

            try
            {
                project = string.Equals(filePath, Script.Project.Project?.FilePath, StringComparison.OrdinalIgnoreCase)
                    ? Script.Project.Project
                    : ScriptProject.Load(filePath);
            }
            catch (Exception ex)
            {
                Live.Console.Print($"'{Minguk.Tools.Projects.Solution.NameOf(entry)}' 을(를) 못 읽었다 - {ex.Message}");
                continue;
            }

            var unit = ReferenceEquals(project, Script.Project.Project) ? Script.Project.ToUnit() : project.ToUnit(new Dictionary<string, string>());

            if (await BuildProjectAsync(project, unit)) built++;
        }

        StatusText = $"전체 빌드 끝 - {built}/{solution.Projects.Count}개 빌드됨.";
        Live.Console.Print(StatusText);
    }

    /// <summary>
    /// 하나로 굳힌 것(<paramref name="unit"/>)을 빌드해 그 프로젝트 폴더의 <c>bin\&lt;이름&gt;.mtsx</c> 로 쓴다.
    /// 리소스가 있으면 옆에 같이 복사한다. 성공하면 참.
    /// </summary>
    /// <remarks>
    /// 소스가 아니라 IL 이라 텍스트로는 못 본다(작정하면 디컴파일러로는 봄). 일반 사용자는 이 파일을 플레이어에서 실행만 한다.
    /// 진입점 이름이 우리 것(<see cref="CompiledScriptBuilder.EntryTypeName"/>)이라 Roslyn 을 올려도 예전 파일이 그대로 돈다.
    /// </remarks>
    private async Task<bool> BuildProjectAsync(ScriptProject project, ScriptUnit? unit)
    {
        try
        {
            if (unit is null || string.IsNullOrEmpty(unit.EntryPath))
            {
                StatusText = $"'{project.Name}' - 시작 파일이 없어 빌드를 건너뜁니다 (솔루션 탐색기에서 .csx 오른쪽 → 시작 파일로 설정).";
                return false;
            }

            if (Script.Engine is not ICompiledScriptEngine builder)
            {
                StatusText = "프로젝트는 C# 만 빌드합니다.";
                return false;
            }

            var name = project.Name;

            StatusText = $"'{name}' 을(를) 빌드하는 중...";
            Live.Console.Print($"빌드 시작: {name}");

            var (bytes, errors) = await builder.BuildAsync(unit, name);

            if (bytes is null || errors.Count > 0)
            {
                StatusText = $"빌드 실패: {(errors.Count > 0 ? errors[0].ToString() : "알 수 없는 오류")}";
                foreach (var error in System.Linq.Enumerable.Take(errors, 20)) Live.Console.Print($"빌드 오류({name}): {error}");
                return false;
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

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "빌드에 실패했다");
            StatusText = $"'{project.Name}' 빌드 중 오류: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 스크립트의 <c>프로젝트실행("사격장")</c> - 스크립트 화면은 <b>소스를 연결</b>해서 그대로 돈다(다시 빌드할
    /// 필요 없이, 고친 것이 바로 반영된다). 플레이 화면의 <see cref="PlayViewModel"/> 쪽은 빌드된 것(.mtsx)을 읽는다.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-19) "스크립트에서 돌리면 프로젝트 파일을 연결시켜서 실행되게" - 개발 중엔 매번 빌드하지 않고
    /// 바로 다음 프로젝트로 넘어가 보게. 실행 전엔 편집 중인 파일을 다 저장한다 - 옆 프로젝트는 디스크만 읽는다.
    /// </remarks>
    private async Task<IReadOnlyList<ScriptError>> RunProjectFromSourceAsync(string name, LiveScriptHost template, System.Threading.CancellationToken token)
    {
        Guard(() => Script.Project.SaveAll());

        if (FindSiblingProjectFolder(name) is not { } folder)
            return [new ScriptError(0, "솔루션 밖이라 다른 프로젝트를 못 찾습니다.")];

        var mtsprojPath = System.IO.Path.Combine(folder, name + ScriptProject.Extension);

        if (!System.IO.File.Exists(mtsprojPath))
            return [new ScriptError(0, $"프로젝트 '{name}' 을(를) 못 찾았습니다 ({mtsprojPath}).")];

        ScriptProject other;

        try { other = ScriptProject.Load(mtsprojPath); }
        catch (Exception ex) { return [new ScriptError(0, $"프로젝트를 못 읽었습니다: {ex.Message}")]; }

        var unit = other.ToUnit(new Dictionary<string, string>());

        if (string.IsNullOrEmpty(unit.EntryPath))
            return [new ScriptError(0, $"'{name}' - 시작 파일이 없습니다 (솔루션 탐색기에서 .csx 오른쪽 → 시작 파일로 설정).")];

        if (Script.Engine is not IProjectScriptEngine engine)
            return [new ScriptError(0, "프로젝트는 C# 만 이어서 돌립니다.")];

        var previous = SwitchedRecognitionRoot;

        await SwitchProjectContextAsync(folder);

        try
        {
            var subHost = template.WithResourceRoot(folder);
            var subApi = new LiveScriptApi(subHost, token);

            return await engine.RunLiveAsync(unit, subApi, null, token);
        }
        finally
        {
            // 돌아왔으면 부른 쪽의 모델·자리로(프로젝트이동 중이면 그대로).
            await RestoreProjectContextAsync(previous, template);
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
    /// 도구 줄 배치 판. 올리면 저장된 옛 자리를 버리고 XAML 의 기본 배치로 시작한다 - 안 올리면 옛 자리가 새 배치를 덮는다.
    /// 1: 성격별로 두 줄(사용자, 2026-09-17) - 윗줄 표준·디버그·실행 설정, 아랫줄 캡처·인식. 시작 대기는 실행 설정으로, 빌드는 디버그 끝으로.
    /// 2: 일시정지를 둘로 - 디버그 「실행 일시정지」, 캡처 「캡처 일시정지」(2026-09-17).
    /// 3: 인식 줄에 「영역 이미지 저장」 더함(2026-09-18) - 옛 배치를 그대로 살리면 새 단추가 안 보인다.
    /// 4: 「영역 이미지 저장」을 인식 줄에서 뺐다(사용자, 2026-09-18 "검출 Bar는 지금처럼") - 영역 패널 도구 줄(하단 영역 그리드 위)에만 글자를 붙여 남긴다.
    /// 5: 세 줄로(사용자, 2026-09-18) - 윗줄 표준·실행 설정, 가운데 캡처, 아랫줄 인식·디버그.
    /// </summary>
    private const int BarLayoutVersion = 5;

    /// <summary>
    /// 배치 형식이 바뀌면 올린다 - 옛 배치를 새 화면에 되살리면 없는 창을 찾거나 새 창이 사라진다.
    /// 3: 솔루션 탐색기 왼쪽 · 미리보기|문서 좌우 · 도구 모음 세 줄(2026-09-13). 옛 배치를 그대로 살리면 새 기본이 안 보인다.
    /// 4: 영역 패널을 아래 탭에서 솔루션 탐색기 탭 그룹으로(2026-09-15, 사용자).
    /// 5: 솔루션 탐색기 탭 그룹에 도움말 패널(2026-09-15, 사용자).
    /// 6: 영역 패널을 아래 탭 줄 맨 왼쪽으로(2026-09-16, 사용자 - 칸이 늘어 좁고, 탭이 있다는 것을 알아채게).
    /// </summary>
    private const int DockLayoutVersion = 6;

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
        if (string.IsNullOrEmpty(saved) || GetSetting(BarLayoutKey + "Version", 0) != BarLayoutVersion) return;

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
            SetSetting(BarLayoutKey + "Version", BarLayoutVersion);
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

        ApplyDocumentTabLayout(_dock);
    }

    /// <summary>
    /// 스크립트 문서 탭은 넘치면 여러 줄로 - VS 의 "여러 행 탭"(사용자, 2026-09-19).
    /// </summary>
    /// <remarks>
    /// XAML 에 적어도 저장해 둔 배치를 되살리면 그때의 값(한 줄 스크롤)으로 덮인다 - 배치에 탭 머리 모양까지 적힌다. 그래서 되살린 뒤에 다시 건다.
    /// 배치 버전(<see cref="DockLayoutVersion"/>)을 올리면 사람이 옮겨 둔 창 배치가 다 날아가 그러지 않았다.
    /// </remarks>
    public static void ApplyDocumentTabLayout(DevExpress.Xpf.Docking.DockLayoutManager dock)
    {
        if (dock.GetItem("ProjectDocumentGroup") is not DevExpress.Xpf.Docking.DocumentGroup group) return;

        // 열거형은 다른 조각(Layout.Core)에 있어 이름을 적지 않고 속성의 형식에서 값을 꺼낸다 - XAML 의 "MultiLine" 과 같다.
        var property = DevExpress.Xpf.Docking.DocumentGroup.TabHeaderLayoutTypeProperty;
        group.SetValue(property, Enum.Parse(property.PropertyType, "MultiLine"));
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

        if (IsPaused) IsPaused = false;
        else if (Live.Debug.IsPaused) Live.Debug.Continue();
        else if (Player.IsIdle) Player.RunOnce();
    }

    /// <summary>대상 창이 닫혀 캡처가 멈췄다 - 눈이 없는 스크립트는 세울 수밖에 없다(옛 검출로 계속 쏘거나 "눈이 없습니다" 예외로 끝나던 것).</summary>
    protected override void OnCaptureEnded(string reason)
    {
        if (Player.IsIdle) return;

        Logger.Info($"스크립트 중지 - {reason}");
        Player.Stop();
        StatusText = $"캡처가 멈춰 스크립트도 멈췄습니다 - {reason}.";
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

    /// <summary>입력 전달이 꺼져 있고 영역 보기면 영역 지정 없이도 자리를 고르고 옮기고 크기를 바꾼다(사용자, 2026-09-15). 자리 편집 도구가 있는 화면이다.</summary>
    protected override bool EditsRegionsWithoutPicking => true;

    /// <summary>
    /// 새 자리를 만들었다 - 영역 탭을 앞으로 띄우고 그 줄의 이름 칸을 편집 상태로 연다(이름 칸은 여기 하나다, 2026-09-15).
    /// </summary>
    /// <remarks>탭을 띄운 뒤 그리드가 그려져야 편집기가 열려 한 박자 늦게(Background) 연다. 못 열면 이름은 「자리N」 그대로다.</remarks>
    protected override void OnRegionCreated(NamedRegion region)
    {
        ShowToolWindow("RegionsPanel");

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => Guard(() =>
        {
            if (FindControl<GridControl>("RegionsGridObjectService") is not { View: TreeListView view } grid) return;

            grid.CurrentItem = region;
            grid.CurrentColumn = grid.Columns["Name"];
            view.Focus();
            view.ShowEditor();
            view.BestFitColumns();
        }));
    }

    /// <summary>새 칸 - 영역 목록을 앞으로 띄우고 트리의 그 칸 줄 이름을 편집 상태로 연다.</summary>
    protected override void OnCellCreated(RegionCell cell)
    {
        ShowToolWindow("RegionsPanel");

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => Guard(() =>
        {
            if (FindControl<GridControl>("RegionsGridObjectService") is not { View: TreeListView view } grid) return;

            // AutoExpandAllNodes 는 처음 불러올 때만 펼친다 - 나중에 더한 칸이 접힌 자리 밑에 숨지 않게 펼친다.
            view.ExpandAllNodes();
            grid.CurrentItem = cell;
            grid.CurrentColumn = grid.Columns["Name"];
            view.Focus();
            view.ShowEditor();
            view.BestFitColumns();
        }));
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

        FitRegionsGridColumns();

        // 영역 탭이 맨 처음 고른 탭이 아니면 이때는 아직 안 그려져 있어 폭을 못 잰다(사용자, 2026-09-18 여러 번 "너무 빽빽해") -
        // 그 탭을 실제로 고르는 순간 다시 잰다.
        if (_dock is { } dock)
            Disposables.Add(RxEvents.From<DevExpress.Xpf.Docking.Base.DockItemActivatedEventHandler, DevExpress.Xpf.Docking.Base.DockItemActivatedEventArgs>(
                    handler => (sender, e) => handler(sender, e),
                    h => dock.DockItemActivated += h,
                    h => dock.DockItemActivated -= h)
                .Listen(OnDockItemActivated));

        // 첫 준비가 유독 느리다(C# 은 첫 컴파일, 파이썬은 런타임 받기). 미리 치러 둔다.
        _ = Script.PrepareAsync();
    }

    private void OnDockItemActivated(DevExpress.Xpf.Docking.Base.DockItemActivatedEventArgs e)
    {
        if (e.Item?.Name == "RegionsPanel") FitRegionsGridColumns();
    }

    /// <summary>
    /// 영역 그리드 열 너비를 내용에 맞춘다(사용자, 2026-09-16). XAML 첨부 속성은 GridControl 에서만 돌고 그때는 열이 아직 없어 안 먹는다(라벨링 화면과 같다).
    /// </summary>
    /// <remarks>
    /// 영역 탭은 아래 탭 줄의 안 고른 탭이라 아직 안 떴을 수 있다 - 뜰 때 한 번 더 건다(뜨면서 너비를 픽셀로 다시 쓰지 않게).
    /// <b>Loaded 만으로는 이르다</b>(사용자, 2026-09-19 "클릭하니까 넓어지네") - Loaded 는 떴다는 뜻이지 줄까지 다 그렸다는 뜻이
    /// 아니라, 그 순간 재면 아직 빈 칸 기준으로 좁게 잰다. 실제 화면을 눌러야 다시 그려지며 맞았던 것도 그래서다.
    /// 렌더가 끝난 뒤(Background)로 미뤄서 잰다 - ContextIdle 은 더 낮아서 캡처 미리보기가 계속 돌면
    /// (30fps, Render 우선순위) 큐가 그 밑까지 안 내려가 영영 안 불렸다(실측 - --script-screen 검사가 그걸로 잡혔다).
    /// </remarks>
    private void FitRegionsGridColumns()
    {
        if (FindControl<GridControl>("RegionsGridObjectService") is not { } grid) return;

        if (grid.IsLoaded)
        {
            DeferredBestFit(grid);
            return;
        }

        // 뜰 때 한 번만(Take(1)). 여러 번 불려도 기다리는 것은 하나 - 새로 걸면 옛것이 풀린다.
        _regionsGridLoaded.Disposable = RxEvents.From<System.Windows.RoutedEventHandler, System.Windows.RoutedEventArgs>(
                handler => (sender, e) => handler(sender, e),
                h => grid.Loaded += h,
                h => grid.Loaded -= h)
            .Take(1)
            .Listen(_ => DeferredBestFit(grid));
    }

    /// <summary>영역 그리드가 뜨기를 기다리는 구독(<see cref="FitRegionsGridColumns"/>).</summary>
    private readonly System.Reactive.Disposables.SerialDisposable _regionsGridLoaded = new();

    private static void DeferredBestFit(GridControl grid)
        => grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => BestFitRegionsGridColumns(grid)));

    /// <summary>
    /// 내용에 딱 맞추면(Auto) 값 칸이 서로 붙어 빽빽했다(사용자, 2026-09-18 "너무 빽빽해") - 잰 값에 열마다 50px 씩 숨 쉴 자리를 더한다
    /// (<see cref="Minguk.Base.Controls.BaseTreeListView.BestFitColumnsWithPadding"/>). Auto 와 달리 그 뒤로는 고정 픽셀이라
    /// (내용이 바뀌어도 안 줄어든다) 성격상 이쪽이 맞다.
    /// </summary>
    private static void BestFitRegionsGridColumns(GridControl grid)
    {
        if (grid.View is Minguk.Base.Controls.BaseTreeListView view) view.BestFitColumnsWithPadding(50);
    }

    /// <inheritdoc/>
    public override string? ProjectSwitchBlocker()
        => Player.IsRunning ? "스크립트가 도는 중에는 프로젝트를 바꿀 수 없습니다 - 먼저 중지(F6)하세요." : base.ProjectSwitchBlocker();

    /// <summary>검출·자리(바탕)에 더해 솔루션 탐색기·실행 대상을 새 시작 프로젝트로. 열린 탭은 그대로 둔다.</summary>
    public override void FollowProject()
    {
        base.FollowProject();

        if (IsInitialized) Script.FollowStartupProject();
    }

    protected override void ReleaseResources()
    {
        Player.Stop();

        // 놓아 주지 않으면 앱이 살아 있는 동안 그 키가 잠긴 채로 남는다.
        foreach (var claim in _hotkeyClaims) claim.Dispose();
        _hotkeyClaims.Clear();
        _editor = null;
        _regionsGridLoaded.Dispose();
        _dock = null;

        Live.Dispose();
        Script.Dispose();
        _ownEvents.Dispose();

        base.ReleaseResources();
    }
}
