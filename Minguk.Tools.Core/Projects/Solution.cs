using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Projects;

/// <summary>프로젝트의 종류. 이름으로 저장한다 - 순서를 바꿔도 예전 파일의 뜻이 안 변하게. 늘리기만 한다.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SolutionProjectKind
{
    /// <summary>보통 프로젝트. 제 사진·라벨·모델·스크립트를 든 런 하나.</summary>
    Normal,

    /// <summary>공유 프로젝트. 소스만 있고 다른 프로젝트가 물어서 같이 컴파일한다(VS 의 .shproj).</summary>
    Shared
}

/// <summary>솔루션 파일에 적힌 프로젝트 하나.</summary>
public sealed class SolutionProjectEntry
{
    /// <summary>솔루션 폴더 기준 상대 경로. <c>/</c> 로 적는다.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public SolutionProjectKind Kind { get; set; }
}

/// <summary>
/// 솔루션 - 게임 하나. VS 의 <c>.sln</c> 처럼 프로젝트 목록을 든 파일 하나다.
/// </summary>
/// <remarks>
/// <b>목록에 있는 것만 프로젝트다</b>(VS 와 같다). 폴더에 프로젝트를 만들어 놓기만 해서는 안 들어간다 -
/// 백업으로 둔 옛 폴더가 저절로 끼어들면 "왜 이 런이 목록에 있지" 가 된다.
///
/// 경로는 <b>솔루션 폴더 기준 상대</b>로 적는다. 폴더째 다른 PC 로 옮겨도 그대로 열린다.
/// 설계는 <c>docs/프로젝트-설계.md</c>.
/// </remarks>
public sealed class Solution
{
    public const string Extension = ".mtsln";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>시작 프로젝트(상대 경로). 실행하면 이것이 돈다. 비어 있으면 첫 보통 프로젝트.</summary>
    [JsonPropertyName("startupProject")]
    public string StartupProject { get; set; } = string.Empty;

    [JsonPropertyName("projects")]
    public List<SolutionProjectEntry> Projects { get; set; } = [];

    /// <summary>솔루션 파일의 전체 경로. 저장하지 않는다.</summary>
    [JsonIgnore]
    public string FilePath { get; private set; } = string.Empty;

    [JsonIgnore]
    public string Directory => System.IO.Path.GetDirectoryName(FilePath) ?? string.Empty;

    // ── 읽기·쓰기 ────────────────────────────────────────────────────────

    public static Solution Load(string filePath)
    {
        var full = System.IO.Path.GetFullPath(filePath);

        var solution = JsonSerializer.Deserialize<Solution>(File.ReadAllText(full, Encoding.UTF8), Json)
                       ?? throw new InvalidDataException($"솔루션 파일을 읽지 못했습니다: {full}");

        solution.FilePath = full;
        solution.Normalize();

        return solution;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(FilePath)) throw new InvalidOperationException("저장할 자리가 없습니다.");

        Normalize();

        System.IO.Directory.CreateDirectory(Directory);

        // UTF-8(BOM 없이). 한글 이름을 ANSI 로 쓰면 다른 PC 에서 깨진다.
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json), new UTF8Encoding(false));
    }

    /// <summary>
    /// 새 솔루션을 만든다 - <c>&lt;부모&gt;\&lt;이름&gt;\&lt;이름&gt;.mtsln</c>. VS 처럼 솔루션마다 폴더다.
    /// </summary>
    /// <remarks>이미 있는 솔루션 파일은 덮어쓰지 않는다 - 사람이 쌓아 둔 목록을 한 번에 날린다.</remarks>
    public static Solution Create(string parentDirectory, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("솔루션 이름이 비었습니다.", nameof(name));

        var parent = System.IO.Path.GetFullPath(parentDirectory);

        // 사람이 이미 그 이름의 폴더 안을 골랐으면 한 겹 더 만들지 않는다.
        var folder = string.Equals(System.IO.Path.GetFileName(parent), name, StringComparison.OrdinalIgnoreCase)
            ? parent
            : System.IO.Path.Combine(parent, name);

        var filePath = System.IO.Path.Combine(folder, name + Extension);

        if (File.Exists(filePath))
            throw new IOException($"같은 이름의 솔루션이 이미 있습니다: {filePath}");

        var solution = new Solution { Name = name, FilePath = System.IO.Path.GetFullPath(filePath) };

        solution.Save();

        return solution;
    }

    // ── 자리 ─────────────────────────────────────────────────────────────

    /// <summary>상대 경로를 전체 경로로.</summary>
    public string FullPath(string relative)
        => System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));

    /// <summary>전체 경로를 상대로. 솔루션 폴더 밖이면 null.</summary>
    public string? RelativePath(string fullPath)
    {
        var relative = System.IO.Path.GetRelativePath(Directory, System.IO.Path.GetFullPath(fullPath));

        return relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative)
            ? null
            : relative.Replace('\\', '/');
    }

    /// <summary>그 프로젝트가 든 폴더(전체 경로). 사진·라벨·모델이 여기 있다.</summary>
    public string DirectoryOf(SolutionProjectEntry entry)
        => System.IO.Path.GetDirectoryName(FullPath(entry.Path)) ?? Directory;

    /// <summary>사람이 읽는 프로젝트 이름 - 파일 이름에서 확장자를 뗀 것.</summary>
    public static string NameOf(SolutionProjectEntry entry)
        => System.IO.Path.GetFileNameWithoutExtension(entry.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>돌릴 수 있는 프로젝트들. 공유 프로젝트는 뺀다 - 소스만 있어 혼자 못 돈다.</summary>
    public IEnumerable<SolutionProjectEntry> Runnable()
        => Projects.Where(entry => entry.Kind == SolutionProjectKind.Normal);

    /// <summary>
    /// 시작 프로젝트. 적어 둔 것이 없거나 목록에서 사라졌으면 첫 보통 프로젝트.
    /// </summary>
    /// <remarks>
    /// null 을 돌려줄 수 있다 - 프로젝트가 하나도 없는 솔루션이다. 부르는 쪽이 "프로젝트를 먼저 만드세요" 라 말해야 한다.
    /// </remarks>
    public SolutionProjectEntry? Startup()
    {
        var found = Projects.FirstOrDefault(entry =>
            entry.Kind == SolutionProjectKind.Normal &&
            string.Equals(entry.Path, StartupProject, StringComparison.OrdinalIgnoreCase));

        return found ?? Runnable().FirstOrDefault();
    }

    // ── 목록 고치기 ──────────────────────────────────────────────────────

    /// <summary>프로젝트를 목록에 넣는다. 이미 있으면 그것을 돌려준다.</summary>
    /// <param name="projectFilePath">.mtsproj 의 전체 경로. 솔루션 폴더 안이어야 한다.</param>
    public SolutionProjectEntry Add(string projectFilePath, SolutionProjectKind kind = SolutionProjectKind.Normal)
    {
        var relative = RelativePath(projectFilePath)
                       ?? throw new InvalidOperationException($"솔루션 폴더 밖의 프로젝트는 넣을 수 없습니다 - 먼저 폴더 안으로 옮기세요: {projectFilePath}");

        var found = Projects.FirstOrDefault(entry => string.Equals(entry.Path, relative, StringComparison.OrdinalIgnoreCase));

        if (found is not null) return found;

        var entry2 = new SolutionProjectEntry { Path = relative, Kind = kind };

        Projects.Add(entry2);

        // 첫 보통 프로젝트면 시작 프로젝트로 둔다 - 아무것도 안 골라 둔 채로 실행을 누르게 하지 않는다.
        if (kind == SolutionProjectKind.Normal && string.IsNullOrEmpty(StartupProject)) StartupProject = relative;

        return entry2;
    }

    /// <summary>목록에서 뺀다. 폴더와 파일은 건드리지 않는다 - 지우는 것은 사람이 탐색기에서 한다.</summary>
    public bool Remove(SolutionProjectEntry entry)
    {
        var removed = Projects.RemoveAll(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase)) > 0;

        if (removed && string.Equals(StartupProject, entry.Path, StringComparison.OrdinalIgnoreCase))
            StartupProject = Runnable().FirstOrDefault()?.Path ?? string.Empty;

        return removed;
    }

    /// <summary>파일이 실제로 있는지. 없어진 것은 목록에서 조용히 빼지 않고 화면에 "찾을 수 없음" 으로 남긴다.</summary>
    public bool Exists(SolutionProjectEntry entry) => File.Exists(FullPath(entry.Path));

    private void Normalize()
    {
        Projects = [.. Projects
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .Select(entry => new SolutionProjectEntry { Path = Clean(entry.Path), Kind = entry.Kind })
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];

        StartupProject = string.IsNullOrWhiteSpace(StartupProject) ? string.Empty : Clean(StartupProject);

        if (string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(FilePath))
            Name = System.IO.Path.GetFileNameWithoutExtension(FilePath);
    }

    private static string Clean(string path) => path.Replace('\\', '/').Trim().TrimStart('/');
}
