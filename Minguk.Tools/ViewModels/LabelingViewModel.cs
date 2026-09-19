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
/// 모아 둔 그림에 검출 사각형을 찍는 화면.
/// </summary>
/// <remarks>
/// <b>어디에 서 있는 화면인가</b>
///
/// 캡처 화면이 그림을 모으고(<c>데이터셋에 담기</c>), 여기서 라벨을 찍고, 그 라벨로
/// 학습한 모델을 <c>Inference/OnnxDmlEngine</c> 이 돌린다. 지금은 그 가운데 토막이다.
///
/// <b>자동으로 저장한다</b>
///
/// 그림을 넘길 때마다 앞 그림의 라벨을 저장한다. 수백 장을 찍는 일이라 장마다 저장을
/// 누르게 하면 반드시 잊고, 잊은 것은 되돌릴 수 없다. 대신 무엇이 저장됐는지 늘 보이게
/// <see cref="StatusText"/> 에 적는다.
/// </remarks>
public partial class LabelingViewModel : DocumentViewModelBase, IFollowsProject
{
    public static LabelingViewModel Create() => ViewModelSource.Create(() => new LabelingViewModel());

    public LabelingViewModel()
    {
        Caption = "라벨링";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/picture-edit.png");

        Items = [];
        Boxes = [];
        Classes = [];
        Predictions = [];

        DoReloadCommand = new DelegateCommand(DoReload, false);
        DoOpenFolderCommand = new DelegateCommand(DoOpenFolder, false);
        DoSaveCommand = new DelegateCommand(DoSave, () => HasImage, false);
        DoDeleteBoxCommand = new DelegateCommand(DoDeleteBox, () => SelectedBoxIndex >= 0, false);
        DoClearBoxesCommand = new DelegateCommand(DoClearBoxes, () => Boxes.Count > 0, false);
        DoDeleteImageCommand = new DelegateCommand(DoDeleteImage, () => HasImage && !IsExtractingVideo, false);
        DoAddClassCommand = new DelegateCommand(DoAddClass, false);
        DoDeleteClassCommand = new DelegateCommand(DoDeleteClass, () => SelectedClassIndex >= 0, false);
        DoPreviousCommand = new DelegateCommand(DoPrevious, () => Items.Count > 0, false);
        DoNextCommand = new DelegateCommand(DoNext, () => Items.Count > 0, false);
        DoNextUnlabeledCommand = new DelegateCommand(DoNextUnlabeled, () => Items.Count > 0, false);
        DoCopyPreviousCommand = new DelegateCommand(DoCopyPrevious, () => HasImage && Items.Count > 1, false);
        DoCopyBoxesCommand = new DelegateCommand(DoCopyBoxes, () => Boxes.Count > 0, false);
        DoPasteBoxesCommand = new DelegateCommand(DoPasteBoxes, () => HasImage && _boxClipboard is { Count: > 0 }, false);
        DoTrainCommand = new DelegateCommand(DoTrain, () => CanTrain, false);
        DoCancelTrainCommand = new DelegateCommand(DoCancelTrain, () => IsTraining, false);
        DoDetectCommand = new DelegateCommand(DoDetect, () => !IsDetecting && HasImage, false);
        DoClearPredictionsCommand = new DelegateCommand(DoClearPredictions, () => Predictions.Count > 0, false);
        DoAdoptPredictionsCommand = new DelegateCommand(DoAdoptPredictions, () => Predictions.Count > 0, false);
        DoZoomResetCommand = new DelegateCommand(() => LabelZoom = 1, false);
        InitializeVideoCommands();
        ExtractInterval = "1초";

        // 0 이면 캔버스가 0.5 로 잘라 보이는데 콤보는 0% 라 어긋난다. 저장값은 RestoreSettings 가 덮는다.
        LabelZoom = 1;
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────

    /// <summary>검출 그리드. 더하기·더블 클릭이 이름 칸을 열 때 쓴다(<c>ShowEditor</c>).</summary>
    private DevExpress.Xpf.Grid.GridControl? _classGrid;

    private DevExpress.Xpf.Grid.TableView? _classGridView;

    /// <summary>이름 칸을 열어도 되는 순간인지. 더블 클릭·더하기 직후에만 참이고, 칸이 닫히면 다시 거짓이다.</summary>
    private bool _allowClassEdit;

    /// <summary>그림 목록 그리드. 열 너비를 합쳐 패널 너비(<see cref="ImagesPanelWidth"/>)를 잰다.</summary>
    private DevExpress.Xpf.Grid.GridControl? _imagesGrid;

    /// <summary>검출 패널. 너비를 설정에 남기고 되돌린다 - 도킹 배치 전체는 저장하지 않는다(그림·학습 패널은 내용에서 매번 잰다).</summary>
    private DevExpress.Xpf.Docking.LayoutPanel? _classesPanel;

    /// <summary>그리드 열 구성을 바꾸면 올린다 - 옛 배치가 새 열을 몰라 엉킨다.</summary>
    private const int GridLayoutVersion = 2; // 2: 그림 목록의 loss·◀ 열을 뺐다(2026-09-14)


    private const string ImagesGridLayoutKey = "ImagesGridLayout";
    private const string ClassGridLayoutKey = "ClassGridLayout";
    private const string ClassesPanelWidthKey = "ClassesPanelWidth";

    protected override void InitializeControls()
    {
        _imagesGrid = FindControl<DevExpress.Xpf.Grid.GridControl>("ImagesGridObjectService");

        if (_imagesGrid is not null)
        {
            // 모든 열을 내용 너비(Auto)로. XAML 의 첨부 속성으로는 안 된다 - 콜백이 GridControl 에서만 돌고, 그때는 열이
            // 아직 없어 아무것도 안 바뀐다(실측: 전부 Pixel 로 남았다). 열이 다 만들어진 여기서 직접 부른다.
            Minguk.Base.Dependency.GridControlDependency.ApplyColumnAutoWidth(_imagesGrid, true);
            _imagesGrid.LayoutUpdated += OnImagesGridLayoutUpdated;
        }

        _classGrid = FindControl<DevExpress.Xpf.Grid.GridControl>("ClassGridObjectService");
        _classGridView = _classGrid?.View as DevExpress.Xpf.Grid.TableView;
        _classesPanel = FindControl<DevExpress.Xpf.Docking.LayoutPanel>("ClassesPanelObjectService");

        if (_classGridView is not { } view) return;

        // 이름 칸은 더블 클릭·더하기 직후에만 열린다(VS 솔루션 탐색기처럼) - 한 번 누를 때마다 열리면 줄을 고르려다
        // 편집이 된다. 색 칸은 한 번 눌러 바로 고른다 - 글이 아니라 고르기라 실수로 열려도 잃는 것이 없다.
        view.ShowingEditor += OnClassEditorShowing;
        view.HiddenEditor += OnClassEditorHidden;
        view.RowDoubleClick += OnClassRowDoubleClick;
    }

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
    /// 데이터셋 자리는 캡처 화면과 <b>같이</b> 본다 - Automation 에서 고른 프로젝트 폴더다(<see cref="LabelDataset.ConfiguredRoot"/>).
    /// </summary>
    protected override void RestoreSettings()
    {
        DatasetRoot = LabelDataset.ConfiguredRoot;

        TrainEpochs = GetSetting(nameof(TrainEpochs), 20);
        ShowTrainingBatch = GetSetting(nameof(ShowTrainingBatch), false);
        AutoDetectNewImages = GetSetting(nameof(AutoDetectNewImages), true);

        var extractInterval = GetSetting(nameof(ExtractInterval), "1초");
        ExtractInterval = ExtractIntervalChoices.Contains(extractInterval) ? extractInterval : "1초";

        // 목록에 없는 값(손으로 고친 설정)이면 640.
        var size = GetSetting("YoloImageSize", Vision.Training.YoloTrainer.DefaultImageSize);
        SelectedYoloImageSize = YoloImageSizes.Contains(size) ? size : Vision.Training.YoloTrainer.DefaultImageSize;
        MinimumScore = GetSetting(nameof(MinimumScore), 0.5);
        LabelZoom = GetSetting(nameof(LabelZoom), 1.0);

        // GPU 선택은 앱 전체 설정이다(시작할 때 App 이 읽는다). 저장된 번호에 맞는 항목을 고른다.
        var gpu = AppSettingUtility.Get(Vision.Training.LibTorchRuntime.GpuSettingKey, -1);
        SelectedGpuOption = gpu >= 0
            ? GpuOptions.FirstOrDefault(o => o.StartsWith($"GPU {gpu} ", StringComparison.Ordinal) || o == $"GPU {gpu}") ?? GpuOptions[0]
            : GpuOptions[0];
        GpuNotice = null;

        // 그리드 배치(정렬·열 순서)와 검출 패널 너비는 그리드가 자리를 잡은 뒤(ContextIdle)에 얹는다(캡처 화면과 같은 이유).
        // 그림 그리드는 복원이 열을 Pixel 로 되돌리므로 그 뒤에 다시 Auto 로 놓는다.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(RestoreLayouts));
    }

    // ── 배치 저장·복원 ───────────────────────────────────────────────────

    /// <summary>
    /// 남기는 것: 두 그리드의 정렬·열 순서(<c>LayoutSerializationService</c>), 검출 패널 너비.
    /// 안 남기는 것: 그림 패널 너비(열 합에서 잰다), 학습 패널 높이(내용에서 잰다), 도킹 배치 전체(끌기·띄우기를 막아 둬 바뀔 것이 없다).
    /// </summary>
    private void RestoreLayouts() => Guard(() =>
    {
        if (GetSetting(nameof(GridLayoutVersion), 0) == GridLayoutVersion)
        {
            RestoreGridLayout("ImagesGridLayoutService", ImagesGridLayoutKey);
            RestoreGridLayout("ClassGridLayoutService", ClassGridLayoutKey);
        }
        else
        {
            Logger.Debug("그리드 열 구성이 바뀌었다. 저장된 배치를 버리고 기본으로 시작한다.");
        }

        // 복원이 열 너비를 Pixel 로 써 넣는다. 열은 내용 너비여야 하므로 다시 Auto 로.
        if (_imagesGrid is not null) Minguk.Base.Dependency.GridControlDependency.ApplyColumnAutoWidth(_imagesGrid, true);

        var width = GetSetting(ClassesPanelWidthKey, 0d);
        if (width > 50 && _classesPanel is not null) _classesPanel.ItemWidth = new System.Windows.GridLength(width, System.Windows.GridUnitType.Pixel);
    });

    private void RestoreGridLayout(string serviceName, string settingKey)
    {
        var layout = GetSetting(settingKey, string.Empty);
        if (string.IsNullOrEmpty(layout)) return;

        // 검색 창 펼침 상태는 안 되돌린다 - 검출 그리드는 XAML 이 Never 인데 복원이 펼친 채로 굳힌다(캡처 화면 실측).
        layout = System.Text.RegularExpressions.Regex.Replace(layout, "<property name=\"ActualShowSearchPanel\">[^<]*</property>", string.Empty);

        try
        {
            ServiceContainer.GetService<ILayoutSerializationService>(serviceName).Deserialize(layout);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"{serviceName} 복원 실패. 기본 배치로 시작한다.");
        }
    }

    private void SaveLayouts()
    {
        try
        {
            SetSetting(ImagesGridLayoutKey, ServiceContainer.GetService<ILayoutSerializationService>("ImagesGridLayoutService").Serialize());
            SetSetting(ClassGridLayoutKey, ServiceContainer.GetService<ILayoutSerializationService>("ClassGridLayoutService").Serialize());
            SetSetting(nameof(GridLayoutVersion), GridLayoutVersion);

            if (_classesPanel is { ActualWidth: > 50 } panel) SetSetting(ClassesPanelWidthKey, panel.ActualWidth);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "배치 저장 실패. 다음에 기본 배치로 시작한다.");
        }
    }

    protected override void OnLoaded()
    {
        DoReload();
    }

    protected override void SaveSettings()
    {
        SetSetting(nameof(TrainEpochs), TrainEpochs);
        SetSetting(nameof(ShowTrainingBatch), ShowTrainingBatch);
        SetSetting(nameof(AutoDetectNewImages), AutoDetectNewImages);
        SetSetting(nameof(ExtractInterval), ExtractInterval ?? "1초");
        SetSetting("YoloImageSize", SelectedYoloImageSize);
        SetSetting(nameof(MinimumScore), MinimumScore);
        SetSetting(nameof(LabelZoom), LabelZoom);
        SaveLayouts();
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

        if (_imagesGrid is not null) _imagesGrid.LayoutUpdated -= OnImagesGridLayoutUpdated;

        if (_classGridView is { } view)
        {
            view.ShowingEditor -= OnClassEditorShowing;
            view.HiddenEditor -= OnClassEditorHidden;
            view.RowDoubleClick -= OnClassRowDoubleClick;
        }

        // 학습을 돌려 둔 채 화면을 닫을 수 있다. 결과를 받을 화면이 없어진 뒤에도
        // GPU 를 물고 있을 이유가 없어 취소는 걸어 둔다.
        _trainingCts?.Cancel();

        // 모델은 69MB 를 물고 있다. 화면을 닫으면 놓는다.
        ReleaseModel();
    }

    // ── 시작 프로젝트 따라가기(IFollowsProject) ────────────────────────────

    /// <summary>학습·영상 뽑기는 옛 프로젝트 폴더에 쓰는 중이다 - 끝나거나 멈출 때까지 바꾸지 않는다.</summary>
    public string? ProjectSwitchBlocker()
    {
        if (!IsInitialized) return null;
        if (IsTraining) return "학습 중에는 프로젝트를 바꿀 수 없습니다 - 라벨링 탭에서 학습을 멈춘 뒤 고르세요.";
        if (IsExtractingVideo) return "영상에서 사진을 뽑는 중에는 프로젝트를 바꿀 수 없습니다 - 끝나거나 멈춘 뒤 고르세요.";

        return null;
    }

    /// <summary>고치던 라벨을 옛 프로젝트에 저장한다.</summary>
    public bool PrepareProjectSwitch()
    {
        if (IsInitialized) SaveCurrentIfDirty();

        return true;
    }

    /// <summary>새 프로젝트의 데이터셋·클래스·사진·모델 목록으로 다시 읽는다. 옛 모델은 놓는다.</summary>
    public void FollowProject()
    {
        if (!IsInitialized) return;

        DatasetRoot = LabelDataset.ConfiguredRoot;
        ReleaseModel();
        DoReload();
    }

    public ObservableCollection<LabelingRow> Items { get; }

    /// <summary>지금 그림의 사각형들. <c>Markup/LabelCanvas</c> 가 직접 고친다.</summary>
    public ObservableCollection<LabelBox> Boxes { get; }

    /// <summary>검출 목록(번호·이름·색). 그리드가 보이고 이름은 그 안에서 고쳐 쓴다. 캔버스는 <see cref="ClassNameSnapshot"/> 을 본다.</summary>
    public ObservableCollection<LabelClassRow> Classes { get; }

    /// <summary>모델이 찾아낸 것들. 캔버스가 점선으로 그린다.</summary>
    public ObservableCollection<Markup.PredictedBox> Predictions { get; }
}
