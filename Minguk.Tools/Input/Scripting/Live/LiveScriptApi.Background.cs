using System;
using System.Collections.Generic;
using System.Threading;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 스크립트 옆에서 따로 도는 일 - 줍기·물약·물러나기·화면 돌리기처럼 본 흐름(대상 잡기·공격)과 상관없이 늘 봐야 하는 것.
/// </summary>
/// <remarks>
/// 사용자(2026-09-25) "다 쓰레드로 분리하자.. 화면 전환, 줍기, F1, F3, 뒤로 물러서기 등등 모두 다" - 한 줄로 돌던 사냥 스크립트는 한 가지를 하는 동안
/// 다른 것을 못 봤다(공격하며 쉬는 0.4초·이름표 찾기 동안 줍기·물약이 밀렸다).
///
/// <b>멈춤</b> - 따로 도는 일도 본 흐름과 같은 중지 토큰을 본다(F6·Pause·실행 시간 상한). 따로 도는 일이 안전장치에 걸리면(대상 창이 앞에 없음 등)
/// 본 흐름도 다음 호출에서 같은 이유로 멈춘다 - 뒤에서만 막히고 앞은 모르는 채 도는 일이 없게. 따로 도는 일이 <c>끝()</c> 을 부르면 스크립트 전체가 끝난다.
/// 그 밖의 오류는 출력에 알리고 1초 쉰 뒤 이어 돈다 - 줍기 한 번 틀렸다고 사냥이 서지 않게.
///
/// <b>끝낼 때</b> - 스크립트가 끝나면(<see cref="ReleaseAll"/>) 누른 키를 떼기 <b>전에</b> 따로 도는 일부터 세우고 기다린다. 거꾸로 하면 뗀 뒤에 따로 도는 일이
/// 키를 다시 눌러 게임에 눌린 채 남는다.
///
/// 따로 도는 일끼리·본 흐름과 같은 변수를 나눠 보면 한쪽이 쓰는 사이 다른 쪽이 읽을 수 있다 - 숫자·참거짓·글 하나를 바꿔 끼우는 정도는 괜찮고,
/// 목록을 같이 고치려면 <c>lock</c> 을 쓴다.
/// </remarks>
public partial class LiveScriptApi
{
    /// <summary>따로 도는 일이 걸린 안전장치 - 본 흐름이 다음 호출에서 이것으로 멈춘다.</summary>
    private volatile string? _backgroundGuard;

    /// <summary>따로 도는 일이 <c>끝()</c> 을 불렀다 - 스크립트 전체를 끝낸다.</summary>
    private volatile bool _backgroundEnded;

    /// <summary>스크립트가 끝나 따로 도는 일을 세운다.</summary>
    private volatile bool _backgroundStopping;

    private readonly List<Thread> _backgroundThreads = [];

    /// <summary>끝낼 때 따로 도는 일이 이번 바퀴를 마치기를 기다리는 시간(ms).</summary>
    private const int BackgroundJoinMs = 1500;

    /// <summary>
    /// 그 일을 따로(다른 스레드에서) 간격마다 되풀이한다 - 스크립트가 끝나거나 멈출 때까지.
    /// </summary>
    /// <param name="name">출력·로그에 보일 이름.</param>
    /// <param name="intervalMs">한 번 하고 다음까지 쉬는 시간(ms). 10 보다 작으면 10.</param>
    /// <param name="work">한 번 할 일.</param>
    public void RunInBackground(string name, int intervalMs, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var label = string.IsNullOrWhiteSpace(name) ? "이름 없음" : name.Trim();
        var interval = Math.Max(10, intervalMs);

        var thread = new Thread(() => BackgroundLoop(label, interval, work))
        {
            IsBackground = true,
            Name = $"스크립트 따로 돌리기: {label}"
        };

        lock (_backgroundThreads) _backgroundThreads.Add(thread);

        _host.Trace?.Invoke(new ScriptCall(DateTime.Now, "RunInBackground", $"{Quote(label)}, {interval}", "시작", 0));
        thread.Start();
    }

    public void 따로돌리기(string 이름, int 간격, Action 할일) => RunInBackground(이름, 간격, 할일);

    private void BackgroundLoop(string name, int interval, Action work)
    {
        while (!_backgroundStopping && !IsStopped())
        {
            try
            {
                work();
                Wait(interval);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ScriptStoppedException)
            {
                _backgroundEnded = true;
                break;
            }
            catch (ScriptGuardException ex)
            {
                // 본 흐름도 같은 이유로 세운다 - 다음 호출의 ThrowIfStopping 이 이것을 던진다.
                _backgroundGuard ??= ex.Message;
                break;
            }
            catch (Exception ex)
            {
                if (_backgroundStopping || IsStopped()) break;

                Logger.Warn(ex, $"따로 도는 「{name}」 에서 오류");
                Print($"따로 도는 「{name}」 에서 오류가 나 1초 뒤 이어 돕니다 - {ex.Message}");

                try { Wait(1000); }
                catch (Exception) { break; }
            }
        }
    }

    /// <summary>따로 도는 일을 모두 세우고 이번 바퀴를 마치기를 기다린다. <see cref="ReleaseAll"/>·<see cref="Dispose"/> 가 부른다.</summary>
    private void StopBackground()
    {
        _backgroundStopping = true;

        Thread[] threads;

        lock (_backgroundThreads) threads = [.. _backgroundThreads];

        var deadline = Environment.TickCount64 + BackgroundJoinMs;

        foreach (var thread in threads)
        {
            if (thread == Thread.CurrentThread) continue;

            var left = (int)Math.Max(0, deadline - Environment.TickCount64);

            if (!thread.Join(left)) Logger.Warn($"따로 도는 일이 {BackgroundJoinMs}ms 안에 안 끝났다: {thread.Name}");
        }
    }

    /// <summary>따로 도는 일이 세운 것 - 본 흐름의 <see cref="ThrowIfStopping"/> 이 본다.</summary>
    private void ThrowIfBackgroundStopped()
    {
        if (_backgroundGuard is { } message)
        {
            Outcome = LiveScriptOutcome.Guarded;
            GuardMessage = message;
            throw new ScriptGuardException(message);
        }

        if (_backgroundEnded)
        {
            Outcome = LiveScriptOutcome.Stopped;
            throw new ScriptStoppedException();
        }
    }
}
