using System;
using Minguk.Base.Utilities;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;

using DevExpress.Mvvm;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.ViewModels;

public partial class LabelingViewModel
{
    // ── 커맨드 ───────────────────────────────────────────────────────────

    public DelegateCommand DoReloadCommand { get; private set; } = null!;

    /// <summary>지금 그림을 라벨과 함께 휴지통으로. Ctrl+Delete · 그림 목록에서 Delete.</summary>
    public DelegateCommand DoDeleteImageCommand { get; private set; } = null!;

    public DelegateCommand DoOpenFolderCommand { get; private set; } = null!;


    public DelegateCommand DoSaveCommand { get; private set; } = null!;

    public DelegateCommand DoDeleteBoxCommand { get; private set; } = null!;

    public DelegateCommand DoClearBoxesCommand { get; private set; } = null!;

    public DelegateCommand DoAddClassCommand { get; private set; } = null!;

    /// <summary>고른 검출을 지운다. 아무 라벨에도 안 쓰인 것만 - 뒤 번호는 라벨 파일까지 같이 당긴다.</summary>
    public DelegateCommand DoDeleteClassCommand { get; private set; } = null!;

    public DelegateCommand DoPreviousCommand { get; private set; } = null!;

    public DelegateCommand DoNextCommand { get; private set; } = null!;

    public DelegateCommand DoNextUnlabeledCommand { get; private set; } = null!;

    /// <summary>앞 장의 사각형을 가져온다. 연달아 담은 그림은 검출 자리가 거의 같다.</summary>
    public DelegateCommand DoCopyPreviousCommand { get; private set; } = null!;

    /// <summary>지금 그림의 사각형을 복사해 둔다(Ctrl+C). 순서를 안 가려도 되고 여러 장에 거듭 붙일 수 있다.</summary>
    public DelegateCommand DoCopyBoxesCommand { get; private set; } = null!;

    /// <summary>복사해 둔 사각형을 지금 그림에 더한다(Ctrl+V). 지금 있는 것은 안 지운다.</summary>
    public DelegateCommand DoPasteBoxesCommand { get; private set; } = null!;

    /// <summary>그림 판 확대를 1(창에 맞춤)로 되돌린다.</summary>
    public DelegateCommand DoZoomResetCommand { get; private set; } = null!;

    // ── 그림 목록 패널 너비 ──────────────────────────────────────────────

    /// <summary>
    /// 그림 목록 패널의 너비(px). 열을 다 합친 것에 행 번호 칸·세로 스크롤 막대를 더한 값이다.
    /// </summary>
    /// <remarks>
    /// 열은 내용 너비(Auto)라 그리드 너비와 무관하게 정해진다. 그것을 패널 너비로 되돌려 주지 않으면 패널은 비율(0.22*)로
    /// 잡혀 이름이 긴 그림에서 가로 스크롤이 생기거나, 짧으면 오른쪽이 빈다. 0 이면(첫 배치 전) 화면이 기본 비율을 쓴다.
    /// </remarks>
    public double ImagesPanelWidth
    {
        get => GetProperty(() => ImagesPanelWidth);
        set => SetProperty(() => ImagesPanelWidth, value);
    }

    // ── 그림 판 확대 ─────────────────────────────────────────────────────

    /// <summary>
    /// 그림 판 배율. 1 이 창에 맞춤.
    /// </summary>
    /// <remarks>
    /// 캔버스(<c>LabelCanvas.Zoom</c>)와 양방향이라 휠로 바꾼 값이 도구 줄 콤보에도 뜨고, 콤보에서 고른 값이
    /// 캔버스로 간다. 범위는 캔버스와 같다 - 콤보에 아무 값이나 쳐도 여기서 잘린다.
    /// </remarks>
    public double LabelZoom
    {
        get => GetProperty(() => LabelZoom);
        set => SetProperty(() => LabelZoom, Math.Clamp(Math.Round(value, 2), Markup.LabelCanvas.MinimumZoom, Markup.LabelCanvas.MaximumZoom));
    }

    /// <summary>확대 콤보의 프리셋. 캡처 미리보기와 같은 값이다.</summary>
    public ObservableCollection<double> ZoomOptions { get; } = new([0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 10.0]);

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

    // ── 검출 이름 ──────────────────────────────────────────────────────────

    /// <summary>새로 그릴 사각형에 붙일 검출. 목록의 자리가 곧 번호다.</summary>
    public int SelectedClassIndex
    {
        get => GetProperty(() => SelectedClassIndex);
        set => SetProperty(() => SelectedClassIndex, value, OnSelectedClassChanged);
    }

    /// <summary>
    /// 검출 목록(그리드)이 고른 줄.
    /// </summary>
    /// <remarks>
    /// 그리드는 <c>SelectedItem</c> 으로만 묶이므로 번호(<see cref="SelectedClassIndex"/>)와 여기서 서로 맞춘다.
    /// 캔버스·라벨은 여전히 번호를 쓴다 - 이름은 바뀌어도 번호는 그대로기 때문이다.
    /// </remarks>
    public LabelClassRow? SelectedClass
    {
        get => GetProperty(() => SelectedClass);
        set => SetProperty(() => SelectedClass, value, OnSelectedClassRowChanged);
    }

    /// <summary>지금 고른 검출의 색. 사각형에 쓰이는 색과 같아야 어느 검출을 찍는 중인지 안다.</summary>
    public Brush CurrentClassBrush
    {
        get => GetProperty(() => CurrentClassBrush);
        set => SetProperty(() => CurrentClassBrush, value);
    }

    /// <summary>
    /// 캔버스에 넘길 이름 목록.
    /// </summary>
    /// <remarks>
    /// <see cref="Classes"/> 를 그대로 넘기지 않고 이름만 복사해 넘긴다. 캔버스가 받는 것은
    /// 읽기만 하는 목록이어야 하고, 이름이 바뀔 때마다 새 목록을 넘겨야 다시 그린다.
    /// </remarks>
    public IReadOnlyList<string> ClassNameSnapshot
    {
        get => GetProperty(() => ClassNameSnapshot);
        set => SetProperty(() => ClassNameSnapshot, value);
    }

    /// <summary>캔버스에 넘길 검출 색 목록(번호 순). 고른 색이 없는 번호는 기본 색이다. 색을 고르면 새 목록을 넘긴다.</summary>
    public IReadOnlyList<Color> ClassColors
    {
        get => GetProperty(() => ClassColors);
        set => SetProperty(() => ClassColors, value);
    }

    // ── 학습 ─────────────────────────────────────────────────────────────

    public DelegateCommand DoTrainCommand { get; private set; } = null!;

    public DelegateCommand DoCancelTrainCommand { get; private set; } = null!;

    /// <summary>몇 바퀴 돌릴지. 한 바퀴 = 학습 그림을 전부 한 번씩 보는 것.</summary>
    /// <remarks>
    /// 기본 20 은 작은 데이터셋에서 흔히 쓰는 값이다. 적으면 아무것도 못 배우고 많으면
    /// 외워 버린다(과적합). 얼마가 맞는지는 데이터마다 달라 화면에서 고치게 둔다.
    /// 왜 여러 바퀴인지는 화면 툴팁에 적었다(사용자, 2026-09-15) - 한 번 볼 때 조금씩만 고치고, 바퀴마다 같은 그림을 다르게 비틀어 본다.
    /// </remarks>
    public int TrainEpochs
    {
        get => GetProperty(() => TrainEpochs);
        set => SetProperty(() => TrainEpochs, value);
    }

    /// <summary>고를 수 있는 YOLO 학습 크기(480·640·960·1280).</summary>
    public int[] YoloImageSizes { get; } = Vision.Training.YoloTrainer.ImageSizes;

    /// <summary>
    /// 다음 YOLO 학습의 크기. 저장한다(<c>YoloImageSize</c>). 지금 쓰는 모델의 크기는 옆 줄 요약("960x960")에 뜬다 - 바꾸면 다시 학습해야 먹는다.
    /// </summary>
    /// <remarks>
    /// 작은 검출(멀리 있는 봇)을 잘 찾게 960·1280 까지 넓혔다(사용자, 2026-09-15 - 학습·찾기가 느려져도 그래픽 카드는 나중에 바꾼다).
    /// D-FINE 은 640 고정이라 YOLO 가 골라져 있을 때만 켜진다.
    /// </remarks>
    public int SelectedYoloImageSize
    {
        get => GetProperty(() => SelectedYoloImageSize);
        set => SetProperty(() => SelectedYoloImageSize, value);
    }

    /// <summary>학습 크기 콤보를 켤지 - YOLO 가 골라져 있고 학습 중이 아닐 때.</summary>
    public bool CanChooseYoloImageSize => !IsTraining && Vision.Training.YoloTrainer.WeightsFor(SelectedModelChoice?.Name) is not null;

    /// <summary>학습 크기 칸 툴팁.</summary>
    public string ImageSizeHelp { get; } =
        "모델이 그림을 이 크기(정사각, 한 변 px)로 줄여서 봅니다. 게임 화면 1920×1080 은 비율을 지키고 위아래에 여백을 넣어 맞춥니다.\n" +
        "\n" +
        "• 키우면 작은 검출(멀리 있는 봇)을 더 잘 찾습니다. 640 에서는 1920 화면이 3분의 1로 줄어 60px 봇이 20px 이 됩니다 - 1280 이면 40px.\n" +
        "• 대신 학습과 검출이 느려집니다. 픽셀 수에 비례해 960 은 640 의 약 2.3배, 1280 은 약 4배 일을 합니다.\n" +
        "• 그래픽 카드 메모리에 맞게 한 번에 넣는 그림 수를 알아서 줄입니다(YOLO11n: 480 → 14장 · 640 → 8장 · 960 → 4장 · 1280 → 2장).\n" +
        "\n" +
        "YOLO11n 98장 60바퀴, GTX 1060 3GB: 640 은 약 5분(실측). 960·1280 은 픽셀 수로 어림하면 약 12분·20분입니다(아직 안 재 봤습니다).\n" +
        "바꾸면 다시 학습해야 먹습니다. D-FINE 은 640 고정입니다.";

    /// <summary>바퀴 칸 툴팁 - 왜 여러 바퀴를 돌리는지(사용자 요청, 2026-09-15).</summary>
    public string EpochHelp { get; } =
        "바퀴(epoch) = 학습 그림을 전부 한 번씩 보는 것입니다. 98장이면 한 바퀴에 98장(묶음 8장씩 13묶음)을 봅니다.\n" +
        "\n" +
        "왜 여러 바퀴를 돌리나\n" +
        "• 한 번 볼 때 조금씩만 고칩니다. 한 묶음을 보고 크게 고치면 그 묶음에만 맞춰 흔들려서, 여러 번 보며 조금씩 맞춰 갑니다.\n" +
        "• 바퀴마다 같은 그림을 다르게 봅니다. 네 장을 이어 붙이고 크기·자리·색을 비틀어서, 바퀴가 늘면 검출이 놓일 수 있는 모습을 더 많이 봅니다.\n" +
        "• 처음 몇 바퀴는 거의 못 찾습니다. 아래 loss 꺾은선이 내려가는 동안 배우는 중이고, 평평해지면 더 돌려도 얻는 것이 적습니다.\n" +
        "\n" +
        "너무 적으면 검출을 못 찾고, 너무 많으면 학습 그림만 외워(과적합) 새 장면에서 못 찾습니다.\n" +
        "YOLO11n 은 98장 60바퀴에 약 5분이었습니다. 그림을 크게 늘렸거나 loss 가 아직 내려가는 중이면 바퀴를 늘리세요.";

    public bool IsTraining
    {
        get => GetProperty(() => IsTraining);
        set => SetProperty(() => IsTraining, value, () =>
        {
            DoTrainCommand.RaiseCanExecuteChanged();
            DoCancelTrainCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(IsTrainingBatchVisible));
            RaisePropertyChanged(nameof(CanChooseYoloImageSize));
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

    /// <summary>지금 바퀴에서 끝난 묶음 / 한 바퀴의 묶음 수. YOLO 가 묶음마다 알린다. 모르면 0.</summary>
    public int TrainBatchDone
    {
        get => GetProperty(() => TrainBatchDone);
        set => SetProperty(() => TrainBatchDone, value, () => RaisePropertyChanged(nameof(TrainProgressLabel)));
    }

    public int TrainBatchTotal
    {
        get => GetProperty(() => TrainBatchTotal);
        set => SetProperty(() => TrainBatchTotal, value, () => RaisePropertyChanged(nameof(TrainProgressLabel)));
    }

    /// <summary>"12/60 바퀴 · 묶음 3/13". 묶음을 모르면(D-FINE) 바퀴만.</summary>
    public string TrainProgressLabel
        => TrainEpochsTotal <= 0
            ? string.Empty
            : $"{TrainEpochsDone}/{TrainEpochsTotal} 바퀴" + (TrainBatchTotal > 0 ? $" · 묶음 {TrainBatchDone}/{TrainBatchTotal}" : string.Empty);

    /// <summary>
    /// 학습 묶음을 라벨 판 자리에 보일지(사용자, 2026-09-15 - 없어진 "학습 중인 그림 보기" 를 되살림). 저장한다.
    /// </summary>
    /// <remarks>
    /// 켜 두면 학습하는 동안 라벨 판 위에 묶음 그림이 덮여 라벨을 못 고친다 - 그래서 기본은 끔이고, 끄면 곧바로 라벨 판이 돌아온다.
    /// 옛 학습(TorchSharp)은 그림을 한 장씩 넘겨 그 그림을 띄웠지만, YOLO 는 여러 장을 이어 붙여 묶음으로 배우므로 묶음을 보인다.
    /// </remarks>
    public bool ShowTrainingBatch
    {
        get => GetProperty(() => ShowTrainingBatch);
        set => SetProperty(() => ShowTrainingBatch, value, () => RaisePropertyChanged(nameof(IsTrainingBatchVisible)));
    }

    /// <summary>바퀴마다 새로 오는 학습 묶음 그림(라벨 상자 포함). 학습을 새로 시작하면 비운다.</summary>
    public System.Windows.Media.ImageSource? TrainingBatchImage
    {
        get => GetProperty(() => TrainingBatchImage);
        set => SetProperty(() => TrainingBatchImage, value, () => RaisePropertyChanged(nameof(IsTrainingBatchVisible)));
    }

    /// <summary>라벨 판 대신 묶음 그림을 덮을지 - 켰고, 학습 중이고, 그림이 왔을 때. 학습이 끝나면 라벨 판이 알아서 돌아온다.</summary>
    public bool IsTrainingBatchVisible => ShowTrainingBatch && IsTraining && TrainingBatchImage is not null;

    /// <summary>
    /// 고를 수 있는 GPU. "자동" 다음에 카드마다 하나씩. 이름은 Windows 가 아는 대로 적는다.
    /// </summary>
    /// <remarks>
    /// 순서는 CUDA 의 번호 순서(대개 nvidia-smi 와 같다)로 믿는다. 어긋나면 이름을 보고 고른다.
    /// </remarks>
    public System.Collections.Generic.IReadOnlyList<string> GpuOptions { get; } = BuildGpuOptions();

    /// <summary>고른 GPU. 바꾸면 설정에 바로 저장하고, 적용은 다음 실행부터다.</summary>
    public string? SelectedGpuOption
    {
        get => GetProperty(() => SelectedGpuOption);
        set => SetProperty(() => SelectedGpuOption, value, OnGpuOptionChanged);
    }

    /// <summary>"바꾸면 다시 실행해야 적용됩니다" 같은 안내. 비어 있으면 안 보인다.</summary>
    public string? GpuNotice
    {
        get => GetProperty(() => GpuNotice);
        set => SetProperty(() => GpuNotice, value);
    }

    private static System.Collections.Generic.IReadOnlyList<string> BuildGpuOptions()
    {
        // NVML 이 카드 전부를 PCI 순서로 준다(nvidia-smi 와 같은 번호). 화면이 붙은 카드에는 (화면) 을 붙인다.
        var gpus = Vision.Training.GpuProbe.List();
        var auto = Vision.Training.GpuProbe.PickForTraining(gpus);

        var options = new System.Collections.Generic.List<string>
        {
            auto is { } picked && gpus.Count >= 2
                ? $"자동 - 지금은 GPU {picked.Index}{(picked.HasDisplay ? string.Empty : " (화면 없는 카드)")}"
                : "자동"
        };

        if (gpus.Count == 0)
        {
            // 드라이버가 없거나 NVML 을 못 읽는다. 번호만 준다.
            options.Add("GPU 0");
            options.Add("GPU 1");
            return options;
        }

        options.AddRange(gpus.Select(g => $"GPU {g.Index} - {g.Name.Replace("NVIDIA ", string.Empty)}{(g.HasDisplay ? " (화면)" : string.Empty)}"));

        return options;
    }

    private void OnGpuOptionChanged()
    {
        var index = ParseGpuIndex(SelectedGpuOption);
        var saved = AppSettingUtility.Get(Vision.Training.LibTorchRuntime.GpuSettingKey, -1);

        if (index == saved) return;

        AppSettingUtility.Set(Vision.Training.LibTorchRuntime.GpuSettingKey, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // YOLO 학습은 따로 뜨는 파이썬이라 다음 학습부터 바로 이 카드로 돈다. 안내를 띄울 것이 없다.
        GpuNotice = null;
    }

    private static int ParseGpuIndex(string? option)
    {
        if (string.IsNullOrEmpty(option) || !option.StartsWith("GPU ", StringComparison.Ordinal)) return -1;

        var rest = option[4..];
        var end = rest.IndexOf(' ');

        return int.TryParse(end < 0 ? rest : rest[..end], out var index) ? index : -1;
    }

    /// <summary>검출이 지금 실제로 쓰는 모델 한 줄(ONNX 가 있으면 그것). 그 모델의 쪽지에서 읽는다.</summary>
    public string? ModelSummary
    {
        get => GetProperty(() => ModelSummary);
        set => SetProperty(() => ModelSummary, value);
    }

    /// <summary>고를 수 있는 모델들(보관본 ONNX + 이 화면의 학습 모델). "쓰는 모델" 콤보가 보인다.</summary>
    public ObservableCollection<Vision.Training.DetectorChoice> ModelChoices { get; } = [];

    /// <summary>
    /// 검출에 쓸 모델. 고르면 그 모델이 검출 자리(detector.onnx)에 앉는다 - 켜 둔 스크립트·플레이 화면도 몇 초 안에 따라온다.
    /// </summary>
    public Vision.Training.DetectorChoice? SelectedModelChoice
    {
        get => GetProperty(() => SelectedModelChoice);
        set => SetProperty(() => SelectedModelChoice, value, () =>
        {
            RaisePropertyChanged(nameof(TrainButtonText));
            OnSelectedModelChoiceChanged();
        });
    }

    /// <summary>학습 버튼 글. 무엇으로 학습할지 누르기 전에 보이게 콤보에 고른 모델 이름을 붙인다.</summary>
    /// <remarks>앱이 못 돌리는 모델(우리가 모르는 이름)이면 꺼진 채 그렇게 적는다.</remarks>
    public string TrainButtonText => SelectedModelChoice is { } choice
        ? (IsTrainable(choice.Name) ? $"{choice.Name} 학습" : $"{choice.Name} 은 앱에서 학습 못 함")
        : "학습";


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
    /// 라벨이 하나도 없는 그림으로 넘어가면 자동 라벨을 알아서 돌린다(사용자, 2026-09-15). 저장한다, 기본 켬.
    /// </summary>
    /// <remarks>
    /// 방향키로 휙휙 넘기는 동안은 안 돌린다 - 멈춘 뒤 0.25초. 찍어 둔 라벨이 있는 그림은 건드리지 않는다(점선이 실선 위에 겹쳐 헷갈린다).
    /// 학습하는 동안은 쉰다 - 같은 카드에서 CUDA 학습과 DirectML 추론이 겹쳐 GPU 가 리셋된 적이 있다(TrainingActivity).
    /// </remarks>
    public bool AutoDetectNewImages
    {
        get => GetProperty(() => AutoDetectNewImages);
        set => SetProperty(() => AutoDetectNewImages, value);
    }

    /// <summary>
    /// 이보다 신뢰도가 낮은 것은 안 보여 준다.
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
