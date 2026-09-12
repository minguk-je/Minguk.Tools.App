using System;
using System.Runtime.InteropServices;

namespace Minguk.Tools.Input.Interop;

/// <summary>
/// 살아 있는 동안 윈도우의 시계 눈금을 1ms 로 당긴다. 다 쓰면 반드시 <see cref="Dispose"/> 로 되돌린다.
/// </summary>
/// <remarks>
/// <b>왜</b> - 윈도우의 기본 스케줄러 눈금은 15.6ms 다. 그래서 <c>Sleep(4)</c> 도 <c>WaitOne(8)</c> 도 실제로는
/// 15ms 를 쉰다. 마우스를 사람처럼 움직이려면 8ms 쯤마다 조금씩 보내야 하는데, 눈금이 15.6ms 면 걸음이 절반으로
/// 줄고 한 걸음이 그만큼 커져 뚝뚝 끊긴다(실측: 걸음 상한 6에 한 걸음 200카운트).
///
/// <b>값싼 것이 아니다</b> - 프로세스가 아니라 <b>시스템 전체</b>의 눈금이라, 켜 두면 다른 프로그램의 타이머도
/// 자주 깨어나 전력을 더 쓴다. 그래서 앱이 뜰 때 켜지 않고 <b>실제로 움직이는 동안만</b> 켠다.
/// 게임은 대개 이미 1ms 로 당겨 두므로 게임 중에는 사실상 공짜다.
///
/// 중첩해서 켜도 된다 - 켠 만큼 꺼야 원래대로 돌아가는 것은 OS 가 센다.
/// </remarks>
public sealed class PrecisionTimer : IDisposable
{
    private readonly bool _raised;

    private bool _disposed;

    public PrecisionTimer()
    {
        // 실패해도 그냥 둔다. 눈금이 굵으면 움직임이 덜 부드러울 뿐, 못 쓰게 되는 것은 아니다.
        _raised = NativeMethods.timeBeginPeriod(1) == 0;
    }

    /// <summary>눈금을 실제로 당겼는가. 못 당겼으면 기다림이 ~15ms 단위로 논다.</summary>
    public bool IsRaised => _raised;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        if (_raised) NativeMethods.timeEndPeriod(1);
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll", ExactSpelling = true)]
        internal static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll", ExactSpelling = true)]
        internal static extern uint timeEndPeriod(uint period);
    }
}
