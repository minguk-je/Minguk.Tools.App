using System;
using System.Threading;
using System.Threading.Tasks;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 한 번 돌릴 때 필요한 것. 계획, 보낼 경로, 그리고 보내기 직전에 할 일(대상 창을 앞으로).
/// </summary>
public sealed record ScriptRunContext(SequencePlan Plan, InputService Service, Func<Task>? BeforeRun = null);

/// <summary>
/// 계획을 실제 입력으로 돌리는 것 - 시작 전 대기, 한 바퀴/반복, 중지, 진행 표시.
/// </summary>
/// <remarks>
/// 입력 자동화 화면의 실행부를 떼어 온 것이다. 편집 화면(한 번씩 돌려 보기)과 플레이 화면(반복)이
/// 같은 것을 써야 "편집에서는 되는데 플레이에서는 안 되는" 일이 없다.
///
/// 무엇을 돌릴지는 <see cref="ScriptRunContext"/> 를 돌려주는 함수가 정한다. 돌릴 수 없으면(틀린 줄,
/// 빈 계획) 그 함수가 이유를 알리고 null 을 준다 - 여기서는 조용히 안 돈다.
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

    /// <summary>단계 사이 대기.</summary>
    public int IntervalMs { get => GetProperty(() => IntervalMs); set => SetProperty(() => IntervalMs, value); }

    /// <summary>대기 시간에 얹을 무작위 편차(±ms). 0 이면 편차 없음.</summary>
    public int JitterMs { get => GetProperty(() => JitterMs); set => SetProperty(() => JitterMs, value); }

    /// <summary>시작을 누르고 실제로 보내기까지 기다리는 시간. 이 사이에 대상 창을 앞으로 가져와야 한다.</summary>
    public int StartDelaySeconds { get => GetProperty(() => StartDelaySeconds); set => SetProperty(() => StartDelaySeconds, value); }

    /// <summary>반복 최대 횟수. 0 이면 중지할 때까지.</summary>
    public int MaxLoops { get => GetProperty(() => MaxLoops); set => SetProperty(() => MaxLoops, value); }

    /// <summary>타이밍 설정을 되살린다. 화면의 RestoreSettings 에서 부른다.</summary>
    public void Restore(Func<string, int, int> get)
    {
        HoldTimeMs = get(nameof(HoldTimeMs), 30);
        IntervalMs = get(nameof(IntervalMs), 60);
        JitterMs = get(nameof(JitterMs), 0);
        // 손으로 대상 창을 앞으로 가져오려면 3초는 빠듯하다.
        StartDelaySeconds = get(nameof(StartDelaySeconds), 5);
        MaxLoops = get(nameof(MaxLoops), 0);
    }

    public void Save(Action<string, int> set)
    {
        set(nameof(HoldTimeMs), HoldTimeMs);
        set(nameof(IntervalMs), IntervalMs);
        set(nameof(JitterMs), JitterMs);
        set(nameof(StartDelaySeconds), StartDelaySeconds);
        set(nameof(MaxLoops), MaxLoops);
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
        if (IsRunning) return;

        var context = _resolve();
        if (context is null) return;

        context.Service.JitterMs = JitterMs;

        // 시작할 때 한 번 굳혀 두므로, 도는 도중에 글을 고쳐도 그 바퀴에는 영향이 없다.
        var steps = context.Plan.Build(context.Service, HoldTimeMs).Steps;

        if (steps.Count == 0)
        {
            MessengerUtility.SendMainMessage("보낼 것이 하나도 적혀 있지 않습니다.");
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsRunning = true;
        LoopCount = 0;

        // Progress<T> 는 만든 스레드(여기서는 UI)로 보고를 넘겨 준다.
        var progress = new Progress<string>(symbol => CurrentStep = symbol);

        _ = RunAsync(context, steps, loop, progress, _cts.Token);
    }

    private async Task RunAsync(
        ScriptRunContext context,
        System.Collections.Generic.IReadOnlyList<InputStep> steps,
        bool loop,
        IProgress<string> progress,
        CancellationToken token)
    {
        try
        {
            if (!await CountDownAsync(token)) return;

            if (context.BeforeRun is not null) await context.BeforeRun();

            do
            {
                // 취소 토큰은 Task.Run 에 넘기지 않는다. 시작 전에 이미 취소돼 있으면 그 오버로드는
                // 예외를 던지는데 여기서는 취소가 정상 경로다. 러너가 토큰을 직접 보고 조용히 멈춘다.
                var finished = await Task.Run(
                    () => SequenceRunner.RunOnceAsync(steps, IntervalMs, progress, context.Service.Jitter, token));

                if (!finished) break;

                LoopCount++;
            }
            while (loop && !token.IsCancellationRequested && (MaxLoops <= 0 || LoopCount < MaxLoops));
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "스크립트를 돌리다 멈췄다");
            CurrentStep = $"실패: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            CurrentStep = token.IsCancellationRequested ? "중지함" : (CurrentStep?.StartsWith("실패") == true ? CurrentStep : "끝남");
            MessengerUtility.SendMainMessage($"스크립트를 마쳤습니다. ({LoopCount}회)");
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
