using System;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

using DevExpress.Mvvm;

using Minguk.Tools.Projects.Settings;

namespace Minguk.Tools.ViewModels.Settings;

/// <summary>
/// 설정 판의 칸 하나(구역이면 <see cref="Children"/> 를 든다) - 판(<c>SettingsFormCanvas</c>)이 이것을 보고 그리고, 값을 바꾸면 <see cref="Value"/> 에 쓴다.
/// </summary>
/// <remarks>
/// 화면 모델(<c>SolutionSettingsViewModel</c>)은 판 컨트롤을 모른다(사용자, 2026-09-17 "MVVM 으로, Collection 으로") - 칸 모델 트리를 만들어 내놓고,
/// 층이 바뀌면(스크립트가 썼거나 파일을 다시 읽음) <see cref="Sync"/> 로 값·굵게·경고를 고친다. 판은 <c>PropertyChanged</c> 로 편집기를 따라 고친다.
/// 사람이 고친 값만 화면 모델에 알린다 - <see cref="Sync"/> 가 넣은 값은 되돌려 알리지 않는다(안 그러면 읽은 값을 다시 저장한다).
/// </remarks>
public sealed class SettingsFieldViewModel : BindableBase
{
    private readonly Action<SettingsFieldViewModel, JsonNode?>? _edited;
    private bool _syncing;

    /// <param name="item">양식의 칸(디자인이면 고치는 양식 그 객체 - 판이 끌어 옮긴 결과를 여기에 되읽는다).</param>
    /// <param name="edited">사람이 값을 바꿨을 때. 디자인·구역이면 null.</param>
    public SettingsFieldViewModel(SettingsItem item, Action<SettingsFieldViewModel, JsonNode?>? edited)
    {
        Item = item;
        _edited = edited;
    }

    public SettingsItem Item { get; }

    /// <summary>구역 안의 칸들(양식 순서).</summary>
    public ObservableCollection<SettingsFieldViewModel> Children { get; } = [];

    /// <summary>합친 값(프로젝트 → 솔루션 → 처음 값). 판이 쓰면 화면 모델이 편집 대상 층에 저장한다.</summary>
    public JsonNode? Value
    {
        get => GetValue<JsonNode?>();
        set => SetValue(value, () =>
        {
            if (!_syncing) _edited?.Invoke(this, value);
        });
    }

    /// <summary>편집 대상 층에서 덮어쓴 값인가 - 판이 라벨을 굵게 한다.</summary>
    public bool IsOverridden { get => GetValue<bool>(); private set => SetValue(value); }

    /// <summary>겹침·형식 틀림 경고. 있으면 라벨 앞 ⚠, 툴팁에 사연.</summary>
    public string? Warning
    {
        get => GetValue<string?>();
        private set => SetValue(value, () => RaisePropertiesChanged(nameof(Label), nameof(ToolTip)));
    }

    public string Label => Warning is null ? Item.DisplayLabel : "⚠ " + Item.DisplayLabel;

    public string? ToolTip
    {
        get
        {
            var tooltip = string.IsNullOrWhiteSpace(Item.Tooltip) ? null : Item.Tooltip;

            if (string.IsNullOrWhiteSpace(Warning)) return tooltip;
            return tooltip is null ? Warning : tooltip + "\n\n" + Warning;
        }
    }

    /// <summary>층의 값으로 맞춘다 - 사람이 고친 것이 아니라 화면 모델에 되돌려 알리지 않는다.</summary>
    public void Sync(SettingsEntry entry, bool overridden)
    {
        _syncing = true;

        try
        {
            Value = entry.Value;
        }
        finally
        {
            _syncing = false;
        }

        IsOverridden = overridden;
        Warning = entry.Warning;
    }
}

/// <summary>판에 줄 것 - 뿌리 칸과 디자인인가. 바뀌면(새 객체) 판을 다시 짓는다.</summary>
public sealed record SettingsFormState(SettingsFieldViewModel Root, bool IsDesign);
