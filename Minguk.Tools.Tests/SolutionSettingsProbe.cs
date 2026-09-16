using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

using Minguk.Tools.Input;
using Minguk.Tools.Input.Adapters;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;
using Minguk.Tools.Projects;
using Minguk.Tools.Projects.Settings;

namespace Minguk.Tools.Tests;

/// <summary>
/// 솔루션 설정(<c>docs/솔루션-설정.md</c>) - 양식·값 왕복, 찾는 순서, 이름 겹침, 완성품 자리, 스크립트 읽기·쓰기. <c>--solution</c> 이 부른다.
/// </summary>
public static class SolutionSettingsProbe
{
    public static int Run(string root)
    {
        var failures = 0;

        SettingsLayer.ResetCache();

        var solution = Solution.Create(Path.Combine(root, "설정"), "디아2");
        var runA = MakeProject(solution, "안달리엘런");
        var runB = MakeProject(solution, "메피스토런");
        solution.Save();

        try
        {
            // ── 양식·값 왕복 ──
            var shared = SolutionSettings.ForProject(runA);

            var form = new SettingsForm();
            var potion = new SettingsItem { Kind = SettingsItemKind.Group, Name = "", Label = "물약", Orientation = SettingsOrientation.Vertical, Children = [] };
            potion.Children.Add(new SettingsItem { Kind = SettingsItemKind.Number, Name = "물약HP", Label = "먹을 HP(%)", Default = 40, Min = 0, Max = 100 });
            potion.Children.Add(new SettingsItem { Kind = SettingsItemKind.Check, Name = "자동줍기", Default = true });
            form.Root.Children!.Add(potion);
            form.Root.Children.Add(new SettingsItem { Kind = SettingsItemKind.Combo, Name = "난이도", Items = ["보통", "악몽", "지옥"], Default = "지옥" });
            form.Root.Children.Add(new SettingsItem
            {
                Kind = SettingsItemKind.List, Name = "물약목록",
                Columns = [new() { Name = "키" }, new() { Name = "HP", Kind = SettingsItemKind.Number }, new() { Name = "켜기", Kind = SettingsItemKind.Check }]
            });

            shared.Solution!.SaveForm(form);

            var reread = SettingsForm.Load(shared.Solution.FormPath);
            failures += Expect(reread.ValueItems().Select(i => i.Name).SequenceEqual(["물약HP", "자동줍기", "난이도", "물약목록"]) && reread.Find("난이도")!.Items!.Count == 3,
                "설정 양식이 트리째 파일로 오간다", string.Join(", ", reread.ValueItems().Select(i => $"{i.Name}:{i.Kind}")));

            // ── 찾는 순서: 기본값 → 솔루션 → 프로젝트 ──
            var a = SolutionSettings.ForProject(runA);
            var b = SolutionSettings.ForProject(runB);

            var byDefault = a.Find("물약HP")!;
            a.SetValue("물약HP", 30, SettingsLayerKind.Solution);
            var bySolutionA = a.Find("물약HP")!;
            var bySolutionB = b.Find("물약HP")!;
            a.SetValue("물약HP", 25);
            var byProjectA = a.Find("물약HP")!;
            var stillSolutionB = b.Find("물약HP")!;

            failures += Expect(Num(byDefault) == 40 && byDefault.Source is null
                               && Num(bySolutionA) == 30 && Num(bySolutionB) == 30 && bySolutionA.Source == SettingsLayerKind.Solution
                               && Num(byProjectA) == 25 && byProjectA.IsProjectValue && Num(stillSolutionB) == 30,
                "설정 값은 프로젝트 → 솔루션 → 기본값 순서로 찾고, 프로젝트 값은 그 런에만",
                $"기본 {Num(byDefault)} · 솔루션 A {Num(bySolutionA)} B {Num(bySolutionB)} · 프로젝트 A {Num(byProjectA)} B {Num(stillSolutionB)}");

            a.ResetValue("물약HP");
            failures += Expect(Num(a.Find("물약HP")!) == 30, "↺ 는 프로젝트 값을 빼서 솔루션 값이 보이게 한다", $"{Num(a.Find("물약HP")!)}");

            // 형식이 안 맞는 값은 막고, 파일에 틀린 값이 있으면 기본값 + 경고.
            var refused = Throws<InvalidCastException>(() => a.SetValue("물약HP", "많이"));
            a.Project.SetValue("난이도", 3);
            var bad = a.Find("난이도")!;
            failures += Expect(refused && bad.Value!.GetValue<string>() == "지옥" && bad.Warning is not null,
                "칸 종류와 안 맞는 값은 쓰기를 막고, 이미 있으면 기본값을 쓰고 경고한다", $"막음 {refused} · 값 {bad.Value?.ToJsonString()} · 경고 {bad.Warning}");
            a.Project.RemoveValue("난이도");

            // 파일에 쓰이고 다시 읽힌다.
            a.Flush();
            SettingsLayer.ResetCache();
            var reloaded = SolutionSettings.ForProject(runA).Find("물약HP")!;
            failures += Expect(Num(reloaded) == 30 && File.Exists(Path.Combine(solution.Directory, SolutionSettingsFiles.ValuesFile)),
                "설정 값이 settings.values.json 에 쓰이고 다시 읽힌다", $"{Num(reloaded)}");

            // ── 이름 겹침 ──
            a = SolutionSettings.ForProject(runA);
            b = SolutionSettings.ForProject(runB);

            var projectForm = new SettingsForm();
            projectForm.Root.Children!.Add(new SettingsItem { Kind = SettingsItemKind.Text, Name = "보스", Default = "안다리엘" });
            a.Project.SaveForm(projectForm);

            var toProject = a.CheckNewName("물약HP", SettingsLayerKind.Project);
            var toSolution = b.CheckNewName("보스", SettingsLayerKind.Solution);
            var freeName = a.CheckNewName("새이름", SettingsLayerKind.Project);
            var badShape = a.CheckNewName("물약 HP", SettingsLayerKind.Project);
            failures += Expect(toProject is not null && toSolution?.Contains("안달리엘런") == true && freeName is null && badShape is not null,
                "겹치는 이름은 만들 때 막는다(프로젝트 → 솔루션 공통, 공통 → 모든 프로젝트)", $"프로젝트 {toProject} / 공통 {toSolution} / 모양 {badShape}");

            // 손으로 겹쳐 놓으면 프로젝트 정의가 이기고 경고.
            projectForm.Root.Children.Add(new SettingsItem { Kind = SettingsItemKind.Text, Name = "물약HP", Default = "프로젝트 것" });
            a.Project.SaveForm(projectForm);
            var clash = a.Find("물약HP")!;
            var entries = a.Entries();
            failures += Expect(clash.Layer == SettingsLayerKind.Project && clash.Warning is not null && entries.Count(e => e.Item.Name == "물약HP") == 1
                               && Num(b.Find("물약HP")!) == 30,
                "이미 겹친 이름은 프로젝트 정의가 이기고 경고한다(다른 프로젝트는 공통 그대로)", $"{clash.Layer} · {clash.Value?.ToJsonString()} · {clash.Warning}");
            projectForm.Root.Children.RemoveAt(1);
            a.Project.SaveForm(projectForm);

            // ── 완성품 자리(bin)에서 찾는다 ──
            var fromBin = SolutionSettings.ForScriptRoot(Path.Combine(runA, "bin"));
            failures += Expect(string.Equals(fromBin.ProjectDirectory, Path.GetFullPath(runA), StringComparison.OrdinalIgnoreCase) && fromBin.Find("보스") is not null && fromBin.Find("난이도") is not null,
                "완성품(bin)에서도 프로젝트·솔루션 설정을 찾는다", fromBin.ProjectDirectory);

            // ── 목록 ──
            a.SetValue("물약목록", SettingsValue.FromObject(new[]
            {
                new Dictionary<string, object?> { ["키"] = "1", ["HP"] = 30, ["켜기"] = true },
                new Dictionary<string, object?> { ["키"] = "2", ["HP"] = 60, ["켜기"] = false }
            }), SettingsLayerKind.Solution);

            // ── 스크립트 ──
            failures += CheckScripts(runA);
        }
        finally
        {
            SettingsLayer.ResetCache();
        }

        return failures;
    }

    private static int CheckScripts(string projectDirectory)
    {
        var failures = 0;
        var printed = new List<string>();

        var host = new LiveScriptHost
        {
            Service = new InputService(InputAdapterFactory.CreateSilent()),
            RequiresForeground = false,
            Target = () => null,
            Hub = null!, // 설정만 부른다 - 눈을 안 쓴다.
            ResourceRoot = projectDirectory,
            Print = printed.Add,
            Watch = (_, _) => { },
            HoldTimeMs = 1
        };

        var api = new LiveScriptApi(host, CancellationToken.None);

        var hp = api.Setting<int>("물약HP");
        var pick = api.Setting<bool>("자동줍기");
        var rows = api.SettingList("물약목록");
        var hpObject = api.Setting("물약HP");
        failures += Expect(hp == 30 && pick && rows.Count == 2 && rows[1].Get<int>("HP") == 60 && (string?)rows[0]["키"] == "1" && hpObject is long and 30,
            "스크립트가 설정을 형식대로 읽는다(설정<int>·설정·설정목록)", $"HP {hp} · 줍기 {pick} · 행 {rows.Count} · 객체 {hpObject?.GetType().Name}");

        var missing = Throws<ScriptGuardException>(() => api.Setting("없는칸"));
        var wrongType = Throws<ScriptGuardException>(() => api.Setting<int>("난이도"));
        failures += Expect(missing && wrongType, "없는 이름·형식 틀림은 멈추고 이유를 말한다", $"없음 {missing} · 형식 {wrongType}");

        // 설정 탭(같은 한 벌)에서 바꾸면 도는 스크립트가 곧바로 본다.
        SolutionSettings.ForProject(projectDirectory).SetValue("물약HP", 55);
        var live = api.Setting<int>("물약HP");

        api.SetSetting("물약HP", 45);
        var seenByTab = SolutionSettings.ForProject(projectDirectory).Find("물약HP")!;
        failures += Expect(live == 55 && Num(seenByTab) == 45 && seenByTab.IsProjectValue,
            "설정 탭에서 바꾼 값이 도는 스크립트에 보이고, 스크립트가 쓴 값이 설정 탭에 보인다", $"스크립트가 본 값 {live} · 탭이 본 값 {Num(seenByTab)}");

        // 엔진에서도 - C#(제네릭)과 JavaScript.
        var cs = new RoslynScriptEngine().RunLiveAsync("출력(설정<int>(\"물약HP\") + 1); 설정저장(\"자동줍기\", false); 출력(설정있나(\"보스\"));", api, null, CancellationToken.None).GetAwaiter().GetResult();
        var js = new JavaScriptEngine().RunLiveAsync("출력(설정('물약HP') + 2); 출력(설정목록('물약목록').Count); 설정저장('물약HP', 50);", api, null, CancellationToken.None).GetAwaiter().GetResult();
        var afterJs = api.Setting<int>("물약HP");
        failures += Expect(cs.Count == 0 && js.Count == 0 && printed.Contains("46") && printed.Contains("True") && printed.Contains("47") && printed.Contains("2")
                           && !api.Setting<bool>("자동줍기") && afterJs == 50,
            "C#·JavaScript 스크립트에서 설정을 읽고 쓴다",
            $"C# 오류 {string.Join(" / ", cs)} · JS 오류 {string.Join(" / ", js)} · 출력 {string.Join(", ", printed)}");

        api.ReleaseAll();

        return failures;
    }

    private static string MakeProject(Solution solution, string name)
    {
        var folder = Path.Combine(solution.Directory, name);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name + ".mtsproj");
        File.WriteAllText(path, "{}");

        solution.Projects.Add(new SolutionProjectEntry { Path = name + "/" + name + ".mtsproj", Kind = SolutionProjectKind.Normal });

        return folder;
    }

    private static double Num(SettingsEntry entry) => entry.Value is JsonValue v ? SettingsValue.ToDouble(v) : double.NaN;

    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    private static int Expect(bool ok, string name, string detail)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] 솔루션 설정: {name} — {detail}");
        return ok ? 0 : 1;
    }
}
