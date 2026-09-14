using System;
using System.IO;
using System.Linq;

using Minguk.Tools.Projects;

namespace Minguk.Tools.Tests;

/// <summary>
/// 솔루션·프로젝트 모델 검사. 화면 없이 돌고 임시 폴더만 만진다 - <c>--vision</c> 처럼 안전하다.
/// </summary>
/// <remarks>
/// 여기서 보는 것은 <b>파일로 오가는 값</b>이다. 만들고, 프로젝트를 넣고, 저장했다 다시 읽었을 때
/// 같은 것이 나오는가. 경로가 상대로 적히는가(폴더째 옮겨도 열려야 한다). 시작 프로젝트가 사라졌을 때
/// 무엇을 돌려주는가.
///
/// 앱 설정을 건드리는 최근 목록은 여기서 안 본다 - 하네스가 사용자 설정 파일을 더럽히면 안 된다.
/// </remarks>
public static class SolutionProbe
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "minguk-solution-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(root);

        var failures = 0;

        try
        {
            failures += CheckCreate(root);
            failures += CheckAddAndRoundTrip(root);
            failures += CheckStartup(root);
            failures += CheckOutsideRefused(root);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (Exception)
            {
                // 지우다 실패해도 검사 결과를 뒤집지 않는다. 임시 폴더다.
            }
        }

        Console.WriteLine(failures == 0 ? "솔루션 검사 통과" : $"솔루션 검사 실패 {failures}건");

        return failures;
    }

    /// <summary>솔루션마다 폴더를 만든다(VS 와 같다).</summary>
    private static int CheckCreate(string root)
    {
        var solution = Solution.Create(Path.Combine(root, "만들기"), "디아2");

        var expected = Path.Combine(root, "만들기", "디아2", "디아2" + Solution.Extension);
        var ok = string.Equals(solution.FilePath, expected, StringComparison.OrdinalIgnoreCase) && File.Exists(expected);

        Report(ok, "솔루션 만들기", ok ? solution.FilePath : $"{solution.FilePath} (기대: {expected})");

        // 같은 이름으로 또 만들면 거절해야 한다 - 쌓아 둔 목록을 덮어쓰면 안 된다.
        var refused = false;

        try
        {
            Solution.Create(Path.Combine(root, "만들기"), "디아2");
        }
        catch (IOException)
        {
            refused = true;
        }

        Report(refused, "이미 있는 솔루션은 안 덮는다", refused ? "거절함" : "덮어썼다");

        return (ok ? 0 : 1) + (refused ? 0 : 1);
    }

    /// <summary>프로젝트를 넣고 저장했다 다시 읽으면 그대로여야 한다. 경로는 상대다.</summary>
    private static int CheckAddAndRoundTrip(string root)
    {
        var solution = Solution.Create(Path.Combine(root, "왕복"), "오버워치");

        var run = MakeProject(solution.Directory, "사격장");
        var shared = MakeProject(solution.Directory, "공용");

        solution.Add(run);
        solution.Add(shared, SolutionProjectKind.Shared);
        solution.Save();

        var again = Solution.Load(solution.FilePath);

        var relative = again.Projects.All(entry => !Path.IsPathRooted(entry.Path) && entry.Path.Contains('/'));
        var kinds = again.Projects.Count == 2
                    && again.Projects[0].Kind == SolutionProjectKind.Normal
                    && again.Projects[1].Kind == SolutionProjectKind.Shared;

        Report(relative, "경로는 상대로 적힌다", string.Join(" · ", again.Projects.Select(entry => entry.Path)));
        Report(kinds, "종류가 이름으로 오간다", string.Join(" · ", again.Projects.Select(entry => entry.Kind.ToString())));

        // 넣은 것을 또 넣어도 늘지 않는다.
        again.Add(run);
        var noDuplicate = again.Projects.Count == 2;

        Report(noDuplicate, "같은 프로젝트를 두 번 안 넣는다", $"{again.Projects.Count}개");

        // 폴더 자리도 맞아야 한다 - 사진·라벨이 여기 있다.
        var folder = again.DirectoryOf(again.Projects[0]);
        var folderOk = string.Equals(folder, Path.Combine(again.Directory, "사격장"), StringComparison.OrdinalIgnoreCase);

        Report(folderOk, "프로젝트 폴더 자리", folder);

        return (relative ? 0 : 1) + (kinds ? 0 : 1) + (noDuplicate ? 0 : 1) + (folderOk ? 0 : 1);
    }

    /// <summary>시작 프로젝트 - 처음 넣은 것이 되고, 빼면 다음 것으로 넘어가고, 공유 프로젝트는 안 된다.</summary>
    private static int CheckStartup(string root)
    {
        var solution = Solution.Create(Path.Combine(root, "시작"), "디아2");

        var shared = solution.Add(MakeProject(solution.Directory, "공용"), SolutionProjectKind.Shared);
        var first = solution.Add(MakeProject(solution.Directory, "안달리엘런"));
        var second = solution.Add(MakeProject(solution.Directory, "메피스토런"));

        var startupIsFirst = solution.Startup()?.Path == first.Path;
        Report(startupIsFirst, "첫 보통 프로젝트가 시작 프로젝트", Solution.NameOf(solution.Startup()!));

        // 공유 프로젝트는 돌릴 수 없다.
        var runnable = solution.Runnable().ToArray();
        var sharedOut = runnable.Length == 2 && runnable.All(entry => entry.Path != shared.Path);
        Report(sharedOut, "공유 프로젝트는 돌릴 목록에 없다", string.Join(" · ", runnable.Select(Solution.NameOf)));

        // 시작 프로젝트를 빼면 다음 것으로 넘어간다 - 아무것도 안 골라진 채로 두지 않는다.
        solution.StartupProject = second.Path;
        solution.Remove(second);
        var movedOn = solution.Startup()?.Path == first.Path;
        Report(movedOn, "시작 프로젝트를 빼면 다음 것으로", Solution.NameOf(solution.Startup()!));

        // 적어 둔 것이 목록에 없어도 터지지 않는다(사람이 폴더를 지운 경우).
        solution.StartupProject = "없는런/없는런.mtsproj";
        var fallback = solution.Startup()?.Path == first.Path;
        Report(fallback, "없어진 시작 프로젝트는 첫 것으로", Solution.NameOf(solution.Startup()!));

        return (startupIsFirst ? 0 : 1) + (sharedOut ? 0 : 1) + (movedOn ? 0 : 1) + (fallback ? 0 : 1);
    }

    /// <summary>솔루션 폴더 밖은 못 넣는다 - 폴더째 옮겨도 열리는 성질이 거기서 나온다.</summary>
    private static int CheckOutsideRefused(string root)
    {
        var solution = Solution.Create(Path.Combine(root, "바깥"), "오버워치");

        var outside = MakeProject(Path.Combine(root, "딴데"), "남의런");

        var refused = false;

        try
        {
            solution.Add(outside);
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Report(refused, "솔루션 폴더 밖 프로젝트는 거절", refused ? "거절함" : "넣어 버렸다");

        return refused ? 0 : 1;
    }

    /// <summary>빈 .mtsproj 파일 하나. 여기서는 내용이 아니라 자리만 본다.</summary>
    private static string MakeProject(string parent, string name)
    {
        var folder = Path.Combine(parent, name);

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name + ".mtsproj");

        File.WriteAllText(path, "{}");

        return path;
    }

    private static void Report(bool ok, string name, string detail)
        => Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name} — {detail}");
}
