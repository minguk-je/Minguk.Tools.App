using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

using Minguk.Tools.Projects.Settings;

namespace Minguk.Tools.ViewModels.Settings;

/// <summary>
/// 설정 탭 오른쪽 속성 창(<c>PropertyGridControl</c>)이 보는 칸 한 개. 칸 종류마다 보이는 속성이 달라 종류별로 나눈다.
/// </summary>
/// <remarks>
/// 영어 이름(열거형 Vertical 등)이 화면에 뜨지 않게 방향·모양은 참거짓 칸으로 둔다(화면 글은 모두 한국어).
/// 고치면 <see cref="Changed"/> - 화면 모델이 판을 다시 그리고 양식을 저장한다. 이름은 <see cref="CheckName"/> 이 막는다.
/// </remarks>
public abstract class SettingsItemEditor : INotifyPropertyChanged
{
    protected SettingsItemEditor(SettingsItem item) => Item = item;

    [Browsable(false)]
    public SettingsItem Item { get; }

    /// <summary>이름을 바꿔도 되는가. 안 되면 한국어 이유.</summary>
    [Browsable(false)]
    public Func<string, SettingsItem, string?>? CheckName { get; set; }

    /// <summary>고친 값이 칸에 들어갔다. 인자는 거절한 이유(없으면 null).</summary>
    [Browsable(false)]
    public Action<string?>? Changed { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    [Category("이름")]
    [DisplayName("종류")]
    [Display(Order = 4)]
    [Description("칸의 종류. 바꾸려면 지우고 새로 놓습니다.")]
    public string KindText => SolutionSettings.KindName(Item.Kind);

    [Category("이름")]
    [DisplayName("라벨")]
    [Display(Order = 1)]
    [Description("화면에 보이는 이름. 비우면 이름을 보입니다.")]
    public string Label
    {
        get => Item.Label ?? string.Empty;
        set => Apply(() => Item.Label = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    [Category("이름")]
    [DisplayName("툴팁")]
    [Display(Order = 3)]
    [Description("마우스를 올리면 보이는 설명.")]
    public string Tooltip
    {
        get => Item.Tooltip ?? string.Empty;
        set => Apply(() => Item.Tooltip = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    protected void Apply(Action change, string? refused = null)
    {
        if (refused is null) change();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        Changed?.Invoke(refused);
    }

    public static SettingsItemEditor Create(SettingsItem item) => item.Kind switch
    {
        SettingsItemKind.Group => new GroupEditor(item),
        SettingsItemKind.Number => new NumberEditor(item),
        SettingsItemKind.Slider => new NumberEditor(item),
        SettingsItemKind.Check => new CheckEditor(item),
        SettingsItemKind.Combo => new ComboEditor(item),
        SettingsItemKind.List => new ListEditor(item),
        SettingsItemKind.Splitter => new SplitterEditor(item),
        _ => new TextEditor(item)
    };
}

/// <summary>나누기 막대 - 고칠 것이 없다(방향은 든 구역을 따른다). 툴팁만.</summary>
public sealed class SplitterEditor(SettingsItem item) : SettingsItemEditor(item);

/// <summary>구역.</summary>
public sealed class GroupEditor(SettingsItem item) : SettingsItemEditor(item)
{
    [Category("칸 배치")]
    [DisplayName("가로로 놓기")]
    [Display(Order = 10)]
    [Description("안의 칸을 옆으로 나란히 놓습니다. 끄면 위아래로.")]
    public bool IsHorizontal
    {
        get => Item.Orientation == SettingsOrientation.Horizontal;
        set => Apply(() => Item.Orientation = value ? SettingsOrientation.Horizontal : SettingsOrientation.Vertical);
    }

    [Category("칸 배치")]
    [DisplayName("탭으로 보이기")]
    [Display(Order = 11)]
    [Description("안의 구역들을 탭으로 보입니다. 안에 구역을 넣어야 탭이 됩니다.")]
    public bool IsTabs
    {
        get => Item.View == SettingsGroupView.Tabs;
        set => Apply(() => Item.View = value ? SettingsGroupView.Tabs : null);
    }
}

/// <summary>값을 드는 칸 - 이름·기본값.</summary>
public abstract class ValueItemEditor(SettingsItem item) : SettingsItemEditor(item)
{
    [Category("이름")]
    [DisplayName("이름")]
    [Display(Order = 2)]
    [Description("스크립트가 부르는 이름 - 설정<int>(\"이름\"). 띄어쓰기·'.' 은 못 씁니다. 솔루션 공통과 프로젝트에서 겹칠 수 없습니다.")]
    public string Name
    {
        get => Item.Name;
        set
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed == Item.Name) return;

            Apply(() => Item.Name = trimmed, CheckName?.Invoke(trimmed, Item));
        }
    }
}

public sealed class TextEditor(SettingsItem item) : ValueItemEditor(item)
{
    [Category("칸 값")]
    [DisplayName("처음 값")]
    [Display(Order = 10)]
    [Description("값을 한 번도 안 바꿨을 때 쓰는 값. 지금 값은 [디자인] 을 끄고 판에서 바꿉니다.")]
    public string Default
    {
        get => Item.Default is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : string.Empty;
        set => Apply(() => Item.Default = string.IsNullOrEmpty(value) ? null : JsonValue.Create(value));
    }
}

public sealed class CheckEditor(SettingsItem item) : ValueItemEditor(item)
{
    [Category("칸 값")]
    [DisplayName("처음 값")]
    [Display(Order = 10)]
    [Description("값을 한 번도 안 바꿨을 때 켜져 있을지. 지금 값은 [디자인] 을 끄고 판에서 바꿉니다.")]
    public bool Default
    {
        get => Item.Default is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.True;
        set => Apply(() => Item.Default = JsonValue.Create(value));
    }
}

/// <summary>숫자·슬라이더.</summary>
public sealed class NumberEditor(SettingsItem item) : ValueItemEditor(item)
{
    [Category("칸 값")]
    [DisplayName("처음 값")]
    [Display(Order = 10)]
    [Description("값을 한 번도 안 바꿨을 때 쓰는 값. 지금 값은 [디자인] 을 끄고 판에서 바꿉니다.")]
    public double Default
    {
        get => Item.Default is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number ? SettingsValue.ToDouble(v) : 0;
        set => Apply(() => Item.Default = SettingsValue.FromObject(value));
    }

    [Category("칸 값")]
    [DisplayName("최소")]
    [Display(Order = 11)]
    [Description("이보다 작게는 못 넣습니다. 비우면 제한 없음(슬라이더는 0).")]
    public double? Min
    {
        get => Item.Min;
        set => Apply(() => Item.Min = value, value is { } min && Item.Max is { } max && min > max ? "최소가 최대보다 클 수 없습니다." : null);
    }

    [Category("칸 값")]
    [DisplayName("최대")]
    [Display(Order = 12)]
    [Description("이보다 크게는 못 넣습니다. 비우면 제한 없음(슬라이더는 100).")]
    public double? Max
    {
        get => Item.Max;
        set => Apply(() => Item.Max = value, value is { } max && Item.Min is { } min && min > max ? "최대가 최소보다 작을 수 없습니다." : null);
    }

    [Category("칸 값")]
    [DisplayName("간격")]
    [Display(Order = 13)]
    [Description("화살표·슬라이더 한 칸에 바뀌는 양.")]
    public double? Step
    {
        get => Item.Step;
        set => Apply(() => Item.Step = value, value is <= 0 ? "간격은 0 보다 커야 합니다." : null);
    }

    [Category("칸 값")]
    [DisplayName("소수 자리")]
    [Display(Order = 14)]
    [Description("0 이면 정수만. 스크립트에서 설정<int> 로 읽으려면 0.")]
    public int Decimals
    {
        get => Item.Decimals ?? 0;
        set => Apply(() => Item.Decimals = value == 0 ? null : value, value is < 0 or > 6 ? "소수 자리는 0~6 입니다." : null);
    }
}

public sealed class ComboEditor(SettingsItem item) : ValueItemEditor(item)
{
    [Category("칸 값")]
    [DisplayName("항목")]
    [Display(Order = 10)]
    [Description("고를 수 있는 글. 쉼표로 나눕니다 - 보통, 악몽, 지옥.")]
    public string Items
    {
        get => string.Join(", ", Item.Items ?? []);
        set
        {
            var items = SplitList(value);
            Apply(() => Item.Items = items, items.Count == 0 ? "항목이 하나는 있어야 합니다." : items.Distinct().Count() != items.Count ? "같은 항목이 두 번 있습니다." : null);
        }
    }

    [Category("칸 값")]
    [DisplayName("처음 값")]
    [Display(Order = 11)]
    [Description("값을 한 번도 안 바꿨을 때 고를 항목. 비우면 첫 항목. 지금 값은 [디자인] 을 끄고 판에서 바꿉니다.")]
    public string Default
    {
        get => Item.Default is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.GetValue<string>() : string.Empty;
        set
        {
            var text = value?.Trim() ?? string.Empty;
            Apply(() => Item.Default = text.Length == 0 ? null : JsonValue.Create(text),
                text.Length > 0 && Item.Items?.Contains(text) != true ? $"「{text}」 은(는) 항목에 없습니다." : null);
        }
    }

    internal static List<string> SplitList(string? text)
        => [.. (text ?? string.Empty).Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

/// <summary>목록 - 열 적기.</summary>
public sealed class ListEditor(SettingsItem item) : ValueItemEditor(item)
{
    [Category("칸 값")]
    [DisplayName("열")]
    [Display(Order = 10)]
    [Description("열 이름:종류 를 쉼표로 나눕니다. 종류는 글자·숫자·체크·콤보(항목|항목). 예: 키:글자, HP:숫자, 켜기:체크, 모드:콤보(보통|지옥)")]
    public string Columns
    {
        get => string.Join(", ", (Item.Columns ?? []).Select(Format));
        set
        {
            var (columns, error) = Parse(value);
            Apply(() => Item.Columns = columns, error);
        }
    }

    [Category("칸 값")]
    [DisplayName("처음 행")]
    [Display(Order = 11)]
    [Description("값을 한 번도 안 바꿨을 때 들어 있는 행. … 을 눌러 표에서 넣습니다. 지금 값은 [디자인] 을 끄고 판에서 바꿉니다.")]
    public string DefaultRows
    {
        get => DefaultRowsValue.Count == 0 ? "없음" : $"{DefaultRowsValue.Count}행";
        // 속성 창이 읽기 전용 줄의 … 단추를 끄므로 setter 를 둔다. 글로는 못 고치고 대화 상자로만 고친다(SetDefaultRows).
        set { }
    }

    /// <summary>처음 행 사본 - 대화 상자가 고친다.</summary>
    [Browsable(false)]
    public JsonArray DefaultRowsValue => Item.Default is JsonArray rows ? (JsonArray)rows.DeepClone() : [];

    /// <summary>처음 행을 갈아 넣는다. 빈 배열이면 뺀다(빈 목록이 처음 값).</summary>
    public void SetDefaultRows(JsonArray rows)
        => Apply(() => Item.Default = rows.Count == 0 ? null : rows.DeepClone(), rows.All(r => r is JsonObject) ? null : "처음 행이 표 모양이 아닙니다.");

    private static string Format(SettingsColumn column) => column.Kind switch
    {
        SettingsItemKind.Combo => $"{column.Name}:콤보({string.Join("|", column.Items ?? [])})",
        _ => $"{column.Name}:{SolutionSettings.KindName(column.Kind)}"
    };

    internal static (List<SettingsColumn> Columns, string? Error) Parse(string? text)
    {
        var columns = new List<SettingsColumn>();

        // 콤보 괄호 안의 쉼표를 안 자르게 괄호 밖 쉼표로만 나눈다.
        foreach (var part in SplitOutsideParens(text ?? string.Empty))
        {
            var colon = part.IndexOf(':');
            var name = (colon < 0 ? part : part[..colon]).Trim();
            var kindText = colon < 0 ? "글자" : part[(colon + 1)..].Trim();

            if (SettingsForm.CheckNameShape(name) is { } shape) return (columns, $"열 「{name}」: {shape}");

            var column = new SettingsColumn { Name = name };

            if (kindText.StartsWith("콤보", StringComparison.Ordinal))
            {
                column.Kind = SettingsItemKind.Combo;
                var open = kindText.IndexOf('(');
                var close = kindText.LastIndexOf(')');
                column.Items = open >= 0 && close > open
                    ? [.. kindText[(open + 1)..close].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                    : [];
            }
            else
            {
                column.Kind = kindText switch
                {
                    "글자" => SettingsItemKind.Text,
                    "숫자" => SettingsItemKind.Number,
                    "체크" => SettingsItemKind.Check,
                    _ => (SettingsItemKind)(-1)
                };

                if ((int)column.Kind < 0) return (columns, $"열 「{name}」 의 종류 「{kindText}」 를 모릅니다 - 글자·숫자·체크·콤보(항목|항목) 중에서 적으세요.");
            }

            columns.Add(column);
        }

        if (columns.Count == 0) return (columns, "열이 하나는 있어야 합니다.");
        if (columns.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != columns.Count) return (columns, "같은 이름의 열이 두 번 있습니다.");

        return (columns, null);
    }

    private static IEnumerable<string> SplitOutsideParens(string text)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(': depth++; break;
                case ')': depth = Math.Max(0, depth - 1); break;
                case ',' or '\n' when depth == 0:
                    if (text[start..i].Trim() is { Length: > 0 } piece) yield return piece;
                    start = i + 1;
                    break;
            }
        }

        if (text[start..].Trim() is { Length: > 0 } last) yield return last;
    }
}

/// <summary>도구 상자 한 줄.</summary>
/// <param name="Glyph">DevExpress 컨트롤 아이콘(<see cref="DevExpressGlyph"/>). 못 읽으면 null - 글자만 보인다.</param>
public sealed record SettingsToolboxItem(SettingsItemKind Kind, string Title, string Description, System.Windows.Media.ImageSource? Glyph = null);

/// <summary>
/// DevExpress.Images 의 16x16 PNG 를 읽는다(사용자, 2026-09-17 - 도구 상자에 DevExpress 컨트롤 아이콘).
/// </summary>
/// <remarks>
/// 경로는 DLL 안에 있는 것을 확인하고 적었다. 없는 경로면 터지지 않고 null 을 준다 - 내장 SVG(<c>dx:DXImage</c>)는 없는 경로면
/// 런타임에 터져서(CLAUDE.md) PNG 를 pack URI 로 직접 읽고 잡는다. 어셈블리 이름은 버전을 박지 않게 DevExpress 상수(<c>AssemblyInfo.SRAssemblyImages</c>)로.
/// 검사 <c>--settings-screen</c> 이 도구 상자 아이콘이 모두 읽혔는지 본다.
/// </remarks>
public static class DevExpressGlyph
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static System.Windows.Media.ImageSource? Load(string path)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/{AssemblyInfo.SRAssemblyImages};component/{path}", UriKind.Absolute);
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"DevExpress 아이콘을 읽지 못했습니다: {path}");
            return null;
        }
    }
}
