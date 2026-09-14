using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Minguk.Base.Utilities;

namespace Minguk.Tools.Projects;

/// <summary>
/// 지금 열려 있는 솔루션과 시작 프로젝트. 앱에 하나뿐이다.
/// </summary>
/// <remarks>
/// <b>왜 전역인가</b> - 화면 넷(캡처·라벨링·스크립트·플레이)이 같은 솔루션을 봐야 한다. 화면마다 들면
/// 한쪽에서만 바꿔 놓고 담은 사진이 왜 안 보이는지 한참 찾게 된다 - 데이터셋 자리를 앱 전체 키 하나로
/// 둔 것과 같은 이유다(<see cref="Vision.Labeling.LabelDataset"/> 의 ConfiguredRoot 주석).
///
/// <b>시작 프로젝트를 바꿔도 열린 탭은 안 따라간다.</b> VS 가 그렇다 - 바뀌는 것은 굵게 표시되는 프로젝트와
/// 새 탭을 열 때의 기본값이다. 그래서 여기는 "지금 무엇이 시작 프로젝트인가" 만 들고, 탭은 제 프로젝트를 따로 든다.
/// </remarks>
public static class SolutionWorkspace
{
    /// <summary>마지막에 연 솔루션 파일. 시작 창이 최근 목록 맨 위에 놓는다.</summary>
    public const string LastSolutionKey = "Project.LastSolution";

    /// <summary>최근 솔루션 목록. 최신 우선, 줄바꿈으로 나눈다.</summary>
    public const string RecentSolutionsKey = "Project.RecentSolutions";

    public const int RecentLimit = 10;

    /// <summary>지금 열린 솔루션. 아직 안 열었으면 null.</summary>
    public static Solution? Current { get; private set; }

    /// <summary>지금 시작 프로젝트. 솔루션이 없거나 프로젝트가 하나도 없으면 null.</summary>
    public static SolutionProjectEntry? Startup => Current?.Startup();

    /// <summary>시작 프로젝트의 폴더 - 사진·라벨·모델·영역이 있는 자리.</summary>
    public static string? StartupDirectory
        => Current is { } solution && solution.Startup() is { } entry ? solution.DirectoryOf(entry) : null;

    /// <summary>솔루션이나 시작 프로젝트가 바뀌었다. 화면들이 제목·목록을 다시 그린다.</summary>
    public static event EventHandler? Changed;

    /// <summary>솔루션을 연다. 최근 목록에 올린다.</summary>
    public static Solution Open(string solutionFilePath)
    {
        var solution = Solution.Load(solutionFilePath);

        Current = solution;

        Remember(solution.FilePath);
        Changed?.Invoke(null, EventArgs.Empty);

        return solution;
    }

    /// <summary>이미 읽어 둔 솔루션을 그대로 건다(새로 만든 직후 등).</summary>
    /// <param name="solution">걸 솔루션.</param>
    /// <param name="remember">최근 목록에 올릴지. 검사 하네스는 끈다 - 사용자 최근 목록에 임시 솔루션을 끼워 넣으면 안 된다.</param>
    public static void Use(Solution solution, bool remember = true)
    {
        Current = solution ?? throw new ArgumentNullException(nameof(solution));

        if (remember) Remember(solution.FilePath);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Close()
    {
        Current = null;

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>시작 프로젝트를 바꾸고 솔루션 파일에 적는다.</summary>
    public static void SetStartup(SolutionProjectEntry entry)
    {
        if (Current is not { } solution) return;
        if (entry.Kind != SolutionProjectKind.Normal) return;

        solution.StartupProject = entry.Path;
        solution.Save();

        Changed?.Invoke(null, EventArgs.Empty);
    }

    // ── 최근 목록 ────────────────────────────────────────────────────────

    /// <summary>최근에 연 솔루션들. 파일이 없어진 것도 그대로 준다 - 시작 창이 흐리게 보이고 지울지 묻는다.</summary>
    public static IReadOnlyList<string> Recent()
    {
        var saved = AppSettingUtility.Get(RecentSolutionsKey, string.Empty);

        if (string.IsNullOrWhiteSpace(saved)) return [];

        return [.. saved
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(RecentLimit)];
    }

    /// <summary>최근 목록에서 뺀다. 파일은 건드리지 않는다.</summary>
    public static void Forget(string solutionFilePath)
    {
        var kept = Recent().Where(path => !Same(path, solutionFilePath)).ToArray();

        AppSettingUtility.Set(RecentSolutionsKey, string.Join('\n', kept));
    }

    private static void Remember(string solutionFilePath)
    {
        if (string.IsNullOrWhiteSpace(solutionFilePath)) return;

        var full = Path.GetFullPath(solutionFilePath);

        var list = new List<string> { full };

        list.AddRange(Recent().Where(path => !Same(path, full)));

        AppSettingUtility.Set(RecentSolutionsKey, string.Join('\n', list.Take(RecentLimit)));
        AppSettingUtility.Set(LastSolutionKey, full);
    }

    private static bool Same(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // 못 읽는 경로(옛 설정에 남은 쓰레기)는 같지 않은 것으로 본다. 여기서 터지면 시작 창이 안 뜬다.
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
