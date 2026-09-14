using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

using Minguk.Base.Views;

using Minguk.Tools.Input.Scripting.Projects;
using Minguk.Tools.Projects;
using Minguk.Tools.Vision;

using Newtonsoft.Json;

namespace Minguk.Tools.ViewModels;

/// <summary>최근 목록 한 줄.</summary>
/// <param name="Name">솔루션 이름.</param>
/// <param name="FilePath">.mtsln 전체 경로.</param>
/// <param name="Exists">파일이 아직 있는지.</param>
public sealed record RecentSolutionRow(string Name, string FilePath, bool Exists)
{
    /// <summary>목록에 보이는 자리. 없어진 것은 그렇게 적는다 - 조용히 빼면 사람이 지운 줄 모른다.</summary>
    public string Where => Exists ? Path.GetDirectoryName(FilePath) ?? FilePath : FilePath + "  (없음)";
}

/// <summary>
/// 시작 창 - 앱을 켤 때마다 먼저 뜬다. 어느 솔루션으로 일할지 고른다.
/// </summary>
/// <remarks>
/// VS 2026 시작 창과 같은 모양이다(사용자 결정 2026-09-14): 왼쪽에 최근 목록, 오른쪽에 열기·새로 만들기.
///
/// <b>솔루션 없이 열리는 화면이 없다.</b> 캡처·라벨링·스크립트·플레이가 모두 프로젝트 폴더를 봐야 하므로,
/// 여기서 그만두면 앱이 닫힌다. 설계는 <c>docs/프로젝트-설계.md</c>.
///
/// <b>이름은 대화 상자로 묻지 않고 이 창 안에서 적는다.</b> 만들기를 누르면 오른쪽이 이름 두 칸으로 바뀐다 -
/// VS 의 새 프로젝트 화면과 같고, 대화 상자 서비스를 하나 더 물지 않아도 된다.
/// </remarks>
public class StartWindowViewModel : ViewModelBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static StartWindowViewModel Create() => ViewModelSource.Create(() => new StartWindowViewModel());

    protected StartWindowViewModel()
    {
        Recent = [];

        NewSolutionName = string.Empty;
        NewProjectName = string.Empty;

        OnInitializedCommand = new DelegateCommand(OnInitialized);
        DoOpenSelectedCommand = new DelegateCommand(DoOpenSelected);
        DoBrowseCommand = new DelegateCommand(DoBrowse);
        DoBeginCreateCommand = new DelegateCommand(DoBeginCreate);
        DoCreateCommand = new DelegateCommand(DoCreate);
        DoCancelCreateCommand = new DelegateCommand(() => IsCreating = false);
        DoForgetCommand = new DelegateCommand(DoForget);
    }

    /// <summary>
    /// 창을 닫는 길. <b>ICurrentDialogService 가 아니다</b> - 그쪽은 대화 상자 서비스로 띄운 화면에만 들어온다.
    /// 시작 창은 진짜 창(ShowDialog)이라 그것이 늘 null 이었고, 그래서 열기를 눌러도 창이 안 닫혔다(실측).
    /// </summary>
    protected ICurrentWindowService CurrentWindowService => this.GetService<ICurrentWindowService>();
    protected IMessageBoxService MessageBoxService => this.GetService<IMessageBoxService>();
    protected IOpenFileDialogService OpenFileDialogService => this.GetService<IOpenFileDialogService>();

    public ObservableCollection<RecentSolutionRow> Recent { get; }

    public RecentSolutionRow? Selected { get => GetProperty(() => Selected); set => SetProperty(() => Selected, value); }

    /// <summary>오른쪽이 "새로 만들기" 칸으로 바뀌었는지.</summary>
    public bool IsCreating { get => GetProperty(() => IsCreating); set => SetProperty(() => IsCreating, value); }

    /// <summary>새 솔루션(게임) 이름. 이 이름으로 폴더가 만들어진다.</summary>
    public string NewSolutionName { get => GetProperty(() => NewSolutionName); set => SetProperty(() => NewSolutionName, value); }

    /// <summary>첫 프로젝트(모드·스테이지·런) 이름.</summary>
    public string NewProjectName { get => GetProperty(() => NewProjectName); set => SetProperty(() => NewProjectName, value); }

    /// <summary>솔루션이 만들어질 자리. 작업공간 아래다.</summary>
    public string NewSolutionPath => Path.Combine(ProjectPaths.Root, NewSolutionName ?? string.Empty);

    /// <summary>열 때 만들기 칸을 펴 둘지. Automation 화면의 "새 솔루션" 버튼이 켠다 - 최근 목록을 볼 일이 없다.</summary>
    public bool StartInCreateMode { get; set; }

    /// <summary>고른 솔루션. 창이 닫힌 뒤 앱이 이것을 본다. null 이면 사람이 그만둔 것이다.</summary>
    public Solution? Result { get; private set; }

    public ICommand OnInitializedCommand { get; }

    public ICommand DoOpenSelectedCommand { get; }

    public ICommand DoBrowseCommand { get; }

    public ICommand DoBeginCreateCommand { get; }

    public ICommand DoCreateCommand { get; }

    public ICommand DoCancelCreateCommand { get; }

    public ICommand DoForgetCommand { get; }

    private void OnInitialized() => Guard(() =>
    {
        Recent.Clear();

        foreach (var path in SolutionWorkspace.Recent())
        {
            Recent.Add(new RecentSolutionRow(
                Path.GetFileNameWithoutExtension(path),
                path,
                File.Exists(path)));
        }

        Selected = Recent.FirstOrDefault(row => row.Exists);

        // 최근이 하나도 없으면 처음 쓰는 사람이다 - 만들기 칸을 미리 열어 둔다. 새 솔루션 버튼으로 연 때도 같다.
        if (Recent.Count == 0 || StartInCreateMode) DoBeginCreate();
    });

    /// <summary>목록에서 고른 것을 연다. 파일이 없어졌으면 목록에서 지울지 묻는다.</summary>
    private void DoOpenSelected() => Guard(() =>
    {
        if (Selected is not { } row) return;

        if (!row.Exists)
        {
            var answer = MessageBoxService?.ShowMessage(
                $"이 자리에 솔루션이 없습니다.\n\n{row.FilePath}\n\n최근 목록에서 지울까요?",
                "시작", MessageButton.YesNo, MessageIcon.Question);

            if (answer == MessageResult.Yes) Forget(row);

            return;
        }

        Finish(Solution.Load(row.FilePath));
    });

    /// <summary>최근 목록에서 빼기만 한다. 파일은 건드리지 않는다.</summary>
    private void DoForget() => Guard(() =>
    {
        if (Selected is { } row) Forget(row);
    });

    private void DoBrowse() => Guard(() =>
    {
        var service = OpenFileDialogService;

        if (service is null) return;

        service.Filter = $"솔루션 (*{Solution.Extension})|*{Solution.Extension}";
        service.InitialDirectory = Directory.Exists(ProjectPaths.Root) ? ProjectPaths.Root : string.Empty;

        if (!service.ShowDialog()) return;

        Finish(Solution.Load(service.File.GetFullName()));
    });

    private void DoBeginCreate() => Guard(() =>
    {
        NewSolutionName = string.Empty;
        NewProjectName = string.Empty;

        IsCreating = true;
    });

    /// <summary>
    /// 새 솔루션과 첫 프로젝트를 만든다.
    /// </summary>
    /// <remarks>
    /// <b>빈 솔루션은 만들지 않는다</b> - 프로젝트가 없으면 캡처도 라벨링도 갈 자리가 없어서, 만들자마자
    /// "프로젝트를 만드세요" 를 또 만나게 된다.
    /// </remarks>
    private void DoCreate() => Guard(() =>
    {
        if (!Valid(NewSolutionName, "게임 이름") || !Valid(NewProjectName, "프로젝트 이름")) return;

        var solution = Solution.Create(ProjectPaths.Root, NewSolutionName.Trim());

        var projectFolder = Path.Combine(solution.Directory, NewProjectName.Trim());
        var project = ScriptProject.Create(projectFolder, NewProjectName.Trim(), DefaultEntrySource);

        // 사진·라벨 자리를 미리 만든다. 캡처 화면이 담을 때 만들기는 하지만, 처음부터 보이는 편이 낫다.
        Directory.CreateDirectory(Path.Combine(projectFolder, "images"));
        Directory.CreateDirectory(Path.Combine(projectFolder, "labels"));

        solution.Add(project.FilePath);
        solution.Save();

        Finish(solution);
    });

    /// <summary>파일 이름이 될 글자다. 못 쓰는 문자를 여기서 막는다 - 만들다 터지면 무엇이 문제인지 알기 어렵다.</summary>
    private bool Valid(string? name, string what)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBoxService?.ShowMessage($"{what}을(를) 적으세요.", "시작", MessageButton.OK, MessageIcon.Warning);

            return false;
        }

        if (name.Trim().IndexOfAny(Path.GetInvalidFileNameChars()) < 0) return true;

        MessageBoxService?.ShowMessage($"{what}에 쓸 수 없는 문자가 있습니다: {name}", "시작", MessageButton.OK, MessageIcon.Warning);

        return false;
    }

    private void Finish(Solution solution)
    {
        SolutionWorkspace.Use(solution);

        Result = solution;

        CurrentWindowService?.Close();
    }

    private void Forget(RecentSolutionRow row)
    {
        SolutionWorkspace.Forget(row.FilePath);

        Recent.Remove(row);

        Selected = Recent.FirstOrDefault(item => item.Exists);
    }

    /// <summary>여기서 터지면 앱이 아예 안 뜬다. 무엇이 문제인지 보여 주고 창은 살려 둔다.</summary>
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.Name);
        }
    }

    private const string DefaultEntrySource =
        "// 시작 파일입니다. 같은 프로젝트의 다른 .csx 에 만든 함수를 그대로 부를 수 있습니다.\n출력(\"안녕하세요\");\n";
}
