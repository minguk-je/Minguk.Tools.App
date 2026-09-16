using System;
using System.Diagnostics;
using System.Threading;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 실행 일시정지 - 화면의 [일시정지] 가 닫고 [계속] 이 연다. 스크립트는 API 를 부를 때마다 여기서 기다린다.
/// </summary>
/// <remarks>
/// 중단점(<see cref="ScriptDebugSession"/>)은 줄마다 묻는 언어(JS·파이썬)만 되고 C# 은 디버거 없이 돈다 - 그래서 줄이 아니라
/// <b>API 호출</b>에서 멈춘다. 모든 언어·빌드한 것(.mtsx)이 같은 API 를 지나므로 어디서나 먹는다. API 를 안 부르는 계산만 도는 반복문은 못 멈춘다.
/// 화면(UI 스레드)이 닫고 열고, 스크립트 스레드가 기다린다. 기다리는 중에 중지하면 토큰이 깨운다.
/// </remarks>
public sealed class ScriptPauseGate
{
    private readonly ManualResetEventSlim _open = new(true);

    public bool IsPaused => !_open.IsSet;

    public void Pause() => _open.Reset();

    public void Resume() => _open.Set();

    /// <summary>
    /// 닫혀 있으면 열리거나 중지될 때까지 붙든다. 멈추기 직전에 <paramref name="onPaused"/> 를 한 번(키 떼기·알림). 멈춰 있던 시간(ms)을 돌려준다.
    /// </summary>
    public long WaitWhilePaused(CancellationToken token, Action? onPaused = null)
    {
        if (_open.IsSet) return 0;

        onPaused?.Invoke();

        var watch = Stopwatch.StartNew();
        WaitHandle.WaitAny([_open.WaitHandle, token.WaitHandle]);
        return watch.ElapsedMilliseconds;
    }
}
