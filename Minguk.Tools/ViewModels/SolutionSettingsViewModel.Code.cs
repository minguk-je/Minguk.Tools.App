using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;
using Minguk.Tools.Markup.Settings;
using Minguk.Tools.Projects.Settings;
using Minguk.Tools.ViewModels.Settings;

namespace Minguk.Tools.ViewModels;

public partial class SolutionSettingsViewModel
{
    private SolutionSettings? _settings;
    private string _restoredLayer = nameof(SettingsLayerKind.Solution);

    /// <summary>디자인에서 고치는 양식(편집 대상 층의 사본). 고칠 때마다 층에 저장한다.</summary>
    private SettingsForm? _working;

    private SettingsItem? _selected;

    /// <summary>우리가 양식을 저장하는 중 - 층이 알려 오는 바뀜을 흘려보낸다.</summary>
    private bool _savingForm;

    private bool _switching;

    /// <summary>
    /// 프로젝트 폴더로 연다. 솔루션 탭이 화면을 새로 열 때 한 번 - 검사 하네스는 임시 프로젝트로 부른다.
    /// </summary>
    public void OpenProject(string? projectDirectory) => Guard(() =>
    {
        DetachLayers();

        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            ProjectDirectory = null;
            _settings = null;
            Layers = [];
            SetStatus("솔루션을 열고 프로젝트를 고르면 설정 화면을 만들 수 있습니다.");
            _canvas?.Build(SettingsForm.NewRoot(), design: false);
            RaiseCommands();
            return;
        }

        _settings = SolutionSettings.ForProject(projectDirectory);
        ProjectDirectory = _settings.ProjectDirectory;

        var project = Path.GetFileName(_settings.ProjectDirectory);
        Layers = _settings.Solution is null
            ? [new SettingsLayerOption(SettingsLayerKind.Project, $"이 프로젝트 ({project})")]
            : [new SettingsLayerOption(SettingsLayerKind.Solution, "솔루션 공통"), new SettingsLayerOption(SettingsLayerKind.Project, $"이 프로젝트 ({project})")];

        _settings.Project.Changed += OnLayerChanged;
        if (_settings.Solution is not null) _settings.Solution.Changed += OnLayerChanged;

        _switching = true;

        try
        {
            SelectedLayer = Layers.FirstOrDefault(l => l.Kind.ToString() == _restoredLayer) ?? Layers[0];
        }
        finally
        {
            _switching = false;
        }

        LoadWorking();
        Rebuild();
        ReportWarnings();
        RaiseCommands();
    });

    private SettingsLayerKind TargetKind => SelectedLayer?.Kind ?? SettingsLayerKind.Project;

    private void DetachLayers()
    {
        if (_settings is null) return;

        _settings.Project.Changed -= OnLayerChanged;
        if (_settings.Solution is not null) _settings.Solution.Changed -= OnLayerChanged;
    }

    private void LoadWorking()
    {
        if (_settings is null) return;

        lock (SettingsLayer.Gate) _working = _settings.LayerOf(TargetKind).Form.Clone();

        FormPath = _settings.LayerOf(TargetKind).FormPath;
        Select(null);
    }

    // ── 그리기 ───────────────────────────────────────────────────────────

    /// <summary>판을 다시 짓는다. 디자인은 편집 대상 층 양식, 미리보기는 합친 양식.</summary>
    private void Rebuild()
    {
        if (_canvas is null || _settings is null) return;

        if (IsDesign)
        {
            _canvas.Build(_working?.Root ?? SettingsForm.NewRoot(), design: true, _selected);
            return;
        }

        _canvas.Build(PreviewRoot(), design: false);
    }

    /// <summary>
    /// 미리보기 뿌리 - 솔루션 공통 칸 다음에 프로젝트 칸. 둘 다 있으면 프로젝트 칸은 「이 프로젝트」 구역에 묶는다.
    /// </summary>
    /// <remarks>칸 객체는 층의 양식 그대로 쓴다(복사 안 함). 미리보기는 트리를 고치지 않는다.</remarks>
    private SettingsItem PreviewRoot()
    {
        var root = SettingsForm.NewRoot();

        lock (SettingsLayer.Gate)
        {
            var solution = _settings!.Solution?.Form.Root.Children ?? [];
            var project = _settings.Project.Form.Root.Children ?? [];

            root.Children!.AddRange(solution);

            if (project.Count == 0) return root;

            if (solution.Count == 0)
            {
                root.Children.AddRange(project);
                return root;
            }

            root.Children.Add(new SettingsItem
            {
                Kind = SettingsItemKind.Group,
                Label = $"이 프로젝트 ({Path.GetFileName(_settings.ProjectDirectory)})",
                Orientation = SettingsOrientation.Vertical,
                Children = [.. project]
            });
        }

        return root;
    }

    /// <summary>
    /// 지금 층에서 덮어쓴 값인가. 프로젝트 칸은 늘 프로젝트 층에 쓰므로 프로젝트 값이면 덮어쓴 것이다.
    /// </summary>
    private bool IsOverridden(SettingsEntry entry)
        => entry.Source == (entry.Layer == SettingsLayerKind.Project ? SettingsLayerKind.Project : TargetKind);

    // ── 미리보기: 값 ─────────────────────────────────────────────────────

    private void OnCanvasValueEdited(object? sender, (string Name, JsonNode? Value) e) => Guard(() =>
    {
        if (_settings is null) return;

        try
        {
            _settings.SetValue(e.Name, e.Value, TargetKind);
            SetStatus($"「{e.Name}」 을(를) {(TargetKind == SettingsLayerKind.Solution ? "솔루션 공통" : "이 프로젝트")}에 저장했습니다.");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidCastException)
        {
            SetStatus(ex.Message, error: true);
            _canvas?.Refresh(e.Name);
        }
    });

    private void OnCanvasValueReset(object? sender, string name) => Guard(() =>
    {
        if (_settings?.Find(name) is not { } entry) return;

        var layer = entry.Layer == SettingsLayerKind.Project ? SettingsLayerKind.Project : TargetKind;

        _settings.ResetValue(name, layer);
        SetStatus($"「{name}」 의 덮어쓴 값을 뺐습니다.");
    });

    /// <summary>층이 바뀌었다(스크립트가 값을 썼거나, 파일을 다시 읽었거나, 우리가 저장했거나). 아무 스레드에서나 온다.</summary>
    private void OnLayerChanged(object? sender, string? name)
    {
        if (_savingForm) return;

        void Apply() => Guard(() =>
        {
            if (_canvas is null) return;

            if (name is not null)
            {
                _canvas.Refresh(name);
                return;
            }

            // 양식이 바뀌었다 - 디자인 중이면 고치던 것을 덮지 않는다.
            if (!IsDesign) Rebuild();
        });

        if (DispatcherService is { } dispatcher) dispatcher.BeginInvoke(Apply);
        else Apply();
    }

    // ── 디자인 ───────────────────────────────────────────────────────────

    private void OnIsDesignChanged() => Guard(() =>
    {
        if (_switching) return;

        if (!IsDesign)
        {
            CommitCanvas();
            Select(null);
        }
        else
        {
            LoadWorking();
        }

        Rebuild();
        RaiseCommands();
    });

    private void OnSelectedLayerChanged() => Guard(() =>
    {
        if (_switching || _settings is null) return;

        // 층을 바꾸기 전에 고치던 양식을 넣는다.
        if (IsDesign) CommitCanvas();

        LoadWorking();
        Rebuild();
    });

    private void OnCanvasSelectedItemChanged(object? sender, SettingsItem? item) => Guard(() =>
    {
        CommitCanvas();
        Select(item);
    });

    private void OnCanvasLayoutMayHaveChanged(object? sender, EventArgs e) => Guard(CommitCanvas);

    /// <summary>판에서 끌어 옮긴 결과를 되읽어 바뀌었으면 저장한다.</summary>
    private void CommitCanvas()
    {
        if (!IsDesign || _canvas is null || _working is null || !ReferenceEquals(_canvas.Root, _working.Root)) return;

        if (_canvas.ReadBack()) SaveWorking("배치를 저장했습니다.");
    }

    private void Select(SettingsItem? item)
    {
        _selected = item;

        if (item is null)
        {
            SelectedEditor = null;
        }
        else
        {
            var editor = SettingsItemEditor.Create(item);

            editor.CheckName = (name, self) => _settings?.CheckNewName(name, TargetKind, self);
            editor.Changed = refused => Guard(() =>
            {
                if (refused is not null)
                {
                    SetStatus(refused, error: true);
                    return;
                }

                SaveWorking($"「{item.DisplayLabel}」 을(를) 고쳤습니다.");
                Rebuild();
            });

            SelectedEditor = editor;
        }

        RaiseCommands();
    }

    /// <summary>도구 상자 두 번 누르기·Enter·놓기 - 고른 구역 안에, 고른 칸이 있으면 그 뒤에, 아니면 맨 끝에.</summary>
    private void DoAddItem(SettingsToolboxItem tool) => Guard(() =>
    {
        if (_selected is { Kind: SettingsItemKind.Group } group && _working is not null && !ReferenceEquals(group, _working.Root))
            AddItem(tool, group, SettingsDropPlacement.Inside);
        else if (_selected is not null)
            AddItem(tool, _selected, SettingsDropPlacement.After);
        else
            AddItem(tool, null, SettingsDropPlacement.End);
    });

    /// <summary>도구 상자에서 판으로 끌어 놓았다 - 판이 계산한 자리에 넣는다.</summary>
    private void OnCanvasToolDropped(object? sender, SettingsDrop drop) => Guard(() =>
    {
        if (!IsDesign || Toolbox.FirstOrDefault(t => t.Kind == drop.Kind) is not { } tool) return;

        AddItem(tool, drop.Anchor, drop.Placement);
    });

    private void AddItem(SettingsToolboxItem tool, SettingsItem? anchor, SettingsDropPlacement placement)
    {
        if (_working is null || _settings is null) return;

        CommitCanvas();

        var item = new SettingsItem { Kind = tool.Kind };

        switch (tool.Kind)
        {
            case SettingsItemKind.Group:
                item.Label = "구역";
                item.Orientation = SettingsOrientation.Vertical;
                item.Children = [];
                break;
            case SettingsItemKind.Number:
                item.Default = JsonValue.Create(0L);
                break;
            case SettingsItemKind.Slider:
                item.Min = 0;
                item.Max = 100;
                item.Default = JsonValue.Create(50L);
                break;
            case SettingsItemKind.Check:
                item.Default = JsonValue.Create(false);
                break;
            case SettingsItemKind.Combo:
                item.Items = ["항목1", "항목2"];
                break;
            case SettingsItemKind.List:
                item.Columns = [new SettingsColumn { Name = "이름" }, new SettingsColumn { Name = "값", Kind = SettingsItemKind.Number }];
                break;
        }

        if (item.HasValue) item.Name = UniqueName(tool.Title);

        // 기준 칸이 이 양식에 없으면(판을 그린 뒤 양식이 바뀌었다) 맨 끝에 둔다.
        var parent = anchor is null ? null : FindParent(_working.Root, anchor);

        if (placement == SettingsDropPlacement.Inside && anchor is { Kind: SettingsItemKind.Group } && (parent is not null || ReferenceEquals(anchor, _working.Root)))
        {
            (anchor.Children ??= []).Add(item);
        }
        else if (placement is SettingsDropPlacement.Before or SettingsDropPlacement.After && parent is not null)
        {
            var index = parent.Children!.IndexOf(anchor!);
            parent.Children.Insert(placement == SettingsDropPlacement.Before ? index : index + 1, item);
        }
        else
        {
            _working.Root.Children!.Add(item);
        }

        _selected = item;
        SaveWorking($"「{tool.Title}」 칸을 놓았습니다{(item.HasValue ? $" - 스크립트에서 설정(\"{item.Name}\") 로 읽습니다" : "")}.");
        Rebuild();
        Select(item);
    }

    private string UniqueName(string title)
    {
        for (var n = 1; ; n++)
        {
            var name = title + n;
            if (_settings!.CheckNewName(name, TargetKind) is null) return name;
        }
    }

    private void DoDeleteItem() => Guard(() =>
    {
        if (_working is null || _selected is null) return;

        CommitCanvas();

        if (FindParent(_working.Root, _selected) is not { } parent) return;

        var label = _selected.DisplayLabel;

        parent.Children!.Remove(_selected);
        SaveWorking($"「{label}」 칸을 지웠습니다. 값은 파일에 남아 칸을 되살리면 돌아옵니다([안 쓰는 값 정리] 로 지웁니다).");
        Select(null);
        Rebuild();
    });

    private void DoMove(int delta) => Guard(() =>
    {
        if (_working is null || _selected is null) return;

        CommitCanvas();

        if (FindParent(_working.Root, _selected) is not { } parent) return;

        var index = parent.Children!.IndexOf(_selected);
        var target = Math.Clamp(index + delta, 0, parent.Children.Count - 1);
        if (target == index) return;

        parent.Children.RemoveAt(index);
        parent.Children.Insert(target, _selected);

        SaveWorking("순서를 바꿨습니다.");
        Rebuild();
    });

    private static SettingsItem? FindParent(SettingsItem root, SettingsItem child)
    {
        if (root.Children is null) return null;
        if (root.Children.Any(c => ReferenceEquals(c, child))) return root;

        foreach (var group in root.Children)
        {
            if (FindParent(group, child) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// 고치던 양식을 층에 저장한다. 이름이 틀리거나 겹치면 저장하지 않고 이유를 띄운다.
    /// </summary>
    private bool SaveWorking(string done)
    {
        if (_working is null || _settings is null) return false;

        if (Validate(_working) is { } problem)
        {
            SetStatus($"저장하지 않았습니다 - {problem}", error: true);
            return false;
        }

        _savingForm = true;

        try
        {
            _settings.LayerOf(TargetKind).SaveForm(_working.Clone());
        }
        finally
        {
            _savingForm = false;
        }

        SetStatus(done);
        return true;
    }

    /// <summary>저장 전 검사 - 이름 모양, 양식 안 겹침, 다른 층과 겹침.</summary>
    private string? Validate(SettingsForm form)
    {
        foreach (var item in form.ValueItems())
        {
            if (SettingsForm.CheckNameShape(item.Name) is { } shape) return $"「{item.DisplayLabel}」: {shape}";
        }

        if (form.DuplicateNames() is { Count: > 0 } duplicates) return $"같은 이름이 두 번 있습니다: {string.Join(", ", duplicates)}";

        foreach (var item in form.ValueItems())
        {
            // 편집 대상 층 안의 겹침은 위에서 봤다 - 저장된 양식에 같은 이름이 있는 것은 자기 자신이라 다른 층과만 본다.
            if (_settings!.CheckOtherLayers(item.Name, TargetKind) is { } clash) return clash;
        }

        return null;
    }

    // ── 도구 모음 ────────────────────────────────────────────────────────

    private void DoRemoveUnused() => Guard(() =>
    {
        if (_settings is null) return;

        var defined = _settings.Entries().Select(e => e.Item.Name).ToHashSet(StringComparer.Ordinal);
        var removed = _settings.LayerOf(TargetKind).RemoveUnused(defined.Contains);

        SetStatus(removed.Count == 0 ? "지울 값이 없습니다." : $"칸이 없는 값 {removed.Count}개를 지웠습니다: {string.Join(", ", removed)}");
    });

    private void DoReload() => Guard(() =>
    {
        if (_settings is null) return;

        _settings.Project.Reload();
        _settings.Solution?.Reload();

        LoadWorking();
        Rebuild();
        ReportWarnings();
    });

    private void DoOpenFolder() => Guard(() =>
    {
        if (_settings is null) return;

        var folder = _settings.LayerOf(TargetKind).Directory;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    });

    /// <summary>목록 칸의 처음 행 - 열대로 만든 표를 대화 상자로 띄우고, 확인이면 양식에 넣는다(저장은 편집기의 Changed).</summary>
    private void DoEditListRows() => Guard(() =>
    {
        if (SelectedEditor is not ListEditor editor || ListRowsDialogService is not { } dialogs) return;

        if (editor.Item.Columns is not { Count: > 0 } columns)
        {
            SetStatus("열이 없습니다 - 속성 창의 「열」 을 먼저 적으세요.", error: true);
            return;
        }

        var dialog = new SettingsListRowsViewModel(columns, editor.DefaultRowsValue);
        var ok = new UICommand { Caption = "확인", IsDefault = true, Id = MessageResult.OK };
        var cancel = new UICommand { Caption = "취소", IsCancel = true, Id = MessageResult.Cancel };

        if (dialogs.ShowDialog([ok, cancel], $"「{editor.Item.DisplayLabel}」 처음 행", dialog) != ok) return;

        editor.SetDefaultRows(dialog.Rows);
    });

    private void ReportWarnings()
    {
        if (_settings is null) return;

        var warnings = _settings.Entries().Where(e => e.Warning is not null).ToList();

        if (warnings.Count > 0)
            SetStatus($"⚠ 칸 {warnings.Count}개에 경고가 있습니다 - {warnings[0].Warning}", error: true);
        else
            SetStatus(IsDesign ? "칸을 끌어 옮기고, 고른 칸은 오른쪽 속성에서 고칩니다." : "값을 바꾸면 곧바로 저장합니다. 도는 스크립트도 다음 호출부터 바뀐 값을 봅니다.");
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText = error && !text.StartsWith('⚠') ? "⚠ " + text : text;
        IsStatusError = error;

        if (error) Logger.Info(text);
    }

    private void RaiseCommands()
    {
        AddItemCommand.RaiseCanExecuteChanged();
        DeleteItemCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        RemoveUnusedCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
        EditListRowsCommand.RaiseCanExecuteChanged();
    }
}
