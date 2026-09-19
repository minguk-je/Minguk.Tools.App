using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Tools.Capture;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;

namespace Minguk.Tools.Tests;

/// <summary>
/// 프로젝트실행(함수처럼 돌아온다)과 프로젝트이동(끝내고 넘어간다, 쌓이지 않는다) - 사용자, 2026-09-19.
/// </summary>
/// <remarks>
/// 가짜 "프로젝트" 는 이름 → 스크립트 글. 화면이 주는 <see cref="LiveScriptHost.RunProject"/> 자리에 그 글을 새 API 로 돌리는 것을 꽂는다.
/// 쌓였는지는 RunProject 안에 몇 겹 들어가 있는지(깊이)로 본다 - 이동은 늘 1겹, 실행 안의 실행은 2겹.
/// </remarks>
internal static partial class Program
{
    /// <summary>
    /// 조준 배율 두 가지 안전장치(사용자, 2026-09-19) - 조준 모드에서 배율이 너무 낮으면 한 번 말해 주고, 일반(메뉴) 모드는 배율을 배우지 않는다.
    /// </summary>
    private static void TestAimScaleGuards(CaptureTarget monitor)
    {
        (List<string> Printed, List<double> Learned, IReadOnlyList<ScriptError> Errors) Run(string source, double scale)
        {
            var printed = new List<string>();
            var learned = new List<double>();

            var host = new LiveScriptHost
            {
                Service = new InputService(new RecordingAdapter()),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor) { FrameTicks = 1 },
                Print = printed.Add,
                Watch = (_, _) => { },
                HoldTimeMs = 1,
                AimScale = scale,
                AimScaleLearned = learned.Add,
                IsAimScaleAuto = () => true
            };

            using var api = new LiveScriptApi(host, CancellationToken.None);
            var errors = new RoslynScriptEngine().RunLiveAsync(source, api, null, CancellationToken.None).GetAwaiter().GetResult();

            return (printed, learned, errors);
        }

        // 조준 모드, 배율 10% - 두 번 겨눠도 경고는 한 번.
        var (low, _, lowErrors) = Run("조준(100, 100); 조준(100, 100);", 0.1);
        var warnings = low.Count(p => p.Contains("조준 배율이"));

        Check("조준 배율이 너무 낮으면(10%) 조준 모드에서 한 번만 알려 준다", lowErrors.Count == 0 && warnings == 1, $"경고 {warnings}번 · {low.FirstOrDefault(p => p.Contains("조준 배율이"))}");

        // 정상 배율(100%)이면 말이 없다.
        var (normal, _, _) = Run("조준(100, 100);", 1.0);

        Check("조준 배율이 정상(100%)이면 알리지 않는다", !normal.Any(p => p.Contains("조준 배율이")), $"출력 {normal.Count}줄");

        // 일반(메뉴) 모드는 낮은 배율이어도 말하지 않고 배우지도 않는다. 가짜 어댑터의 커서는 (0,0) 이라 그 자리를 겨누면 바로 닿는다.
        var (menu, learned, menuErrors) = Run("마우스모드(\"일반\"); 조준(0, 0);", 0.1);

        Check("일반(메뉴) 모드에서는 낮은 배율이어도 알리지 않고 배율을 배우지도 않는다",
              menuErrors.Count == 0 && !menu.Any(p => p.Contains("조준 배율이")) && learned.Count == 0,
              $"오류 {menuErrors.Count} · 출력 {menu.Count}줄 · 배운 것 {learned.Count}번");
    }

    private static void TestProjectMoves(CaptureTarget monitor)
    {
        TestAimScaleGuards(monitor);

        (List<string> Printed, IReadOnlyList<ScriptError> Errors, int MaxDepth) RunFlow(string start, Dictionary<string, string> projects)
            => RunFlowWith(start, name => projects.TryGetValue(name, out var text) ? text : null);

        (List<string> Printed, IReadOnlyList<ScriptError> Errors, int MaxDepth) RunFlowWith(string start, Func<string, string?> lookup)
        {
            var printed = new List<string>();
            var engine = new RoslynScriptEngine();
            var depth = 0;
            var maxDepth = 0;

            LiveScriptHost? host = null;

            async Task<IReadOnlyList<ScriptError>> RunProject(string name, LiveScriptHost template, CancellationToken token)
            {
                if (lookup(name) is not { } text) return [new ScriptError(0, $"'{name}' 없음")];

                maxDepth = Math.Max(maxDepth, Interlocked.Increment(ref depth));

                try
                {
                    return await engine.RunLiveAsync(text, new LiveScriptApi(template.WithResourceRoot(null), token), null, token);
                }
                finally
                {
                    Interlocked.Decrement(ref depth);
                }
            }

            host = new LiveScriptHost
            {
                Service = new InputService(new RecordingAdapter()),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor),
                Print = printed.Add,
                Watch = (_, _) => { },
                HoldTimeMs = 1,
                RunProject = RunProject
            };

            var api = new LiveScriptApi(host, CancellationToken.None);

            // 화면 실행기(LiveScriptSession)가 하는 그대로 - 처음 것을 돌리고 이동이 남아 있으면 반복문으로 넘어간다.
            var errors = host.Moves.RunWithMovesAsync(
                () => engine.RunLiveAsync(start, api, null, CancellationToken.None),
                name => RunProject(name, host, CancellationToken.None),
                printed.Add,
                CancellationToken.None).GetAwaiter().GetResult();

            return (printed, errors, maxDepth);
        }

        // 넘어가기만 하는 흐름 - 쌓이지 않는다(늘 1겹), 이동 뒤 줄은 안 돈다.
        {
            var (printed, errors, maxDepth) = RunFlow("출력(\"메인\"); 프로젝트이동(\"영웅\"); 출력(\"메인 뒤\");", new()
            {
                ["영웅"] = "출력(\"영웅\"); 프로젝트이동(\"사격장\"); 출력(\"영웅 뒤\");",
                ["사격장"] = "출력(\"사격장\");"
            });

            var shown = string.Join(",", printed.Where(p => !p.StartsWith("프로젝트 '", StringComparison.Ordinal)));

            Check("프로젝트이동: 지금 것을 끝내고 넘어간다 - 이동 뒤 줄은 안 돌고, 쌓이지 않는다(늘 1겹)",
                  errors.Count == 0 && shown == "메인,영웅,사격장" && maxDepth == 1,
                  $"{shown} · 깊이 {maxDepth} · 오류 {errors.Count}");
        }

        // 돌고 도는 흐름 - 사격장 → 메인 → 사격장 … 을 30바퀴 돌아도 1겹(프로젝트실행으로 했으면 60겹).
        {
            var mains = 0;

            var (printed, errors, maxDepth) = RunFlowWith("프로젝트이동(\"사격장\");", name => name switch
            {
                "사격장" => "출력(\"사격장\"); 프로젝트이동(\"메인\");",
                "메인" => ++mains < 30 ? "프로젝트이동(\"사격장\");" : "출력(\"그만\");",
                _ => null
            });

            var rounds = printed.Count(p => p == "사격장");

            Check("프로젝트이동: 돌고 도는 흐름(30바퀴)도 쌓이지 않는다",
                  errors.Count == 0 && rounds == 30 && printed.Last() == "그만" && maxDepth == 1,
                  $"사격장 {rounds}번 · 깊이 {maxDepth} · 오류 {errors.Count}");
        }

        // 프로젝트실행 - 함수처럼 기다렸다가 다음 줄로 돌아온다. 부른 쪽의 끝()은 그 프로젝트만 끝낸다.
        {
            var (printed, errors, maxDepth) = RunFlow("프로젝트실행(\"공용\"); 출력(\"돌아옴\");", new()
            {
                ["공용"] = "출력(\"공용\"); 끝(); 출력(\"공용 뒤\");"
            });

            Check("프로젝트실행: 기다렸다가 다음 줄로 돌아오고, 그 안의 끝()은 그 프로젝트만 끝낸다",
                  errors.Count == 0 && string.Join(",", printed) == "공용,돌아옴" && maxDepth == 1,
                  $"{string.Join(",", printed)} · 깊이 {maxDepth}");
        }

        // 프로젝트실행 안에서 이동 - 쌓인 것을 모두 걷고(부른 쪽 다음 줄도 안 돈다) 맨 바깥에서 넘어간다.
        {
            var (printed, errors, maxDepth) = RunFlow("프로젝트실행(\"영웅\"); 출력(\"메인 뒤\");", new()
            {
                ["영웅"] = "출력(\"영웅\"); 프로젝트이동(\"사격장\");",
                ["사격장"] = "출력(\"사격장\");"
            });

            var shown = string.Join(",", printed.Where(p => !p.StartsWith("프로젝트 '", StringComparison.Ordinal)));

            Check("프로젝트실행 안에서 이동하면 부른 쪽도 끝나고 맨 바깥에서 넘어간다",
                  errors.Count == 0 && shown == "영웅,사격장" && maxDepth == 1,
                  $"{shown} · 깊이 {maxDepth}");
        }
    }
}
