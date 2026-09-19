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
    private static void TestProjectMoves(CaptureTarget monitor)
    {
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
