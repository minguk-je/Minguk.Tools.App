using System.Collections.Generic;

using DevExpress.Mvvm;

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

    /// <summary>편집 대상 층에서 덮어쓴 값을 모두 뺀다 - 칸들이 아래 층 값(솔루션 공통·처음 값)으로 돌아간다. 편집 대상 콤보 뒤 [초기값].</summary>
    public DelegateCommand ResetValuesCommand { get; private set; } = null!;

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

    /// <summary>
    /// 값 창(플레이·스크립트의 [설정])의 자리·크기 <c>왼쪽,위,너비,높이</c>. 창이 옮겨지거나 커질 때마다 <c>WindowBoundsBehavior</c> 가 적고, 닫을 때 저장한다.
    /// </summary>
    public string? WindowBounds { get => GetProperty(() => WindowBounds); set => SetProperty(() => WindowBounds, value); }

    private const string WindowBoundsKey = "ValuesWindowBounds";

    /// <summary>양식 파일 자리 - 상태 표시줄에 보인다.</summary>
    public string? FormPath { get => GetProperty(() => FormPath); private set => SetProperty(() => FormPath, value); }

    // ── 판 ── 화면 모델은 판 컨트롤을 모른다(사용자, 2026-09-17 "MVVM 으로"). 판은 아래를 바인딩으로 받는다.

    /// <summary>판에 그릴 것 - 칸 모델 트리(<see cref="SettingsFieldViewModel"/>)와 디자인인가. 새 객체를 넣으면 판이 다시 짓는다.</summary>
    public SettingsFormState? Form { get => GetProperty(() => Form); private set => SetProperty(() => Form, value); }

    /// <summary>판에서 고른 칸(디자인). 판을 누르면 판이 쓰고, 칸을 놓거나 지우면 화면 모델이 쓴다.</summary>
    public SettingsItem? SelectedFormItem { get => GetProperty(() => SelectedFormItem); set => SetProperty(() => SelectedFormItem, value, OnSelectedFormItemChanged); }

    /// <summary>판에 도구 상자의 칸을 놓았다 - 인자는 판이 계산한 자리.</summary>
    public DelegateCommand<SettingsDrop> CanvasDropCommand { get; private set; } = null!;

    /// <summary>디자인에서 손을 뗐다 - 판이 끌어 옮긴 결과를 칸 트리에 되읽었다. 인자는 바뀌었는가.</summary>
    public DelegateCommand<bool> CanvasLayoutChangedCommand { get; private set; } = null!;
}
