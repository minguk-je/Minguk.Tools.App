using System;
using System.Diagnostics;
using System.Threading;

using Minguk.Tools.Input.Interop;

namespace Minguk.Tools.Tests;

/// <summary>
/// 윈도우 시계 눈금을 1ms 로 당기는 것이 실제로 먹는가(<see cref="PrecisionTimer"/>).
/// </summary>
/// <remarks>
/// <b>왜</b> - 마우스를 사람처럼 움직이려면 8ms 마다 조금씩 보내야 한다. 그런데 윈도우 기본 눈금은 15.6ms 라
/// 8ms 를 부탁해도 15.6ms 를 쉰다 - 걸음이 절반으로 줄고 한 걸음이 그만큼 굵어져 시야가 뚝뚝 끊긴다.
/// 당기기가 조용히 안 먹으면 움직임만 느려지고 아무도 모르므로, 숫자로 본다.
/// </remarks>
internal static partial class Program
{
    private static void TestPrecisionTimer()
    {
        const int Rounds = 20;

        var coarse = MeasureSleeps(Rounds, raise: false);
        var fine = MeasureSleeps(Rounds, raise: true);

        // 눈금이 15.6ms 면 20번에 300ms 쯤, 1ms 면 40ms 안쪽이다. 넉넉히 잡아 100ms 로 가른다.
        Check("시계 눈금을 1ms 로 당기면 짧은 기다림이 짧아진다",
              fine < 100 && fine < coarse,
              $"기본 {coarse:0}ms → 당김 {fine:0}ms (1ms 씩 {Rounds}번)");
    }

    private static double MeasureSleeps(int rounds, bool raise)
    {
        using var precise = raise ? new PrecisionTimer() : null;

        // 첫 번째는 눈금이 바뀌는 틈이 섞인다. 버린다.
        Thread.Sleep(1);

        var watch = Stopwatch.StartNew();

        for (var i = 0; i < rounds; i++) Thread.Sleep(1);

        watch.Stop();

        return watch.Elapsed.TotalMilliseconds;
    }
}
