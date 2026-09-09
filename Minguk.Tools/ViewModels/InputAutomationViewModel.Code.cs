using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 스크립트에서 읽어 둔 계획으로 시퀀스를 만든다.
    /// 시작할 때 한 번 굳혀 두므로, 도는 도중에 글을 고쳐도 그 바퀴에는 영향이 없다.
    /// </summary>
    private InputSequence BuildSequence() => _plan.Build(_service!, HoldTimeMs);

    // ── 스크립트 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 글이 바뀔 때마다 읽어 계획으로 만든다.
    /// </summary>
    /// <remarks>
    /// 틀린 줄이 있어도 나머지는 그대로 계획에 담는다. 오타 한 줄 때문에 순서 미리보기가
    /// 통째로 비면 무엇을 고쳐야 하는지 오히려 알기 어렵다.
    /// 대신 실행은 막는다 - 반쪽짜리 시퀀스가 나가는 것이 더 나쁘다.
    /// </remarks>
    private void OnScriptTextChanged() => Guard(() =>
    {
        SequenceScript.TryParse(ScriptText, out _plan, out var errors);

        ScriptError = errors.Count == 0
            ? null
            : string.Join("   ·   ", errors.Select(e => e.ToString()));

        UpdateSequenceText();
    });

    /// <summary>
    /// 고른 종류의 본보기 줄을 캐럿이 있는 줄 아래에 끼운다.
    /// </summary>
    /// <remarks>
    /// 형식을 외우게 하지 않으려는 것이다. 편집기를 아직 못 잡았으면 끝에 붙인다 -
    /// 자리가 덜 좋을 뿐 하려던 일은 된다.
    /// </remarks>
    private void DoAddStep(SequenceStepKind kind) => Guard(() =>
    {
        var line = SequenceScript.ToText(new SequencePlan { Steps = [Sample(kind)] });

        if (_editor is null)
        {
            ScriptText = string.IsNullOrEmpty(ScriptText) ? line : ScriptText + Environment.NewLine + line;
            return;
        }

        InsertLine(line);
    });

    /// <summary>본보기 줄에 쓸 값. 이동만 지금 커서 자리를 쓴다 - (0,0) 은 쓸 일이 거의 없다.</summary>
    private SequenceStepDefinition Sample(SequenceStepKind kind)
    {
        var step = new SequenceStepDefinition { Kind = kind };

        if (kind == SequenceStepKind.Type) step.Text = "안녕하세요";

        if (kind == SequenceStepKind.MoveTo && _adapter?.GetCursorPosition() is { } p)
        {
            step.X = p.X;
            step.Y = p.Y;
        }

        return step;
    }

    private void InsertLine(string line)
    {
        var document = _editor!.Document;

        if (document.TextLength == 0)
        {
            document.Insert(0, line);
            _editor.CaretOffset = line.Length;
        }
        else
        {
            var at = document.GetLineByOffset(_editor.CaretOffset).EndOffset;
            var inserted = Environment.NewLine + line;

            document.Insert(at, inserted);
            _editor.CaretOffset = at + inserted.Length;
        }

        // 넣자마자 이어서 고칠 수 있게 편집기로 초점을 돌린다.
        _editor.Focus();
    }

    private void DoResetSteps() => Guard(() => ScriptText = SequenceScript.ToText(SequencePlan.CreateDefault()));

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

        // 조합키를 쓰는 이유
        //   RegisterHotKey 는 시스템 전역이다. 맨 F5 로 잡으면 이 화면이 열려 있는 동안
        //   모든 앱에서 F5 를 빼앗는다 - Visual Studio 의 디버그 시작, 브라우저 새로고침까지.
        //   Ctrl+Alt 조합은 다른 프로그램과 부딪힐 일이 훨씬 적다.
        const ModifierKeys Combo = ModifierKeys.Control | ModifierKeys.Alt;

        (string Label, Key Key, Action Action)[] bindings =
        [
            ("Ctrl+Alt+F5 1회", Key.F5, () => { if (IsIdle) DoRunOnce(); }),
            ("Ctrl+Alt+F6 반복/중지", Key.F6, ToggleLoop),
            ("Ctrl+Alt+F4 좌표 담기", Key.F4, PickCursorPosition)
        ];

        var live = new List<string>();
        var failed = new List<string>();

        foreach (var (label, key, action) in bindings)
        {
            if (_hotkeys.TryRegister(key, Combo, action)) live.Add(label);
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
    /// 지금 커서 자리를 이동 줄로 만들어 끼운다.
    /// </summary>
    /// <remarks>
    /// 버튼으로 두면 쓸모가 없다. 버튼을 누르는 순간 커서가 그 버튼 위에 있기 때문이다.
    /// 대상 창 위에 커서를 둔 채 눌러야 하므로 단축키로만 제공한다.
    /// </remarks>
    private void PickCursorPosition() => Guard(() =>
    {
        if (IsRunning || _adapter is null) return;

        if (_adapter.GetCursorPosition() is not { } p) return;

        DoAddStep(SequenceStepKind.MoveTo);
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
    /// 시퀀스를 굳히고 돌리기 시작한다. 실제 전송은 <see cref="RunAsync"/> 가 백그라운드로 넘긴다.
    /// </summary>
    private void Start(bool loop) => Guard(() =>
    {
        if (_service is null || IsRunning) return;

        if (HasScriptError)
        {
            // 반쪽짜리 시퀀스가 나가는 것보다 안 나가는 것이 낫다.
            MessengerUtility.SendMainMessage("스크립트에 고칠 줄이 있습니다.");
            return;
        }

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

    /// <summary>
    /// 대기를 마치고 시퀀스를 돌린다. 돌리는 일은 스레드풀로 넘긴다.
    /// </summary>
    /// <remarks>
    /// <b>왜 Task.Run 인가</b>
    ///
    /// 이 메서드는 UI 스레드에서 시작되고, 안쪽 await 들이 UI 의 SynchronizationContext 를
    /// 잡는다. 그냥 두면 전송 전체가 UI 스레드에서 돈다. 검증 하네스는 일부러 Task.Run 으로
    /// 감싸 돌리는데, "실제 앱과 같은 조건" 이라고 적어 두고 정작 앱이 그렇지 않았다.
    ///
    /// <b>다만 이것은 눈에 보이는 버그를 고친 것이 아니다.</b> 단계 간격 1ms 로 26자를 보내는
    /// 조건에서 대상이 메모장이든 이 앱 자신의 입력란이든(= 보내는 스레드와 받는 창의 UI
    /// 스레드가 같은 경우) 유실은 없었다. 단계마다 await 이 있어 그 사이에 메시지 펌프가
    /// 도는 덕이다. UI 스레드가 무언가에 막혔을 때 전송 간격이 끌려가지 않도록 떼어 놓는,
    /// 예방에 가까운 변경이다.
    ///
    /// 대기(<see cref="CountDownAsync"/>)와 횟수 세기는 UI 스레드에 남겨 둔다 - 화면에 바로
    /// 비치는 것들이고 넘겨 봐야 득이 없다. 진행 보고는 <see cref="Progress{T}"/> 가 UI
    /// 컨텍스트를 잡아 두므로 그대로 UI 로 온다.
    ///
    /// 취소 토큰은 <see cref="Task.Run(Func{Task}, CancellationToken)"/> 에 넘기지 않는다.
    /// 시작 전에 이미 취소돼 있으면 그 오버로드는 예외를 던지는데, 여기서는 취소가 정상
    /// 경로다. <see cref="SequenceRunner"/> 가 토큰을 직접 보고 조용히 멈춘다.
    /// </remarks>
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
                    var finished = await Task.Run(
                        () => SequenceRunner.RunOnceAsync(steps, IntervalMs, progress, _service!.Jitter, token));

                    if (!finished) break;

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
