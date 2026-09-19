using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

using Minguk.Base.Utilities;

using Minguk.Tools.Projects;
using Minguk.Tools.Source;

namespace Minguk.Tools.ViewModels;

/// <summary>프로젝트 콤보의 한 줄.</summary>
/// <param name="Name">프로젝트 이름.</param>
/// <param name="Entry">솔루션 파일에 적힌 항목.</param>
public sealed record ProjectChoice(string Name, SolutionProjectEntry Entry);

/// <summary>
/// Automation 화면 - 왼쪽 메뉴의 Automation 을 누르면 열리는 최상위 탭 하나.
/// </summary>
/// <remarks>
/// <b>TamsTools 의 StreamMode 화면과 같은 짜임이다</b>(사용자, 2026-09-14 - "Depth 로 내려간다"). StreamMode 가 위에서 사이트·작업장을
/// 고르고 아래 탭들이 그 선택을 따라가듯, 여기서는 위에서 <b>솔루션 → 프로젝트</b>를 고르고 아래 탭(화면캡처·라벨링·스크립트·
/// 플레이·입력 테스트·학습환경)이 그 프로젝트를 따라간다. 프로젝트마다 바깥 탭을 여는 방식은 한 번 만들었다가 이것으로 바꿨다.
///
/// <b>아래 탭은 진짜 문서다</b> - 안쪽에 <c>DocumentGroup</c> + 이름 붙인 <c>TabbedDocumentUIService</c> 를 한 겹 더 둔다(StreamMode 와 같다).
/// 그래서 부모 넣기·닫기·파기를 DevExpress 가 해 준다. <c>DXTabControl</c> 로 두면 그 신호를 손으로 넘겨야 하고, 틀리면 캡처 세션이
/// 게임 창을 붙잡는다. 서비스는 <b>이름으로</b> 꺼낸다 - 셸(MainView)에도 같은 서비스가 있어 이름 없이 꺼내면 바깥 것을 잡는다.
///
/// <b>프로젝트를 바꾸면 아래 탭은 그대로 두고 화면마다 그 자리에서 따라간다</b>(<see cref="IFollowsProject"/>, 사용자 2026-09-19) -
/// 예전에는 닫고 다시 열어 탭이 번쩍이고 캡처가 끊겼다. 막는 화면(스크립트가 도는 중·학습 중)이 있거나 저장을 그만두면 콤보를 되돌린다.
/// <b>솔루션</b>을 바꾸거나 새 프로젝트·솔루션을 만들 때는 지금도 닫고 다시 연다.
/// </remarks>
public class AutomationMainViewModel : DocumentViewModelBase, Modules.IMainShell
{
    public const string DocumentServiceName = "AutomationDocumentManagerService";

    public static AutomationMainViewModel Create() => ViewModelSource.Create(() => new AutomationMainViewModel());

    /// <summary>콤보를 채우는 동안 바뀜 알림으로 화면을 다시 열지 않게 막는다.</summary>
    private bool _filling;

    protected AutomationMainViewModel()
    {
        Caption = "솔루션";
        // 탭 아이콘은 메뉴 아이콘과 같게(MainMenu 의 tree.png). 안 넣으면 이 탭만 아이콘 없이 뜬다.
        CaptionImage = Minguk.Image.FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/tree.png");

        Solutions = [];
        Projects = [];

        DoNewProjectCommand = new DelegateCommand(DoNewProject);
        DoNewSolutionCommand = new DelegateCommand(DoNewSolution);
        DoNewSharedProjectCommand = new DelegateCommand(DoNewSharedProject);
    }

    /// <summary>지금 솔루션에 공유 프로젝트(여러 런이 같이 쓰는 스크립트)를 더한다.</summary>
    public System.Windows.Input.ICommand DoNewSharedProjectCommand { get; }

    /// <summary>지금 솔루션에 프로젝트를 하나 더한다. 만들고 바로 그 프로젝트로 넘어간다.</summary>
    public System.Windows.Input.ICommand DoNewProjectCommand { get; }

    /// <summary>새 솔루션(첫 프로젝트 포함)을 만들고 그리로 넘어간다.</summary>
    public System.Windows.Input.ICommand DoNewSolutionCommand { get; }

    /// <summary>아래 탭을 여는 서비스. 셸에도 같은 종류가 있어 반드시 이름으로 꺼낸다.</summary>
    protected IDocumentManagerService? AutomationDocumentManagerService => GetService<IDocumentManagerService>(DocumentServiceName);

    public ObservableCollection<RecentSolutionRow> Solutions { get; }

    public ObservableCollection<ProjectChoice> Projects { get; }

    public RecentSolutionRow? SelectedSolution
    {
        get => GetProperty(() => SelectedSolution);
        set => SetProperty(() => SelectedSolution, value, OnSelectedSolutionChanged);
    }

    public ProjectChoice? SelectedProject
    {
        get => GetProperty(() => SelectedProject);
        set => SetProperty(() => SelectedProject, value, OnSelectedProjectChanged);
    }

    /// <summary>솔루션을 찾는 뿌리 - 환경 메뉴에서 정한 작업공간. 위 칸 맨 앞에 보인다.</summary>
    public string? ProjectsRoot { get => GetProperty(() => ProjectsRoot); set => SetProperty(() => ProjectsRoot, value); }

    /// <summary>지금 프로젝트 폴더. 위 칸 옆에 흐리게 보인다.</summary>
    public string? ProjectDirectory { get => GetProperty(() => ProjectDirectory); set => SetProperty(() => ProjectDirectory, value); }

    // ── 셸 줄 : 부모(본 창)에 그대로 넘긴다 ────────────────────────────────

    string? Modules.IMainShell.MainMessage
    {
        get => Shell?.MainMessage;
        set { if (Shell is not null) Shell.MainMessage = value; }
    }

    string? Modules.IMainShell.SubMessage
    {
        get => Shell?.SubMessage;
        set { if (Shell is not null) Shell.SubMessage = value; }
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────

    protected override void InitializeObservable()
    {
        if (AutomationDocumentManagerService is { } service)
            service.ActiveDocumentChanged += OnChildActivated;

        // 솔루션 탐색기에서 프로젝트 순서를 바꾸면 콤보도 그 순서로(사용자, 2026-09-19). 정적 이벤트라 ReleaseResources 에서 푼다.
        SolutionWorkspace.Changed += OnSolutionWorkspaceChanged;
    }

    /// <summary>솔루션의 프로젝트 순서가 콤보와 달라졌으면 고른 것을 둔 채 다시 채운다. 순서가 같으면 아무것도 안 한다(시작 프로젝트 바꾸기도 이 알림을 낸다).</summary>
    private void OnSolutionWorkspaceChanged(object? sender, EventArgs e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Guard(() =>
    {
        if (SolutionWorkspace.Current is not { } solution || _filling) return;

        var order = solution.Runnable().Select(entry => entry.Path).ToList();

        if (order.SequenceEqual(Projects.Select(choice => choice.Entry.Path))) return;

        RefillProjectsKeepingStartup();
    })));

    protected override void OnLoaded()
    {
        FillSolutions();

        OpenChildren();
    }

    /// <summary>이 탭이 앞으로 오면 아래에서 고른 화면이 단축키(F8 담기 등)를 받게 한다.</summary>
    protected override void OnActivated() => ActiveChild()?.NotifyActivated();

    /// <summary>앱이 꺼질 때 셸은 이 탭만 안다 - 아래 화면들에게 넘긴다.</summary>
    protected override void SaveSettings()
    {
        foreach (var child in Children())
            child.SaveSettingsNow();
    }

    /// <summary>
    /// 이 탭이 닫힐 때 아래 문서를 닫는다. DestroyOnClose 라 각 화면의 OnClose → OnDestroy 를 DevExpress 가 부른다 - 캡처 세션이 여기서 놓인다.
    /// </summary>
    protected override void ReleaseResources()
    {
        if (AutomationDocumentManagerService is { } service)
            service.ActiveDocumentChanged -= OnChildActivated;

        SolutionWorkspace.Changed -= OnSolutionWorkspaceChanged;

        CloseChildren();
    }

    // ── 위 칸 : 솔루션 → 프로젝트 ────────────────────────────────────────

    private void FillSolutions()
    {
        _filling = true;

        try
        {
            Solutions.Clear();

            // 작업공간 아래의 솔루션들이다(깊이로 내려간다: 작업공간 → 솔루션 → 프로젝트). 최근 목록이 아니다 -
            // 그러면 폴더에 넣어 둔 솔루션도 한 번 열기 전에는 안 보인다.
            ProjectsRoot = Vision.ProjectPaths.Root;

            var found = Solution.FindUnder(ProjectsRoot).ToList();

            // 지금 연 솔루션이 작업공간 밖(열기로 딴 데서 연 것)이면 목록에 같이 둔다 - 안 그러면 콤보가 비어 보인다.
            var current = SolutionWorkspace.Current?.FilePath;

            if (current is not null && !found.Any(path => string.Equals(path, current, StringComparison.OrdinalIgnoreCase)))
                found.Add(current);

            foreach (var path in found)
                Solutions.Add(new RecentSolutionRow(Path.GetFileNameWithoutExtension(path), path, true));


            SelectedSolution = Solutions.FirstOrDefault(row => string.Equals(row.FilePath, current, StringComparison.OrdinalIgnoreCase))
                               ?? Solutions.FirstOrDefault();

            FillProjects();
        }
        finally
        {
            _filling = false;
        }
    }

    private void FillProjects()
    {
        Projects.Clear();

        if (SolutionWorkspace.Current is not { } solution) return;

        foreach (var entry in solution.Runnable())
            Projects.Add(new ProjectChoice(Solution.NameOf(entry), entry));

        var startup = solution.Startup();

        SelectedProject = Projects.FirstOrDefault(choice => choice.Entry.Path == startup?.Path) ?? Projects.FirstOrDefault();

        ApplyProject();
    }

    private void OnSelectedSolutionChanged() => Guard(() =>
    {
        if (_filling || SelectedSolution is not { } row) return;

        if (!CloseChildren())
        {
            RevertSolution();
            return;
        }

        SolutionWorkspace.Open(row.FilePath);

        _filling = true;

        try
        {
            FillProjects();
        }
        finally
        {
            _filling = false;
        }

        OpenChildren();
    });

    private void OnSelectedProjectChanged() => Guard(() =>
    {
        if (_filling || SelectedProject is not { } choice) return;

        // 탭을 닫지 않는다 - 화면마다 그 자리에서 새 프로젝트를 따라간다(IFollowsProject, 사용자 2026-09-19).
        var followers = Children().OfType<IFollowsProject>().ToList();

        if (followers.Select(f => f.ProjectSwitchBlocker()).FirstOrDefault(reason => reason is not null) is { } blocker)
        {
            RevertProject(blocker);
            return;
        }

        foreach (var follower in followers)
        {
            if (!follower.PrepareProjectSwitch())
            {
                RevertProject("그만두어 프로젝트를 바꾸지 않았습니다.");
                return;
            }
        }

        SolutionWorkspace.SetStartup(choice.Entry);

        ApplyProject();

        foreach (var follower in followers)
            Guard(follower.FollowProject);

        MessengerUtility.SendMainMessage($"프로젝트를 '{choice.Name}' (으)로 바꿨습니다.");
    });

    /// <summary>고른 프로젝트 폴더를 위 칸에 보인다. 화면들은 시작 프로젝트(SolutionWorkspace)에서 자리를 직접 읽는다.</summary>
    private void ApplyProject()
    {
        if (SolutionWorkspace.Current is not { } solution || SelectedProject is not { } choice)
        {
            ProjectDirectory = null;
            return;
        }

        ProjectDirectory = solution.DirectoryOf(choice.Entry);

        // 옛 소문자 폴더(images·labels·captures·recordings)를 대문자로 시작하게 맞춘다(사용자, 2026-09-15). 아래 화면이 닫힌 뒤라 쥔 파일이 없다.
        if (Vision.ProjectPaths.NormalizeFolderCase(ProjectDirectory) is > 0 and var renamed)
            Logger.Info($"프로젝트 폴더 이름을 대문자로 맞췄다: {renamed}개 · {ProjectDirectory}");
    }

    /// <summary>닫기를 막은 화면이 있어 바꾸지 못했다 - 콤보를 지금 솔루션으로 되돌린다.</summary>
    private void RevertSolution()
    {
        _filling = true;

        try
        {
            var current = SolutionWorkspace.Current?.FilePath;
            SelectedSolution = Solutions.FirstOrDefault(row => string.Equals(row.FilePath, current, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _filling = false;
        }

        MessengerUtility.SendMainMessage("닫지 않은 화면이 있어 솔루션을 바꾸지 않았습니다. 저장하거나 닫은 뒤 다시 고르세요.");
    }

    private void RevertProject(string? reason = null)
    {
        _filling = true;

        try
        {
            var startup = SolutionWorkspace.Current?.Startup();
            SelectedProject = Projects.FirstOrDefault(choice => choice.Entry.Path == startup?.Path);
        }
        finally
        {
            _filling = false;
        }

        MessengerUtility.SendMainMessage(reason ?? "닫지 않은 화면이 있어 프로젝트를 바꾸지 않았습니다. 저장하거나 닫은 뒤 다시 고르세요.");
    }

    // ── 새로 만들기 ──────────────────────────────────────────────────────

    /// <summary>
    /// 지금 솔루션에 새 프로젝트를 만든다 - 폴더, .mtsproj, 사진·라벨 자리. 솔루션에 넣고 시작 프로젝트로 두고 넘어간다.
    /// </summary>
    /// <remarks>
    /// <b>이름을 먼저 묻고 만든 뒤에 넘어간다.</b> 아래 화면을 먼저 닫으면 이름 칸에서 그만뒀을 때 캡처만 끊긴다.
    /// 만든 뒤 닫기를 막는 화면이 있으면 프로젝트는 남기고(목록에 보인다) 넘어가지 않는다.
    /// </remarks>
    private void DoNewProject() => Guard(() =>
    {
        if (SolutionWorkspace.Current is not { } solution)
        {
            MessengerUtility.SendMainMessage("솔루션이 없습니다. 새 솔루션부터 만드세요.");
            return;
        }

        var name = Views.NameInputWindow.Ask(
            "새 프로젝트",
            $"'{solution.Name}' 에 더할 프로젝트(모드·스테이지·런) 이름을 적으세요.",
            candidate => Directory.Exists(Path.Combine(solution.Directory, candidate))
                ? "같은 이름의 폴더가 이미 있습니다."
                : null);

        if (name is null) return;

        var folder = Path.Combine(solution.Directory, name);
        var project = Input.Scripting.Projects.ScriptProject.Create(folder, name, NewProjectEntrySource);

        // 사진·라벨 자리를 미리 만든다 - 캡처 화면이 담을 때 만들기는 하지만 처음부터 보이는 편이 낫다.
        Directory.CreateDirectory(Path.Combine(folder, Vision.ProjectPaths.ImagesFolder));
        Directory.CreateDirectory(Path.Combine(folder, Vision.ProjectPaths.LabelsFolder));

        var entry = solution.Add(project.FilePath);
        solution.Save();

        MessengerUtility.SendMainMessage($"프로젝트 '{name}' 을(를) 만들었습니다 - {folder}");

        if (!CloseChildren())
        {
            RefillProjectsKeepingStartup();
            MessengerUtility.SendMainMessage($"프로젝트 '{name}' 을(를) 만들었지만 닫지 않은 화면이 있어 넘어가지 않았습니다. 목록에서 고르세요.");
            return;
        }

        SolutionWorkspace.SetStartup(entry);

        _filling = true;

        try
        {
            FillProjects();
        }
        finally
        {
            _filling = false;
        }

        OpenChildren();
    });

    /// <summary>
    /// 지금 솔루션에 공유 프로젝트를 만든다 - 폴더, .mtsproj, 소스 하나. 솔루션에 <c>Shared</c> 로 넣는다.
    /// </summary>
    /// <remarks>
    /// 프로젝트 콤보에는 안 뜨고(혼자 못 돈다) 아래 화면도 안 바뀐다 - 그래서 화면을 닫지 않는다.
    /// 어느 프로젝트에 물릴지는 사람이 정한다(VS 와 같다) - 스크립트 화면 솔루션 탐색기에서 추가 > 공유 프로젝트 참조.
    /// </remarks>
    private void DoNewSharedProject() => Guard(() =>
    {
        if (SolutionWorkspace.Current is not { } solution)
        {
            MessengerUtility.SendMainMessage("솔루션이 없습니다. 새 솔루션부터 만드세요.");
            return;
        }

        var name = Views.NameInputWindow.Ask(
            "새 공유 프로젝트",
            $"'{solution.Name}' 의 여러 프로젝트가 같이 쓸 스크립트(공유 프로젝트) 이름을 적으세요.",
            candidate => Directory.Exists(Path.Combine(solution.Directory, candidate))
                ? "같은 이름의 폴더가 이미 있습니다."
                : null);

        if (name is null) return;

        var folder = Path.Combine(solution.Directory, name);
        var project = Input.Scripting.Projects.ScriptProject.CreateShared(folder, name);

        solution.Add(project.FilePath, SolutionProjectKind.Shared);
        solution.Save();

        MessengerUtility.SendMainMessage($"공유 프로젝트 '{name}' 을(를) 만들었습니다 - 스크립트 화면 솔루션 탐색기에서 추가 > 공유 프로젝트 참조로 물리세요 ({folder}).");
    });

    /// <summary>
    /// 새 솔루션. 시작 창을 만들기 칸이 펴진 채 띄운다(게임 이름 + 첫 프로젝트).
    /// </summary>
    private void DoNewSolution() => Guard(() =>
    {
        var previous = SolutionWorkspace.Current?.FilePath;

        var start = Views.StartWindow.ForCreate();

        if (System.Windows.Application.Current?.MainWindow is { IsVisible: true } owner) start.Owner = owner;

        start.ShowDialog();

        if (start.Chosen is null) return;

        // 시작 창이 이미 새 솔루션을 지금 것으로 걸었다. 아래 화면이 닫히지 않으면 앞 솔루션으로 되돌린다 - 위 칸과 아래 화면이 어긋나면 안 된다.
        if (!CloseChildren())
        {
            if (previous is not null) SolutionWorkspace.Open(previous);

            FillSolutions();
            MessengerUtility.SendMainMessage($"솔루션 '{start.Chosen.Name}' 을(를) 만들었지만 닫지 않은 화면이 있어 넘어가지 않았습니다. 목록에서 고르세요.");
            return;
        }

        FillSolutions();

        OpenChildren();
    });

    /// <summary>프로젝트 목록만 다시 채운다. 시작 프로젝트(지금 보는 것)는 그대로 둔다.</summary>
    private void RefillProjectsKeepingStartup()
    {
        _filling = true;

        try
        {
            FillProjects();
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>새 프로젝트의 main.csx. 이스케이프 없는 원시 문자열로 둔다.</summary>
    private const string NewProjectEntrySource = """
        // 시작 파일입니다. 같은 프로젝트의 다른 .csx 에 만든 함수를 그대로 부를 수 있습니다.
        출력("안녕하세요");

        """;

    // ── 아래 탭 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 아래 탭을 다 연다. 처음부터 다 있고 닫기 버튼은 없다(사용자 결정). 첫 탭이 앞으로 온다.
    /// </summary>
    /// <remarks>
    /// 문서를 만들어도 화면의 무거운 일(데이터셋 읽기·캡처)은 그 탭이 처음 보일 때 돈다 - 바탕이 View 의 Loaded 를 기다린다.
    /// </remarks>
    private void OpenChildren()
    {
        if (AutomationDocumentManagerService is not { } service) return;

        _reshuffling = true;

        try
        {
            OpenChildrenCore(service);
        }
        finally
        {
            _reshuffling = false;
        }
    }

    private void OpenChildrenCore(IDocumentManagerService service)
    {

        if (SolutionWorkspace.Current is null || SelectedProject is null) return;

        IDocument? first = null;
        IDocument? remembered = null;

        // 마지막에 보던 아래 탭으로 복구한다(사용자, 2026-09-19 - 켤 때마다 스크립트 탭을 다시 눌러야 했다).
        var last = AppSettingUtility.Get(LastTabKey, string.Empty);

        foreach (var screen in AutomationScreens.All())
        {
            var document = service.FindDocumentByIdOrCreate(screen.ViewName, owner =>
            {
                var created = owner.CreateDocument(screen.ViewName, null, this);

                created.Id = screen.ViewName;
                created.Title = screen.Title;
                created.DestroyOnClose = true;

                return created;
            });

            first ??= document;

            if (screen.ViewName == last) remembered = document;
        }

        (remembered ?? first)?.Show();
    }

    /// <summary>아래 문서를 전부 닫는다. 하나라도 닫기를 막으면 false.</summary>
    private bool CloseChildren()
    {
        if (AutomationDocumentManagerService is not { } service) return true;

        _reshuffling = true;

        try
        {
            return CloseChildrenCore(service);
        }
        finally
        {
            _reshuffling = false;
        }
    }

    private bool CloseChildrenCore(IDocumentManagerService service)
    {

        foreach (var document in service.Documents.ToList())
        {
            // 이미 닫힌 문서를 다시 닫으면 DevExpress 안에서 NRE 가 난다(StreamMode 에서 겪었다).
            try
            {
                document.Close();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "문서 닫기 실패 : {0}", document.Title);
            }
        }

        return !service.Documents.Any();
    }

    private const string LastTabKey = "Automation.LastTab";

    /// <summary>아래 탭을 닫거나 여는 중 - 그동안 앞에 오는 탭은 사람이 고른 것이 아니다.</summary>
    private bool _reshuffling;

    private void OnChildActivated(object? sender, ActiveDocumentChangedEventArgs e) => Guard(() =>
    {
        // 탭을 닫고 여는 동안에는 기억하지 않는다 - 닫힐 때 남은 탭이 차례로 앞에 와서 맨 끝(설정)이 기억됐다(사용자, 2026-09-19).
        if (!_reshuffling && e.NewDocument?.Id is string id && !string.IsNullOrEmpty(id)) AppSettingUtility.Set(LastTabKey, id);

        ActiveChild()?.NotifyActivated();
    });

    private DocumentViewModelBase? ActiveChild() => ContentViewModel(AutomationDocumentManagerService?.ActiveDocument);

    private System.Collections.Generic.IEnumerable<DocumentViewModelBase> Children()
        => AutomationDocumentManagerService?.Documents.Select(ContentViewModel).OfType<DocumentViewModelBase>() ?? [];

    private static DocumentViewModelBase? ContentViewModel(IDocument? document)
        => (document?.Content as System.Windows.FrameworkElement)?.DataContext as DocumentViewModelBase
           ?? document?.Content as DocumentViewModelBase;
}
