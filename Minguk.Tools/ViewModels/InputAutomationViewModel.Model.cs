using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using DevExpress.Mvvm;
using ICSharpCode.AvalonEdit.Highlighting;
using Minguk.Tools.Helper;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Sequencing;
using Minguk.Tools.Input.Targets;

namespace Minguk.Tools.ViewModels;

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

    /// <summary>고른 종류의 줄을 캐럿 자리에 끼워 넣는다. 형식을 외우지 않아도 되게.</summary>
    public DelegateCommand<SequenceStepKind> DoAddStepCommand { get; private set; } = null!;

    public DelegateCommand DoResetStepsCommand { get; private set; } = null!;

    public DelegateCommand DoClearTestPadCommand { get; private set; } = null!;

    public DelegateCommand DoRefreshWindowsCommand { get; private set; } = null!;

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

    // ── 대상 창 ──────────────────────────────────────────────────────────
    //    PostMessage 만 쓴다. 진짜 커서를 안 움직이므로 "포커스를 가진 창" 에 기댈 수 없고,
    //    어느 창에 넣을지 정해 줘야 한다.

    public ObservableCollection<WindowTarget> WindowTargets { get; } = [];

    /// <summary>고른 대상 창. null 이면 마지막 좌표 아래의 창으로 간다(어디일지 알 수 없다).</summary>
    public WindowTarget? SelectedWindowTarget
    {
        get => GetProperty(() => SelectedWindowTarget);
        set => SetProperty(() => SelectedWindowTarget, value, UpdateSequenceText);
    }

    /// <summary>대상 창을 골라야 하는 경로인지. 아니면 그 줄을 아예 접는다.</summary>
    public bool NeedsWindowTarget => _service is not null && !_service.SupportsTyping;

    // ── 보낼 것 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 편집기에 든 스크립트. <b>이것이 원본이다</b> - 단계 목록은 여기서 읽어 낸다.
    /// </summary>
    /// <remarks>
    /// 예전에는 그리드에 줄을 담았는데, 한 줄 고치는 데 마우스가 여러 번 필요하고 통째로
    /// 복사하거나 남에게 주는 것이 안 됐다. 글로 두면 편집기가 이미 잘하는 일
    /// (되돌리기·여러 줄 선택·찾아 바꾸기·붙여넣기)이 전부 따라온다.
    /// 형식은 <see cref="SequenceScript"/> 에 적혀 있다.
    /// </remarks>
    public string? ScriptText
    {
        get => GetProperty(() => ScriptText);
        set => SetProperty(() => ScriptText, value, OnScriptTextChanged);
    }

    /// <summary>틀린 줄들을 한 번에 모아 둔 것. 없으면 null.</summary>
    public string? ScriptError
    {
        get => GetProperty(() => ScriptError);
        set => SetProperty(() => ScriptError, value, () => RaisePropertyChanged(nameof(HasScriptError)));
    }

    public bool HasScriptError => !string.IsNullOrEmpty(ScriptError);

    /// <summary>
    /// 고른 경로가 스크립트의 일부를 보내지 못할 때 무엇이 왜 빠지는지. 없으면 null.
    /// </summary>
    /// <remarks>
    /// 이것이 없으면 PostMessage 를 골라 둔 사용자는 "순서" 가 비고 실행해도 아무 일이
    /// 없는 이유를 알 수 없다. 조용히 빠지는 것을 화면에 적는다.
    /// </remarks>
    public string? PathWarning
    {
        get => GetProperty(() => PathWarning);
        set => SetProperty(() => PathWarning, value, () => RaisePropertyChanged(nameof(HasPathWarning)));
    }

    public bool HasPathWarning => !string.IsNullOrEmpty(PathWarning);

    /// <summary>
    /// 구문 강조 정의. 화면이 편집기의 SyntaxHighlighting 에 그대로 물린다.
    /// </summary>
    /// <remarks>
    /// 화면에서 직접 고르지 않고 ViewModel 이 들고 있는 이유는, 테마에 따라 다른 것을 줘야
    /// 하는데 그 판단이 XAML 에서 할 일이 아니기 때문이다.
    /// </remarks>
    public IHighlightingDefinition Highlighting { get; } = SequenceScriptHighlighting.Current;

    /// <summary>편집기 색. AvalonEdit 은 DevExpress 테마를 안 타므로 여기서 준다.</summary>
    public Brush EditorBackground { get; } = SequenceScriptHighlighting.Background;

    public Brush EditorForeground { get; } = SequenceScriptHighlighting.Foreground;

    public Brush EditorLineNumberForeground { get; } = SequenceScriptHighlighting.LineNumberForeground;

    public Brush EditorBorder { get; } = SequenceScriptHighlighting.Border;

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

    // ── 시험 입력란 ──────────────────────────────────────────────────────

    /// <summary>
    /// 보낸 입력을 받아 볼 자리.
    /// </summary>
    /// <remarks>
    /// 대상 창을 따로 띄우지 않고 여기를 클릭해 두면 바로 확인할 수 있다.
    /// 메모장 같은 남의 창을 쓰면 지난 내용이 섞여 있어 "이번에 무엇이 들어갔는지" 를
    /// 매번 전후로 재야 한다.
    ///
    /// 저장하지 않는다. 시험용으로 친 글이 다음에 열 때 남아 있을 이유가 없다.
    ///
    /// 도는 동안에도 잠기지 않는다 - 잠그면 입력을 받을 수 없어 있으나 마나다.
    /// </remarks>
    public string? TestPadText { get => GetProperty(() => TestPadText); set => SetProperty(() => TestPadText, value); }

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
