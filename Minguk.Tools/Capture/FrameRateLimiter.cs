using System.Diagnostics;
using System.Threading;

namespace Minguk.Tools.Capture;

/// <summary>
/// 초당 몇 장까지만 받는다 - 캡처 세션(WGC·영상)과 미리보기가 같이 쓴다.
/// </summary>
/// <remarks>
/// <b>직전 도착이 아니라 박자에 맞춘다</b>(실측 2026-09-15). 예전 규칙은 "직전 장이 온 뒤 1/fps 의 90% 가 안 지났으면 버린다" 였다.
/// 60Hz 모니터를 상한 60 으로 잡으면 간격이 16.7ms 로 고르지 않고 12~15ms 로 일찍 오는 장이 섞이는데(모니터 5대, 59/60Hz 섞임),
/// 그런 장을 버리면 다음 장까지 33ms 를 기다려 <b>31~58fps</b> 로 떨어졌다(<c>--capture-fps</c>: 상한 없이 60.0, 상한 60 이면 31.4·44.0·55.0).
/// 지금은 1/fps 간격의 박자를 두고 <b>박자보다 1/3 간격 넘게 이른 장만</b> 버린다. 박자는 받은 장의 도착이 아니라 박자에서 한 간격씩 민다 -
/// 한 장이 일찍 와도 다음 박자가 당겨지지 않는다. 한 간격 넘게 늦었으면(원본이 느리다) 몰아 받지 않게 지금부터 다시 센다.
/// 1/3 인 이유 - 절반이면 60Hz 를 30 으로 솎을 때 16.7ms 에 온 장이 경계에 걸려 받혀 버린다.
/// 캡처 콜백 한 줄에서만 <see cref="TryAccept"/> 를 부른다(잠금 없음). 상한은 다른 스레드에서 바꿔도 된다.
/// </remarks>
public sealed class FrameRateLimiter
{
    private long _intervalTicks;
    private long _nextDue;

    /// <param name="fps">상한. 0 이하면 제한하지 않는다.</param>
    public FrameRateLimiter(int fps = 0) => Fps = fps;

    /// <summary>상한(fps). 0 이하면 제한하지 않는다. 바꾸면 박자를 다시 잡는다.</summary>
    public int Fps
    {
        get
        {
            var interval = Interlocked.Read(ref _intervalTicks);
            return interval > 0 ? (int)(Stopwatch.Frequency / interval) : 0;
        }
        set
        {
            Interlocked.Exchange(ref _intervalTicks, value > 0 ? Stopwatch.Frequency / value : 0);
            Interlocked.Exchange(ref _nextDue, 0);
        }
    }

    /// <summary>이 시각(<see cref="Stopwatch.GetTimestamp"/>)에 온 장을 받을지. 받으면 다음 박자로 넘어간다.</summary>
    public bool TryAccept(long timestamp)
    {
        var interval = Interlocked.Read(ref _intervalTicks);
        if (interval <= 0) return true;

        var due = Interlocked.Read(ref _nextDue);

        if (due != 0 && timestamp < due - (interval / 3))
            return false;

        Interlocked.Exchange(ref _nextDue, due == 0 || timestamp - due > interval ? timestamp + interval : due + interval);
        return true;
    }

    /// <summary>박자를 버린다 - 다음 장은 무조건 받고 거기서 다시 센다(세션을 새로 시작할 때).</summary>
    public void Reset() => Interlocked.Exchange(ref _nextDue, 0);
}
