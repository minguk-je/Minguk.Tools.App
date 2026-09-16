using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Minguk.Tools.Projects.Settings;

/// <summary>칸 하나를 합쳐 본 결과.</summary>
/// <param name="Item">정의.</param>
/// <param name="Layer">정의가 있는 층.</param>
/// <param name="Value">찾는 순서대로 고른 값.</param>
/// <param name="Source">값이 온 곳 - 층이면 그 층, 기본값이면 null.</param>
/// <param name="Warning">겹침·형식 안 맞음. 없으면 null.</param>
public sealed record SettingsEntry(SettingsItem Item, SettingsLayerKind Layer, JsonNode? Value, SettingsLayerKind? Source, string? Warning)
{
    /// <summary>프로젝트가 값을 덮었는가 - 설정 탭이 굵게 보인다.</summary>
    public bool IsProjectValue => Source == SettingsLayerKind.Project;
}

/// <summary>
/// 프로젝트 하나가 보는 설정 - 솔루션 공통 층 + 프로젝트 층을 합친다(<c>docs/솔루션-설정.md</c>).
/// </summary>
/// <remarks>
/// <b>찾는 순서</b>: 프로젝트 값 → 솔루션 값 → 정의의 기본값. 형식이 안 맞는 값은 건너뛴다.
/// <b>이름 하나에 정의 하나</b> - 이미 겹쳐 있으면 프로젝트 정의가 이기고 경고를 단다(실행을 멈추지 않는다).
///
/// 층은 폴더마다 앱에 한 벌(<see cref="SettingsLayer.For"/>)이라, 이 객체는 가볍고 몇 번이든 만들어도 된다.
/// </remarks>
public sealed class SolutionSettings
{
    private SolutionSettings(string projectDirectory, string? solutionFilePath)
    {
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        SolutionFilePath = solutionFilePath;

        Project = SettingsLayer.For(ProjectDirectory);
        Solution = solutionFilePath is null ? null : SettingsLayer.For(Path.GetDirectoryName(solutionFilePath)!);
    }

    public string ProjectDirectory { get; }

    /// <summary>프로젝트 폴더 바로 위의 솔루션 파일. 솔루션 밖 프로젝트면 null.</summary>
    public string? SolutionFilePath { get; }

    public SettingsLayer Project { get; }

    public SettingsLayer? Solution { get; }

    public SettingsLayer LayerOf(SettingsLayerKind kind) => kind == SettingsLayerKind.Solution && Solution is not null ? Solution : Project;

    /// <summary>프로젝트 폴더로 연다. 폴더 바로 위에 <c>.mtsln</c> 이 있으면 그것이 솔루션이다.</summary>
    public static SolutionSettings ForProject(string projectDirectory)
    {
        var full = Path.GetFullPath(projectDirectory);
        var parent = Path.GetDirectoryName(full);

        var solution = parent is not null && Directory.Exists(parent)
            ? Directory.EnumerateFiles(parent, "*" + Projects.Solution.Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : null;

        return new SolutionSettings(full, solution);
    }

    /// <summary>
    /// 스크립트 자리에서 연다 - 프로젝트 폴더거나 완성품이 든 <c>bin</c>. <c>bin</c> 이면 그 위가 프로젝트다.
    /// </summary>
    public static SolutionSettings ForScriptRoot(string root)
    {
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

        if (string.Equals(Path.GetFileName(full), "bin", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(full) is { } project)
            full = project;

        return ForProject(full);
    }

    // ── 합쳐 읽기 ────────────────────────────────────────────────────────

    /// <summary>값을 드는 칸 전부 - 솔루션 공통 먼저, 프로젝트 칸 나중. 겹친 이름은 프로젝트 것 하나만.</summary>
    public IReadOnlyList<SettingsEntry> Entries()
    {
        lock (SettingsLayer.Gate)
        {
            var projectItems = Project.Form.ValueItems().ToList();
            var projectNames = projectItems.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
            var list = new List<SettingsEntry>();

            if (Solution is not null)
            {
                foreach (var item in Solution.Form.ValueItems())
                {
                    if (projectNames.Contains(item.Name)) continue;

                    list.Add(Resolve(item, SettingsLayerKind.Solution, null));
                }
            }

            var solutionNames = Solution?.Form.ValueItems().Select(i => i.Name).ToHashSet(StringComparer.Ordinal) ?? [];

            foreach (var item in projectItems)
            {
                var warning = solutionNames.Contains(item.Name)
                    ? $"솔루션 공통에도 「{item.Name}」 이 있습니다 - 이 프로젝트의 정의를 씁니다. 한쪽 칸의 이름을 바꾸세요."
                    : null;

                list.Add(Resolve(item, SettingsLayerKind.Project, warning));
            }

            return list;
        }
    }

    public SettingsEntry? Find(string name)
    {
        lock (SettingsLayer.Gate)
        {
            var item = Project.Form.Find(name);

            if (item is not null)
            {
                var warning = Solution?.Form.Find(name) is not null
                    ? $"솔루션 공통에도 「{name}」 이 있습니다 - 이 프로젝트의 정의를 씁니다. 한쪽 칸의 이름을 바꾸세요."
                    : null;

                return Resolve(item, SettingsLayerKind.Project, warning);
            }

            return Solution?.Form.Find(name) is { } shared ? Resolve(shared, SettingsLayerKind.Solution, null) : null;
        }
    }

    private SettingsEntry Resolve(SettingsItem item, SettingsLayerKind layer, string? warning)
    {
        string? badValue = null;

        if (Project.TryGetValue(item.Name, out var projectValue))
        {
            if (SettingsValue.Fits(item, projectValue)) return new SettingsEntry(item, layer, projectValue, SettingsLayerKind.Project, warning);
            badValue = "프로젝트";
        }

        // 프로젝트 칸의 값은 프로젝트 층에만 둔다 - 솔루션 층에 같은 이름 값이 남아 있어도 다른 칸의 것이다.
        if (layer == SettingsLayerKind.Solution && Solution is not null && Solution.TryGetValue(item.Name, out var solutionValue))
        {
            if (SettingsValue.Fits(item, solutionValue)) return new SettingsEntry(item, layer, solutionValue, SettingsLayerKind.Solution, Join(warning, BadValue(badValue)));
            badValue = badValue is null ? "솔루션" : badValue + "·솔루션";
        }

        var fallback = item.Default is not null && SettingsValue.Fits(item, item.Default) ? item.Default : SettingsForm.EmptyValue(item);

        return new SettingsEntry(item, layer, fallback, null, Join(warning, BadValue(badValue)));
    }

    private static string? BadValue(string? where)
        => where is null ? null : $"{where} 값이 칸 종류와 맞지 않아 처음 값을 씁니다.";

    private static string? Join(string? a, string? b) => a is null ? b : b is null ? a : a + " " + b;

    // ── 쓰기 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 값을 쓴다. 층을 안 주면 프로젝트 층 - 스크립트가 쓰는 자리다.
    /// </summary>
    /// <exception cref="KeyNotFoundException">그런 칸이 없다.</exception>
    /// <exception cref="InvalidCastException">칸 종류와 맞지 않는다.</exception>
    public void SetValue(string name, JsonNode? value, SettingsLayerKind layer = SettingsLayerKind.Project)
    {
        var entry = Find(name) ?? throw new KeyNotFoundException(MissingMessage(name));

        if (!SettingsValue.Fits(entry.Item, value))
            throw new InvalidCastException($"설정 「{name}」 은(는) {KindName(entry.Item.Kind)} 칸이라 {value?.ToJsonString() ?? "null"} 을(를) 넣을 수 없습니다.");

        // 프로젝트 칸은 솔루션 층에 쓸 수 없다 - 거기 두면 아무도 안 읽는다.
        if (entry.Layer == SettingsLayerKind.Project) layer = SettingsLayerKind.Project;

        LayerOf(layer).SetValue(name, value);
    }

    /// <summary>그 층의 값을 빼서 아래 층이 보이게(↺).</summary>
    public bool ResetValue(string name, SettingsLayerKind layer = SettingsLayerKind.Project) => LayerOf(layer).RemoveValue(name);

    public void Flush()
    {
        Project.Flush();
        Solution?.Flush();
    }

    // ── 이름 겹침 막기 ───────────────────────────────────────────────────

    /// <summary>
    /// 그 층 양식에 이 이름의 칸을 새로 둘 수 있는가. 안 되면 한국어 이유.
    /// </summary>
    /// <param name="name">새 이름.</param>
    /// <param name="layer">넣을 층.</param>
    /// <param name="self">이름을 바꾸는 중인 칸 자신 - 자기와는 안 겹친다.</param>
    public string? CheckNewName(string? name, SettingsLayerKind layer, SettingsItem? self = null)
    {
        if (SettingsForm.CheckNameShape(name) is { } shape) return shape;

        lock (SettingsLayer.Gate)
        {
            var own = LayerOf(layer).Form.ValueItems().FirstOrDefault(i => i.Name == name && !ReferenceEquals(i, self));
            if (own is not null) return $"이 양식에 「{name}」 이 이미 있습니다.";

            return CheckOtherLayers(name!, layer);
        }
    }

    /// <summary>다른 층과만 겹치는지 본다 - 프로젝트 층이면 솔루션 공통, 솔루션 층이면 모든 프로젝트. 저장 전 검사가 쓴다.</summary>
    public string? CheckOtherLayers(string name, SettingsLayerKind layer)
    {
        lock (SettingsLayer.Gate)
        {
            if (layer == SettingsLayerKind.Project || Solution is null)
            {
                return Solution?.Form.Find(name!) is not null
                    ? $"솔루션 공통에 「{name}」 이 이미 있습니다 - 이 런에서만 값을 바꾸려면 미리보기에서 덮어쓰세요."
                    : null;
            }

            // 솔루션 공통에 넣으면 모든 프로젝트가 본다 - 어느 프로젝트에든 같은 이름이 있으면 막는다.
            foreach (var (projectName, directory) in SolutionProjectDirectories())
            {
                if (SettingsLayer.For(directory).Form.Find(name!) is not null)
                    return $"프로젝트 「{projectName}」 에 「{name}」 이 이미 있습니다 - 공통으로 옮기려면 그 프로젝트의 칸을 먼저 지우세요.";
            }

            return null;
        }
    }

    /// <summary>솔루션에 든 프로젝트 폴더들(이름, 폴더). 파일이 없어진 것은 뺀다.</summary>
    private IEnumerable<(string Name, string Directory)> SolutionProjectDirectories()
    {
        if (SolutionFilePath is null || !File.Exists(SolutionFilePath)) return [];

        try
        {
            var solution = Projects.Solution.Load(SolutionFilePath);

            return [.. solution.Projects
                .Select(p => solution.FullPath(p.Path))
                .Where(File.Exists)
                .Select(p => (Path.GetFileNameWithoutExtension(p), Path.GetDirectoryName(p)!))];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static string MissingMessage(string name) => $"설정 「{name}」 이 없습니다 - 설정 탭에서 그 이름의 칸을 만드세요.";

    public static string KindName(SettingsItemKind kind) => kind switch
    {
        SettingsItemKind.Group => "구역",
        SettingsItemKind.Text => "글자",
        SettingsItemKind.Number => "숫자",
        SettingsItemKind.Check => "체크",
        SettingsItemKind.Combo => "콤보",
        SettingsItemKind.Slider => "슬라이더",
        SettingsItemKind.List => "목록",
        _ => kind.ToString()
    };
}
