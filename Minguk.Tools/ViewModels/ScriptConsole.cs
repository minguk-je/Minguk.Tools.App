using System;
using System.Collections.Generic;
using System.Linq;

using DevExpress.Mvvm;

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
    private const int MaxLines = 100;

    private readonly Action<Action> _onUi;
    private readonly Queue<string> _lines = new();
    private readonly Dictionary<string, string> _watches = new();
    private readonly object _gate = new();

    public ScriptConsole(Action<Action> onUi) => _onUi = onUi;

    /// <summary>출력 줄들. 최근 것이 아래.</summary>
    public string? Text { get => GetProperty(() => Text); private set => SetProperty(() => Text, value); }

    /// <summary>"체력=120 · 대상=일반 봇" 처럼 이름표=값 한 줄.</summary>
    public string? Watches { get => GetProperty(() => Watches); private set => SetProperty(() => Watches, value); }

    public void Print(string line)
    {
        string text;

        lock (_gate)
        {
            _lines.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {line}");
            while (_lines.Count > MaxLines) _lines.Dequeue();
            text = string.Join(Environment.NewLine, _lines);
        }

        _onUi(() => Text = text);
    }

    public void Watch(string name, string value)
    {
        string text;

        lock (_gate)
        {
            _watches[name] = value;
            text = string.Join(" · ", _watches.Select(p => $"{p.Key}={p.Value}"));
        }

        _onUi(() => Watches = text);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _watches.Clear();
        }

        _onUi(() =>
        {
            Text = null;
            Watches = null;
        });
    }
}
