using System.Collections.Generic;

using DevExpress.Mvvm;

using Minguk.Tools.Markup.Settings;
using Minguk.Tools.Projects.Settings;
using Minguk.Tools.ViewModels.Settings;

namespace Minguk.Tools.ViewModels;

/// <summary>편집 대상 층 콤보의 한 줄.</summary>
public sealed record SettingsLayerOption(SettingsLayerKind Kind, string Title)
{
    public override string ToString() => Title;
}

public partial class SolutionSettingsViewModel
{
    // ── 커맨드 ───────────────────────────────────────────────────────────

    public DelegateCommand<SettingsToolboxItem> AddItemCommand { get; private set; } = null!;

    public DelegateCommand DeleteItemCommand { get; private set; } = null!;

    public DelegateCommand MoveUpCommand { get; private set; } = null!;

    public DelegateCommand MoveDownCommand { get; private set; } = null!;

    public DelegateCommand RemoveUnusedCommand { get; private set; } = null!;

    public DelegateCommand ReloadCommand { get; private set; } = null!;

    public DelegateCommand OpenFolderCommand { get; private set; } = null!;

    /// <summary>목록 칸의 처음 행을 표 대화 상자로 고친다 - 속성 창 `처음 행` 의 ….</summary>
    public DelegateCommand EditListRowsCommand { get; private set; } = null!;

    // ── 서비스 ───────────────────────────────────────────────────────────

    /// <summary>처음 행 대화 상자. View 의 <c>ListRowsDialogService</c>.</summary>
    protected IDialogService? ListRowsDialogService => GetService<IDialogService>("ListRowsDialogService");

    // ── 바인딩 프로퍼티 ──────────────────────────────────────────────────

    /// <summary>지금 프로젝트 폴더. 솔루션이 없으면 null - 화면이 안내만 한다.</summary>
    public string? ProjectDirectory { get => GetProperty(() => ProjectDirectory); private set => SetProperty(() => ProjectDirectory, value, () => RaisePropertyChanged(nameof(HasProject))); }

    public bool HasProject => ProjectDirectory is not null;

    public IReadOnlyList<SettingsLayerOption> Layers { get => GetProperty(() => Layers); private set => SetProperty(() => Layers, value); }

    /// <summary>편집 대상 층. 디자인은 이 층의 양식을, 미리보기는 이 층에 값을 쓴다.</summary>
    public SettingsLayerOption? SelectedLayer { get => GetProperty(() => SelectedLayer); set => SetProperty(() => SelectedLayer, value, OnSelectedLayerChanged); }

    /// <summary>디자인(칸 놓기)인가, 미리보기(값 바꾸기)인가.</summary>
    public bool IsDesign { get => GetProperty(() => IsDesign); set => SetProperty(() => IsDesign, value, OnIsDesignChanged); }

    public IReadOnlyList<SettingsToolboxItem> Toolbox { get; private set; } = [];

    /// <summary>속성 창이 보는 칸. 아무것도 안 골랐으면 null.</summary>
    public SettingsItemEditor? SelectedEditor { get => GetProperty(() => SelectedEditor); private set => SetProperty(() => SelectedEditor, value); }

    public string? StatusText { get => GetProperty(() => StatusText); private set => SetProperty(() => StatusText, value); }

    /// <summary>상태 글이 오류인가(빨갛게).</summary>
    public bool IsStatusError { get => GetProperty(() => IsStatusError); private set => SetProperty(() => IsStatusError, value); }

    /// <summary>양식 파일 자리 - 상태 표시줄에 보인다.</summary>
    public string? FormPath { get => GetProperty(() => FormPath); private set => SetProperty(() => FormPath, value); }

    // ── 컨트롤 참조 ──────────────────────────────────────────────────────

    private SettingsFormCanvas? _canvas;
}
