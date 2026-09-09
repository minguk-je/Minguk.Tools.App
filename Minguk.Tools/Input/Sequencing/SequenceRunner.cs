using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Input.Sequencing;

/// <summary>
/// 만들어 둔 단계들을 차례로 실행한다.
///
/// 왜 만드는 쪽과 나눠 두는가
///   무엇을 보낼지 정하는 일과, 그것을 몇 번 어떤 간격으로 돌릴지는 다른 관심사다.
///   나눠 두면 같은 시퀀스를 한 번만 돌려 보고 그대로 반복으로 넘길 수 있다.
///
/// 취소에 대하여
///   단계 하나를 실행하는 도중에는 끊지 않는다. 키를 누른 채로 끝나면 대상 창에
///   눌린 키가 남기 때문이다. 단계 사이에서만 끊는다.
/// </summary>
public static class SequenceRunner
{
    /// <summary>시퀀스를 한 바퀴 실행한다.</summary>
    /// <param name="intervalMs">단계 사이 대기. 지터를 얹으려면 <paramref name="jitter"/> 를 넘긴다.</param>
    /// <param name="jitter">대기 시간에 편차를 얹는 함수. 보통 <see cref="InputService.Jitter(int)"/>.</param>
    /// <returns>끝까지 돌았으면 true, 중간에 취소되었으면 false.</returns>
    public static async Task<bool> RunOnceAsync(
        IReadOnlyList<InputStep> steps,
        int intervalMs,
        IProgress<string>? progress = null,
        Func<int, int>? jitter = null,
        CancellationToken token = default)
    {
        foreach (var step in steps)
        {
            if (token.IsCancellationRequested) return false;

            progress?.Report(step.Symbol);
            await step.RunAsync(progress, token);

            if (!await DelayAsync(intervalMs, jitter, token)) return false;
        }

        return true;
    }

    /// <summary>중지할 때까지 시퀀스를 반복한다.</summary>
    /// <remarks>
    /// 한 바퀴가 끝날 때마다 처음으로 돌아간다. 단계 안에 상태를 든 것이 있으면
    /// (이동처럼 한 변씩 나아가는 단계) 그 상태는 이어진다.
    /// </remarks>
    public static async Task RunLoopAsync(
        IReadOnlyList<InputStep> steps,
        int intervalMs,
        IProgress<string>? progress = null,
        Func<int, int>? jitter = null,
        CancellationToken token = default)
    {
        if (steps.Count == 0) return;

        while (!token.IsCancellationRequested)
        {
            if (!await RunOnceAsync(steps, intervalMs, progress, jitter, token)) return;
        }
    }

    /// <returns>대기를 마쳤으면 true, 취소되었으면 false.</returns>
    private static async Task<bool> DelayAsync(int intervalMs, Func<int, int>? jitter, CancellationToken token)
    {
        var delay = jitter?.Invoke(intervalMs) ?? intervalMs;

        try
        {
            await Task.Delay(Math.Max(1, delay), token);
            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
