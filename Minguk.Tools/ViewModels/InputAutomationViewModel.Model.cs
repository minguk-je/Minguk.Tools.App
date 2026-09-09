using System;
using System.Collections.ObjectModel;
using DevExpress.Mvvm;
using Minguk.Tools.Input;

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

    /// <summary>순서대로 한 글자씩 누른다. 한글·영문·숫자·문장부호를 다룬다.</summary>
    public string? Text
    {
        get => GetProperty(() => Text);
        set => SetProperty(() => Text, value, UpdateSequenceText);
    }

    public bool IncludeEnter
    {
        get => GetProperty(() => IncludeEnter);
        set => SetProperty(() => IncludeEnter, value, UpdateSequenceText);
    }

    public bool IncludeClick
    {
        get => GetProperty(() => IncludeClick);
        set => SetProperty(() => IncludeClick, value, UpdateSequenceText);
    }

    public bool IncludeMove
    {
        get => GetProperty(() => IncludeMove);
        set => SetProperty(() => IncludeMove, value, UpdateSequenceText);
    }

    public int MoveX { get => GetProperty(() => MoveX); set => SetProperty(() => MoveX, value); }

    public int MoveY { get => GetProperty(() => MoveY); set => SetProperty(() => MoveY, value); }

    public bool IncludeScroll
    {
        get => GetProperty(() => IncludeScroll);
        set => SetProperty(() => IncludeScroll, value, UpdateSequenceText);
    }

    /// <summary>굴릴 칸 수. 양수가 위, 음수가 아래.</summary>
    public int ScrollNotches
    {
        get => GetProperty(() => ScrollNotches);
        set => SetProperty(() => ScrollNotches, value, UpdateSequenceText);
    }

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
