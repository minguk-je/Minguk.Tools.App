using Minguk.Base.Utilities;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

using Minguk.Image;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 모아 둔 그림에 몹 사각형을 찍는 화면.
/// </summary>
/// <remarks>
/// <b>어디에 서 있는 화면인가</b>
///
/// 캡처 모니터가 그림을 모으고(<c>데이터셋에 담기</c>), 여기서 라벨을 찍고, 그 라벨로
/// 학습한 모델을 <c>Inference/OnnxDmlEngine</c> 이 돌린다. 지금은 그 가운데 토막이다.
///
/// <b>자동으로 저장한다</b>
///
/// 그림을 넘길 때마다 앞 그림의 라벨을 저장한다. 수백 장을 찍는 일이라 장마다 저장을
/// 누르게 하면 반드시 잊고, 잊은 것은 되돌릴 수 없다. 대신 무엇이 저장됐는지 늘 보이게
/// <see cref="StatusText"/> 에 적는다.
/// </remarks>
public partial class LabelingViewModel : DocumentViewModelBase
{
    public static LabelingViewModel Create() => ViewModelSource.Create(() => new LabelingViewModel());

    public LabelingViewModel()
    {
        Caption = "라벨링";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/picture-edit.png");

        Items = [];
        Boxes = [];
        ClassNames = [];
        Predictions = [];

        DoReloadCommand = new DelegateCommand(DoReload, false);
        DoOpenFolderCommand = new DelegateCommand(DoOpenFolder, false);
        DoChooseFolderCommand = new DelegateCommand(DoChooseFolder, false);
        DoSaveCommand = new DelegateCommand(DoSave, () => HasImage, false);
        DoDeleteBoxCommand = new DelegateCommand(DoDeleteBox, () => SelectedBoxIndex >= 0, false);
        DoClearBoxesCommand = new DelegateCommand(DoClearBoxes, () => Boxes.Count > 0, false);
        DoAddClassCommand = new DelegateCommand(DoAddClass, false);
        DoRenameClassCommand = new DelegateCommand(DoRenameClass, () => SelectedClassIndex >= 0, false);
        DoPreviousCommand = new DelegateCommand(DoPrevious, () => Items.Count > 0, false);
        DoNextCommand = new DelegateCommand(DoNext, () => Items.Count > 0, false);
        DoNextUnlabeledCommand = new DelegateCommand(DoNextUnlabeled, () => Items.Count > 0, false);
        DoCopyPreviousCommand = new DelegateCommand(DoCopyPrevious, () => HasImage && Items.Count > 1, false);
        DoTrainCommand = new DelegateCommand(DoTrain, () => !IsTraining, false);
        DoCancelTrainCommand = new DelegateCommand(DoCancelTrain, () => IsTraining, false);
        DoDetectCommand = new DelegateCommand(DoDetect, () => !IsDetecting && HasImage, false);
        DoClearPredictionsCommand = new DelegateCommand(DoClearPredictions, () => Predictions.Count > 0, false);
        DoAdoptPredictionsCommand = new DelegateCommand(DoAdoptPredictions, () => Predictions.Count > 0, false);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────

    protected override void InitializeObservable()
    {
        // 사각형이 늘거나 줄면 "안 저장한 것이 있다" 를 세우고 버튼 상태를 다시 본다.
        // 컨트롤이 컬렉션을 직접 고치므로, 바뀐 것을 알 길은 이것뿐이다.
        Boxes.CollectionChanged += OnBoxesChanged;

        // 캔버스가 점선을 눌러 라벨로 옮기면 여기서는 모른다. 컬렉션이 바뀌면 버튼 상태를 다시 본다.
        Predictions.CollectionChanged += OnPredictionsChanged;
        Disposables.Add(Disposable.Create(() => Predictions.CollectionChanged -= OnPredictionsChanged));

        Disposables.Add(Disposable.Create(() => Boxes.CollectionChanged -= OnBoxesChanged));
    }

    /// <summary>
    /// 데이터셋 자리는 캡처 모니터와 <b>같이</b> 본다. 그래서 화면별 설정이 아니라
    /// <see cref="LabelDataset.ConfiguredRoot"/> 를 쓴다.
    /// </summary>
    protected override void RestoreSettings()
    {
        DatasetRoot = LabelDataset.ConfiguredRoot;

        TrainEpochs = GetSetting(nameof(TrainEpochs), 20);
        MinimumScore = GetSetting(nameof(MinimumScore), 0.5);
        FollowTraining = GetSetting(nameof(FollowTraining), false);

        // GPU 선택은 앱 전체 설정이다(시작할 때 App 이 읽는다). 저장된 번호에 맞는 항목을 고른다.
        var gpu = AppSettingUtility.Get(Vision.Training.LibTorchRuntime.GpuSettingKey, -1);
        SelectedGpuOption = gpu >= 0
            ? GpuOptions.FirstOrDefault(o => o.StartsWith($"GPU {gpu} ", StringComparison.Ordinal) || o == $"GPU {gpu}") ?? GpuOptions[0]
            : GpuOptions[0];
        GpuNotice = null;

        var size = GetSetting(nameof(SelectedInputSize), InputSizes[0]);

        SelectedInputSize = InputSizes.Contains(size) ? size : InputSizes[0];
    }

    protected override void OnLoaded()
    {
        DoReload();
        UpdateTrainingNotice();
    }

    protected override void SaveSettings()
    {
        if (!string.IsNullOrWhiteSpace(DatasetRoot)) LabelDataset.ConfiguredRoot = DatasetRoot;

        SetSetting(nameof(TrainEpochs), TrainEpochs);
        SetSetting(nameof(MinimumScore), MinimumScore);
        SetSetting(nameof(FollowTraining), FollowTraining);
        SetSetting(nameof(SelectedInputSize), SelectedInputSize ?? InputSizes[0]);
    }

    /// <summary>
    /// 닫기 전에 찍던 것을 저장한다.
    /// </summary>
    /// <remarks>
    /// <c>OnDestroy</c> 는 virtual 이 아니다. 베이스가 <c>SaveSettings → ReleaseResources</c>
    /// 순으로 부르므로 여기가 닫힐 때 마지막으로 손댈 수 있는 자리다.
    /// 라벨 파일 자리는 절대 경로로 이미 들고 있어 설정을 먼저 쓰든 나중에 쓰든 상관없다.
    /// </remarks>
    protected override void ReleaseResources()
    {
        SaveCurrentIfDirty();

        // 학습을 돌려 둔 채 화면을 닫을 수 있다. 결과를 받을 화면이 없어진 뒤에도
        // GPU 를 물고 있을 이유가 없어 취소는 걸어 둔다.
        _trainingCts?.Cancel();

        // 모델은 69MB 를 물고 있다. 화면을 닫으면 놓는다.
        ReleaseModel();
    }

    public ObservableCollection<LabelingRow> Items { get; }

    /// <summary>지금 그림의 사각형들. <c>Markup/LabelCanvas</c> 가 직접 고친다.</summary>
    public ObservableCollection<LabelBox> Boxes { get; }

    /// <summary>몹 이름들. 콤보와 캔버스가 같이 본다.</summary>
    public ObservableCollection<string> ClassNames { get; }

    /// <summary>모델이 찾아낸 것들. 캔버스가 점선으로 그린다.</summary>
    public ObservableCollection<Markup.PredictedBox> Predictions { get; }
}
