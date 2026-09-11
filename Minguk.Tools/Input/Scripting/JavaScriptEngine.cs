using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Runtime.Debugger;
using Jint.Runtime;
using Minguk.Tools.Input.Sequencing;

using Minguk.Tools.Input.Scripting.Live;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 자바스크립트로 쓴 스크립트를 Jint 로 돌려 단계 목록을 받아 온다.
/// </summary>
/// <remarks>
/// <b>왜 Jint 인가</b>
///
/// 순수 .NET 으로 쓰인 인터프리터다. 받아 올 런타임도, 따라붙는 네이티브도 없어서
/// 파이썬처럼 첫 실행 때 뭔가를 설치할 필요가 없다. V8 을 껴안는 ClearScript 가 훨씬 빠르지만
/// 플랫폼마다 네이티브 바이너리가 수십 MB 씩 따라온다 - 여기서 도는 것은 단계를 적는
/// 짧은 글이라 속도가 문제 될 일이 없다.
///
/// <b>한도를 건다</b>
///
/// 스크립트가 <c>while(true)</c> 를 돌면 이 앱이 같이 멈춘다. C# 과 파이썬은 사용자가 그렇게
/// 쓸 일이 드물다고 보고 두었지만, Jint 는 한도를 거는 것이 한 줄이라 걸어 둔다.
/// 걸린 것도 사용자가 고쳐야 할 오류로 돌려준다.
/// </remarks>
public sealed class JavaScriptEngine : IScriptEngine
{
    /// <summary>스크립트 하나가 돌 수 있는 시간. 넘으면 오류로 돌려준다.</summary>
    private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(5);

    public string Name => "JavaScript (Jint)";

    /// <summary>받아 올 것이 없다. 늘 돌릴 수 있다.</summary>
    public bool IsReady => true;

    public string? UnavailableReason => null;

    public string SampleSource => string.Join(Environment.NewLine,
    [
        "// 자바스크립트로 씁니다. 한글 이름도 됩니다 - 글자(\"...\") · 이동(x, y)",
        "Type(\"안녕하세요\");",
        "Enter();"
    ]);

    /// <summary>준비할 것이 없다.</summary>
    public Task PrepareAsync(IProgress<string>? progress = null, CancellationToken token = default)
        => Task.CompletedTask;

    public Task<(SequencePlan Plan, IReadOnlyList<ScriptError> Errors)> RunAsync(
        string? source, CancellationToken token = default)
    {
        var text = source ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult<(SequencePlan, IReadOnlyList<ScriptError>)>((new SequencePlan(), []));

        return Task.Run(() => Execute(text, token), token);
    }

    // ── 실시간 모드 ─────────────────────────────────────────────────────

    public Task<IReadOnlyList<ScriptError>> CheckLiveAsync(string? source, CancellationToken token = default)
    {
        var text = source ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<IReadOnlyList<ScriptError>>([]);

        // 파싱만 한다. 문법 오류는 여기서 줄 번호와 함께 나온다.
        return Task.Run(() =>
        {
            try
            {
                Engine.PrepareScript(text);
                return (IReadOnlyList<ScriptError>)[];
            }
            catch (Exception ex)
            {
                return [new ScriptError(LineOf(ex), ex.Message)];
            }
        }, token);
    }

    /// <summary>된다. Jint 의 디버거가 문장마다 알려 준다.</summary>
    public bool SupportsStepping => true;

    public Task<IReadOnlyList<ScriptError>> RunLiveAsync(string? source, LiveScriptApi api, ScriptDebugSession? debug = null, CancellationToken token = default)
    {
        var text = source ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<IReadOnlyList<ScriptError>>([]);

        return Task.Run(() => ExecuteLive(text, api, debug, token));
    }

    /// <summary>사용자 글의 이름. 디버거가 문장마다 알려 줄 때 우리 껍데기 함수(shim)와 가른다.</summary>
    private const string UserSource = "script";

    /// <summary>변수 목록에서 뺄 이름 - 우리가 심은 것들.</summary>
    private static readonly HashSet<string> HiddenNames =
    [
        .. ScriptApiCatalog.LiveNames, "api", "MouseButton",
        // 전역 범위에 늘 있는 붙박이들. 함수(생성자)는 값으로 걸러지지만 이것들은 객체·상수라 이름으로 뺀다.
        "Infinity", "NaN", "undefined", "globalThis", "Atomics", "Intl", "JSON", "Math", "Reflect", "Temporal", "console"
    ];

    /// <summary>
    /// 실시간으로 돌린다. 시간 상한이 없다 - 게임을 보며 도는 스크립트는 사용자가 멈출 때까지 돈다.
    /// 멈추는 것은 토큰(중지·F9·시간 상한)이다.
    /// </summary>
    private static IReadOnlyList<ScriptError> ExecuteLive(string source, LiveScriptApi api, ScriptDebugSession? debug, CancellationToken token)
    {
        var engine = new Engine(options =>
        {
            options.LimitRecursion(64).CancellationToken(token);

            if (debug is not null)
            {
                // 문장마다 Step 이 온다. 거기서 중단점·한 줄씩을 본다. 중단점이 없어도 켜 둔다 -
                // 도는 중에 중단점을 찍을 수 있어야 한다. 문장마다 한 번 묻는 값은 게임 스크립트에서 표가 안 난다.
                options.Debugger.Enabled = true;
                options.Debugger.InitialStepMode = StepMode.Into;
            }
        });

        if (debug is not null)
        {
            engine.Debugger.Step += (_, info) =>
            {
                // 우리 껍데기 함수 안이면 넘긴다. 사용자 글의 줄만 멈출 자리다.
                if (info.Location.SourceFile != UserSource) return StepMode.Over;

                var line = info.Location.Start.Line;

                if (debug.ShouldBreak(line)) debug.Pause(line, DescribeLocals(info), token);

                return StepMode.Into;
            };
        }

        try
        {
            engine.SetValue("api", api);
            engine.SetValue("MouseButton", new
            {
                Left = (int)MouseButton.Left,
                Right = (int)MouseButton.Right,
                Middle = (int)MouseButton.Middle
            });

            // 표의 이름마다 api 를 감싸는 함수를 만든다. 대리자를 하나씩 적지 않아도 되고, 인자는 그대로 넘어간다.
            engine.Execute(string.Join("\n",
                ScriptApiCatalog.LiveNames.Select(n => $"function {n}() {{ return api.{n}.apply(api, arguments); }}")), "shim");

            engine.Execute(source, UserSource);

            return [];
        }
        catch (Exception) when (api.Outcome == LiveScriptOutcome.Stopped || token.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception) when (api.Outcome == LiveScriptOutcome.Guarded)
        {
            return [new ScriptError(0, api.GuardMessage ?? "안전장치가 막았습니다.")];
        }
        catch (JavaScriptException ex)
        {
            return [new ScriptError(ex.Location.Start.Line, ex.Message)];
        }
        catch (Exception ex)
        {
            return [new ScriptError(LineOf(ex), ex.Message)];
        }
    }

    /// <summary>멈춘 자리의 변수들. 우리가 심은 이름과 함수는 뺀다. 너무 길면 자른다.</summary>
    private static string DescribeLocals(DebugInformation info)
    {
        var text = new StringBuilder();
        var count = 0;

        foreach (var scope in info.CurrentScopeChain)
        {
            foreach (var name in scope.BindingNames)
            {
                if (HiddenNames.Contains(name) || name.StartsWith("__", StringComparison.Ordinal)) continue;

                string value;

                try
                {
                    var bound = scope.GetBindingValue(name);
                    if (bound is Jint.Native.Function.Function) continue;
                    value = bound?.ToString() ?? "undefined";
                }
                catch (Exception)
                {
                    value = "?";
                }

                text.AppendLine($"{name}={value}");

                if (++count >= 40) return text.ToString().TrimEnd();
            }
        }

        return text.ToString().TrimEnd();
    }

    private static (SequencePlan, IReadOnlyList<ScriptError>) Execute(string source, CancellationToken token)
    {
        var api = new SequenceScriptApi();

        // 스크립트마다 새로 만든다. 앞선 것이 남긴 이름이 다음에 보이면 지운 줄이 계속 도는
        // 것처럼 굴어서 무엇 때문에 되는지 알 수 없어진다.
        var engine = new Engine(options => options
            .TimeoutInterval(RunLimit)
            .LimitRecursion(64)
            .CancellationToken(token));

        Bind(engine, api);

        try
        {
            engine.Execute(source);

            return (api.ToPlan(), []);
        }
        catch (JavaScriptException ex)
        {
            return (new SequencePlan(), [new ScriptError(ex.Location.Start.Line, ex.Message)]);
        }
        catch (TimeoutException)
        {
            return (new SequencePlan(),
                [new ScriptError(0, $"스크립트가 {RunLimit.TotalSeconds:0}초 안에 안 끝났습니다 - 끝나지 않는 반복이 있는지 보세요.")]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 문법 오류(파서)도 여기로 온다. Jint 의 파서 예외 타입은 판마다 바뀌어 왔으므로
            // 타입을 짚지 않고 메시지를 그대로 보여 준다.
            return (new SequencePlan(), [new ScriptError(LineOf(ex), ex.Message)]);
        }
    }

    /// <summary>
    /// 스크립트가 부를 것들을 심는다.
    /// </summary>
    /// <remarks>
    /// 객체 하나를 통째로 주지 않고 이름을 하나씩 심는다. C# · 파이썬과 쓰는 모양이 같아야
    /// 언어를 오갈 때 헷갈리지 않는다.
    /// API 를 늘리면 여기와 구문 강조(<c>Resource/SequenceScript.*.xshd</c>)를 같이 고친다.
    /// </remarks>
    private static void Bind(Engine engine, SequenceScriptApi api)
    {
        Pair(engine, "Type", "글자", new Action<string>(api.Type));
        Pair(engine, "TypeLine", "줄입력", new Action<string>(api.TypeLine));
        Pair(engine, "Enter", "엔터", new Action(api.Enter));
        Pair(engine, "ToggleHangul", "한영", new Action(api.ToggleHangul));
        Pair(engine, "RightClick", "우클릭", new Action(api.RightClick));
        Pair(engine, "MoveTo", "이동", new Action<int, int>(api.MoveTo));
        Pair(engine, "Scroll", "휠", new Action<int>(api.Scroll));
        Pair(engine, "Wait", "쉬기", new Action<int>(api.Wait));

        // 인자를 생략할 수 있는 것들은 자바스크립트 쪽에서 기본값을 채워 준다.
        Pair(engine, "Click", "클릭", new Action<object?>(b => api.Click(ToButton(b))));
        Pair(engine, "ClickAt", "이동클릭", new Action<int, int, object?>((x, y, b) => api.ClickAt(x, y, ToButton(b))));

        engine.SetValue("MouseButton", new
        {
            Left = (int)MouseButton.Left,
            Right = (int)MouseButton.Right,
            Middle = (int)MouseButton.Middle
        });
    }

    private static void Pair(Engine engine, string english, string korean, Delegate action)
    {
        engine.SetValue(english, action);
        engine.SetValue(korean, action);
    }

    /// <summary>
    /// 자바스크립트에서 넘어온 버튼 값을 읽는다.
    /// </summary>
    /// <remarks>
    /// 안 적으면(undefined) 좌클릭이다. 대부분 좌클릭이라 매번 적게 하면 성가시다.
    /// 숫자(<c>MouseButton.Right</c>)와 글자(<c>"right"</c>) 둘 다 받는다 -
    /// 자바스크립트로 쓰는 사람은 글자로 넘기는 쪽이 더 자연스럽다.
    /// </remarks>
    private static MouseButton ToButton(object? value) => value switch
    {
        null => MouseButton.Left,
        double d => (MouseButton)(int)d,
        int i => (MouseButton)i,
        string s when s.Equals("right", StringComparison.OrdinalIgnoreCase) => MouseButton.Right,
        string s when s.Equals("middle", StringComparison.OrdinalIgnoreCase) => MouseButton.Middle,
        _ => MouseButton.Left
    };

    /// <summary>메시지에 섞여 있는 줄 번호를 뽑아 본다. 못 뽑으면 0.</summary>
    private static int LineOf(Exception ex)
    {
        var message = ex.Message ?? string.Empty;

        // 파서는 "(<anonymous>:2:10)" 처럼 줄:칸 을 붙인다. 판마다 문구가 바뀌어 왔으므로 둘 다 본다.
        var position = System.Text.RegularExpressions.Regex.Match(message, @":(\d+):\d+\)");
        if (position.Success && int.TryParse(position.Groups[1].Value, out var parsed)) return parsed;

        var marker = message.IndexOf("line ", StringComparison.OrdinalIgnoreCase);

        if (marker < 0) return 0;

        var digits = string.Empty;

        for (var i = marker + 5; i < message.Length && char.IsDigit(message[i]); i++)
            digits += message[i];

        return int.TryParse(digits, out var line) ? line : 0;
    }

    /// <summary>엔진을 스크립트마다 새로 만들므로 들고 있는 것이 없다.</summary>
    public void Dispose()
    {
    }
}
