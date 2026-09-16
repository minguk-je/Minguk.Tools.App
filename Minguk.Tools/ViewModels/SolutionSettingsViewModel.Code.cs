using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;
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
            SetStatus(IsValuesOnly ? "완성품의 프로젝트 폴더를 찾지 못했습니다." : "솔루션을 열고 프로젝트를 고르면 설정 화면을 만들 수 있습니다.");
            _fields.Clear();
            Form = new SettingsFormState(new SettingsFieldViewModel(SettingsForm.NewRoot(), null), IsDesign: false);
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

    /// <summary>이름 → 미리보기 칸 모델. 층이 바뀌면 그 칸만 맞춘다.</summary>
    private readonly Dictionary<string, SettingsFieldViewModel> _fields = new(StringComparer.Ordinal);

    /// <summary>판을 다시 짓는다 - 칸 모델 트리를 새로 만들어 <see cref="Form"/> 에 넣는다. 디자인은 편집 대상 층 양식(그 객체 그대로), 미리보기는 합친 양식.</summary>
    private void Rebuild()
    {
        if (_settings is null) return;

        _fields.Clear();

        Form = IsDesign
            ? new SettingsFormState(CreateField(_working?.Root ?? SettingsForm.NewRoot(), design: true), IsDesign: true)
            : new SettingsFormState(CreateField(PreviewRoot(), design: false), IsDesign: false);

        SelectedFormItemFromCode(_selected);
    }

    private SettingsFieldViewModel CreateField(SettingsItem item, bool design)
    {
        var field = new SettingsFieldViewModel(item, design || !item.HasValue ? null : OnFieldEdited);

        foreach (var child in item.Children ?? [])
            field.Children.Add(CreateField(child, design));

        if (item.HasValue && _settings?.Find(item.Name) is { } entry)
        {
            field.Sync(entry, !design && IsOverridden(entry));
            if (!design) _fields[item.Name] = field;
        }

        return field;
    }

    /// <summary>층의 값으로 칸 모델을 맞춘다. 이름이 null 이면 전부.</summary>
    private void SyncFields(string? name)
    {
        if (_settings is null) return;

        foreach (var (key, field) in _fields)
        {
            if (name is not null && key != name) continue;
            if (_settings.Find(key) is { } entry) field.Sync(entry, IsOverridden(entry));
        }
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

    /// <summary>미리보기에서 사람이 값을 바꿨다(판이 칸 모델에 씀) - 편집 대상 층에 저장한다. 안 되면 칸을 층의 값으로 되돌린다.</summary>
    private void OnFieldEdited(SettingsFieldViewModel field, JsonNode? value) => Guard(() =>
    {
        if (_settings is null) return;

        var name = field.Item.Name;

        try
        {
            _settings.SetValue(name, value, TargetKind);
            SetStatus($"「{name}」 을(를) {(TargetKind == SettingsLayerKind.Solution ? "솔루션 공통" : "이 프로젝트")}에 저장했습니다.");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidCastException)
        {
            SetStatus(ex.Message, error: true);
            SyncFields(name);
        }
    });

    /// <summary>
    /// [초기값] - 편집 대상 층에서 덮어쓴 값을 모두 뺀다. 프로젝트 칸은 늘 프로젝트 층에 쓰므로 거기서 뺀다(<see cref="IsOverridden"/> 과 같은 규칙).
    /// </summary>
    /// <remarks>되돌릴 수 없어 한 번 묻는다. 도는 스크립트가 쓴 값도 빠진다.</remarks>
    private void DoResetValues() => Guard(() =>
    {
        if (_settings is null) return;

        var overridden = _settings.Entries().Where(IsOverridden).ToList();

        if (overridden.Count == 0)
        {
            SetStatus("덮어쓴 값이 없습니다 - 모두 처음 값(또는 솔루션 공통 값)입니다.");
            return;
        }

        var target = SelectedLayer?.Title ?? "이 층";
        var names = string.Join(", ", overridden.Take(8).Select(e => e.Item.DisplayLabel)) + (overridden.Count > 8 ? $" 외 {overridden.Count - 8}개" : string.Empty);

        if (MessageBoxService is { } box
            && box.ShowMessage($"「{target}」 에서 덮어쓴 값 {overridden.Count}개를 빼고 처음 값으로 돌립니다.\n\n{names}\n\n되돌릴 수 없습니다. 계속할까요?",
                               "초기값", MessageButton.YesNo, MessageIcon.Question) != MessageResult.Yes)
            return;

        ResetOverriddenValues();
    });

    /// <summary>묻지 않고 뺀다 - [초기값] 이 물은 뒤 부르고, 검사 하네스가 바로 부른다. 뺀 개수를 준다.</summary>
    public int ResetOverriddenValues()
    {
        if (_settings is null) return 0;

        var overridden = _settings.Entries().Where(IsOverridden).ToList();

        foreach (var entry in overridden)
            _settings.ResetValue(entry.Item.Name, entry.Layer == SettingsLayerKind.Project ? SettingsLayerKind.Project : TargetKind);

        SetStatus($"덮어쓴 값 {overridden.Count}개를 빼고 처음 값으로 돌렸습니다.");
        return overridden.Count;
    }

    /// <summary>층이 바뀌었다(스크립트가 값을 썼거나, 파일을 다시 읽었거나, 우리가 저장했거나). 아무 스레드에서나 온다.</summary>
    private void OnLayerChanged(object? sender, string? name)
    {
        if (_savingForm) return;

        void Apply() => Guard(() =>
        {
            if (Form is null) return;

            if (name is not null)
            {
                SyncFields(name);
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

        LoadWorking();
        Rebuild();
    });

    /// <summary>
    /// 미리보기에서 「나누기」로 칸 크기를 바꿨다 - 그 칸이 든 층(솔루션 공통·프로젝트)의 양식에 크기를 적고 저장한다.
    /// </summary>
    /// <remarks>
    /// 미리보기 칸은 층 양식의 객체 그대로라(<see cref="PreviewRoot"/>) 그 객체에 적고 그 양식을 저장한다 - 복사본으로 바꿔 끼우면 판이 든 칸이 층에서 떨어져 다음 끌기가 안 저장된다.
    /// 우리가 저장해 오는 바뀜 알림은 흘려보낸다(판을 다시 지으면 끌던 화면이 깜빡인다).
    /// </remarks>
    private void OnCanvasSizeChanged(SettingsItemSize size) => Guard(() =>
    {
        if (_settings is null || IsDesign) return;

        var layers = _settings.Solution is null ? new[] { _settings.Project } : new[] { _settings.Solution, _settings.Project };

        foreach (var layer in layers)
        {
            SettingsForm form;
            lock (SettingsLayer.Gate) form = layer.Form;

            if (!form.Root.Flatten().Any(i => ReferenceEquals(i, size.Item))) continue;

            if (size.Width is { } width) size.Item.Width = width;
            if (size.Height is { } height) size.Item.Height = height;

            _savingForm = true;

            try
            {
                layer.SaveForm(form);
            }
            finally
            {
                _savingForm = false;
            }

            var what = string.Join(" · ", new[] { size.Width is { } w ? $"너비 {w:0}px" : null, size.Height is { } h ? $"높이 {h:0}px" : null }.Where(t => t is not null));
            SetStatus($"「{size.Item.DisplayLabel}」 크기를 저장했습니다 - {what}.");
            return;
        }
    });

    private bool _selectingFromCode;

    /// <summary>판에서 칸을 골랐다(누름).</summary>
    private void OnSelectedFormItemChanged()
    {
        if (_selectingFromCode) return;

        Guard(() => Select(SelectedFormItem));
    }

    /// <summary>화면 모델이 고른 칸을 판에 알린다 - 되돌아와 다시 고르지 않게.</summary>
    private void SelectedFormItemFromCode(SettingsItem? item)
    {
        _selectingFromCode = true;

        try
        {
            SelectedFormItem = item;
        }
        finally
        {
            _selectingFromCode = false;
        }
    }

    /// <summary>
    /// 판이 끌어 옮긴 결과를 칸 트리(고치는 양식 그 객체)에 되읽었다 - 바뀌었으면 저장한다. 판은 손을 뗄 때마다·칸을 놓기 전에 부른다.
    /// </summary>
    private void OnCanvasLayoutChanged(bool changed) => Guard(() =>
    {
        if (IsDesign && changed && Form is { IsDesign: true } form && _working is not null && ReferenceEquals(form.Root.Item, _working.Root))
            SaveWorking("배치를 저장했습니다.");
    });

    private void Select(SettingsItem? item)
    {
        _selected = item;
        SelectedFormItemFromCode(item);

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
    private void OnCanvasDropped(SettingsDrop drop) => Guard(() =>
    {
        if (!IsDesign || Toolbox.FirstOrDefault(t => t.Kind == drop.Kind) is not { } tool) return;

        AddItem(tool, drop.Anchor, drop.Placement);
    });

    private void AddItem(SettingsToolboxItem tool, SettingsItem? anchor, SettingsDropPlacement placement)
    {
        if (_working is null || _settings is null) return;

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
            case SettingsItemKind.Splitter:
                // 값·이름이 없다 - 라벨은 상태 글("「나누기」 칸을 지웠습니다")에만 쓰인다.
                item.Label = "나누기";
                break;
        }

        if (item.HasValue) item.Name = UniqueName(tool.Title);

        Insert(item, anchor, placement);

        _selected = item;
        SaveWorking($"「{tool.Title}」 칸을 놓았습니다{(item.HasValue ? $" - 스크립트에서 설정(\"{item.Name}\") 로 읽습니다" : "")}.");
        Rebuild();
        Select(item);
    }

    /// <summary>고치는 양식에 칸을 넣는다 - 구역 안(맨 끝) · 기준 칸 앞·뒤 · 맨 끝. 기준 칸이 이 양식에 없으면(판을 그린 뒤 양식이 바뀌었다) 맨 끝.</summary>
    private void Insert(SettingsItem item, SettingsItem? anchor, SettingsDropPlacement placement)
    {
        if (_working is null) return;

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
    }

    private string UniqueName(string title) => UniqueName(title, null);

    /// <summary>제목 뒤에 빈 번호. 저장된 양식·다른 층·고치는 양식·<paramref name="taken"/>(같이 붙여 넣는 칸들)과 안 겹친다.</summary>
    private string UniqueName(string title, IReadOnlySet<string>? taken)
    {
        var working = _working?.Root.Flatten().Select(i => i.Name).ToHashSet(StringComparer.Ordinal) ?? [];

        for (var n = 1; ; n++)
        {
            var name = title + n;
            if (working.Contains(name) || taken?.Contains(name) == true) continue;
            if (_settings!.CheckNewName(name, TargetKind) is null) return name;
        }
    }

    private void DoDeleteItem() => Guard(() =>
    {
        if (_working is null || _selected is null) return;

        if (FindParent(_working.Root, _selected) is not { } parent) return;

        var label = _selected.DisplayLabel;

        parent.Children!.Remove(_selected);
        SaveWorking($"「{label}」 칸을 지웠습니다. 값은 파일에 남아 칸을 되살리면 돌아옵니다([안 쓰는 값 정리] 로 지웁니다).");
        Select(null);
        Rebuild();
    });

    /// <summary>
    /// 복사한 칸 - 앱 안 한 벌(화면이 여럿이어도 같이 본다). 시스템 클립보드는 안 쓴다 - 화면 모델이 WPF 클립보드를 모르게, 그리고 붙여 넣을 곳은 설정 탭뿐이다.
    /// </summary>
    private static SettingsItem? _clipboard;

    /// <summary>복사한 원본 칸 - 그 칸을 고른 채 붙이면 안이 아니라 뒤에 넣는다(구역을 복사해 붙이면 자기 안으로 들어갔다).</summary>
    private static WeakReference<SettingsItem>? _clipboardSource;

    /// <summary>Ctrl+C - 고른 칸을 복제해 담는다(담은 뒤 원본을 고쳐도 복사본은 그대로).</summary>
    private void DoCopyItem() => Guard(() =>
    {
        if (_selected is null) return;

        _clipboard = _selected.Clone();
        _clipboardSource = new WeakReference<SettingsItem>(_selected);
        SetStatus($"「{_selected.DisplayLabel}」 칸을 복사했습니다 - 붙여 넣을 곳을 고르고 Ctrl+V.");
        RaiseCommands();
    });

    /// <summary>
    /// Ctrl+V - 복사한 칸을 한 번 더 복제해 넣는다(여러 번 붙여 넣어도 서로 다른 객체). 값을 드는 칸은 이름을 새로 붙인다 - 이름 끝 숫자를 떼고 빈 번호를 찾는다(「글자1」 → 「글자2」).
    /// </summary>
    /// <remarks>넣을 자리는 도구 상자 두 번 누르기와 같다 - 단 복사한 칸 자신을 고른 채면 그 뒤(구역을 복사해 곧바로 붙이면 옆에 하나 더). 값은 복사하지 않는다 - 새 이름이라 처음 값에서 시작한다.</remarks>
    private void DoPasteItem() => Guard(() =>
    {
        if (_clipboard is null || _working is null || _settings is null) return;

        var item = _clipboard.Clone();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var valued in item.Flatten().Where(i => i.HasValue))
        {
            var stem = valued.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (stem.Length == 0) stem = SolutionSettings.KindName(valued.Kind);

            valued.Name = UniqueName(stem, taken);
            taken.Add(valued.Name);
        }

        var pastingOnSource = _clipboardSource?.TryGetTarget(out var source) == true && ReferenceEquals(source, _selected);

        if (_selected is { Kind: SettingsItemKind.Group } group && !ReferenceEquals(group, _working.Root) && !pastingOnSource)
            Insert(item, group, SettingsDropPlacement.Inside);
        else if (_selected is not null)
            Insert(item, _selected, SettingsDropPlacement.After);
        else
            Insert(item, null, SettingsDropPlacement.End);

        _selected = item;
        SaveWorking($"「{item.DisplayLabel}」 칸을 붙여 넣었습니다.");
        Rebuild();
        Select(item);
    });

    private void DoMove(int delta) => Guard(() =>
    {
        if (_working is null || _selected is null) return;

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
        else if (IsValuesOnly && !_settings.Entries().Any())
            SetStatus("이 프로젝트에는 설정 칸이 없습니다. 빌더의 솔루션 → 설정 탭에서 칸을 놓으면 여기서 값을 바꿀 수 있습니다.");
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
        CopyItemCommand.RaiseCanExecuteChanged();
        PasteItemCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        RemoveUnusedCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
        EditListRowsCommand.RaiseCanExecuteChanged();
        ResetValuesCommand.RaiseCanExecuteChanged();
    }
}
