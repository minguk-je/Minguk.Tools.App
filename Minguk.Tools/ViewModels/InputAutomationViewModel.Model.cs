using System;
using System.Collections.ObjectModel;
using System.Linq;
using DevExpress.Mvvm;
using Minguk.Tools.Input;

using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 콤보에 한글 이름으로 보이게 하려고 값과 이름을 짝지어 둔 것.
/// </summary>
/// <remarks>
/// 열거형을 그대로 물리면 목록이 영문 식별자로 나온다. 표시 이름을 열거형에 붙일 수도 있지만
/// (Description 특성 같은 것) 그러면 읽는 쪽마다 리플렉션을 돌려야 한다.
/// </remarks>
public sealed record NamedValue<T>(T Value, string Name);

/// <summary>
/// 화면이 들고 있는 상태와 커맨드 선언만 모은 쪽.
/// XAML 을 열지 않아도 이 파일만 보면 무엇을 바인딩하는지 알 수 있게 둔다.
/// </summary>
public partial class InputAutomationViewModel
{
    // ── 커맨드 ───────────────────────────────────────────────────────────

    public DelegateCommand DoRunOnceCommand { get; private set; } = null!;

    public DelegateCommand DoStartLoopCommand { get; private set; } = null!;

    public DelegateCommand DoStopCommand { get; private set; } = null!;

    /// <summary>고른 종류의 단계를 끝에 담는다.</summary>
    public DelegateCommand<SequenceStepKind> DoAddStepCommand { get; private set; } = null!;

    public DelegateCommand DoRemoveStepCommand { get; private set; } = null!;

    public DelegateCommand DoDuplicateStepCommand { get; private set; } = null!;

    public DelegateCommand DoMoveStepUpCommand { get; private set; } = null!;

    public DelegateCommand DoMoveStepDownCommand { get; private set; } = null!;

    public DelegateCommand DoResetStepsCommand { get; private set; } = null!;

    // ── 입력 경로 ────────────────────────────────────────────────────────

    public ObservableCollection<InputBackend> InputBackends { get; set; }
        = new((InputBackend[])Enum.GetValues(typeof(InputBackend)));

    public InputBackend SelectedInputBackend
    {
        get => GetProperty(() => SelectedInputBackend);
        set => SetProperty(() => SelectedInputBackend, value, OnSelectedInputBackendChanged);
    }

    /// <summary>지금 어느 경로로 나가는지, 못 쓰면 왜 못 쓰는지.</summary>
    public string? AdapterStatus { get => GetProperty(() => AdapterStatus); set => SetProperty(() => AdapterStatus, value); }

    // ── 보낼 것 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 위에서 아래로 이 순서대로 나간다. 그리드에서 직접 고치고, 버튼으로 넣고 빼고 옮긴다.
    /// </summary>
    /// <remarks>
    /// 예전에는 체크박스 몇 개로 켜고 껐는데, 그러면 순서가 코드에 박혀서 바꿀 수가 없었다
    /// (글자 → 클릭 → 이동 → 휠 고정). "이동한 다음 클릭" 같은 흔한 것도 못 했다.
    /// </remarks>
    public ObservableCollection<SequenceStepDefinition> Steps { get; }
        = [];

    /// <summary>그리드에서 고른 줄. 삭제·복제·순서 바꾸기가 이것을 본다.</summary>
    public SequenceStepDefinition? SelectedStep
    {
        get => GetProperty(() => SelectedStep);
        set => SetProperty(() => SelectedStep, value, OnSelectedStepChanged);
    }

    /// <summary>단계 종류 콤보에 물린다.</summary>
    public ObservableCollection<NamedValue<SequenceStepKind>> StepKinds { get; }
        = [.. Enum.GetValues<SequenceStepKind>().Select(k => new NamedValue<SequenceStepKind>(k, SequenceStepKindNames.Of(k)))];

    /// <summary>마우스 버튼 콤보에 물린다.</summary>
    public ObservableCollection<NamedValue<MouseButton>> MouseButtons { get; }
        = [.. Enum.GetValues<MouseButton>().Select(b => new NamedValue<MouseButton>(b, b switch
        {
            MouseButton.Right => "우클릭",
            MouseButton.Middle => "휠클릭",
            _ => "좌클릭"
        }))];

    // ── 고른 단계가 쓰는 칸만 보여 주려고 화면이 묻는 것 ───────────────────
    //    종류마다 쓰는 칸이 다르다. 안 쓰는 칸까지 늘 띄워 두면 무엇을 채워야 하는지 흐려진다.

    public bool HasSelectedStep => SelectedStep is not null;

    public bool IsTypeStep => SelectedStep?.Kind == SequenceStepKind.Type;

    public bool IsClickStep => SelectedStep?.Kind == SequenceStepKind.Click;

    public bool IsMoveStep => SelectedStep?.Kind == SequenceStepKind.MoveTo;

    public bool IsScrollStep => SelectedStep?.Kind == SequenceStepKind.Scroll;

    public bool IsWaitStep => SelectedStep?.Kind == SequenceStepKind.Wait;

    // ── 타이밍 ───────────────────────────────────────────────────────────

    /// <summary>키·버튼을 누르고 있는 시간.</summary>
    public int HoldTimeMs { get => GetProperty(() => HoldTimeMs); set => SetProperty(() => HoldTimeMs, value); }

    /// <summary>단계 사이 대기.</summary>
    public int IntervalMs { get => GetProperty(() => IntervalMs); set => SetProperty(() => IntervalMs, value); }

    /// <summary>대기 시간에 얹을 무작위 편차(±ms). 0 이면 편차 없음.</summary>
    public int JitterMs { get => GetProperty(() => JitterMs); set => SetProperty(() => JitterMs, value); }

    /// <summary>
    /// 시작 버튼을 누르고 실제로 보내기까지 기다리는 시간.
    /// 이 사이에 대상 창을 앞으로 가져와야 한다 - 안 그러면 입력이 이 앱으로 들어온다.
    /// </summary>
    public int StartDelaySeconds { get => GetProperty(() => StartDelaySeconds); set => SetProperty(() => StartDelaySeconds, value); }

    /// <summary>반복 최대 횟수. 0 이면 중지할 때까지.</summary>
    public int MaxLoops { get => GetProperty(() => MaxLoops); set => SetProperty(() => MaxLoops, value); }

    // ── 표시 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 어떤 단축키가 살아 있는지. 다른 프로그램이 쥐고 있어 등록에 실패한 것도 여기 적는다.
    /// </summary>
    public string? HotkeyStatus { get => GetProperty(() => HotkeyStatus); set => SetProperty(() => HotkeyStatus, value); }

    /// <summary>"Enter → 안 → 녕 → 좌클릭" 처럼 무엇이 어떤 순서로 나가는지.</summary>
    public string? SequenceText { get => GetProperty(() => SequenceText); set => SetProperty(() => SequenceText, value); }

    /// <summary>지금 어느 단계인지.</summary>
    public string? CurrentStep { get => GetProperty(() => CurrentStep); set => SetProperty(() => CurrentStep, value); }

    public int LoopCount { get => GetProperty(() => LoopCount); set => SetProperty(() => LoopCount, value); }

    /// <summary>도는 중에는 설정을 잠근다. 도중에 바뀌면 중간에 엉뚱한 것이 나간다.</summary>
    public bool IsRunning
    {
        get => GetProperty(() => IsRunning);
        set => SetProperty(() => IsRunning, value, OnIsRunningChanged);
    }

    /// <summary>설정 패널의 IsEnabled 에 물린다.</summary>
    public bool IsIdle => !IsRunning;
}
