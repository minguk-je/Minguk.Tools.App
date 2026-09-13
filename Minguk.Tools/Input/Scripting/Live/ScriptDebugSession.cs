using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;

using DevExpress.Mvvm;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>실행이 어떻게 나아가는지.</summary>
public enum ScriptStepMode
{
    /// <summary>중단점에서만 멈춘다.</summary>
    Run,

    /// <summary>다음 줄에서 멈춘다(한 줄씩).</summary>
    Step
}

/// <summary>
/// 디버그 세션 - 중단점, 한 줄씩, 멈춘 자리와 그때의 변수. 화면과 엔진이 같이 본다.
/// </summary>
/// <remarks>
/// <b>두 스레드가 만나는 자리다.</b> 화면(UI 스레드)이 중단점을 켜고 계속·한 줄을 누르고, 엔진(스크립트 스레드)이
/// 줄마다 <see cref="ShouldBreak"/> 를 묻고 <see cref="Pause"/> 에서 기다린다. 중단점 목록은 화면용 컬렉션과
/// 스레드용 집합을 따로 두고 컬렉션이 바뀔 때 집합을 갈아 끼운다 - 스크립트 스레드가 UI 컬렉션을 읽으면 안 된다.
///
/// 언어마다 되는 것이 다르다. 자바스크립트(Jint)와 파이썬(sys.settrace)은 줄 단위로 멈추고 변수를 보여 준다.
/// C# 은 Roslyn 스크립트가 디버거 없이 돌아 멈추지 못한다 - 그때는 호출 로그(<see cref="ScriptCall"/>)로 본다.
/// 멈춘 채로 중지(Pause·중지 버튼)를 누르면 토큰이 깨워 그 자리에서 끝난다.
/// </remarks>
public sealed class ScriptDebugSession : ViewModelBase
{
    private readonly Action<Action> _onUi;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _resume = new(false);
    private HashSet<int> _breakLines = [];
    private volatile ScriptStepMode _mode;

    public ScriptDebugSession(Action<Action> onUi)
    {
        _onUi = onUi;

        Breakpoints.CollectionChanged += OnBreakpointsChanged;

        ContinueCommand = new DelegateCommand(Continue, () => IsPaused, false);
        StepCommand = new DelegateCommand(StepNext, () => IsPaused, false);
    }

    // ── 화면이 보는 것 ───────────────────────────────────────────────────

    /// <summary>중단점이 있는 줄 번호(1부터). 편집기 여백이 켜고 끈다. UI 스레드에서만 만진다.</summary>
    public ObservableCollection<int> Breakpoints { get; } = [];

    /// <summary>줄에서 멈춰 있는가.</summary>
    public bool IsPaused
    {
        get => GetProperty(() => IsPaused);
        private set => SetProperty(() => IsPaused, value, () =>
        {
            ContinueCommand.RaiseCanExecuteChanged();
            StepCommand.RaiseCanExecuteChanged();
        });
    }

    /// <summary>멈춘 줄. 안 멈춰 있으면 0. 편집기가 이 줄을 칠한다.</summary>
    public int PausedLine { get => GetProperty(() => PausedLine); private set => SetProperty(() => PausedLine, value); }

    /// <summary>멈춘 자리의 변수들. "이름=값" 한 줄씩.</summary>
    public string? Locals { get => GetProperty(() => Locals); private set => SetProperty(() => Locals, value); }

    /// <summary>"3번째 줄에서 멈춤" 같은 한 줄.</summary>
    public string? Status { get => GetProperty(() => Status); private set => SetProperty(() => Status, value); }

    public DelegateCommand ContinueCommand { get; }

    public DelegateCommand StepCommand { get; }

    /// <summary>이 언어가 줄 단위로 멈출 수 있는가. 화면이 한 줄·계속 버튼을 켜고 끄는 데 쓴다.</summary>
    public bool SupportsStepping { get => GetProperty(() => SupportsStepping); set => SetProperty(() => SupportsStepping, value); }

    /// <summary>멈췄다. UI 스레드에서 온다.</summary>
    public event EventHandler? Paused;

    /// <summary>지금 나아가는 방식. 시작 전에 <see cref="ScriptStepMode.Step"/> 으로 두면 첫 줄에서 멈춘다.</summary>
    public ScriptStepMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    /// <summary>중단점이 하나라도 있거나 한 줄씩이면 디버거를 켜야 한다. 아니면 엔진이 그냥 돈다 - 줄마다 묻는 값이 있다.</summary>
    public bool WantsDebugger
    {
        get
        {
            lock (_gate) return _mode == ScriptStepMode.Step || _breakLines.Count > 0;
        }
    }

    /// <summary>계속. 다음 중단점까지 간다.</summary>
    public void Continue()
    {
        _mode = ScriptStepMode.Run;
        _resume.Set();
    }

    /// <summary>한 줄. 다음 줄에서 다시 멈춘다.</summary>
    public void StepNext()
    {
        _mode = ScriptStepMode.Step;
        _resume.Set();
    }

    /// <summary>실행이 끝났다. 멈춤 표시를 지우고 한 줄씩 모드도 푼다.</summary>
    public void Reset()
    {
        _mode = ScriptStepMode.Run;
        _resume.Set();

        _onUi(() =>
        {
            IsPaused = false;
            PausedLine = 0;
            Locals = null;
            Status = null;
        });
    }

    // ── 엔진이 부르는 것 (스크립트 스레드) ───────────────────────────────

    /// <summary>이 줄에서 멈춰야 하나. 한 줄씩이면 늘, 아니면 중단점이 있을 때.</summary>
    public bool ShouldBreak(int line)
    {
        if (_mode == ScriptStepMode.Step) return true;

        lock (_gate) return _breakLines.Contains(line);
    }

    /// <summary>
    /// 멈춘다. 계속·한 줄을 누를 때까지 이 스레드를 붙든다. 중지되면 <see cref="OperationCanceledException"/>.
    /// </summary>
    public void Pause(int line, string? locals, CancellationToken token)
    {
        _resume.Reset();

        _onUi(() =>
        {
            PausedLine = line;
            Locals = locals;
            Status = $"{line}번째 줄에서 멈춤";
            IsPaused = true;
            Paused?.Invoke(this, EventArgs.Empty);
        });

        try
        {
            var woke = WaitHandle.WaitAny([_resume.WaitHandle, token.WaitHandle]);

            if (woke == 1) token.ThrowIfCancellationRequested();
        }
        finally
        {
            _onUi(() =>
            {
                IsPaused = false;
                PausedLine = 0;
                Status = _mode == ScriptStepMode.Step ? "한 줄 실행 중" : "실행 중";
            });
        }
    }

    private void OnBreakpointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var snapshot = Breakpoints.ToHashSet();

        lock (_gate) _breakLines = snapshot;
    }
}
