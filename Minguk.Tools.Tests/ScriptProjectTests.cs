using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Minguk.Tools.Capture;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;
using Minguk.Tools.Input.Scripting.Projects;

namespace Minguk.Tools.Tests;

/// <summary>
/// 스크립트 프로젝트(<see cref="ScriptProject"/>) - 여러 파일 컴파일, 파일별 오류, 저장 안 한 글, 리소스, 목록 저장·옮기기.
/// </summary>
/// <remarks>
/// 입력은 가짜 어댑터로 간다. 실제로는 아무것도 안 나간다.
/// </remarks>
internal static partial class Program
{
    private static void TestScriptProject()
    {
        var folder = Path.Combine(Path.GetTempPath(), "minguk-project-" + Guid.NewGuid().ToString("N"));

        try
        {
            var project = ScriptProject.Create(folder, "사격장", "출력(더하기(1, 2));\n출력(리소스글(\"설정.json\"));");

            ScriptProject.WriteText(project.FullPath("공통/계산.csx"), "int 더하기(int a, int b) => a + b;\n");
            project.Add("공통/계산.csx");

            ScriptProject.WriteText(project.FullPath("Resources/설정.json"), "{\"문턱\":3}");
            project.Add("Resources/설정.json");
            project.Save();

            // ── 저장·되읽기 ──
            var loaded = ScriptProject.Load(project.FilePath);
            Check("프로젝트: 저장한 목록을 되읽는다",
                  loaded.Entry == "main.csx" && loaded.Items.Count == 3 &&
                  loaded.Find("공통/계산.csx")?.Kind == ScriptItemKind.Source &&
                  loaded.Find("Resources/설정.json")?.Kind == ScriptItemKind.Resource &&
                  loaded.Folders.Contains("공통") && loaded.Folders.Contains("Resources"),
                  $"시작 {loaded.Entry}, 항목 {string.Join(", ", loaded.Items.Select(i => $"{i.Path}({i.Kind})"))}, 폴더 {string.Join(", ", loaded.Folders)}");

            var engine = new RoslynScriptEngine();
            var noOpen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // ── 다른 파일의 함수를 부르고, 리소스를 읽는다 ──
            {
                var (errors, printed) = RunProject(engine, loaded.ToUnit(noOpen), loaded.Directory);

                Check("프로젝트: 다른 파일의 함수와 리소스를 쓴다",
                      errors.Count == 0 && printed.SequenceEqual(["3", "{\"문턱\":3}"]),
                      errors.Count > 0 ? string.Join(" / ", errors) : string.Join(" | ", printed));
            }

            // ── 다른 파일에서 틀리면 그 파일과 줄로 알린다 ──
            {
                var open = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [loaded.FullPath("공통/계산.csx")] = "// 첫 줄\nint 더하기(int a, int b) => a + ;\n"
                };

                var errors = engine.CheckLiveAsync(loaded.ToUnit(open)).GetAwaiter().GetResult();
                var first = errors.FirstOrDefault();

                Check("프로젝트: 저장 안 한 글로 컴파일하고, 틀린 곳을 그 파일·줄로 준다",
                      errors.Count > 0 && first.Line == 2 && string.Equals(first.File, loaded.FullPath("공통/계산.csx"), StringComparison.OrdinalIgnoreCase),
                      errors.Count > 0 ? string.Join(" / ", errors.Select(e => $"{e} [{e.File}]")) : "(오류 없음)");
            }

            // ── 시작 파일의 줄 번호가 머리말만큼 밀리지 않는다 ──
            {
                var open = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [loaded.EntryPath] = "출력(1);\n없는함수();\n"
                };

                var errors = engine.CheckLiveAsync(loaded.ToUnit(open)).GetAwaiter().GetResult();
                var first = errors.FirstOrDefault();

                Check("프로젝트: 시작 파일의 오류 줄이 머리말만큼 밀리지 않는다",
                      errors.Count > 0 && first.Line == 2 && string.Equals(first.File, loaded.EntryPath, StringComparison.OrdinalIgnoreCase),
                      errors.Count > 0 ? string.Join(" / ", errors.Select(e => $"{e} [{e.File}]")) : "(오류 없음)");
            }

            // ── 없는 리소스는 이유를 말하고 멈춘다 ──
            {
                var open = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [loaded.EntryPath] = "리소스글(\"없다.txt\");" };
                var (errors, _) = RunProject(engine, loaded.ToUnit(open), loaded.Directory);

                Check("프로젝트: 없는 리소스는 이름을 말하고 멈춘다",
                      errors.Count == 1 && errors[0].Message.Contains("없다.txt"),
                      errors.Count > 0 ? errors[0].Message : "(오류 없음)");
            }

            // ── 완성·분류가 다른 파일을 본다 ──
            {
                var completion = new RoslynCompletionSource(typeof(LiveScriptApi))
                {
                    UnitFor = path => loaded.Find(path) is null ? null : loaded.ToUnit(noOpen)
                };

                const string text = "var 합 = 더하기(1, 2);\n더";
                var suggestions = completion.GetAsync(text, text.Length, filePath: loaded.EntryPath).GetAwaiter().GetResult();
                var tokens = completion.ClassifyAsync(text, filePath: loaded.EntryPath).GetAwaiter().GetResult();
                var call = tokens.FirstOrDefault(t => t.Start == text.IndexOf("더하기", StringComparison.Ordinal) && t.Length == 3);
                var local = tokens.FirstOrDefault(t => t.Start == 4 && t.Length == 1);

                Check("프로젝트: 완성에 다른 파일의 함수가 나오고, 색도 메서드다(오프셋이 머리말만큼 밀리지 않는다)",
                      suggestions.Any(s => s.Text == "더하기") && call.Length == 3 && call.Kind == ScriptTokenKind.Method && local.Length == 1,
                      $"완성 {string.Join(", ", suggestions.Where(s => s.Text.StartsWith('더')).Select(s => s.Text))} / 더하기={call.Kind}({call.Length}) / 합={local.Kind}({local.Length})");
            }

            // ── 파일 옮기기: 디스크와 목록이 같이 간다 ──
            {
                loaded.Move("공통", "라이브러리");
                loaded.Save();

                var again = ScriptProject.Load(loaded.FilePath);
                var (errors, printed) = RunProject(engine, again.ToUnit(noOpen), again.Directory);

                Check("프로젝트: 폴더 이름을 바꾸면 디스크·목록이 같이 바뀌고 그대로 돈다",
                      File.Exists(again.FullPath("라이브러리/계산.csx")) && again.Find("라이브러리/계산.csx") is not null &&
                      !again.Folders.Contains("공통") && errors.Count == 0 && printed.FirstOrDefault() == "3",
                      errors.Count > 0 ? string.Join(" / ", errors) : $"폴더 {string.Join(", ", again.Folders)}, 출력 {string.Join(" | ", printed)}");
            }

            // ── 프로젝트 밖 파일은 목록에 못 넣는다 ──
            {
                var outside = Path.Combine(Path.GetTempPath(), "밖.csx");
                var refused = false;

                try { loaded.Add(outside); }
                catch (InvalidOperationException) { refused = true; }

                Check("프로젝트: 폴더 밖의 파일은 목록에 넣지 않는다", refused, refused ? "막음" : "들어갔다");
            }
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    private static (IReadOnlyList<ScriptError> Errors, List<string> Printed) RunProject(IProjectScriptEngine engine, ScriptUnit unit, string root)
    {
        var printed = new List<string>();
        var target = CaptureTarget.EnumerateMonitors().FirstOrDefault();

        var host = new LiveScriptHost
        {
            Service = new InputService(new RecordingAdapter()),
            RequiresForeground = false,
            Target = () => target,
            Hub = new FakeHub(target!),
            Print = printed.Add,
            Watch = (name, value) => printed.Add($"{name}={value}"),
            HoldTimeMs = 1,
            ResourceRoot = root
        };

        var errors = engine.RunLiveAsync(unit, new LiveScriptApi(host, CancellationToken.None)).GetAwaiter().GetResult();

        return (errors, printed);
    }
}
