using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Projects.Settings;

/// <summary>설정 칸의 종류. 이름으로 저장한다 - 늘리기만 한다.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SettingsItemKind
{
    /// <summary>칸을 묶는 구역. 값이 없다.</summary>
    Group,

    Text,

    Number,

    Check,

    Combo,

    Slider,

    /// <summary>여러 줄 값(GridControl). 값은 행 배열.</summary>
    List,

    /// <summary>
    /// 구역 안 나누기 - 앞 칸에 크기 조절 막대(LayoutControl 의 AllowHorizontalSizing·AllowVerticalSizing)를 켠다. 가로 구역이면 좌우, 세로 구역이면 상하. 값이 없다.
    /// </summary>
    Splitter
}

/// <summary>구역 안에서 칸을 놓는 방향.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SettingsOrientation
{
    Vertical,

    Horizontal
}

/// <summary>구역을 그리는 모양.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SettingsGroupView
{
    GroupBox,

    Tabs
}

/// <summary>목록 칸의 열 하나.</summary>
public sealed class SettingsColumn
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    /// <summary><see cref="SettingsItemKind.Text"/>·<c>Number</c>·<c>Check</c>·<c>Combo</c> 만.</summary>
    [JsonPropertyName("kind")]
    public SettingsItemKind Kind { get; set; } = SettingsItemKind.Text;

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Items { get; set; }
}

/// <summary>
/// 설정 양식의 칸 하나 - 구역이면 아래 칸들을 든다. 양식 전체가 이 트리다(<c>docs/솔루션-설정.md</c>).
/// </summary>
/// <remarks>
/// 배치를 DevExpress LayoutControl XML 로 두지 않고 이 트리로 적는다 - 사람이 읽고 고칠 수 있고, 칸과 배치가 한 곳에 있다.
/// </remarks>
public sealed class SettingsItem
{
    [JsonPropertyName("kind")]
    public SettingsItemKind Kind { get; set; }

    /// <summary>스크립트가 부르는 키. 구역은 비어도 된다.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    [JsonPropertyName("tooltip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tooltip { get; set; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Default { get; set; }

    [JsonPropertyName("min")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Max { get; set; }

    [JsonPropertyName("step")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Step { get; set; }

    [JsonPropertyName("decimals")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Decimals { get; set; }

    /// <summary>콤보 항목.</summary>
    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Items { get; set; }

    /// <summary>목록 열.</summary>
    [JsonPropertyName("columns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SettingsColumn>? Columns { get; set; }

    [JsonPropertyName("orientation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SettingsOrientation? Orientation { get; set; }

    [JsonPropertyName("view")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SettingsGroupView? View { get; set; }

    [JsonPropertyName("children")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SettingsItem>? Children { get; set; }

    /// <summary>칸 너비(px) - 가로 구역에서 뒤의 「나누기」로 끌어 정한 크기. 없으면 내용에 맞춘다.</summary>
    [JsonPropertyName("width")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Width { get; set; }

    /// <summary>칸 높이(px) - 세로 구역에서 뒤의 「나누기」로 끌어 정한 크기. 없으면 내용에 맞춘다.</summary>
    [JsonPropertyName("height")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Height { get; set; }

    /// <summary>값을 드는 칸인가. 구역만 아니다.</summary>
    [JsonIgnore]
    public bool HasValue => Kind is not (SettingsItemKind.Group or SettingsItemKind.Splitter);

    /// <summary>화면에 보일 이름 - 라벨이 없으면 키.</summary>
    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Name : Label;

    /// <summary>자기와 아래 칸 전부(앞에서부터).</summary>
    public IEnumerable<SettingsItem> Flatten()
    {
        yield return this;

        if (Children is null) yield break;

        foreach (var child in Children)
        foreach (var item in child.Flatten())
            yield return item;
    }
}
