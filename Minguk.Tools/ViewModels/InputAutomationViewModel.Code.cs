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
using Minguk.Tools.Input.Targets;

namespace Minguk.Tools.ViewModels;

/// <summary>커맨드가 실제로 하는 일.</summary>
public partial class InputAutomationViewModel
{
    /// <summary>
    /// 고른 경로를 끼운다. 쓸 수 없으면 SendInput 으로 내려앉되, 무엇이 왜 밀려났는지 표시한다.
    /// </summary>
    private void ApplyBackend() => Guard(() =>
    {
        // 고른 대상 창을 넘긴다. 창이 닫혔으면 IntPtr.Zero 를 주어 어댑터가 예전처럼
        // "마지막 좌표 아래 창" 으로 되돌아가게 한다 - 죽은 핸들에 보내면 조용히 사라진다.
        var selection = InputAdapterFactory.CreateWithFallback(
            SelectedInputBackend, InputBackend.SendInput, ResolveTargetWindow);

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

        // 문구가 사실과 어긋나면 읽는 사람이 잘못 믿는다. 두 번 겪었다 -
        // "글자 입력 불가" 라고 적어 두었다가 PostMessage 로는 아무것도 못 넣는 줄 알았고,
        // 다음엔 "한글 불가" 라고 적어 두었다가 한글도 되는 것을 오래 몰랐다.
        AdapterStatus = $"{_adapter.Name} · 한글·영문 입력"
                      + $" · 한/영 전환 {(_service.SupportsHangulToggle ? "가능" : "불가")}"
                      + $" · 진짜 커서 {(_adapter.GetCursorPosition() is null ? "안 움직임" : "움직임")}";
    });

    private void OnSelectedInputBackendChanged()
    {
        if (_adapter is null) return;   // 아직 화면이 뜨기 전이면 OnLoaded 가 끼운다

        ApplyBackend();
        UpdateSequenceText();

        RaisePropertyChanged(nameof(NeedsWindowTarget));

        // 대상 창이 필요해졌는데 목록이 비어 있으면 한 번 채워 준다.
        // 새로고침을 눌러야만 보이면 왜 비어 있는지 알기 어렵다.
        if (NeedsWindowTarget && WindowTargets.Count == 0) DoRefreshWindows();
    }

    // ── 대상 창 ──────────────────────────────────────────────────────────

    /// <summary>어댑터에 넘길 창 핸들. 고른 것이 없거나 이미 닫혔으면 0.</summary>
    private IntPtr ResolveTargetWindow()
    {
        if (SelectedWindowTarget is not { } target) return IntPtr.Zero;

        if (_windows?.IsAlive(target.Handle) == true) return target.Handle;

        return IntPtr.Zero;
    }

    /// <summary>
    /// 지금 떠 있는 창들을 다시 훑는다.
    /// </summary>
    /// <remarks>
    /// 고르고 있던 창이 그대로 있으면 그 선택을 지킨다. 목록을 새로 만들 때마다 선택이
    /// 풀리면 창 하나 늘었다고 다시 골라야 한다.
    /// </remarks>
    private void DoRefreshWindows() => Guard(() =>
    {
        _windows ??= WindowTargetAdapterFactory.Create();

        var chosen = SelectedWindowTarget?.Handle;

        WindowTargets.Clear();

        // 이 앱 자신도 목록에 둔다. 화면 아래 시험 입력란으로 받아 보는 것이 흔한 쓰임이다.
        foreach (var window in _windows.List()) WindowTargets.Add(window);

        SelectedWindowTarget = WindowTargets.FirstOrDefault(w => w.Handle == chosen) is { Handle: not 0 } kept
            ? kept
            : null;

        CurrentStep = $"창 {WindowTargets.Count}개를 찾았다";
    });

    /// <summary>
    /// 커서 아래 창을 대상으로 집는다.
    /// </summary>
    /// <remarks>
    /// 목록에서 고르는 것만으로는 부족하다. 제목이 같은 창이 여럿이면 어느 것인지 알 수 없고,
    /// 대상 창을 눈으로 보면서 집는 편이 확실하다. 좌표 담기(F4)와 같은 이유로 단축키다.
    /// </remarks>
    private void PickWindowUnderCursor() => Guard(() =>
    {
        if (IsRunning || _adapter is null) return;

        _windows ??= WindowTargetAdapterFactory.Create();

        if (ScreenCursor.TryGetPosition() is not { } p)
        {
            CurrentStep = "커서 자리를 알 수 없어 창을 집지 못했다";
            return;
        }

        if (_windows.FromPoint(p.X, p.Y) is not { } window)
        {
            CurrentStep = $"({p.X}, {p.Y}) 아래에 창이 없다";
            return;
        }

        if (WindowTargets.All(w => w.Handle != window.Handle)) WindowTargets.Add(window);

        SelectedWindowTarget = window;
        CurrentStep = $"대상 창을 집었다 - {window.Display}";
    });

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
            : string.Join(Environment.NewLine, errors.Select(e => e.ToString()));

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

        if (kind == SequenceStepKind.MoveTo && ScreenCursor.TryGetPosition() is { } p)
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
        PathWarning = DescribeDroppedSteps();
    });

    /// <summary>
    /// 고른 경로가 못 보내는 단계가 몇 개인지 세어 문장으로 만든다. 없으면 null.
    /// </summary>
    private string? DescribeDroppedSteps()
    {
        if (_service is null || _adapter is null) return null;

        var lines = new List<string>();

        // 창 메시지 경로는 대상 창의 "포커스를 가진 컨트롤" 로 들어간다. 버튼을 누르면
        // 그 순간 포커스가 버튼으로 옮겨 가서 글자가 갈 곳을 잃는다. 실측으로 겪었다.
        // 커널 입력 큐를 쓰는 경로(SendInput·Interception)는 이 문제가 없다.
        // ── 창 메시지 경로에만 해당하는 것 ──
        //    한 번에 판단한다. 능력별로 조기 반환을 두었더니 뒤에 있던 안내가 통째로 빠졌다.
        if (!_adapter.RequiresForegroundTarget)
        {
            // 대상 창의 "포커스를 가진 컨트롤" 로 들어간다. 버튼을 누르면 그 순간 포커스가
            // 버튼으로 옮겨 가서 글자가 갈 곳을 잃는다. 실측으로 겪었다.
            lines.Add("버튼으로 시작하면 포커스가 버튼으로 옮겨 가 입력이 갈 곳을 잃습니다 - "
                      + "대상에 포커스를 둔 채 F11(1회) · F12(반복) 으로 시작하세요.");

            // 대상 창을 안 고르면 마지막 좌표 아래 창으로 간다. 나가긴 나가는데 어디로
            // 갔는지 알 수 없어서, "끝남" 을 보고 됐다고 믿게 된다. 그 전에 말해 준다.
            if (SelectedWindowTarget is null && _plan.Steps.Count > 0)
                lines.Add("대상 창을 고르지 않아 마지막 좌표 아래의 창으로 나갑니다 - "
                          + "어디로 갈지 정하려면 창을 고르세요.");
        }

        // ── 이 경로가 못 보내는 단계 ──
        if (!_service.SupportsHangulToggle)
        {
            var dropped = _plan.Steps.Count(SequenceStepKinds.NeedsScanCode);

            if (dropped > 0)
                lines.Add($"{_adapter.Name} 경로는 한/영 전환을 하지 못해 한/영 단계 {dropped}개가 빠집니다. "
                          + "글자는 한글까지 그대로 나갑니다.");
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

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

        // RegisterHotKey 는 시스템 전역이다. 맨 키로 잡으면 이 화면이 열려 있는 동안
        // 모든 앱에서 그 키를 빼앗는다 - F11 은 브라우저 전체화면, F12 는 개발자 도구,
        // F3 은 흔한 "다음 찾기" 다. 화면을 닫으면 돌려준다(ReleaseResources 가 푼다).
        (string Label, Key Key, ModifierKeys Modifiers, Action Action)[] bindings =
        [
            ("F11 1회", Key.F11, ModifierKeys.None, () => { if (IsIdle) DoRunOnce(); }),
            ("F12 반복/중지", Key.F12, ModifierKeys.None, ToggleLoop),
            ("F4 좌표 담기", Key.F4, ModifierKeys.None, PickCursorPosition),
            ("F3 대상 창 집기", Key.F3, ModifierKeys.None, PickWindowUnderCursor)
        ];

        var live = new List<string>();
        var failed = new List<string>();

        foreach (var (label, key, modifiers, action) in bindings)
        {
            if (_hotkeys.TryRegister(key, modifiers, action)) live.Add(label);
            else failed.Add(label);
        }

        HotkeyStatus = failed.Count == 0
            ? string.Join(" · ", live)
            : string.Join(" · ", live) + $"  (다른 프로그램이 쥐고 있음: {string.Join(", ", failed)})";

        Logger.Debug($"단축키 등록: 성공 {live.Count}, 실패 {failed.Count}");
    });

    /// <summary>F12 는 하나로 시작과 중지를 겸한다. 도는 중에 다시 누르면 멈춘다.</summary>
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

        // 어댑터가 아니라 OS 에게 묻는다. 창 메시지 경로는 커서를 안 움직이므로
        // GetCursorPosition 이 null 인데, 좌표를 집는 것은 그것과 상관없는 일이다.
        if (ScreenCursor.TryGetPosition() is not { } p)
        {
            CurrentStep = "커서 자리를 읽지 못했다";
            return;
        }

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
            // 적은 것이 없는 것과, 적었는데 이 경로가 못 보내는 것은 다르다.
            // 뭉뚱그리면 사용자는 자기가 적은 것이 왜 안 나가는지 알 수 없다.
            MessengerUtility.SendMainMessage(PathWarning ?? "보낼 것이 하나도 적혀 있지 않습니다.");
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
