using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Projects.Settings;

/// <summary>파일 이름과 JSON 옵션을 한 곳에.</summary>
public static class SolutionSettingsFiles
{
    public const string FormFile = "settings.form.json";

    public const string ValuesFile = "settings.values.json";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>UTF-8(BOM 없이)로 쓴다. 쓰다 죽어도 앞 파일이 안 깨지게 옆에 쓰고 바꿔 끼운다.</summary>
    internal static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = path + ".tmp";

        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// 설정 양식 - <c>settings.form.json</c>. 칸과 배치가 한 트리(<see cref="Root"/>)에 있다.
/// </summary>
public sealed class SettingsForm
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("root")]
    public SettingsItem Root { get; set; } = NewRoot();

    /// <summary>값을 드는 칸 전부(배치 순서).</summary>
    public IEnumerable<SettingsItem> ValueItems() => Root.Flatten().Where(i => i.HasValue);

    public SettingsItem? Find(string name)
        => ValueItems().FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.Ordinal));

    public bool IsEmpty => Root.Children is not { Count: > 0 };

    public static SettingsItem NewRoot() => new() { Kind = SettingsItemKind.Group, Orientation = SettingsOrientation.Vertical, Children = [] };

    public static SettingsForm Load(string path)
    {
        if (!File.Exists(path)) return new SettingsForm();

        var form = JsonSerializer.Deserialize<SettingsForm>(File.ReadAllText(path, Encoding.UTF8), SolutionSettingsFiles.Json)
                   ?? new SettingsForm();

        form.Root ??= NewRoot();
        form.Root.Children ??= [];

        return form;
    }

    public void Save(string path) => SolutionSettingsFiles.WriteAtomic(path, JsonSerializer.Serialize(this, SolutionSettingsFiles.Json));

    /// <summary>깊은 복사 - 디자이너가 고치다 버릴 수 있게.</summary>
    public SettingsForm Clone()
        => JsonSerializer.Deserialize<SettingsForm>(JsonSerializer.Serialize(this, SolutionSettingsFiles.Json), SolutionSettingsFiles.Json)!;

    /// <summary>
    /// 이름 규칙 - 비지 않고, 공백·<c>.</c> 이 없다. 틀리면 한국어 이유, 맞으면 null.
    /// </summary>
    public static string? CheckNameShape(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "이름이 비었습니다 - 스크립트가 이 이름으로 부릅니다.";
        if (name.Any(char.IsWhiteSpace)) return "이름에 띄어쓰기를 넣을 수 없습니다 - 화면에 보일 글은 라벨에 적으세요.";
        if (name.Contains('.')) return "이름에 '.' 을 넣을 수 없습니다.";

        return null;
    }

    /// <summary>양식 안에서 이름이 겹친 것들.</summary>
    public IReadOnlyList<string> DuplicateNames()
        => [.. ValueItems().GroupBy(i => i.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key)];

    /// <summary>칸 종류에 맞는 빈 기본값 - 기본값을 안 적은 칸이 읽힐 때.</summary>
    public static JsonNode? EmptyValue(SettingsItem item) => item.Kind switch
    {
        SettingsItemKind.Text => JsonValue.Create(string.Empty),
        SettingsItemKind.Number => JsonValue.Create(Math.Min(Math.Max(0, item.Min ?? double.MinValue), item.Max ?? double.MaxValue)),
        SettingsItemKind.Slider => JsonValue.Create(item.Min ?? 0),
        SettingsItemKind.Check => JsonValue.Create(false),
        SettingsItemKind.Combo => JsonValue.Create(item.Items is { Count: > 0 } items ? items[0] : string.Empty),
        SettingsItemKind.List => new JsonArray(),
        _ => null
    };
}
