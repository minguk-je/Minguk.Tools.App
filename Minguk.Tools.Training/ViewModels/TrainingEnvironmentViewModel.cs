using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

using Minguk.Tools.ViewModels;
using Minguk.Tools.Vision;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Training.ViewModels;

/// <summary>학습 환경 표의 한 줄.</summary>
/// <param name="Name">사람이 읽는 이름.</param>
/// <param name="Path">있어야 하는 자리.</param>
/// <param name="Exists">지금 있는지.</param>
/// <param name="Note">없을 때 무엇을 하면 되는지.</param>
public sealed record TrainingItemRow(string Name, string Path, bool Exists, string Note)
{
    public string State => Exists ? "있음" : "없음";
}

/// <summary>
/// 학습 도구가 갖춰졌는지 보고, 두 폴더를 정하는 화면.
/// </summary>
/// <remarks>
/// 학습을 누르기 전에 무엇이 없는지 한눈에 보려는 것이다. 예전에는 학습 버튼을 눌러야 "환경이 없다" 는 말을
/// 만났고, 그때는 이미 데이터를 다 모은 뒤였다.
///
/// <b>폴더를 여기서 정한다</b>(사용자 결정 2026-09-14). 설정 화면에 두었더니 폴더를 바꾼 뒤 이 화면이 안 따라와,
/// 바꾼 자리가 맞는지 확인할 길이 없었다. 같은 화면에 두면 고치는 즉시 아래 표가 다시 그려진다.
///
/// <b>여기서 환경을 만들지 않는다.</b> 파이썬 5.5GB 를 받는 일은 앱이 몰래 할 일이 아니라
/// <c>도구\학습-환경-준비.ps1</c> 을 한 번 돌리는 일이다. 화면은 무엇이 없는지 말하고 그 자리를 열어 주기만 한다.
/// </remarks>
public class TrainingEnvironmentViewModel : DocumentViewModelBase
{
    public static TrainingEnvironmentViewModel Create() => ViewModelSource.Create(() => new TrainingEnvironmentViewModel());

    /// <summary>칸을 고치는 동안 설정에 쓰지 않기 위한 자물쇠. 복원 중에 켠다.</summary>
    private bool _loading;

    protected TrainingEnvironmentViewModel()
    {
        Caption = "환경";
        CaptionImage = Minguk.Image.FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/gear.png");

        Items = [];

        DoRefreshCommand = new DelegateCommand(DoRefresh);
        DoOpenTrainingRootCommand = new DelegateCommand(() => OpenFolder(TrainingPaths.Root));
        DoOpenProjectsRootCommand = new DelegateCommand(() => OpenFolder(ProjectPaths.Root));
        DoChooseTrainingRootCommand = new DelegateCommand(() => Choose(true));
        DoChooseProjectsRootCommand = new DelegateCommand(() => Choose(false));
    }

    protected IFolderBrowserDialogService FolderBrowserDialogService => GetService<IFolderBrowserDialogService>();

    public ObservableCollection<TrainingItemRow> Items { get; }

    /// <summary>
    /// 학습 도구가 사는 자리 - 파이썬 환경(YOLO·D-FINE), D-FINE 저장소, 사전학습 가중치, 학습 결과.
    /// </summary>
    /// <remarks>
    /// 지워도 <c>도구\학습-환경-준비.ps1</c> 로 다시 만들면 된다. 다만 <b>안에 든 가상환경은 복사해 옮길 수 없다</b> -
    /// pyvenv.cfg 에 그 PC 의 파이썬 경로가 박힌다. 새 자리에는 그 스크립트로 다시 만든다.
    /// </remarks>
    public string? TrainingRoot
    {
        get => GetProperty(() => TrainingRoot);
        set => SetProperty(() => TrainingRoot, value, OnTrainingRootChanged);
    }

    /// <summary>
    /// 작업공간 - 솔루션들이 든 폴더(화면 이름). 내가 만든 것(사진·라벨·모델·스크립트)이 이 아래 쌓인다.
    /// </summary>
    /// <remarks>
    /// 학습 경로와 나눠 둔다(사용자 결정 2026-09-14). 학습 폴더는 도구라 지우고 다시 만들어도 되지만 여기는 다시 만들 수 없다.
    /// 이미 담아 둔 데이터셋은 여기를 바꿔도 따라 옮겨지지 않는다 - 폴더째 옮기고 라벨링 화면에서 다시 고른다.
    /// </remarks>
    public string? ProjectsRoot
    {
        get => GetProperty(() => ProjectsRoot);
        set => SetProperty(() => ProjectsRoot, value, OnProjectsRootChanged);
    }

    public string? Summary { get => GetProperty(() => Summary); set => SetProperty(() => Summary, value); }

    public ICommand DoRefreshCommand { get; }

    public ICommand DoOpenTrainingRootCommand { get; }

    public ICommand DoOpenProjectsRootCommand { get; }

    public ICommand DoChooseTrainingRootCommand { get; }

    public ICommand DoChooseProjectsRootCommand { get; }

    protected override void RestoreSettings()
    {
        _loading = true;

        TrainingRoot = TrainingPaths.Root;
        ProjectsRoot = ProjectPaths.Root;

        _loading = false;
    }

    protected override void OnLoaded() => DoRefresh();

    /// <summary>칸을 고치면 곧바로 설정에 쓰고 표를 다시 그린다 - 바꾼 자리가 맞는지 그 자리에서 보이게.</summary>
    private void OnTrainingRootChanged() => Guard(() =>
    {
        if (_loading || string.IsNullOrWhiteSpace(TrainingRoot)) return;

        if (TrainingPaths.Root != TrainingRoot) TrainingPaths.Root = TrainingRoot.Trim();

        DoRefresh();
    });

    private void OnProjectsRootChanged() => Guard(() =>
    {
        if (_loading || string.IsNullOrWhiteSpace(ProjectsRoot)) return;

        if (ProjectPaths.Root != ProjectsRoot) ProjectPaths.Root = ProjectsRoot.Trim();

        DoRefresh();
    });

    private void Choose(bool isTrainingRoot) => Guard(() =>
    {
        var service = FolderBrowserDialogService;
        var current = isTrainingRoot ? TrainingRoot : ProjectsRoot;

        service.StartPath = Directory.Exists(current)
            ? current!
            : isTrainingRoot ? TrainingPaths.DefaultRoot : ProjectPaths.DefaultRoot;

        if (!service.ShowDialog()) return;

        if (isTrainingRoot) TrainingRoot = service.ResultPath;
        else ProjectsRoot = service.ResultPath;
    });

    private void DoRefresh() => Guard(() =>
    {
        Items.Clear();

        Add("학습경로", TrainingPaths.Root, Directory.Exists(TrainingPaths.Root), "위 칸에서 자리를 정합니다.");
        Add("작업공간", ProjectPaths.Root, Directory.Exists(ProjectPaths.Root), "위 칸에서 자리를 정합니다.");
        Add("YOLO 파이썬", TrainingPaths.YoloPython, File.Exists(TrainingPaths.YoloPython), @"도구\학습-환경-준비.ps1 -What yolo");
        Add("D-FINE 파이썬", TrainingPaths.DFinePython, File.Exists(TrainingPaths.DFinePython), @"도구\학습-환경-준비.ps1 -What dfine");
        Add("D-FINE 저장소", TrainingPaths.DFineRepository, Directory.Exists(TrainingPaths.DFineRepository), @"도구\학습-환경-준비.ps1 -What dfine");
        Add("D-FINE 가중치", TrainingPaths.DFineWeights, File.Exists(TrainingPaths.DFineWeights), @"도구\학습-환경-준비.ps1 -What dfine");
        Add("학습 결과", TrainingPaths.Runs, Directory.Exists(TrainingPaths.Runs), "처음 학습할 때 만들어집니다.");

        var missing = 0;

        foreach (var row in Items)
        {
            if (!row.Exists) missing++;
        }

        Summary = missing == 0
            ? "학습 환경이 다 갖춰졌습니다."
            : $@"{missing}개가 없습니다. 도구\학습-환경-준비.ps1 을 한 번 돌리면 만들어집니다.";

        return;

        void Add(string name, string path, bool exists, string note) => Items.Add(new TrainingItemRow(name, path, exists, note));
    });

    /// <summary>없는 폴더는 만들지 않고 그 위 폴더를 연다 - 빈 폴더를 만들어 두면 다음에 "있음" 으로 보인다.</summary>
    private void OpenFolder(string path) => Guard(() =>
    {
        var target = path;

        while (!string.IsNullOrEmpty(target) && !Directory.Exists(target))
            target = System.IO.Path.GetDirectoryName(target) ?? string.Empty;

        if (string.IsNullOrEmpty(target)) return;

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    });
}
