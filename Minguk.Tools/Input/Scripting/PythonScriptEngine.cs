using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Minguk.Tools.Input.Sequencing;
using Python.Runtime;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 파이썬으로 쓴 스크립트를 Python.NET 으로 돌려 단계 목록을 받아 온다.
/// </summary>
/// <remarks>
/// <b>한 프로세스에 한 번만 켠다</b>
///
/// <c>PythonEngine.Shutdown()</c> 뒤에 다시 <c>Initialize()</c> 하는 것은 깨지기로 유명하다.
/// 그래서 한 번 켜면 앱이 끝날 때까지 켜 둔다 - 화면을 닫아도, 언어를 C# 으로 바꿔도 그대로
/// 둔다. <see cref="Dispose"/> 가 파이썬을 끄지 않는 것은 그래서다.
///
/// <b>GIL</b>
///
/// 파이썬 객체를 만지는 모든 곳은 <c>Py.GIL()</c> 안이어야 한다. 초기화한 스레드가 GIL 을
/// 쥔 채로 있으면 다른 스레드가 영영 못 들어오므로, 켠 직후에 <c>BeginAllowThreads</c> 로
/// 놓아 준다.
///
/// <b>왜 스크립트가 입력을 직접 안 보내는가</b>
///
/// C# 쪽과 같다. 부르는 것은 <see cref="SequenceScriptApi"/> 고 그것은 단계를 적어 둘 뿐이다.
/// 파이썬의 <c>for</c> 문은 단계로 풀려 나와 순서 미리보기에 그대로 보인다.
/// </remarks>
public sealed class PythonScriptEngine : IScriptEngine
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private static bool _started;
    private static string? _startFailure;

    public string Name => $"Python {PythonRuntimeInstaller.Version}";

    public bool IsReady => _started;

    public string? UnavailableReason => _startFailure;

    public string SampleSource => string.Join(Environment.NewLine,
    [
        "# 파이썬으로 씁니다. 한글 이름도 됩니다 - 글자(\"...\") · 이동(x, y)",
        "Type(\"안녕하세요\")",
        "Enter()"
    ]);

    /// <summary>
    /// 런타임을 받아 두고 파이썬을 켠다.
    /// </summary>
    /// <remarks>
    /// 처음 한 번은 11MB 를 받아 오므로 오래 걸린다. 무슨 일을 하는 중인지
    /// <paramref name="progress"/> 로 알린다 - 알리지 않으면 멈춘 것처럼 보인다.
    /// </remarks>
    public async Task PrepareAsync(IProgress<string>? progress = null, CancellationToken token = default)
    {
        if (_started) return;

        await StartGate.WaitAsync(token);

        try
        {
            if (_started) return;

            await PythonRuntimeInstaller.EnsureInstalledAsync(progress, token);

            progress?.Report("파이썬 엔진을 켜는 중...");

            Runtime.PythonDLL = PythonRuntimeInstaller.DllPath;

            // 사용자가 따로 깔아 둔 파이썬을 끌어오지 않도록 우리 것만 가리킨다.
            PythonEngine.PythonHome = PythonRuntimeInstaller.RuntimeDirectory;

            PythonEngine.Initialize();

            // 초기화한 스레드가 GIL 을 쥔 채로 있으면 다른 스레드가 못 들어온다.
            PythonEngine.BeginAllowThreads();

            _started = true;
            _startFailure = null;

            progress?.Report($"파이썬 {PythonRuntimeInstaller.Version} 준비됨");
            Logger.Info($"파이썬 엔진을 켰다. {PythonRuntimeInstaller.RuntimeDirectory}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _startFailure = ex.Message;
            Logger.Error(ex, "파이썬 엔진을 켜지 못했다");
            progress?.Report($"파이썬을 준비하지 못했습니다: {ex.Message}");
        }
        finally
        {
            StartGate.Release();
        }
    }

    public async Task<(SequencePlan Plan, IReadOnlyList<ScriptError> Errors)> RunAsync(
        string? source, CancellationToken token = default)
    {
        var text = source ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return (new SequencePlan(), []);

        if (!_started)
        {
            await PrepareAsync(null, token);

            if (!_started)
                return (new SequencePlan(), [new ScriptError(0, _startFailure ?? "파이썬이 준비되지 않았습니다.")]);
        }

        var api = new SequenceScriptApi();

        return await Task.Run(() => Execute(text, api), token);
    }

    private static (SequencePlan, IReadOnlyList<ScriptError>) Execute(string source, SequenceScriptApi api)
    {
        try
        {
            using (Py.GIL())
            {
                // 스크립트마다 새 범위를 쓴다. 앞선 스크립트가 남긴 이름이 다음에 보이면
                // 지운 줄이 계속 도는 것처럼 굴어서 무엇 때문에 되는지 알 수 없어진다.
                using var scope = Py.CreateScope();

                Bind(scope, api);

                scope.Exec(source);
            }

            return (api.ToPlan(), []);
        }
        catch (PythonException ex)
        {
            return (new SequencePlan(), [new ScriptError(LineOf(ex), Describe(ex))]);
        }
        catch (Exception ex)
        {
            return (new SequencePlan(), [new ScriptError(0, $"스크립트가 도는 중에 멈췄습니다: {ex.Message}")]);
        }
    }

    /// <summary>
    /// 스크립트가 부를 것들을 범위에 심는다.
    /// </summary>
    /// <remarks>
    /// 객체 하나를 통째로 주고 <c>api.Type(...)</c> 으로 쓰게 할 수도 있지만, 이름을 하나씩
    /// 심어 <c>Type(...)</c> 로 쓰게 한다. C# 쪽과 쓰는 모양이 같아야 두 언어를 오갈 때
    /// 헷갈리지 않는다.
    /// </remarks>
    private static void Bind(PyModule scope, SequenceScriptApi api)
    {
        scope.Set("api", api.ToPython());

        foreach (var name in BoundNames)
            scope.Exec($"{name} = api.{name}");

        // MouseButton.Right 처럼 쓰려면 타입도 있어야 한다.
        scope.Set("MouseButton", typeof(MouseButton).ToPython());
    }

    /// <summary>
    /// 스크립트 범위에 심는 이름들.
    /// </summary>
    /// <remarks>
    /// <see cref="SequenceScriptApi"/> 에서 리플렉션으로 뽑지 않고 적어 둔다.
    /// 뽑으면 나중에 그 클래스에 도우미 메서드를 하나 넣는 순간 스크립트에도 조용히 새 이름이
    /// 생긴다. 무엇을 열어 줄지는 정해서 여는 편이 낫다.
    /// API 를 늘리면 여기와 구문 강조(<c>Resource/SequenceScript.*.xshd</c>)를 같이 고친다.
    /// </remarks>
    private static readonly string[] BoundNames =
    [
        "Type", "TypeLine", "Enter", "ToggleHangul", "Click", "RightClick", "ClickAt",
        "MoveTo", "Scroll", "Wait",
        "글자", "줄입력", "엔터", "한영", "클릭", "우클릭", "이동", "이동클릭", "휠", "쉬기"
    ];

    /// <summary>파이썬 예외에서 줄 번호를 뽑는다. 못 뽑으면 0.</summary>
    private static int LineOf(PythonException ex)
    {
        // SyntaxError 는 lineno 를 들고 있고, 실행 중 예외는 트레이스백에 있다.
        try
        {
            using (Py.GIL())
            {
                if (ex.Value is not null && ex.Value.HasAttr("lineno"))
                    return ex.Value.GetAttr("lineno").As<int>();
            }
        }
        catch (Exception)
        {
            // 줄 번호는 거들 뿐이다. 못 뽑았다고 오류 자체를 못 보여 줄 이유는 없다.
        }

        var traceback = ex.StackTrace ?? string.Empty;
        var marker = traceback.LastIndexOf("line ", StringComparison.Ordinal);

        if (marker < 0) return 0;

        var digits = new string(traceback[(marker + 5)..].TakeWhile(char.IsDigit).ToArray());

        return int.TryParse(digits, out var line) ? line : 0;
    }

    private static string Describe(PythonException ex)
        => string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : $"{ex.Type?.Name ?? "오류"}: {ex.Message}";

    /// <summary>
    /// 파이썬은 끄지 않는다.
    /// </summary>
    /// <remarks>
    /// <c>Shutdown()</c> 뒤 재초기화가 깨지기 때문이다. 화면을 닫았다 다시 여는 것은 흔한
    /// 일이라, 끄는 쪽이 오히려 위험하다. 프로세스가 끝나면 같이 없어진다.
    /// </remarks>
    public void Dispose()
    {
    }
}
