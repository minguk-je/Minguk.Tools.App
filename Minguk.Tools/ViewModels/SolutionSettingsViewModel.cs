using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

using Minguk.Image;
using Minguk.Tools.Markup.Settings;
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
public partial class SolutionSettingsViewModel : DocumentViewModelBase
{
    public static SolutionSettingsViewModel Create() => ViewModelSource.Create(() => new SolutionSettingsViewModel());

    public SolutionSettingsViewModel()
    {
        Caption = "설정";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/gear.png");

        Layers = [];
        Toolbox =
        [
            new(SettingsItemKind.Group, "구역", "칸을 묶는 상자. 라벨을 비우면 테두리 없이 묶기만 합니다."),
            new(SettingsItemKind.Text, "글자", "글 한 줄 - 이름·명령어."),
            new(SettingsItemKind.Number, "숫자", "숫자 - 범위·소수 자리를 정합니다."),
            new(SettingsItemKind.Check, "체크", "켜고 끄기."),
            new(SettingsItemKind.Combo, "콤보", "정해 둔 항목 중 하나."),
            new(SettingsItemKind.Slider, "슬라이더", "끌어서 고르는 숫자."),
            new(SettingsItemKind.List, "목록", "여러 줄 값(표) - 물약 목록·스킬 순서.")
        ];

        AddItemCommand = new DelegateCommand<SettingsToolboxItem>(DoAddItem, item => IsDesign && HasProject && item is not null, false);
        DeleteItemCommand = new DelegateCommand(DoDeleteItem, () => IsDesign && SelectedEditor is not null, false);
        MoveUpCommand = new DelegateCommand(() => DoMove(-1), () => IsDesign && SelectedEditor is not null, false);
        MoveDownCommand = new DelegateCommand(() => DoMove(1), () => IsDesign && SelectedEditor is not null, false);
        RemoveUnusedCommand = new DelegateCommand(DoRemoveUnused, () => HasProject, false);
        ReloadCommand = new DelegateCommand(DoReload, () => HasProject, false);
        OpenFolderCommand = new DelegateCommand(DoOpenFolder, () => HasProject, false);
    }

    // ── 생명주기 ─────────────────────────────────────────────────────────

    protected override void InitializeControls()
    {
        _canvas = FindControl<SettingsFormCanvas>("CanvasObjectService");

        if (_canvas is null) return;

        _canvas.EntryOf = name => _settings?.Find(name);
        _canvas.IsOverridden = IsOverridden;
        _canvas.ValueEdited += OnCanvasValueEdited;
        _canvas.ValueReset += OnCanvasValueReset;
        _canvas.SelectedItemChanged += OnCanvasSelectedItemChanged;
        _canvas.LayoutMayHaveChanged += OnCanvasLayoutMayHaveChanged;
        _canvas.ToolDropped += OnCanvasToolDropped;
    }

    protected override void RestoreSettings()
    {
        _restoredLayer = GetSetting("Layer", nameof(SettingsLayerKind.Solution));
    }

    protected override void OnLoaded() => OpenProject(Minguk.Tools.Projects.SolutionWorkspace.StartupDirectory);

    protected override void SaveSettings()
    {
        if (SelectedLayer is { } layer) SetSetting("Layer", layer.Kind.ToString());
    }

    protected override void ReleaseResources()
    {
        if (IsDesign) CommitCanvas();

        DetachLayers();
        _settings?.Flush();

        if (_canvas is not null)
        {
            _canvas.ValueEdited -= OnCanvasValueEdited;
            _canvas.ValueReset -= OnCanvasValueReset;
            _canvas.SelectedItemChanged -= OnCanvasSelectedItemChanged;
            _canvas.LayoutMayHaveChanged -= OnCanvasLayoutMayHaveChanged;
            _canvas.ToolDropped -= OnCanvasToolDropped;
        }
    }
}
