using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Input.Scripting.Projects;

/// <summary>프로젝트 항목의 종류. 이름으로 저장한다 - 순서를 바꿔도 예전 파일의 뜻이 안 변하게. 늘리기만 한다.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScriptItemKind
{
    /// <summary>함께 컴파일되는 소스(.csx).</summary>
    Source,

    /// <summary>스크립트가 읽는 파일 - 그림·데이터·소리. 컴파일에는 안 들어간다.</summary>
    Resource,

    /// <summary>참조할 DLL.</summary>
    Reference
}

/// <summary>프로젝트 파일에 적힌 항목 하나.</summary>
public sealed class ScriptProjectItem
{
    /// <summary>프로젝트 폴더 기준 상대 경로. <c>/</c> 로 적는다.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public ScriptItemKind Kind { get; set; }
}

/// <summary>
/// 스크립트 프로젝트 - VS 의 csproj 처럼 파일 목록을 든 <c>.mtsproj</c> 하나.
/// </summary>
/// <remarks>
/// <b>목록에 있는 것만 프로젝트다.</b> 폴더에 파일을 넣기만 해서는 안 들어간다 - VS 와 같다. 아무 파일이나 폴더에 떨어뜨렸다고
/// 컴파일에 섞이면(백업으로 둔 옛 .csx 등) 같은 함수가 둘이라 영문 모를 오류가 난다.
///
/// 경로는 프로젝트 폴더 기준 상대로 적는다. 폴더째 다른 PC 로 옮겨도 그대로 열린다.
/// 설계는 <c>docs/스크립트-프로젝트-설계.md</c>.
/// </remarks>
public sealed class ScriptProject
{
    public const string Extension = ".mtsproj";

    public const string ResourceFolder = "Resources";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScriptLanguage Language { get; set; } = ScriptLanguage.CSharp;

    /// <summary>시작 파일(상대 경로). 최상위 실행문이 여기서 돈다.</summary>
    [JsonPropertyName("entry")]
    public string Entry { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ScriptProjectItem> Items { get; set; } = [];

    /// <summary>트리에 남길 폴더(빈 폴더 포함). 상대 경로.</summary>
    [JsonPropertyName("folders")]
    public List<string> Folders { get; set; } = [];

    /// <summary>프로젝트 파일의 전체 경로. 저장하지 않는다.</summary>
    [JsonIgnore]
    public string FilePath { get; private set; } = string.Empty;

    [JsonIgnore]
    public string Directory => System.IO.Path.GetDirectoryName(FilePath) ?? string.Empty;

    [JsonIgnore]
    public string EntryPath => string.IsNullOrEmpty(Entry) ? string.Empty : FullPath(Entry);

    // ── 읽기·쓰기 ────────────────────────────────────────────────────────

    public static ScriptProject Load(string filePath)
    {
        var full = System.IO.Path.GetFullPath(filePath);
        var project = JsonSerializer.Deserialize<ScriptProject>(File.ReadAllText(full, Encoding.UTF8), Json)
                      ?? throw new InvalidDataException($"프로젝트 파일을 읽지 못했습니다: {full}");

        project.FilePath = full;
        project.Normalize();

        return project;
    }

    /// <summary>
    /// 새 프로젝트를 만든다 - 폴더, 프로젝트 파일, 시작 파일, Resources 폴더.
    /// </summary>
    /// <remarks>이미 있는 프로젝트 파일은 덮어쓰지 않는다. 사람이 쌓아 둔 목록을 한 번에 날린다.</remarks>
    public static ScriptProject Create(string folder, string name, string entrySource)
    {
        System.IO.Directory.CreateDirectory(folder);

        var filePath = System.IO.Path.Combine(folder, name + Extension);

        if (File.Exists(filePath))
            throw new IOException($"같은 이름의 프로젝트가 이미 있습니다: {filePath}");

        var project = new ScriptProject { Name = name, Entry = "main" + ScriptFiles.Extension(ScriptLanguage.CSharp), FilePath = System.IO.Path.GetFullPath(filePath) };

        if (!File.Exists(project.EntryPath)) WriteText(project.EntryPath, entrySource);

        project.Items.Add(new ScriptProjectItem { Path = project.Entry, Kind = ScriptItemKind.Source });

        // 목록에만 넣으면 탐색기에 "디스크에 없음" 으로 흐리게 뜬다(실측) - 폴더도 만든다.
        System.IO.Directory.CreateDirectory(project.FullPath(ResourceFolder));
        project.AddFolder(ResourceFolder);
        project.Save();

        return project;
    }

    public void Save()
    {
        Normalize();
        WriteText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>UTF-8(BOM 없이). 한글 이름을 ANSI 로 쓰면 다른 PC 에서 깨진다.</summary>
    public static void WriteText(string path, string text)
    {
        var folder = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) System.IO.Directory.CreateDirectory(folder);

        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    // ── 경로 ─────────────────────────────────────────────────────────────

    public string FullPath(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));

    /// <summary>전체 경로를 프로젝트 기준 상대(<c>/</c>)로. 프로젝트 밖이면 null.</summary>
    public string? RelativePath(string fullPath)
    {
        var relative = System.IO.Path.GetRelativePath(Directory, System.IO.Path.GetFullPath(fullPath));

        return relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative) ? null : relative.Replace('\\', '/');
    }

    public ScriptProjectItem? Find(string relativeOrFull)
    {
        var relative = System.IO.Path.IsPathRooted(relativeOrFull) ? RelativePath(relativeOrFull) : Clean(relativeOrFull);

        return relative is null ? null : Items.FirstOrDefault(i => string.Equals(i.Path, relative, StringComparison.OrdinalIgnoreCase));
    }

    // ── 목록 고치기 ──────────────────────────────────────────────────────

    /// <summary>
    /// 항목을 넣는다. 확장자로 종류를 정한다(.csx = 소스, .dll = 참조, 나머지 = 리소스).
    /// </summary>
    /// <returns>넣은 항목. 이미 있으면 그것.</returns>
    public ScriptProjectItem Add(string relativeOrFull, ScriptItemKind? kind = null)
    {
        var relative = System.IO.Path.IsPathRooted(relativeOrFull)
            ? RelativePath(relativeOrFull) ?? throw new InvalidOperationException($"프로젝트 폴더 밖의 파일은 넣을 수 없습니다 - 먼저 폴더 안으로 복사하세요: {relativeOrFull}")
            : Clean(relativeOrFull);

        if (Find(relative) is { } existing) return existing;

        var item = new ScriptProjectItem { Path = relative, Kind = kind ?? KindOf(relative) };
        Items.Add(item);

        var folder = System.IO.Path.GetDirectoryName(relative.Replace('/', '\\'))?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder)) AddFolder(folder);

        return item;
    }

    public static ScriptItemKind KindOf(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csx" or ".cs" => ScriptItemKind.Source,
        ".dll" => ScriptItemKind.Reference,
        _ => ScriptItemKind.Resource
    };

    public void AddFolder(string relative)
    {
        var clean = Clean(relative).TrimEnd('/');
        if (clean.Length == 0) return;

        // 부모 폴더도 남긴다 - 트리가 끊기지 않게.
        var parts = clean.Split('/');
        for (var i = 1; i <= parts.Length; i++)
        {
            var path = string.Join('/', parts.Take(i));
            if (!Folders.Contains(path, StringComparer.OrdinalIgnoreCase)) Folders.Add(path);
        }
    }

    /// <summary>목록에서 뺀다. 파일은 지우지 않는다 - 지우는 것은 사람이 탐색기에서 한다(되돌릴 수 없다).</summary>
    public bool Remove(string relative)
    {
        var item = Find(relative);
        if (item is null) return false;

        Items.Remove(item);
        if (string.Equals(Entry, item.Path, StringComparison.OrdinalIgnoreCase)) Entry = string.Empty;

        return true;
    }

    /// <summary>폴더와 그 안의 항목을 목록에서 뺀다.</summary>
    public void RemoveFolder(string relative)
    {
        var prefix = Clean(relative).TrimEnd('/') + "/";

        Items.RemoveAll(i => i.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        Folders.RemoveAll(f => string.Equals(f + "/", prefix, StringComparison.OrdinalIgnoreCase) || f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (Entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) Entry = string.Empty;
    }

    /// <summary>
    /// 파일(또는 폴더)의 이름·자리를 바꾼다. 디스크의 것도 옮긴다.
    /// </summary>
    public void Move(string fromRelative, string toRelative)
    {
        var from = Clean(fromRelative).TrimEnd('/');
        var to = Clean(toRelative).TrimEnd('/');

        if (string.Equals(from, to, StringComparison.Ordinal)) return;

        var fromFull = FullPath(from);
        var toFull = FullPath(to);

        if (System.IO.Directory.Exists(fromFull))
        {
            System.IO.Directory.Move(fromFull, toFull);

            foreach (var item in Items.Where(i => i.Path.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)))
                item.Path = to + item.Path[from.Length..];

            for (var i = 0; i < Folders.Count; i++)
                if (string.Equals(Folders[i], from, StringComparison.OrdinalIgnoreCase) || Folders[i].StartsWith(from + "/", StringComparison.OrdinalIgnoreCase))
                    Folders[i] = to + Folders[i][from.Length..];

            if (Entry.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)) Entry = to + Entry[from.Length..];
        }
        else
        {
            if (File.Exists(fromFull))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(toFull)!);
                File.Move(fromFull, toFull);
            }

            if (Find(from) is { } item) item.Path = to;
            if (string.Equals(Entry, from, StringComparison.OrdinalIgnoreCase)) Entry = to;
        }

        var parent = System.IO.Path.GetDirectoryName(to.Replace('/', '\\'))?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) AddFolder(parent);
    }

    // ── 컴파일에 넘길 것 ─────────────────────────────────────────────────

    /// <summary>
    /// 컴파일 한 벌을 만든다. 열려 있는 글(전체 경로 → 글)이 있으면 디스크 대신 그것을 쓴다.
    /// </summary>
    public ScriptUnit ToUnit(IReadOnlyDictionary<string, string> openTexts)
    {
        var entry = EntryPath;
        var entryText = string.IsNullOrEmpty(entry)
            ? string.Empty
            : openTexts.TryGetValue(entry, out var open) ? open : File.Exists(entry) ? File.ReadAllText(entry) : string.Empty;

        var sources = Items
            .Where(i => i.Kind == ScriptItemKind.Source && !string.Equals(i.Path, Entry, StringComparison.OrdinalIgnoreCase))
            .Select(i => FullPath(i.Path))
            .ToList();

        var references = Items.Where(i => i.Kind == ScriptItemKind.Reference).Select(i => FullPath(i.Path)).ToList();

        return new ScriptUnit(entry, entryText, sources, references, openTexts, Directory);
    }

    /// <summary>
    /// 리소스 이름을 전체 경로로. 프로젝트 기준 경로(<c>Resources/적.png</c>)도, <c>Resources/</c> 아래 이름(<c>적.png</c>)도 받는다.
    /// </summary>
    public static string? ResolveResource(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var clean = Clean(name);

        foreach (var candidate in new[] { clean, ResourceFolder + "/" + clean })
        {
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, candidate.Replace('/', System.IO.Path.DirectorySeparatorChar)));

            // 프로젝트 밖(../)으로 나가는 이름은 안 받는다.
            if (!full.StartsWith(System.IO.Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(full)) return full;
        }

        return null;
    }

    private static string Clean(string relative) => relative.Replace('\\', '/').TrimStart('/').Trim();

    private void Normalize()
    {
        foreach (var item in Items) item.Path = Clean(item.Path);

        Items = [.. Items.Where(i => i.Path.Length > 0).DistinctBy(i => i.Path, StringComparer.OrdinalIgnoreCase)];
        Folders = [.. Folders.Select(f => Clean(f).TrimEnd('/')).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        Entry = Clean(Entry);
    }
}
