using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Minguk.Base.Utilities;
using System.Windows.Input;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Hotkeys;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.ViewModels;

/// <summary>커맨드가 실제로 하는 일.</summary>
public partial class InputAutomationViewModel
{
    /// <summary>
    /// 고른 경로를 끼운다. 쓸 수 없으면 SendInput 으로 내려앉되, 무엇이 왜 밀려났는지 표시한다.
    /// </summary>
    private void ApplyBackend() => Guard(() =>
    {
        var selection = InputAdapterFactory.CreateWithFallback(
            SelectedInputBackend, InputBackend.SendInput, () => IntPtr.Zero);

        // 이전 어댑터를 버리지 않으면 드라이버 컨텍스트가 그대로 샌다.
        _adapter?.Dispose();
        _adapter = selection.Adapter;
        _service = new InputService(_adapter) { JitterMs = JitterMs };

        if (selection.FellBack)
        {
            // 조용히 다른 경로로 보내면 안 된다. 콤보 선택도 실제 경로에 맞춘다.
            SelectedInputBackend = InputBackend.SendInput;
            AdapterStatus = $"{selection.FellBackFrom} 을 쓸 수 없다 - {selection.Reason} → {_adapter.Name} 으로 바꿨다";
            Logger.Warn($"{selection.FellBackFrom} 사용 불가: {selection.Reason}");
            return;
        }

        AdapterStatus = $"{_adapter.Name} · 글자 입력 {(_service.SupportsTyping ? "가능" : "불가")}"
                      + $" · 진짜 커서 {(_adapter.GetCursorPosition() is null ? "안 움직임" : "움직임")}";
    });

    private void OnSelectedInputBackendChanged()
    {
        if (_adapter is null) return;   // 아직 화면이 뜨기 전이면 OnLoaded 가 끼운다

        ApplyBackend();
        UpdateSequenceText();
    }

    private void OnIsRunningChanged()
    {
        RaisePropertyChanged(nameof(IsIdle));

        DoRunOnceCommand.RaiseCanExecuteChanged();
        DoStartLoopCommand.RaiseCanExecuteChanged();
        DoStopCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 지금 설정으로 시퀀스를 만든다.
    /// 시작할 때 한 번 굳혀 두므로, 도는 도중에 설정이 바뀌어도 그 바퀴에는 영향이 없다.
    /// </summary>
    private InputSequence BuildSequence()
    {
        var sequence = new InputSequence(_service!, HoldTimeMs);

        if (!string.IsNullOrEmpty(Text))
        {
            if (IncludeEnter) sequence.Enter();
            sequence.Type(Text);
            if (IncludeEnter) sequence.Enter();
        }

        if (IncludeClick) sequence.Click();
        if (IncludeMove) sequence.MoveTo(MoveX, MoveY);
        if (IncludeScroll) sequence.Scroll(ScrollNotches);

        return sequence;
    }

    private void UpdateSequenceText() => Guard(() =>
    {
        if (_service is null) return;

        SequenceText = BuildSequence().Describe();
    });

    /// <summary>
    /// 다른 창이 앞에 있을 때도 시작·중지할 수 있게 단축키를 건다.
    /// </summary>
    /// <remarks>
    /// 이 화면의 버튼은 이 앱이 앞에 있어야 누를 수 있는데, 입력은 대상 창이 앞에 있어야
    /// 들어간다. 둘을 동시에 만족할 수 없어서 앱 밖에서 누를 수단이 필요하다.
    ///
    /// 등록은 흔하게 실패한다(다른 프로그램이 같은 조합을 먼저 쥐고 있으면).
    /// 조용히 넘기면 사용자는 눌러도 아무 일이 없는 이유를 알 수 없으므로 화면에 적는다.
    /// </remarks>
    private void RegisterHotkeys() => Guard(() =>
    {
        _hotkeys = GlobalHotkeyAdapterFactory.Create();

        (string Label, Key Key, Action Action)[] bindings =
        [
            ("F5 1회", Key.F5, () => { if (IsIdle) DoRunOnce(); }),
            ("F6 반복/중지", Key.F6, ToggleLoop),
            ("F4 좌표 담기", Key.F4, PickCursorPosition)
        ];

        var live = new List<string>();
        var failed = new List<string>();

        foreach (var (label, key, action) in bindings)
        {
            if (_hotkeys.TryRegister(key, ModifierKeys.None, action)) live.Add(label);
            else failed.Add(label);
        }

        HotkeyStatus = failed.Count == 0
            ? string.Join(" · ", live)
            : string.Join(" · ", live) + $"  (다른 프로그램이 쥐고 있음: {string.Join(", ", failed)})";

        Logger.Debug($"단축키 등록: 성공 {live.Count}, 실패 {failed.Count}");
    });

    /// <summary>F6 은 하나로 시작과 중지를 겸한다. 도는 중에 다시 누르면 멈춘다.</summary>
    private void ToggleLoop()
    {
        if (IsRunning) DoStop();
        else DoStartLoop();
    }

    /// <summary>
    /// 지금 커서 자리를 이동 좌표에 담는다.
    /// </summary>
    /// <remarks>
    /// 버튼으로 두면 쓸모가 없다. 버튼을 누르는 순간 커서가 그 버튼 위에 있기 때문이다.
    /// 대상 창 위에 커서를 둔 채 눌러야 하므로 단축키로만 제공한다.
    /// </remarks>
    private void PickCursorPosition() => Guard(() =>
    {
        if (IsRunning || _adapter is null) return;

        if (_adapter.GetCursorPosition() is not { } p) return;

        MoveX = p.X;
        MoveY = p.Y;
        CurrentStep = $"좌표 담음 ({p.X}, {p.Y})";
    });

    private void DoRunOnce() => Start(loop: false);

    private void DoStartLoop() => Start(loop: true);

    private void DoStop() => Guard(() =>
    {
        _cts?.Cancel();
        CurrentStep = "중지 요청";
    });

    /// <summary>
    /// 시퀀스를 굳히고 백그라운드에서 돌린다.
    /// </summary>
    /// <remarks>
    /// 보내는 스레드와 받는 창의 UI 스레드가 같으면 연속 전송한 키가 유실된다.
    /// 대상이 다른 창이라도 이 앱의 UI 스레드를 막으면 진행 표시가 멎으므로 백그라운드로 돌린다.
    /// </remarks>
    private void Start(bool loop) => Guard(() =>
    {
        if (_service is null || IsRunning) return;

        var steps = BuildSequence().Steps;

        if (steps.Count == 0)
        {
            MessengerUtility.SendMainMessage("보낼 것이 하나도 선택되지 않았습니다.");
            return;
        }

        _service.JitterMs = JitterMs;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsRunning = true;
        LoopCount = 0;

        // Progress<T> 는 만든 스레드(여기서는 UI)로 보고를 넘겨 준다.
        var progress = new Progress<string>(symbol => CurrentStep = symbol);

        _ = RunAsync(steps, loop, progress, _cts.Token);
    });

    private async Task RunAsync(
        System.Collections.Generic.IReadOnlyList<InputStep> steps,
        bool loop,
        IProgress<string> progress,
        CancellationToken token)
    {
        await GuardAsync(async () =>
        {
            try
            {
                if (!await CountDownAsync(token)) return;

                do
                {
                    if (!await SequenceRunner.RunOnceAsync(steps, IntervalMs, progress, _service!.Jitter, token))
                        break;

                    LoopCount++;
                }
                while (loop && !token.IsCancellationRequested && (MaxLoops <= 0 || LoopCount < MaxLoops));
            }
            finally
            {
                IsRunning = false;
                CurrentStep = token.IsCancellationRequested ? "중지함" : "끝남";
                MessengerUtility.SendMainMessage($"입력 자동화를 마쳤습니다. ({LoopCount}회)");
            }
        });
    }

    /// <summary>
    /// 시작 전 대기. 이 사이에 대상 창을 앞으로 가져와야 한다.
    /// 남은 시간을 표시해 주지 않으면 사용자가 언제 옮겨야 할지 알 수 없다.
    /// </summary>
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
