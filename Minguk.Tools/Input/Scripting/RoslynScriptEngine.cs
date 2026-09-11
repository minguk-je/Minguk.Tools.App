using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Minguk.Tools.Input.Sequencing;

using Minguk.Tools.Input.Scripting.Live;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// C# 으로 쓴 스크립트를 Roslyn 으로 돌려 단계 목록을 받아 온다.
/// </summary>
/// <remarks>
/// 스크립트는 <see cref="SequenceScriptApi"/> 를 전역으로 두고 돈다. 부른 것들이 곧바로
/// 나가지 않고 단계로 적히므로, 그 뒤는 예전과 똑같이 굳혀서 돌릴 수 있다.
///
/// <b>왜 컴파일한 것을 붙들고 있는가</b>
///
/// 두 가지 때문이다.
///
/// 하나, 느리다. Roslyn 은 처음 한 번이 특히 오래 걸린다(어셈블리 로딩·참조 해석). 놓아 주면
/// 다음에 또 그 값을 치른다. 글이 안 바뀌었으면 다시 만들 이유가 없다.
///
/// 둘, <b>컴파일한 것은 메모리에 어셈블리로 남고 언로드되지 않는다.</b> 스크립트를 컴파일할
/// 때마다 새 어셈블리가 하나씩 생기고 프로세스가 죽을 때까지 안 없어진다. 그러니 글자 하나
/// 칠 때마다 컴파일하면 안 된다 - 같은 글이면 캐시를 쓰고, 부르는 쪽에서 잠시 묶어 두었다가
/// 부른다(디바운스).
///
/// "GC 가 안 일어나게" 는 <see cref="GC.TryStartNoGCRegion"/> 같은 것으로 하는 일이 아니다.
/// 그것은 할당 예산을 미리 잡아 두는 것이고, 오래 붙들면 오히려 메모리를 밀어 올린다.
/// 여기서 필요한 것은 <b>참조를 들고 있는 것</b>이고, 안 쓰이면 놓아 주는 것이다
/// (<see cref="KeepAlive"/>).
/// </remarks>
public sealed class RoslynScriptEngine : IScriptEngine
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 마지막으로 쓴 뒤 이만큼 지나면 붙들고 있던 것을 놓는다.
    /// </summary>
    /// <remarks>
    /// 화면을 열어 두고 딴 일을 하는 동안까지 들고 있을 이유는 없다. 다시 쓰면 그때 한 번
    /// 더 만들면 된다. 짧게 잡으면 잠깐 자리를 비운 사이에도 놓아 버려 다시 느려진다.
    /// </remarks>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromMinutes(10);

    public string Name => "C# (Roslyn)";

    /// <summary>받아 올 것이 없다. 늘 돌릴 수 있다.</summary>
    public bool IsReady => true;

    public string? UnavailableReason => null;

    public string SampleSource => string.Join(Environment.NewLine,
    [
        "// C# 으로 씁니다. 한글 이름도 됩니다 - 글자(\"...\") · 이동(x, y)",
        "Type(\"안녕하세요\");",
        "Enter();"
    ]);

    /// <summary>화면이 뜰 때 미리 한 번 컴파일해 둔다. 첫 컴파일이 유독 느리다.</summary>
    public Task PrepareAsync(IProgress<string>? progress = null, CancellationToken token = default)
    {
        progress?.Report("C# 스크립트 엔진 준비 중...");
        return WarmUpAsync();
    }

    private readonly Lock _gate = new();

    private string? _cachedSource;
    private ScriptRunner<object>? _cachedRunner;
    private DateTime _lastUsedUtc;
    private Timer? _release;

    /// <summary>지금 컴파일한 것을 들고 있는지. 화면·로그에 보여 줄 수 있게 둔다.</summary>
    public bool IsWarm
    {
        get { lock (_gate) return _cachedRunner is not null; }
    }

    /// <summary>
    /// 스크립트를 돌려 계획을 만든다.
    /// </summary>
    /// <remarks>
    /// 컴파일 오류든 실행 중 예외든 <paramref name="errors"/> 로 나온다. 둘 다 사용자가
    /// 고쳐야 하는 것이고, 어느 쪽인지는 문장으로 구분된다.
    /// </remarks>
    /// <returns>오류가 하나도 없으면 true.</returns>
    public async Task<(SequencePlan Plan, IReadOnlyList<ScriptError> Errors)> RunAsync(
        string? source, CancellationToken token = default)
    {
        var text = source ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return (new SequencePlan(), []);

        ScriptRunner<object> runner;

        try
        {
            runner = GetOrCompile(text, out var errors);

            if (errors.Count > 0) return (new SequencePlan(), errors);
        }
        catch (Exception ex)
        {
            // 컴파일 자체가 터지는 경우(참조를 못 찾는 등). 사용자가 고칠 수 있는 것이 아니라
            // 환경 문제이므로 로그에도 남긴다.
            Logger.Error(ex, "스크립트를 컴파일하지 못했다");
            return (new SequencePlan(), [new ScriptError(0, $"스크립트를 컴파일하지 못했습니다: {ex.Message}")]);
        }

        var api = new SequenceScriptApi();

        try
        {
            await runner(api, token);
        }
        catch (CompilationErrorException ex)
        {
            return (new SequencePlan(), ToErrors(ex.Diagnostics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 스크립트가 도는 중에 터진 것. 여기까지 적힌 단계는 버린다 - 반쪽짜리를
            // 돌리는 것보다 안 돌리는 것이 낫다.
            return (new SequencePlan(), [new ScriptError(0, $"스크립트가 도는 중에 멈췄습니다: {ex.Message}")]);
        }

        return (api.ToPlan(), []);
    }

    // ── 실시간 모드 ─────────────────────────────────────────────────────
    //    전역이 SequenceScriptApi 가 아니라 LiveScriptApi 다. 캐시도 따로 - 같은 글이라도 전역 타입이 다르면 다른 어셈블리다.

    private string? _liveSource;
    private ScriptRunner<object>? _liveRunner;

    public Task<IReadOnlyList<ScriptError>> CheckLiveAsync(string? source, CancellationToken token = default) => Task.Run(() =>
    {
        var text = source ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return (IReadOnlyList<ScriptError>)[];

        try
        {
            // 컴파일만 한다. 틀린 줄이 없으면 그 결과를 캐시에 둔다 - 곧이어 실행을 누르면 다시 컴파일하지 않게.
            lock (_gate)
            {
                if (_liveRunner is not null && _liveSource == text) return (IReadOnlyList<ScriptError>)[];

                var script = CSharpScript.Create(text, LiveScriptOptions, typeof(LiveScriptApi));
                var failures = ToErrors(script.Compile());

                if (failures.Count > 0) return failures;

                _liveSource = text;
                _liveRunner = script.CreateDelegate();
                _lastUsedUtc = DateTime.UtcNow;
                ScheduleRelease();

                return (IReadOnlyList<ScriptError>)[];
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "실시간 스크립트를 컴파일하지 못했다");
            return [new ScriptError(0, $"스크립트를 컴파일하지 못했습니다: {ex.Message}")];
        }
    }, token);

    /// <summary>못 한다. 스크립트 어셈블리는 디버거를 붙일 자리가 없다. 호출 로그가 그 몫을 한다.</summary>
    public bool SupportsStepping => false;

    public async Task<IReadOnlyList<ScriptError>> RunLiveAsync(string? source, LiveScriptApi api, ScriptDebugSession? debug = null, CancellationToken token = default)
    {
        var text = source ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text)) return [];

        ScriptRunner<object> runner;

        try
        {
            runner = GetOrCompileLive(text, out var errors);

            if (errors.Count > 0) return errors;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "실시간 스크립트를 컴파일하지 못했다");
            return [new ScriptError(0, $"스크립트를 컴파일하지 못했습니다: {ex.Message}")];
        }

        try
        {
            await runner(api, token);
            return [];
        }
        catch (Exception) when (api.Outcome == LiveScriptOutcome.Stopped || token.IsCancellationRequested)
        {
            // 중지·F9·끝() - 정상 종료다.
            return [];
        }
        catch (Exception) when (api.Outcome == LiveScriptOutcome.Guarded)
        {
            return [new ScriptError(0, api.GuardMessage ?? "안전장치가 막았습니다.")];
        }
        catch (Exception ex)
        {
            return [new ScriptError(0, $"스크립트가 도는 중에 멈췄습니다: {ex.Message}")];
        }
    }

    private ScriptRunner<object> GetOrCompileLive(string source, out IReadOnlyList<ScriptError> errors)
    {
        lock (_gate)
        {
            _lastUsedUtc = DateTime.UtcNow;
            ScheduleRelease();

            if (_liveRunner is not null && _liveSource == source)
            {
                errors = [];
                return _liveRunner;
            }

            var watch = Stopwatch.StartNew();

            var script = CSharpScript.Create(source, LiveScriptOptions, typeof(LiveScriptApi));
            var failures = ToErrors(script.Compile());

            if (failures.Count > 0)
            {
                errors = failures;
                return _liveRunner ?? throw new InvalidOperationException("컴파일 실패");
            }

            _liveSource = source;
            _liveRunner = script.CreateDelegate();

            Logger.Debug($"실시간 스크립트 컴파일 {watch.ElapsedMilliseconds}ms ({source.Length}자)");

            errors = [];
            return _liveRunner;
        }
    }

    private ScriptRunner<object> GetOrCompile(string source, out IReadOnlyList<ScriptError> errors)
    {
        lock (_gate)
        {
            _lastUsedUtc = DateTime.UtcNow;
            ScheduleRelease();

            if (_cachedRunner is not null && _cachedSource == source)
            {
                errors = [];
                return _cachedRunner;
            }

            var watch = Stopwatch.StartNew();

            var script = CSharpScript.Create(source, ScriptOptions, typeof(SequenceScriptApi));
            var diagnostics = script.Compile();

            var failures = ToErrors(diagnostics);

            if (failures.Count > 0)
            {
                errors = failures;

                // 못 만든 것을 캐시에 두면 다음에도 그걸 돌려준다.
                return _cachedRunner ?? throw new InvalidOperationException("컴파일 실패");
            }

            _cachedSource = source;
            _cachedRunner = script.CreateDelegate();

            Logger.Debug($"스크립트 컴파일 {watch.ElapsedMilliseconds}ms ({source.Length}자)");

            errors = [];
            return _cachedRunner;
        }
    }

    /// <summary>
    /// 붙들고 있는 것을 <see cref="KeepAlive"/> 뒤에 놓도록 예약한다. 쓸 때마다 미뤄진다.
    /// </summary>
    private void ScheduleRelease()
    {
        _release ??= new Timer(_ => ReleaseIfIdle(), null, Timeout.Infinite, Timeout.Infinite);
        _release.Change(KeepAlive, Timeout.InfiniteTimeSpan);
    }

    private void ReleaseIfIdle()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _lastUsedUtc < KeepAlive) return;

            // 만들어 둔 어셈블리 자체는 언로드되지 않는다. 여기서 놓는 것은 델리게이트와
            // 그것이 붙들고 있던 컴파일 결과다.
            _cachedRunner = null;
            _cachedSource = null;
            _liveRunner = null;
            _liveSource = null;

            Logger.Debug($"스크립트 캐시를 놓았다 ({KeepAlive.TotalMinutes:0}분 동안 안 씀)");
        }
    }

    /// <summary>
    /// 빈 스크립트를 한 번 컴파일해 Roslyn 을 깨워 둔다.
    /// </summary>
    /// <remarks>
    /// 첫 컴파일은 어셈블리 로딩과 참조 해석 때문에 유독 오래 걸린다. 사용자가 글을 치기
    /// 시작할 때 그 값을 치르면 첫 미리보기가 눈에 띄게 늦는다. 화면이 뜰 때 미리 치른다.
    /// 실패해도 그냥 둔다 - 진짜 실행은 그때 다시 해 본다.
    /// </remarks>
    public Task WarmUpAsync() => Task.Run(() =>
    {
        try
        {
            var watch = Stopwatch.StartNew();

            CSharpScript.Create("", ScriptOptions, typeof(SequenceScriptApi)).Compile();

            Logger.Debug($"Roslyn 예열 {watch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Roslyn 예열에 실패했다");
        }
    });

    /// <summary>
    /// 스크립트가 쓸 수 있는 것들.
    /// </summary>
    /// <remarks>
    /// 일부러 좁게 연다. 입력 단계를 적는 것이 목적이라 파일이나 네트워크가 필요하지 않고,
    /// 넓게 열어 두면 사용자가 무심코 쓴 코드가 이 앱 권한으로 돈다.
    /// (막는 울타리는 아니다 - 스크립트는 어차피 이 프로세스 안에서 돈다.)
    /// </remarks>
    private static readonly ScriptOptions ScriptOptions = ScriptOptions.Default
        .WithReferences(typeof(SequenceScriptApi).Assembly)
        .WithImports("System", "Minguk.Tools.Input", "Minguk.Tools.Input.Scripting");

    /// <summary>실시간 모드. 몹(ScriptMob) 같은 것을 이름만으로 쓸 수 있게 Live 네임스페이스를 더한다.</summary>
    private static readonly ScriptOptions LiveScriptOptions = ScriptOptions.WithImports("Minguk.Tools.Input.Scripting.Live");

    private static IReadOnlyList<ScriptError> ToErrors(IEnumerable<Diagnostic> diagnostics)
        => [.. diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => new ScriptError(
                d.Location.GetLineSpan().StartLinePosition.Line + 1,
                d.GetMessage()))];

    public void Dispose()
    {
        lock (_gate)
        {
            _release?.Dispose();
            _release = null;
            _cachedRunner = null;
            _cachedSource = null;
        }
    }
}
