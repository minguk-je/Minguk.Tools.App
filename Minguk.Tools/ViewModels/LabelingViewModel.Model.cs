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
