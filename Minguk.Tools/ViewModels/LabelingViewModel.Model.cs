using System.Collections.Generic;
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
