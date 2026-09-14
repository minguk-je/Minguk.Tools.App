using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Minguk.Base.Utilities;

using Minguk.Tools.Input.Scripting.Projects;
using Minguk.Tools.Vision;
using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Projects;

/// <summary>이주 한 걸음. 무엇을 어디로 옮기는지 사람에게 먼저 보여 준다.</summary>
/// <param name="What">사람이 읽는 이름.</param>
/// <param name="From">지금 자리. 만들기만 하는 걸음은 비어 있다.</param>
/// <param name="To">갈 자리.</param>
public sealed record MigrationStep(string What, string From, string To);

/// <summary>이주 계획. 비어 있으면 옮길 것이 없다는 뜻이다.</summary>
/// <param name="SolutionName">만들 솔루션 이름.</param>
/// <param name="ProjectName">만들 프로젝트 이름.</param>
/// <param name="SolutionFile">만들 .mtsln 자리.</param>
/// <param name="ProjectDirectory">프로젝트 폴더 - 사진·라벨·모델이 여기로 온다.</param>
/// <param name="Steps">보여 줄 걸음들.</param>
/// <param name="Junctions">다시 걸어야 하는 링크들.</param>
public sealed record MigrationPlan(
    string SolutionName,
    string ProjectName,
    string SolutionFile,
    string ProjectDirectory,
    IReadOnlyList<MigrationStep> Steps,
    IReadOnlyList<(string Link, string Target)> Junctions)
{
    public bool IsEmpty => Steps.Count == 0;
}

/// <summary>
/// 흩어져 있던 옛 자리를 솔루션 하나로 모은다. 한 번만 돈다.
/// </summary>
/// <remarks>
/// 옛 자리는 셋이었다 - 데이터셋(<c>프로젝트 경로\Datasets\몹</c>), 프레임 저장(<c>프로젝트 경로\captures</c>),
/// 스크립트(<c>%AppData%\Minguk.Tools\Scripts</c>). 게임을 하나 더 붙이면 저 셋이 각각 갈라져야 하는데
/// 지금은 데이터셋만 갈라진다. 설계는 <c>docs/프로젝트-설계.md</c>.
///
/// <b>사진 수백 장은 다시 만들 수 없다.</b> 그래서 이 클래스는 <see cref="Plan"/>(무엇을 어디로)과
/// <see cref="Apply"/>(실제로 옮김)를 나눠 둔다. 부르는 쪽은 반드시 계획을 먼저 보여 주고 한 번 묻는다.
///
/// <b>같은 드라이브면 옮기기는 즉시다</b> - <see cref="Directory.Move"/> 는 이름만 바꾼다. 드라이브가 다르면
/// .NET 이 복사 후 삭제로 처리한다(느리지만 결과는 같다).
///
/// <b>링크를 먼저 끊는다</b> - 시험 폴더(<c>몹-yolo</c>·<c>몹-yolo11s</c>)의 <c>images</c>·<c>labels</c> 가
/// 데이터셋 안을 가리키는 junction 이라, 끊지 않고 옮기면 링크가 죽은 채로 남는다(실측). 옮긴 뒤 새 자리로 다시 건다.
/// </remarks>
public static class SolutionMigration
{
    /// <summary>옛 데이터를 옮겨 담을 첫 솔루션·프로젝트 이름. 이 PC 가 오버워치 사격장으로 시작했다.</summary>
    public const string DefaultSolutionName = "오버워치";

    public const string DefaultProjectName = "사격장";

    /// <summary>아직 솔루션을 안 쓰고 있고, 옛 자리에 옮길 것이 있는가.</summary>
    public static bool IsNeeded()
    {
        var last = AppSettingUtility.Get(SolutionWorkspace.LastSolutionKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(last) && File.Exists(last)) return false;

        return !Plan().IsEmpty;
    }

    /// <summary>무엇을 어디로 옮길지. 실제로는 아무것도 안 건드린다.</summary>
    public static MigrationPlan Plan()
    {
        var solutionDirectory = Path.Combine(ProjectPaths.Root, DefaultSolutionName);
        var solutionFile = Path.Combine(solutionDirectory, DefaultSolutionName + Solution.Extension);
        var projectDirectory = Path.Combine(solutionDirectory, DefaultProjectName);

        var steps = new List<MigrationStep>();
        var junctions = new List<(string Link, string Target)>();

        var dataset = LabelDataset.LegacyRoot;

        if (Directory.Exists(dataset) && !Same(dataset, projectDirectory))
        {
            var images = Directory.Exists(Path.Combine(dataset, "images"))
                ? Directory.EnumerateFiles(Path.Combine(dataset, "images")).Count()
                : 0;

            steps.Add(new MigrationStep($"데이터셋 (사진 {images}장)", dataset, projectDirectory));

            junctions.AddRange(FindJunctionsInto(dataset, projectDirectory));
        }

        var captures = ProjectPaths.LegacyCaptures;

        if (Directory.Exists(captures) && !Same(captures, Path.Combine(projectDirectory, ProjectPaths.CapturesFolder)))
        {
            var count = Directory.EnumerateFiles(captures).Count();

            steps.Add(new MigrationStep($"프레임 저장 ({count}장)", captures, Path.Combine(projectDirectory, ProjectPaths.CapturesFolder)));
        }

        foreach (var script in OldScripts())
            steps.Add(new MigrationStep("스크립트", script, Path.Combine(projectDirectory, Path.GetFileName(script))));

        return new MigrationPlan(DefaultSolutionName, DefaultProjectName, solutionFile, projectDirectory, steps, junctions);
    }

    /// <summary>
    /// 계획대로 옮긴다. 솔루션·프로젝트를 만들고 설정을 새 자리로 돌려 놓는다.
    /// </summary>
    /// <param name="plan"><see cref="Plan"/> 이 돌려준 것.</param>
    /// <param name="log">한 걸음마다 부른다. 화면이나 콘솔에 적는다.</param>
    /// <returns>만들어진 솔루션.</returns>
    public static Solution Apply(MigrationPlan plan, Action<string>? log = null)
    {
        void Say(string text) => log?.Invoke(text);

        // 1. 링크를 먼저 끊는다 - 안 끊고 옮기면 죽은 링크가 남는다.
        foreach (var (link, _) in plan.Junctions)
        {
            if (!Directory.Exists(link)) continue;

            Directory.Delete(link);
            Say($"링크 끊음: {link}");
        }

        // 2. 솔루션과 프로젝트 폴더.
        var solution = File.Exists(plan.SolutionFile)
            ? Solution.Load(plan.SolutionFile)
            : Solution.Create(ProjectPaths.Root, plan.SolutionName);

        Say($"솔루션: {solution.FilePath}");

        // 3. 데이터셋을 통째로 옮긴다. 프로젝트 폴더가 곧 데이터셋 폴더다.
        foreach (var step in plan.Steps)
        {
            if (Directory.Exists(step.From))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(step.To)!);

                MoveDirectory(step.From, step.To);
            }
            else if (File.Exists(step.From))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(step.To)!);

                if (!File.Exists(step.To)) File.Move(step.From, step.To);
            }
            else
            {
                continue;
            }

            Say($"{step.What}: {step.From} → {step.To}");
        }

        Directory.CreateDirectory(plan.ProjectDirectory);

        // 4. 스크립트 프로젝트. 옮겨 온 .csx 를 목록에 넣는다 - 목록에 없는 파일은 프로젝트가 아니다(VS 와 같다).
        var projectFile = Path.Combine(plan.ProjectDirectory, plan.ProjectName + ScriptProject.Extension);

        var project = File.Exists(projectFile)
            ? ScriptProject.Load(projectFile)
            : ScriptProject.Create(plan.ProjectDirectory, plan.ProjectName, DefaultEntrySource);

        foreach (var moved in Directory.EnumerateFiles(plan.ProjectDirectory, "*.csx"))
        {
            if (project.Find(moved) is null) project.Add(moved, ScriptItemKind.Source);
        }

        project.Save();
        Say($"프로젝트: {project.FilePath}");

        // 5. 솔루션 목록에 넣는다.
        solution.Add(projectFile);
        solution.Save();

        // 6. 링크를 새 자리로 다시 건다.
        foreach (var (link, target) in plan.Junctions)
        {
            if (Directory.Exists(link) || !Directory.Exists(target)) continue;

            CreateJunction(link, target);
            Say($"링크 다시 걸음: {link} → {target}");
        }

        // 7. 이 솔루션을 지금 것으로 건다. 데이터셋 자리는 고른 프로젝트에서 읽으므로 옛 키를 고칠 필요가 없다.
        SolutionWorkspace.Use(solution);

        Say("설정을 새 자리로 돌려놓았습니다.");

        return solution;
    }

    /// <summary>옛 스크립트 자리의 파일들. 폴더가 없거나 비었으면 빈 목록.</summary>
    private static IEnumerable<string> OldScripts()
    {
        var folder = Path.Combine(Helper.UserDataPaths.Root, "Scripts");

        if (!Directory.Exists(folder)) return [];

        return Directory.EnumerateFiles(folder)
            .Where(path => !string.Equals(Path.GetExtension(path), ScriptProject.Extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// <paramref name="source"/> 안을 가리키는 링크들. 옮기고 나서 <paramref name="destination"/> 아래로 다시 건다.
    /// </summary>
    /// <remarks>형제 폴더만 본다 - 시험 폴더(몹-yolo 등)가 거기 있다. 드라이브 전체를 뒤질 이유가 없다.</remarks>
    private static IEnumerable<(string Link, string Target)> FindJunctionsInto(string source, string destination)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(source));

        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) yield break;

        foreach (var sibling in Directory.EnumerateDirectories(parent))
        {
            if (Same(sibling, source)) continue;

            foreach (var child in Directory.EnumerateDirectories(sibling))
            {
                var info = new DirectoryInfo(child);

                if (info.LinkTarget is not { } target) continue;

                var full = Path.GetFullPath(target);

                if (!full.StartsWith(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) continue;

                var relative = Path.GetRelativePath(Path.GetFullPath(source), full);

                yield return (child, Path.Combine(destination, relative));
            }
        }
    }

    /// <summary>
    /// junction 을 만든다. 심볼릭 링크가 아니라 junction 이라 관리자 권한이 필요 없다.
    /// </summary>
    /// <remarks>
    /// .NET 의 <see cref="Directory.CreateSymbolicLink"/> 는 심볼릭 링크를 만드는데, 그쪽은 개발자 모드나
    /// 관리자가 아니면 못 만든다. 그래서 <c>mklink /J</c> 를 쓴다.
    /// </remarks>
    private static void CreateJunction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var process = Process.Start(info);

        process?.WaitForExit();
    }

    /// <summary>같은 드라이브면 즉시 끝난다. 대상이 이미 있으면 안쪽을 하나씩 옮긴다.</summary>
    private static void MoveDirectory(string from, string to)
    {
        if (!Directory.Exists(to))
        {
            Directory.Move(from, to);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));

            if (!File.Exists(target)) File.Move(file, target);
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
            MoveDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));

        if (!Directory.EnumerateFileSystemEntries(from).Any()) Directory.Delete(from);
    }

    private static bool Same(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                         Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                         StringComparison.OrdinalIgnoreCase);

    /// <summary>새 프로젝트의 시작 파일. 스크립트 화면이 쓰는 것과 같은 글이다.</summary>
    private const string DefaultEntrySource =
        "// 시작 파일입니다. 같은 프로젝트의 다른 .csx 에 만든 함수를 그대로 부를 수 있습니다.\n출력(\"안녕하세요\");\n";
}
