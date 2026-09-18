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
            failures += CheckScreensFollowProject(root);
            failures += CheckFindUnder(root);
            failures += CheckSharedProject(root);
            failures += CheckDataNotListed();
            failures += SolutionSettingsProbe.Run(root);
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

    /// <summary>
    /// 프로젝트 폴더의 데이터(사진·라벨·프레임·녹화·모델·영역)는 목록·솔루션 탐색기에 안 들어간다(사용자, 2026-09-15 - 탐색기에 Images·Labels 가 떴다).
    /// </summary>
    private static int CheckDataNotListed()
    {
        string[] hidden = ["Images", "images/a.png", "Labels/a.txt", "Captures", "Recordings/x.mp4", "classes.txt", "class-colors.json", "data.yaml", "coco.json",
                           "labels.cache", "regions.json", "settings.form.json", "settings.values.json", "detector.onnx", "detector.yolo11n.onnx.json", "detector.zip.bak", "bin/x.mtsx"];
        string[] shown = ["main.csx", "스크립트.csx", "Resources", "Resources/images/a.png", "Resources/regions.json", "공용/도우미.csx", "ImagesTool.csx"];

        var wrongHidden = hidden.Where(p => !Minguk.Tools.ViewModels.ScriptProjectWorkspace.IsIgnored(p)).ToList();
        var wrongShown = shown.Where(Minguk.Tools.ViewModels.ScriptProjectWorkspace.IsIgnored).ToList();
        var ok = wrongHidden.Count == 0 && wrongShown.Count == 0;

        Report(ok, "데이터(사진·라벨·모델·영역)는 프로젝트 목록에 안 넣고 스크립트·Resources 는 넣는다",
            ok ? $"숨김 {hidden.Length} · 보임 {shown.Length}" : $"안 숨겨짐: {string.Join(", ", wrongHidden)} / 잘못 숨겨짐: {string.Join(", ", wrongShown)}");

        return ok ? 0 : 1;
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

    /// <summary>
    /// 프로젝트를 고르면 스크립트 자리·프레임 저장 자리·플레이 목록이 그 프로젝트를 따라가는가.
    /// </summary>
    /// <remarks>
    /// 이것이 없어 솔루션으로 옮긴 뒤 플레이 목록이 비었고, 새로 저장한 그림이 프로젝트 밖으로 갔다(2026-09-14).
    /// 최근 목록에 안 올린다(<c>remember: false</c>) - 사용자 설정을 더럽히지 않는다. 끝나면 솔루션을 닫아 되돌린다.
    /// </remarks>
    private static int CheckScreensFollowProject(string root)
    {
        var failures = 0;

        var solution = Solution.Create(Path.Combine(root, "따라가기"), "오버워치");
        var projectFile = MakeProject(solution.Directory, "사격장");
        var folder = Path.GetDirectoryName(projectFile)!;

        solution.Add(projectFile);

        SolutionWorkspace.Use(solution, remember: false);

        try
        {
            var scripts = Minguk.Tools.Input.Scripting.ScriptFiles.DefaultDirectory;
            var scriptsOk = string.Equals(Path.GetFullPath(scripts), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
            Report(scriptsOk, "스크립트 자리가 고른 프로젝트", scripts);
            failures += scriptsOk ? 0 : 1;

            // 데이터셋(사진·라벨·모델·영역) 자리 - 라벨링·캡처·검출이 다 이것을 본다. 예전엔 따로 저장한 키를 Automation 이 고쳐 쓰는 다리였다.
            var dataset = Minguk.Tools.Vision.Labeling.LabelDataset.ConfiguredRoot;
            var datasetOk = string.Equals(Path.GetFullPath(dataset), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
            Report(datasetOk, "데이터셋 자리가 고른 프로젝트", dataset);
            failures += datasetOk ? 0 : 1;

            var captures = Minguk.Tools.Vision.ProjectPaths.Captures;
            var capturesOk = string.Equals(Path.GetFullPath(captures), Path.GetFullPath(Path.Combine(folder, "captures")), StringComparison.OrdinalIgnoreCase);
            Report(capturesOk, "프레임 저장이 고른 프로젝트의 captures", captures);
            failures += capturesOk ? 0 : 1;

            // 셸 ▶ 가 찾는 완성품 - 없으면 빌드하라는 이유, 있으면 bin\<프로젝트>.mtsx.
            var (missing, reason) = Minguk.Tools.ViewModels.PlayViewModel.FindStartupBuild();
            var missingOk = missing is null && reason is not null && reason.Contains("빌드");
            Report(missingOk, "셸 ▶ - 완성품이 없으면 빌드하라고 말한다", reason ?? "(이유 없음)");
            failures += missingOk ? 0 : 1;

            var build = Path.Combine(folder, "bin", "사격장" + Minguk.Tools.Input.Scripting.ScriptFiles.CompiledExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(build)!);
            File.WriteAllBytes(build, [0x4D, 0x5A]);

            var (found, _) = Minguk.Tools.ViewModels.PlayViewModel.FindStartupBuild();
            var foundOk = string.Equals(found, build, StringComparison.OrdinalIgnoreCase);
            Report(foundOk, "셸 ▶ - 시작 프로젝트의 bin 완성품을 찾는다", found ?? "(못 찾음)");
            failures += foundOk ? 0 : 1;
        }
        finally
        {
            SolutionWorkspace.Close();
        }

        // 솔루션을 닫으면 옛 자리로 돌아간다 - 고른 프로젝트가 없는데 그 폴더를 계속 보면 안 된다.
        var after = Minguk.Tools.Input.Scripting.ScriptFiles.DefaultDirectory;
        var backOk = !after.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        Report(backOk, "솔루션을 닫으면 스크립트 자리가 옛 자리로", after);
        failures += backOk ? 0 : 1;

        var datasetAfter = Minguk.Tools.Vision.Labeling.LabelDataset.ConfiguredRoot;
        var datasetBackOk = !datasetAfter.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        Report(datasetBackOk, "솔루션을 닫으면 데이터셋 자리도 옛 자리로", datasetAfter);
        failures += datasetBackOk ? 0 : 1;

        return failures;
    }

    /// <summary>
    /// 작업공간 아래의 솔루션을 찾는가 - Automation 위 칸의 솔루션 콤보가 이것으로 채워진다.
    /// </summary>
    /// <remarks>한 겹만 본다: 솔루션 폴더 안의 프로젝트 폴더에 .mtsln 이 있어도 안 잡혀야 한다.</remarks>
    private static int CheckFindUnder(string root)
    {
        var projects = Path.Combine(root, "찾기");

        Solution.Create(projects, "오버워치");
        var diablo = Solution.Create(projects, "디아2");

        // 솔루션 안 프로젝트 폴더에 떨어진 .mtsln - 두 겹 아래라 안 잡혀야 한다.
        Directory.CreateDirectory(Path.Combine(diablo.Directory, "안달리엘런"));
        File.WriteAllText(Path.Combine(diablo.Directory, "안달리엘런", "딴거.mtsln"), "{}");

        // 솔루션이 아닌 폴더 - 안 잡혀야 한다.
        Directory.CreateDirectory(Path.Combine(projects, "그냥폴더"));

        var found = Solution.FindUnder(projects).Select(Path.GetFileNameWithoutExtension).ToArray();
        var ok = found.SequenceEqual(new[] { "디아2", "오버워치" });

        Report(ok, "작업공간 아래 솔루션 찾기(한 겹, 이름순)", string.Join(" · ", found));

        var empty = Solution.FindUnder(Path.Combine(root, "없는폴더")).Count == 0;
        Report(empty, "없는 경로면 빈 목록", empty ? "빈 목록" : "뭔가 나왔다");

        return (ok ? 0 : 1) + (empty ? 0 : 1);
    }

    /// <summary>
    /// 공유 프로젝트 - 런이 물면 그 소스가 컴파일·빌드에 합쳐지는가. 참조는 상대(<c>../</c>)로 오가고, 서로 무는 고리에서 안 멈추는가.
    /// 솔루션 탐색기에 참조 줄과 그 파일이 뜨고, 그 파일은 이름 바꾸기·삭제가 막히는가.
    /// </summary>
    /// <remarks>빌드(IL)까지만 본다 - 돌리려면 모니터·가짜 허브가 필요하고 그것은 <c>--vision</c> 의 빌드 검사가 본다.</remarks>
    private static int CheckSharedProject(string root)
    {
        var failures = 0;

        void Expect(bool ok, string name, string detail)
        {
            Report(ok, name, detail);
            failures += ok ? 0 : 1;
        }

        var solution = Solution.Create(Path.Combine(root, "공유"), "디아2");

        var shared = Minguk.Tools.Input.Scripting.Projects.ScriptProject.CreateShared(Path.Combine(solution.Directory, "공용"), "공용");
        File.WriteAllText(shared.FullPath("공용.csx"), "int 두배(int x) => x * 2;\n\nclass 공용값 { public static int 기본 => 21; }\n");
        solution.Add(shared.FilePath, SolutionProjectKind.Shared);

        var run = Minguk.Tools.Input.Scripting.Projects.ScriptProject.Create(Path.Combine(solution.Directory, "카우방"), "카우방", "출력(\"값=\" + 두배(공용값.기본));\n");
        solution.Add(run.FilePath);
        solution.Save();

        Expect(solution.Runnable().Count() == 1 && string.IsNullOrEmpty(shared.Entry), "공유 프로젝트는 시작 파일이 없고 돌릴 목록에 없다",
               $"돌릴 것 {solution.Runnable().Count()} · 시작 '{shared.Entry}'");

        // 참조 전에는 공용 함수가 없어 빌드가 틀려야 한다 - 뒤의 성공이 참조 덕인지 가르는 잣대.
        var empty = new System.Collections.Generic.Dictionary<string, string>();
        var (before, _) = Minguk.Tools.Input.Scripting.CompiledScriptBuilder.Build(run.ToUnit(empty), "카우방");
        Expect(before is null, "참조 전에는 공용 함수를 못 찾아 빌드가 안 된다", before is null ? "실패(기대대로)" : "되어 버렸다");

        run.AddProjectReference(shared.FilePath);
        run.Save();

        var again = Minguk.Tools.Input.Scripting.Projects.ScriptProject.Load(run.FilePath);
        var reference = again.Items.FirstOrDefault(i => i.Kind == Minguk.Tools.Input.Scripting.Projects.ScriptItemKind.ProjectReference);
        Expect(reference?.Path == "../공용/공용.mtsproj", "프로젝트 참조는 상대 경로·이름(ProjectReference)으로 오간다", reference?.Path ?? "(없음)");

        var unit = again.ToUnit(empty);
        Expect(unit.Sources.Count == 1 && unit.Sources[0].EndsWith("공용.csx", StringComparison.OrdinalIgnoreCase), "컴파일 한 벌에 공유 프로젝트 소스가 들어간다",
               string.Join(", ", unit.Sources.Select(Path.GetFileName)));

        var (after, errors) = Minguk.Tools.Input.Scripting.CompiledScriptBuilder.Build(unit, "카우방");
        Expect(after is not null, "참조한 뒤에는 공용 함수·타입을 불러 빌드된다", after is null ? (errors.Count > 0 ? errors[0].ToString() : "(오류 없음)") : $"{after.Length:N0}바이트");

        // 서로 무는 고리 - 무한히 돌지 않고 소스가 한 번씩만.
        shared.AddProjectReference(run.FilePath);
        shared.Save();

        var cycled = Minguk.Tools.Input.Scripting.Projects.ScriptProject.Load(run.FilePath).ToUnit(empty);
        Expect(cycled.Sources.Count == 1, "서로 무는 고리에서도 멈추고 같은 소스를 두 번 안 넣는다", string.Join(", ", cycled.Sources.Select(Path.GetFileName)));

        // 자기 자신은 못 문다.
        var selfRefused = false;
        try { again.AddProjectReference(again.FilePath); } catch (InvalidOperationException) { selfRefused = true; }
        Expect(selfRefused, "자기 자신은 참조 못 한다", selfRefused ? "거절함" : "넣어 버렸다");

        // 솔루션 탐색기 - 참조 줄과 그 아래 공용.csx, 그 파일은 고칠 수 없다.
        var workspace = new Minguk.Tools.ViewModels.ScriptProjectWorkspace(new Minguk.Tools.ViewModels.ScriptProjectWorkspaceHost { OnUi = action => action() });

        try
        {
            workspace.OpenProject(run.FilePath);

            var referenceNode = workspace.Nodes.FirstOrDefault(n => n.Kind == Minguk.Tools.ViewModels.ScriptNodeKind.ProjectReference);
            var sharedNode = workspace.Nodes.FirstOrDefault(n => n.IsExternal);

            Expect(referenceNode is not null && sharedNode is not null && sharedNode.ParentId == referenceNode.Id && sharedNode.Name == "공용.csx",
                   "솔루션 탐색기에 참조 줄과 그 아래 공유 파일이 뜬다",
                   string.Join(" · ", workspace.Nodes.Select(n => $"{n.Name}({n.Kind})")));

            if (sharedNode is not null)
            {
                workspace.SelectedNode = sharedNode;

                var locked = !workspace.RenameCommand.CanExecute(null) && !workspace.DeleteCommand.CanExecute(null) && !workspace.ExcludeCommand.CanExecute(null)
                             && !workspace.SetEntryCommand.CanExecute(null) && !workspace.Delete(sharedNode);
                var opened = workspace.Open(sharedNode) is { } doc && string.Equals(doc.FilePath, shared.FullPath("공용.csx"), StringComparison.OrdinalIgnoreCase);
                var completion = workspace.UnitFor(shared.FullPath("공용.csx")) is not null;

                Expect(locked && opened && completion, "공유 파일은 열어 고치고 완성이 붙지만, 이름 바꾸기·삭제·제외·시작 파일은 막힌다",
                       $"막힘 {locked} · 열림 {opened} · 완성 {completion} · 파일 남음 {File.Exists(shared.FullPath("공용.csx"))}");
            }

            if (referenceNode is not null)
            {
                workspace.SelectedNode = referenceNode;
                var canDelete = workspace.DeleteCommand.CanExecute(null);
                var removed = workspace.ExcludeCommand.CanExecute(null) && workspace.Exclude(referenceNode);

                Expect(!canDelete && removed && File.Exists(shared.FilePath) && !workspace.Nodes.Any(n => n.IsExternal),
                       "참조 줄은 지우기가 아니라 빼기만 - 공유 프로젝트 파일은 남는다",
                       $"지우기 {canDelete} · 뺌 {removed} · 파일 남음 {File.Exists(shared.FilePath)}");
            }
        }
        finally
        {
            workspace.CloseProject();
            workspace.Dispose();
        }

        // 솔루션 탐색기를 솔루션 전체로 - 뿌리가 솔루션, 그 아래 열린 프로젝트(시작 프로젝트면 굵게)와 다른 프로젝트들.
        // 사용자 최근 목록은 안 건드린다(remember: false), 끝나면 솔루션을 닫는다.
        SolutionWorkspace.Use(solution, remember: false);
        var solutionWorkspace = new Minguk.Tools.ViewModels.ScriptProjectWorkspace(new Minguk.Tools.ViewModels.ScriptProjectWorkspaceHost { OnUi = action => action() });

        try
        {
            solutionWorkspace.OpenProject(run.FilePath);

            var nodes = solutionWorkspace.Nodes;
            var solutionNode = nodes.FirstOrDefault(n => n.Kind == Minguk.Tools.ViewModels.ScriptNodeKind.Solution);
            var opened = nodes.FirstOrDefault(n => n.Id == Minguk.Tools.ViewModels.ScriptProjectWorkspace.RootId);
            var other = nodes.FirstOrDefault(n => n.Kind == Minguk.Tools.ViewModels.ScriptNodeKind.Project && n.IsExternal);
            var otherFile = other is null ? null : nodes.FirstOrDefault(n => n.ParentId == other.Id);

            Expect(solutionNode is not null && opened?.ParentId == solutionNode.Id && opened.IsEntry && other?.ParentId == solutionNode.Id && other.Name == "공용 (공유)" && otherFile?.Name == "공용.csx",
                   "솔루션 탐색기 뿌리가 솔루션 - 열린 프로젝트(시작이면 굵게)와 다른 프로젝트·그 파일이 달린다",
                   string.Join(" · ", nodes.Select(n => $"{n.Name}({n.Kind}{(n.IsExternal ? ",밖" : "")}{(n.IsEntry ? ",굵게" : "")})")));

            if (other is not null)
            {
                solutionWorkspace.SelectedNode = other;

                var canEdit = solutionWorkspace.EditProjectCommand.CanExecute(null);
                var locked = !solutionWorkspace.DeleteCommand.CanExecute(null) && !solutionWorkspace.RenameCommand.CanExecute(null);
                var switched = solutionWorkspace.EditProject(other) && string.Equals(solutionWorkspace.Project?.FilePath, shared.FilePath, StringComparison.OrdinalIgnoreCase);

                Expect(canEdit && locked && switched, "다른 프로젝트 줄은 지우기·이름 바꾸기가 막히고 '이 프로젝트 편집' 으로 그리로 넘어간다",
                       $"편집 켜짐 {canEdit} · 막힘 {locked} · 넘어감 {switched} ({solutionWorkspace.Project?.Name})");
            }
        }
        finally
        {
            solutionWorkspace.CloseProject();
            solutionWorkspace.Dispose();
            SolutionWorkspace.Close();
        }

        return failures;
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
