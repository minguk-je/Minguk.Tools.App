using System.IO;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

using Minguk.Image;
using Minguk.Tools.Projects.Settings;
using Minguk.Tools.ViewModels.Settings;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 설정 탭 - 솔루션·프로젝트마다 사람이 칸을 놓아 설정 화면을 만들고(디자인) 값을 바꾼다(미리보기). 스크립트는 <c>설정("이름")</c> 으로 읽는다.
/// </summary>
/// <remarks>
/// 설계는 <c>docs/솔루션-설정.md</c>. 솔루션 탭 아래 화면이라 고른 프로젝트를 따라간다 - 프로젝트를 바꾸면 솔루션 탭이 이 화면을 닫고 다시 연다.
///
/// <b>양식은 고치면 곧바로 저장한다</b>(환경설정 화면과 같다) - 저장 안 한 양식을 들고 닫기를 묻는 흐름을 두지 않는다.
/// 이름이 틀리거나 겹치면 저장하지 않고 상태 줄에 이유를 띄운다.
/// </remarks>
public partial class SolutionSettingsViewModel : DocumentViewModelBase, IFollowsProject
{
    public static SolutionSettingsViewModel Create() => ViewModelSource.Create(() => new SolutionSettingsViewModel());

    /// <summary>
    /// 플레이 화면이 창으로 띄우는 값 화면 - 디자인 없이 그 프로젝트 폴더의 설정 값만 바꾼다(<c>PlaySettingsView</c>).
    /// </summary>
    /// <remarks>솔루션 탭이 고른 프로젝트가 아니라 완성품의 프로젝트를 본다. 창이라 부모(셸)가 들어오지 않는다.</remarks>
    public static SolutionSettingsViewModel CreateForPlay(string projectDirectory)
    {
        var vm = Create();
        vm._fixedProject = projectDirectory;
        vm.IsValuesOnly = true;
        vm.Caption = $"설정 - {Path.GetFileName(Path.GetDirectoryName(projectDirectory))} / {Path.GetFileName(projectDirectory)}";
        return vm;
    }

    /// <summary>값만 바꾸는 창인가(플레이). 디자인은 켜지 않는다.</summary>
    public bool IsValuesOnly { get; private set; }

    /// <summary>값 창이 보는 프로젝트 폴더. null 이면 솔루션 탭이 고른 프로젝트.</summary>
    private string? _fixedProject;

    protected override bool RequiresParentViewModel => !IsValuesOnly;

    public SolutionSettingsViewModel()
    {
        Caption = "설정";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/gear.png");

        Layers = [];
        Toolbox =
        [
            // 아이콘은 DevExpress.Images 의 컨트롤 그림(사용자, 2026-09-17). 슬라이더(TrackBar) 전용 그림은 없어 가로 게이지를 쓴다.
            new(SettingsItemKind.Group, "구역", "칸을 묶는 상자. 라벨을 비우면 테두리 없이 묶기만 합니다.", DevExpressGlyph.Load("Images/Toolbox Items/Panel_16x16.png")),
            new(SettingsItemKind.Text, "글자", "글 한 줄 - 이름·명령어.", DevExpressGlyph.Load("Images/Content/TextBox_16x16.png")),
            new(SettingsItemKind.Number, "숫자", "숫자 - 범위·소수 자리를 정합니다.", DevExpressGlyph.Load("Images/Number Formats/Number_16x16.png")),
            new(SettingsItemKind.Check, "체크", "켜고 끄기.", DevExpressGlyph.Load("Images/Content/CheckBox_16x16.png")),
            new(SettingsItemKind.Combo, "콤보", "정해 둔 항목 중 하나.", DevExpressGlyph.Load("Images/Filter Elements/ComboBox_16x16.png")),
            new(SettingsItemKind.Slider, "슬라이더", "끌어서 고르는 숫자.", DevExpressGlyph.Load("Images/Gauges/GaugeStyleLinearHorizontal_16x16.png")),
            new(SettingsItemKind.List, "목록", "여러 줄 값(표) - 물약 목록·스킬 순서.", DevExpressGlyph.Load("Images/Grid/Grid_16x16.png")),
            // LayoutControl 의 크기 조절 막대(AllowHorizontalSizing·AllowVerticalSizing, 사용자 2026-09-17 "LayoutSplitter · 좌우 상하 조정"). 도킹(DockLayoutManager)이 아니다 - 판은 폼이고, 도킹은 VS 창 틀용이다.
            new(SettingsItemKind.Splitter, "나누기", "앞 칸의 크기 조절 막대. 칸 오른쪽을 끌어 좌우, 아래쪽을 끌어 상하 크기를 바꿉니다. 바꾼 크기는 양식에 저장됩니다.", DevExpressGlyph.Load("Images/Grid/ColumnWidth_16x16.png"))
        ];

        AddItemCommand = new DelegateCommand<SettingsToolboxItem>(DoAddItem, item => IsDesign && HasProject && item is not null, false);
        DeleteItemCommand = new DelegateCommand(DoDeleteItem, () => IsDesign && SelectedEditor is not null, false);
        CopyItemCommand = new DelegateCommand(DoCopyItem, () => IsDesign && SelectedEditor is not null, false);
        PasteItemCommand = new DelegateCommand(DoPasteItem, () => IsDesign && HasProject && _clipboard is not null, false);
        MoveUpCommand = new DelegateCommand(() => DoMove(-1), () => IsDesign && SelectedEditor is not null, false);
        MoveDownCommand = new DelegateCommand(() => DoMove(1), () => IsDesign && SelectedEditor is not null, false);
        RemoveUnusedCommand = new DelegateCommand(DoRemoveUnused, () => HasProject, false);
        ReloadCommand = new DelegateCommand(DoReload, () => HasProject, false);
        OpenFolderCommand = new DelegateCommand(DoOpenFolder, () => HasProject, false);
        EditListRowsCommand = new DelegateCommand(DoEditListRows, () => IsDesign && SelectedEditor is ListEditor, false);
        ResetValuesCommand = new DelegateCommand(DoResetValues, () => HasProject && !IsDesign, false);
        CanvasDropCommand = new DelegateCommand<SettingsDrop>(OnCanvasDropped, false);
        CanvasLayoutChangedCommand = new DelegateCommand<bool>(OnCanvasLayoutChanged, false);
        CanvasSizeChangedCommand = new DelegateCommand<SettingsItemSize>(OnCanvasSizeChanged, false);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────

    protected override void RestoreSettings()
    {
        _restoredLayer = GetSetting("Layer", nameof(SettingsLayerKind.Solution));

        if (IsValuesOnly) WindowBounds = GetSetting(WindowBoundsKey, string.Empty);
    }

    protected override void OnLoaded() => OpenProject(_fixedProject ?? Minguk.Tools.Projects.SolutionWorkspace.StartupDirectory);

    protected override void SaveSettings()
    {
        if (SelectedLayer is { } layer) SetSetting("Layer", layer.Kind.ToString());
        if (IsValuesOnly && !string.IsNullOrEmpty(WindowBounds)) SetSetting(WindowBoundsKey, WindowBounds);
    }

    protected override void ReleaseResources()
    {
        DetachLayers();
        _settings?.Flush();
    }

    // ── 시작 프로젝트 따라가기(IFollowsProject) ────────────────────────────

    public string? ProjectSwitchBlocker() => null;

    /// <summary>옛 프로젝트 값을 파일에 쓴다.</summary>
    public bool PrepareProjectSwitch()
    {
        _settings?.Flush();

        return true;
    }

    /// <summary>새 프로젝트의 양식·값으로 다시 연다. 플레이의 값 창(프로젝트를 박아 둔 것)은 안 따라간다.</summary>
    public void FollowProject()
    {
        if (!IsInitialized || _fixedProject is not null) return;

        OpenProject(Minguk.Tools.Projects.SolutionWorkspace.StartupDirectory);
    }
}
