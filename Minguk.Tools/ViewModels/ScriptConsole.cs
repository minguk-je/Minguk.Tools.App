using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using DevExpress.Mvvm;

using Minguk.Tools.Input.Scripting.Live;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 스크립트의 출력 칸. <c>출력()</c> 이 적는 줄과 <c>보기()</c> 가 남기는 최신값.
/// </summary>
/// <remarks>
/// 디버그 창(설계 3단계)의 첫 조각이다. 지금은 글 두 덩이 - 줄들과 이름표=값 한 줄. 스크립트 스레드에서
/// 불리므로 화면에 닿는 것은 <c>onUi</c> 로 넘긴다. 줄은 마지막 100개만 둔다 - 반복문이 초당 수십 줄을 적는다.
/// </remarks>
public sealed class ScriptConsole : ViewModelBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private const int MaxLines = 100;
    private const int MaxCalls = 200;

    private readonly Action<Action> _onUi;
    private readonly Queue<string> _lines = new();
    private readonly Dictionary<string, string> _watches = new();
    private readonly Queue<ScriptCall> _calls = new();
    private readonly object _gate = new();

    // 화면 갱신은 칸마다 하나만 걸어 둔다. 반복문이 초당 수천 번 부르면 화면 스레드에 수천 개가 쌓여
    // 캡처·검출까지 밀렸다(실측). 걸린 갱신이 돌 때 그때의 최신 글을 만든다.
    private readonly UiCoalescer _callsUi;
    private readonly UiCoalescer _linesUi;
    private readonly UiCoalescer _watchesUi;

    // 파일 로그: 같은 호출이 같은 결과면 적지 않고 센다. 결과가 바뀔 때, 그리고 적어도 2초에 한 번은 적는다 -
    // 안 그러면 같은 호출만 되풀이하는 동안(클릭 700ms 같은) 로그에 아무것도 안 남아 뭘 했는지 못 본다(실측).
    private readonly Dictionary<string, (string Value, int Skipped, long Ticks)> _logged = new();

    private const int LogEveryMs = 2000;

    public ScriptConsole(Action<Action> onUi)
    {
        _onUi = onUi;
        _callsUi = new UiCoalescer(onUi);
        _linesUi = new UiCoalescer(onUi);
        _watchesUi = new UiCoalescer(onUi);
    }

    /// <summary>화면 갱신을 하나만 걸어 두는 것. 걸린 것이 돌기 전에 또 오면 버린다 - 돌 때 최신 글을 만든다.</summary>
    private sealed class UiCoalescer(Action<Action> onUi)
    {
        private int _pending;

        public void Post(Func<string?> build, Action<string?> apply)
        {
            if (Interlocked.Exchange(ref _pending, 1) == 1) return;

            onUi(() =>
            {
                // 만들기 전에 푼다. 만드는 사이에 온 것은 다음 갱신이 챙긴다.
                Interlocked.Exchange(ref _pending, 0);
                apply(build());
            });
        }
    }

    /// <summary>출력 줄들. 최근 것이 아래.</summary>
    public string? Text { get => GetProperty(() => Text); private set => SetProperty(() => Text, value); }

    /// <summary>"체력=120 · 대상=일반 봇" 처럼 이름표=값 한 줄.</summary>
    public string? Watches { get => GetProperty(() => Watches); private set => SetProperty(() => Watches, value); }

    /// <summary>호출 로그. 시각 · 부른 것 · 결과 · 걸린 시간. 최근 것이 아래.</summary>
    public string? CallsText { get => GetProperty(() => CallsText); private set => SetProperty(() => CallsText, value); }

    /// <summary>API 를 한 번 부를 때마다. 스크립트 스레드에서 온다. 파일 로그에도 남긴다 - 게임에 있는 사람은 이 칸을 못 보고, 나중에 봐야 한다.</summary>
    public void Trace(ScriptCall call)
    {
        string? line = null;

        lock (_gate)
        {
            _calls.Enqueue(call);
            while (_calls.Count > MaxCalls) _calls.Dequeue();

            var value = $"{call.Arguments}|{call.Result}";
            var now = Environment.TickCount64;
            _logged.TryGetValue(call.Name, out var last);

            if (last.Value == value && now - last.Ticks < LogEveryMs)
            {
                _logged[call.Name] = (value, last.Skipped + 1, last.Ticks);
            }
            else
            {
                line = last.Skipped > 0 ? $"호출: {call}  (앞의 같은 {call.Name} {last.Skipped}번 건너뜀)" : $"호출: {call}";
                _logged[call.Name] = (value, 0, now);
            }
        }

        if (line is not null) Logger.Debug(line);

        _callsUi.Post(() => { lock (_gate) return string.Join(Environment.NewLine, _calls); }, text => CallsText = text);
    }

    /// <summary>실행을 새로 시작할 때. 호출 로그만 비운다 - 출력은 지난 실행과 견주고 싶을 수 있다.</summary>
    public void ClearCalls()
    {
        lock (_gate)
        {
            _calls.Clear();
            _logged.Clear();
        }

        _onUi(() => CallsText = null);
    }

    public void Print(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {line}");
            while (_lines.Count > MaxLines) _lines.Dequeue();
        }

        _linesUi.Post(() => { lock (_gate) return string.Join(Environment.NewLine, _lines); }, text => Text = text);
    }

    public void Watch(string name, string value)
    {
        lock (_gate) _watches[name] = value;

        _watchesUi.Post(() => { lock (_gate) return string.Join(" · ", _watches.Select(p => $"{p.Key}={p.Value}")); }, text => Watches = text);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _watches.Clear();
            _calls.Clear();
        }

        _onUi(() =>
        {
            Text = null;
            Watches = null;
            CallsText = null;
        });
    }
}
