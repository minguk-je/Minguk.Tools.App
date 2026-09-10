using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

using DevExpress.Mvvm;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.ViewModels;

public partial class LabelingViewModel
{
    // ── 커맨드 ───────────────────────────────────────────────────────────

    public DelegateCommand DoReloadCommand { get; private set; } = null!;

    public DelegateCommand DoOpenFolderCommand { get; private set; } = null!;

    public DelegateCommand DoChooseFolderCommand { get; private set; } = null!;

    public DelegateCommand DoSaveCommand { get; private set; } = null!;

    public DelegateCommand DoDeleteBoxCommand { get; private set; } = null!;

    public DelegateCommand DoClearBoxesCommand { get; private set; } = null!;

    public DelegateCommand DoAddClassCommand { get; private set; } = null!;

    public DelegateCommand DoRenameClassCommand { get; private set; } = null!;

    public DelegateCommand DoPreviousCommand { get; private set; } = null!;

    public DelegateCommand DoNextCommand { get; private set; } = null!;

    public DelegateCommand DoNextUnlabeledCommand { get; private set; } = null!;

    /// <summary>앞 장의 사각형을 가져온다. 연달아 담은 그림은 몹 자리가 거의 같다.</summary>
    public DelegateCommand DoCopyPreviousCommand { get; private set; } = null!;

    // ── 데이터셋 ─────────────────────────────────────────────────────────

    /// <summary>그림과 라벨을 담아 둔 폴더.</summary>
    public string? DatasetRoot
    {
        get => GetProperty(() => DatasetRoot);
        set => SetProperty(() => DatasetRoot, value);
    }

    /// <summary>목록에서 고른 그림. 바뀌면 앞 그림을 저장하고 새 그림을 읽는다.</summary>
    public LabelingRow? SelectedItem
    {
        get => GetProperty(() => SelectedItem);
        set => SetProperty(() => SelectedItem, value, OnSelectedItemChanged);
    }

    /// <summary>지금 그림. 캔버스가 그린다.</summary>
    public ImageSource? CurrentImage
    {
        get => GetProperty(() => CurrentImage);
        set => SetProperty(() => CurrentImage, value, () => RaisePropertyChanged(nameof(HasImage)));
    }

    public bool HasImage => CurrentImage is not null;

    /// <summary>
    /// 캔버스가 고른 사각형의 자리.
    /// </summary>
    /// <remarks>
    /// 캔버스가 쓰고 화면이 읽는다. 사각형을 마우스로 고르면 여기로 올라와, 지우기 버튼이
    /// 켜지고 목록에서도 같은 줄이 잡힌다.
    /// </remarks>
    public int SelectedBoxIndex
    {
        get => GetProperty(() => SelectedBoxIndex);
        set => SetProperty(() => SelectedBoxIndex, value, () => DoDeleteBoxCommand.RaiseCanExecuteChanged());
    }

    // ── 몹 이름 ──────────────────────────────────────────────────────────

    /// <summary>새로 그릴 사각형에 붙일 몹. 목록의 자리가 곧 번호다.</summary>
    public int SelectedClassIndex
    {
        get => GetProperty(() => SelectedClassIndex);
        set => SetProperty(() => SelectedClassIndex, value, OnSelectedClassChanged);
    }

    /// <summary>지금 고른 몹의 색. 사각형에 쓰이는 색과 같아야 어느 몹을 찍는 중인지 안다.</summary>
    public Brush CurrentClassBrush
    {
        get => GetProperty(() => CurrentClassBrush);
        set => SetProperty(() => CurrentClassBrush, value);
    }

    /// <summary>
    /// 캔버스에 넘길 이름 목록.
    /// </summary>
    /// <remarks>
    /// <see cref="ClassNames"/> 를 그대로 넘기지 않고 한 번 복사해 넘긴다. 캔버스가 받는 것은
    /// 읽기만 하는 목록이어야 하고, 이름이 바뀔 때마다 새 목록을 넘겨야 다시 그린다.
    /// </remarks>
    public IReadOnlyList<string> ClassNameSnapshot
    {
        get => GetProperty(() => ClassNameSnapshot);
        set => SetProperty(() => ClassNameSnapshot, value);
    }

    /// <summary>새 몹 이름을 받는 칸.</summary>
    public string? NewClassName
    {
        get => GetProperty(() => NewClassName);
        set => SetProperty(() => NewClassName, value);
    }

    // ── 학습 ─────────────────────────────────────────────────────────────

    public DelegateCommand DoTrainCommand { get; private set; } = null!;

    public DelegateCommand DoCancelTrainCommand { get; private set; } = null!;

    /// <summary>몇 바퀴 돌릴지.</summary>
    /// <remarks>
    /// 기본 20 은 작은 데이터셋에서 흔히 쓰는 값이다. 적으면 아무것도 못 배우고 많으면
    /// 외워 버린다(과적합). 얼마가 맞는지는 데이터마다 달라 화면에서 고치게 둔다.
    /// </remarks>
    public int TrainEpochs
    {
        get => GetProperty(() => TrainEpochs);
        set => SetProperty(() => TrainEpochs, value);
    }

    public bool IsTraining
    {
        get => GetProperty(() => IsTraining);
        set => SetProperty(() => IsTraining, value, () =>
        {
            DoTrainCommand.RaiseCanExecuteChanged();
            DoCancelTrainCommand.RaiseCanExecuteChanged();
        });
    }

    /// <summary>학습이 지금 무엇을 하는 중인지. 받는 진행률도 여기 뜬다.</summary>
    public string? TrainingStatus
    {
        get => GetProperty(() => TrainingStatus);
        set => SetProperty(() => TrainingStatus, value);
    }

    /// <summary>끝난 바퀴 / 돌릴 바퀴. 막대는 이것으로 찬다.</summary>
    public int TrainEpochsDone
    {
        get => GetProperty(() => TrainEpochsDone);
        set => SetProperty(() => TrainEpochsDone, value, () => RaisePropertyChanged(nameof(TrainProgressLabel)));
    }

    public int TrainEpochsTotal
    {
        get => GetProperty(() => TrainEpochsTotal);
        set => SetProperty(() => TrainEpochsTotal, value, () => RaisePropertyChanged(nameof(TrainProgressLabel)));
    }

    /// <summary>0~100. 진행 막대가 바로 물 수 있게 백분율로 둔다.</summary>
    public double TrainPercent
    {
        get => GetProperty(() => TrainPercent);
        set => SetProperty(() => TrainPercent, value);
    }

    /// <summary>학습 중 흘러나온 loss. 꺾은선이 이것을 그린다. 새로 시작하면 비운다.</summary>
    public System.Collections.ObjectModel.ObservableCollection<double> LossHistory { get; } = [];

    public string TrainProgressLabel
        => TrainEpochsTotal <= 0 ? string.Empty : $"{TrainEpochsDone}/{TrainEpochsTotal} 바퀴";

    /// <summary>학습기가 지금 보고 있는 그림 이름. 끝나면 비운다.</summary>
    public string? TrainingImageName
    {
        get => GetProperty(() => TrainingImageName);
        set => SetProperty(() => TrainingImageName, value);
    }

    /// <summary>
    /// 학습기가 보는 그림을 가운데 화면에도 띄울지.
    /// </summary>
    /// <remarks>
    /// 켜면 학습 중에는 목록 선택이 학습기를 따라다녀 라벨을 못 고친다. 그래서 기본은 끔이고,
    /// 지켜보며 "이건 다시 찍어야겠다" 를 고르고 싶을 때만 켠다. 그림 한 장 그리는 데
    /// 30~50ms 라 초당 두세 장은 부담이 없다.
    /// </remarks>
    public bool FollowTraining
    {
        get => GetProperty(() => FollowTraining);
        set => SetProperty(() => FollowTraining, value);
    }

    /// <summary>고를 수 있는 모델 크기들.</summary>
    public System.Collections.Generic.IReadOnlyList<string> InputSizes { get; } =
        [.. Vision.Training.DetectorTrainer.InputSizes.Select(s => $"{s.Width}x{s.Height}")];

    /// <summary>
    /// 모델이 실제로 볼 크기.
    /// </summary>
    /// <remarks>
    /// 작은 몹을 놓칠 때만 키운다. 1080p 화면에서 60px 짜리 몹은 320x180 으로 줄이면
    /// 10px 가 되어 잘 안 잡힌다. 대신 값이 픽셀 수에 비례해 늘어난다 -
    /// 실측으로 320x180 이 220ms, 640x360 이 587ms 다. 학습 시간도 같은 비율이다.
    ///
    /// 바꾸면 <b>반드시 다시 학습해야 한다.</b> 이미 만들어 둔 모델은 제가 학습된 크기를
    /// 옆에 들고 있어서(<c>detector.json</c>) 여기를 바꿔도 그쪽은 안 흔들린다.
    /// </remarks>
    public string? SelectedInputSize
    {
        get => GetProperty(() => SelectedInputSize);
        set => SetProperty(() => SelectedInputSize, value);
    }

    /// <summary>
    /// 학습을 누르기 전에 알아야 할 것. 다 갖춰져 있으면 null.
    /// </summary>
    /// <remarks>
    /// libtorch 를 아직 안 받았다거나 GPU 가 없다는 것을 <b>누르기 전에</b> 알려 준다.
    /// 2.2GB 를 받고 나서 "CPU 로는 못 씁니다" 라고 하면 안 된다.
    /// </remarks>
    public string? TrainingNotice
    {
        get => GetProperty(() => TrainingNotice);
        set => SetProperty(() => TrainingNotice, value, () => RaisePropertyChanged(nameof(HasTrainingNotice)));
    }

    public bool HasTrainingNotice => !string.IsNullOrEmpty(TrainingNotice);

    /// <summary>지금 폴더의 모델이 어떤 것인지 한 줄. 쪽지(detector.json)에서 읽는다.</summary>
    public string? ModelSummary
    {
        get => GetProperty(() => ModelSummary);
        set => SetProperty(() => ModelSummary, value);
    }

    // ── 찾아보기 ─────────────────────────────────────────────────────────

    public DelegateCommand DoDetectCommand { get; private set; } = null!;

    public DelegateCommand DoClearPredictionsCommand { get; private set; } = null!;

    /// <summary>모델이 찾은 점선을 전부 라벨로 굳힌다. 틀린 것은 굳힌 뒤 지운다.</summary>
    public DelegateCommand DoAdoptPredictionsCommand { get; private set; } = null!;

    public bool IsDetecting
    {
        get => GetProperty(() => IsDetecting);
        set => SetProperty(() => IsDetecting, value, () => DoDetectCommand.RaiseCanExecuteChanged());
    }

    /// <summary>
    /// 이보다 자신 없는 것은 안 보여 준다.
    /// </summary>
    /// <remarks>
    /// 낮추면 놓친 것까지 보이지만 헛것도 같이 늘어난다. 학습이 잘 됐는지 볼 때는
    /// 낮춰 보는 편이 도움이 되므로 화면에서 고치게 둔다.
    /// </remarks>
    public double MinimumScore
    {
        get => GetProperty(() => MinimumScore);
        set => SetProperty(() => MinimumScore, value);
    }

    public string? DetectStatus
    {
        get => GetProperty(() => DetectStatus);
        set => SetProperty(() => DetectStatus, value);
    }

    // ── 알림 ─────────────────────────────────────────────────────────────

    /// <summary>지금 무슨 일이 있었는지 한 줄. 자동 저장이 언제 돌았는지도 여기 뜬다.</summary>
    public string? StatusText
    {
        get => GetProperty(() => StatusText);
        set => SetProperty(() => StatusText, value);
    }

    /// <summary>몇 장 중 몇 장을 찍었는지.</summary>
    public string? ProgressText
    {
        get => GetProperty(() => ProgressText);
        set => SetProperty(() => ProgressText, value);
    }

    /// <summary>안 저장한 사각형이 있는지. 자동 저장이 돌기 전까지 잠깐 참이다.</summary>
    public bool IsDirty
    {
        get => GetProperty(() => IsDirty);
        set => SetProperty(() => IsDirty, value);
    }
}
