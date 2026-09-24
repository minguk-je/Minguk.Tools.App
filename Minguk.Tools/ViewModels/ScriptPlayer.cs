using System;
using System.Threading;
using System.Threading.Tasks;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 한 번 돌릴 때 필요한 것. 한 바퀴를 어떻게 도는지, 그리고 보내기 직전에 할 일(대상 창을 앞으로).
/// </summary>
/// <param name="RunOnce">한 바퀴. 끝까지 돌았으면 true, 멈췄거나 틀렸으면 false. 진행은 progress 로.</param>
/// <param name="BeforeRun">시작 전 대기가 끝난 뒤, 첫 바퀴 전에 한 번.</param>
/// <param name="Prepare">시작을 누르자마자 대기와 <b>나란히</b> 도는 준비(컴파일·글자 읽기 모델 깨우기). 대기가 끝나면 이것이 끝나기를 기다린다.</param>
/// <param name="IsTargetInFront">대상 창이 이미 앞에 있는가 - 그러면 시작 전 대기를 건너뛴다(게임에서 F5 로 시작한 경우).</param>
public sealed record ScriptRunContext(
    Func<IProgress<string>, CancellationToken, Task<bool>> RunOnce,
    Func<Task>? BeforeRun = null,
    Func<CancellationToken, Task>? Prepare = null,
    Func<bool>? IsTargetInFront = null)
{
    /// <summary>계획 모드 - 계획을 단계로 굳혀 차례로 보낸다. 입력 자동화 화면이 하던 것.</summary>
    public static ScriptRunContext ForPlan(SequencePlan plan, InputService service, int holdTimeMs, int intervalMs, Func<Task>? beforeRun = null)
    {
        // 시작할 때 한 번 굳혀 두므로, 도는 도중에 글을 고쳐도 그 바퀴에는 영향이 없다.
        var steps = plan.Build(service, holdTimeMs).Steps;

        return new ScriptRunContext(
            (progress, token) => SequenceRunner.RunOnceAsync(steps, intervalMs, progress, service.Jitter, token),
            beforeRun);
    }
}

/// <summary>
/// 스크립트를 실제로 돌리는 것 - 시작 전 대기, 한 바퀴/반복, 중지, 진행 표시, 실행 시간 상한.
/// </summary>
/// <remarks>
/// 스크립트 화면(한 번씩 돌려 보기)과 플레이 화면(반복)이 같은 것을 써야 "스크립트 화면에서는 되는데 플레이에서는
/// 안 되는" 일이 없다. 한 바퀴를 어떻게 도는지는 <see cref="ScriptRunContext"/> 가 정한다 - 계획 모드는
/// 단계를 차례로 보내고, 실시간 모드는 엔진이 스크립트를 끝까지 돌린다.
///
/// 돌릴 수 없으면(틀린 줄, 빈 계획) 문맥을 주는 함수가 이유를 알리고 null 을 준다 - 여기서는 조용히 안 돈다.
///
/// 전송은 스레드풀로 넘긴다(Task.Run). UI 스레드에서 시작되므로 그냥 두면 await 들이 UI 컨텍스트를
/// 잡아 전송 전체가 UI 에서 돈다. 대기와 횟수 세기는 UI 에 남긴다 - 화면에 바로 비치는 것들이다.
/// </remarks>
public sealed class ScriptPlayer : ViewModelBase
{
    private readonly Func<ScriptRunContext?> _resolve;
    private CancellationTokenSource? _cts;

    public ScriptPlayer(Func<ScriptRunContext?> resolve)
    {
        _resolve = resolve;

        RunOnceCommand = new DelegateCommand(RunOnce, () => IsIdle, false);
        StartLoopCommand = new DelegateCommand(StartLoop, () => IsIdle, false);
        StopCommand = new DelegateCommand(Stop, () => IsRunning, false);
    }

    public DelegateCommand RunOnceCommand { get; }

    public DelegateCommand StartLoopCommand { get; }

    public DelegateCommand StopCommand { get; }

    /// <summary>도는 중에는 설정을 잠근다. 도중에 바뀌면 중간에 엉뚱한 것이 나간다.</summary>
    public bool IsRunning
    {
        get => GetProperty(() => IsRunning);
        private set => SetProperty(() => IsRunning, value, () =>
        {
            RaisePropertyChanged(nameof(IsIdle));
            RunOnceCommand.RaiseCanExecuteChanged();
            StartLoopCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            RunningChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public bool IsIdle => !IsRunning;

    public event EventHandler? RunningChanged;

    /// <summary>지금 어느 단계인지.</summary>
    public string? CurrentStep { get => GetProperty(() => CurrentStep); set => SetProperty(() => CurrentStep, value); }

    public int LoopCount { get => GetProperty(() => LoopCount); set => SetProperty(() => LoopCount, value); }

    // ── 타이밍 ───────────────────────────────────────────────────────────

    /// <summary>키·버튼을 누르고 있는 시간.</summary>
    public int HoldTimeMs { get => GetProperty(() => HoldTimeMs); set => SetProperty(() => HoldTimeMs, value); }

    /// <summary>단계 사이 대기(계획 모드).</summary>
    public int IntervalMs { get => GetProperty(() => IntervalMs); set => SetProperty(() => IntervalMs, value); }

    /// <summary>대기 시간에 얹을 무작위 편차(±ms). 0 이면 편차 없음.</summary>
    public int JitterMs { get => GetProperty(() => JitterMs); set => SetProperty(() => JitterMs, value); }

    /// <summary>시작을 누르고 실제로 보내기까지 기다리는 시간. 이 사이에 대상 창을 앞으로 가져와야 한다.</summary>
    public int StartDelaySeconds { get => GetProperty(() => StartDelaySeconds); set => SetProperty(() => StartDelaySeconds, value); }

    /// <summary>반복 최대 횟수. 0 이면 중지할 때까지.</summary>
    public int MaxLoops { get => GetProperty(() => MaxLoops); set => SetProperty(() => MaxLoops, value); }

    /// <summary>조준 배율(%). 100 이면 화면 픽셀 하나에 마우스 한 카운트. 게임 감도에 맞춰 사람이 조절한다.</summary>
    public int AimScalePercent { get => GetProperty(() => AimScalePercent); set => SetProperty(() => AimScalePercent, value); }

    public double AimScale => Math.Max(1, AimScalePercent) / 100.0;

    /// <summary>조준이 겨눈 결과를 보고 배율을 스스로 맞추는가. 켜 두면 "조준 배율" 칸이 돌면서 고쳐진다.</summary>
    public bool IsAimScaleAuto { get => GetProperty(() => IsAimScaleAuto); set => SetProperty(() => IsAimScaleAuto, value); }

    /// <summary>한 번 실행이 이보다 오래 돌면 멈춘다(초). 0 이면 상한 없음. 끝나지 않는 반복문의 안전장치.</summary>
    public int RunTimeLimitSeconds { get => GetProperty(() => RunTimeLimitSeconds); set => SetProperty(() => RunTimeLimitSeconds, value); }

    /// <summary>타이밍 설정을 되살린다. 화면의 RestoreSettings 에서 부른다.</summary>
    public void Restore(Func<string, int, int> get)
    {
        HoldTimeMs = get(nameof(HoldTimeMs), 30);
        IntervalMs = get(nameof(IntervalMs), 60);
        JitterMs = get(nameof(JitterMs), 0);
        // 손으로 대상 창을 앞으로 가져오려면 3초는 빠듯하다.
        StartDelaySeconds = get(nameof(StartDelaySeconds), 5);
        MaxLoops = get(nameof(MaxLoops), 0);
        RunTimeLimitSeconds = get(nameof(RunTimeLimitSeconds), 600);
        AimScalePercent = get(nameof(AimScalePercent), 100);
        IsAimScaleAuto = get(nameof(IsAimScaleAuto), 1) != 0;
    }

    public void Save(Action<string, int> set)
    {
        set(nameof(HoldTimeMs), HoldTimeMs);
        set(nameof(IntervalMs), IntervalMs);
        set(nameof(JitterMs), JitterMs);
        set(nameof(StartDelaySeconds), StartDelaySeconds);
        set(nameof(MaxLoops), MaxLoops);
        set(nameof(RunTimeLimitSeconds), RunTimeLimitSeconds);
        set(nameof(AimScalePercent), AimScalePercent);
        set(nameof(IsAimScaleAuto), IsAimScaleAuto ? 1 : 0);
    }

    // ── 실행 ─────────────────────────────────────────────────────────────

    public void RunOnce() => Start(loop: false);

    public void StartLoop() => Start(loop: true);

    /// <summary>하나로 시작과 중지를 겸한다(F6). 도는 중에 다시 누르면 멈춘다.</summary>
    public void ToggleLoop()
    {
        if (IsRunning) Stop();
        else StartLoop();
    }

    public void Stop()
    {
        _cts?.Cancel();
        CurrentStep = "중지 요청";
    }

    private void Start(bool loop)
    {
        // 단축키로 시작하는 사람은 이 화면을 못 본다. 왜 안 돌았는지는 로그에라도 남긴다.
        if (IsRunning)
        {
            Logger.Info($"시작 요청을 무시했다 - 이미 도는 중({CurrentStep})");
            return;
        }

        var context = _resolve();

        if (context is null)
        {
            Logger.Info("시작 요청을 무시했다 - 돌릴 문맥이 없다(틀린 줄·빈 스크립트·입력 경로 없음)");
            Chime(Chimes.Failed);
            return;
        }

        // 대기는 "그 사이에 게임으로 넘어가라" 는 시간이다. 게임에서 F5 를 눌렀으면 이미 넘어가 있다 - 매번 1초를 버렸다(사용자, 2026-09-24 "처음 시작할 때 좀 느리게").
        var skipDelay = context.IsTargetInFront?.Invoke() == true;

        Logger.Info($"스크립트 시작({(loop ? "반복" : "1회")}) - {(skipDelay ? "대상 창이 앞에 있어 대기 없이" : $"{StartDelaySeconds}초 대기")}");

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        if (RunTimeLimitSeconds > 0) _cts.CancelAfter(TimeSpan.FromSeconds(RunTimeLimitSeconds));

        IsRunning = true;
        LoopCount = 0;

        // 게임에 있는 사람은 이 화면을 못 본다. 받았다는 것을 소리로 알린다.
        Chime(Chimes.Accepted);

        // Progress<T> 는 만든 스레드(여기서는 UI)로 보고를 넘겨 준다.
        var progress = new Progress<string>(symbol => CurrentStep = symbol);

        _ = RunAsync(context, loop, skipDelay, progress, _cts.Token);
    }

    private async Task RunAsync(ScriptRunContext context, bool loop, bool skipDelay, IProgress<string> progress, CancellationToken token)
    {
        var failed = false;

        try
        {
            // 준비는 대기와 나란히 - 대기가 끝난 뒤에 컴파일하던 때는 둘이 더해져 캐시가 풀린 뒤 첫 실행이 2.9초 걸렸다(실측).
            var prepared = PrepareAsync(context, token);

            if (!skipDelay && !await CountDownAsync(token)) return;

            await prepared;

            if (context.BeforeRun is not null) await context.BeforeRun();

            Chime(Chimes.Started);

            do
            {
                // 취소 토큰은 Task.Run 에 넘기지 않는다. 시작 전에 이미 취소돼 있으면 그 오버로드는
                // 예외를 던지는데 여기서는 취소가 정상 경로다. 한 바퀴가 토큰을 직접 보고 조용히 멈춘다.
                var finished = await Task.Run(() => context.RunOnce(progress, token));

                if (!finished) break;

                LoopCount++;
            }
            while (loop && !token.IsCancellationRequested && (MaxLoops <= 0 || LoopCount < MaxLoops));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "스크립트를 돌리다 멈췄다");
            CurrentStep = $"실패: {ex.Message}";
            failed = true;
        }
        finally
        {
            IsRunning = false;

            if (!failed && CurrentStep?.StartsWith("실패") != true)
                CurrentStep = token.IsCancellationRequested ? "중지함" : "끝남";

            Logger.Info($"스크립트 끝: {CurrentStep} ({LoopCount}회)");
            Chime(CurrentStep?.StartsWith("실패") == true || CurrentStep?.StartsWith("멈춤") == true ? Chimes.Failed : Chimes.Finished);

            MessengerUtility.SendMainMessage($"스크립트를 마쳤습니다. ({LoopCount}회)");
        }
    }

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 소리 신호. 게임이 앞에 있으면 이 화면의 글자는 안 보인다 - F5 가 먹었는지, 돌기 시작했는지, 막혔는지를 귀로 안다.
    /// 짧게 한 번: 받음(대기 시작) · 짧게 두 번: 돌기 시작 · 높게 한 번: 끝남 · 낮게 길게: 실패·막힘.
    /// </summary>
    private static void Chime((int Frequency, int Milliseconds)[] notes) => _ = Task.Run(() =>
    {
        try
        {
            foreach (var (frequency, milliseconds) in notes) System.Console.Beep(frequency, milliseconds);
        }
        catch
        {
            // 소리 장치가 없어도 스크립트는 돈다.
        }
    });

    private static class Chimes
    {
        public static readonly (int, int)[] Accepted = [(880, 90)];
        public static readonly (int, int)[] Started = [(880, 70), (1175, 90)];
        public static readonly (int, int)[] Finished = [(1319, 120)];
        public static readonly (int, int)[] Failed = [(330, 350)];
    }

    /// <summary>준비를 돌린다. 실패해도 막지 않는다 - 실행이 같은 일을 다시 해 보고 그때 이유를 말한다.</summary>
    private static async Task PrepareAsync(ScriptRunContext context, CancellationToken token)
    {
        if (context.Prepare is null) return;

        try
        {
            await context.Prepare(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "시작 준비에 실패했다 - 실행에서 다시 해 본다");
        }
    }

    /// <summary>시작 전 대기. 남은 시간을 표시해 주지 않으면 사용자가 언제 옮겨야 할지 알 수 없다.</summary>
    /// <returns>기다림을 마쳤으면 true, 중간에 취소되었으면 false.</returns>
    private async Task<bool> CountDownAsync(CancellationToken token)
    {
        for (var remain = StartDelaySeconds; remain > 0; remain--)
        {
            CurrentStep = $"{remain}초 뒤 시작 - 대상 창을 앞으로";

            try
            {
                await Task.Delay(1000, token);
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        return true;
    }
}
