using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;

using Minguk.Tools.Capture;
using Minguk.Tools.Capture.Input;
using Minguk.Tools.Input.Interop;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Sequencing;
using Minguk.Tools.Vision.Ocr;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>실시간 스크립트가 어떻게 끝났는지.</summary>
public enum LiveScriptOutcome
{
    /// <summary>끝까지 돌았거나 아직 도는 중.</summary>
    None,

    /// <summary>중지·비상 정지(Pause)·<c>끝()</c> 으로 멈췄다. 오류가 아니다.</summary>
    Stopped,

    /// <summary>안전장치가 막았다. 이유는 <see cref="LiveScriptApi.GuardMessage"/>.</summary>
    Guarded
}

/// <summary>
/// 실시간 모드에서 스크립트가 부르는 것들. <b>부르면 곧바로 나간다.</b>
/// </summary>
/// <remarks>
/// 계획 모드(<see cref="SequenceScriptApi"/>)와 같은 이름을 쓴다 - 같은 글을 두 모드에서 돌릴 수 있어야
/// 사람이 두 벌을 배우지 않는다. 거기에 화면을 읽는 것(검출들·읽기)과 흐름(중지되었나·끝)이 더 있다.
/// 이름은 <see cref="ScriptApiCatalog"/> 표에 있고, 검증이 이 클래스에 그 이름이 다 있는지 센다.
///
/// <b>안전장치</b> - 전부 여기서 건다. 엔진이나 화면에 두면 언어마다 다르게 새어 나간다.
///   - 대상 창이 앞에 없으면 입력을 보내지 않고 멈춘다(<see cref="ScriptGuardException"/>). 엉뚱한 창에 타이핑하는 사고.
///   - 초당 입력 상한. 넘으면 기다린다(멈추지 않는다) - 빠른 반복문이 입력을 쏟지 않게.
///   - 모든 호출이 중지 토큰을 본다. <c>쉬기()</c> 도 토큰으로 기다려서 중지가 바로 먹는다.
///   - 눈이 없으면(캡처 안 돎, 검출 꺼짐) <c>검출들()</c> 은 빈 목록이 아니라 멈추고 이유를 말한다.
///
/// 스크립트 스레드에서 돈다. UI 스레드가 아니라서 입력을 기다려도(await) 화면이 멈추지 않는다.
///
/// <b>sealed 가 아닌 이유</b> - 프로젝트를 DLL 로 빌드하면(<see cref="CompiledScriptBuilder"/>) 스크립트 글이
/// <c>__Compiled : LiveScriptApi</c> 의 메서드 몸이 된다. 그래야 <c>목표()</c>·<c>출력()</c> 같은 public 이름이
/// 상속으로 그대로 스코프에 들어와, 스크립팅 전역과 똑같이 이름만으로 불린다. 진입점 이름을 우리가 쥐므로
/// Roslyn 을 올려도 안 깨진다 - 스크립팅 내부(제출 factory)에 기대지 않는다.
/// </remarks>
public partial class LiveScriptApi : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly LiveScriptHost _host;
    private readonly CancellationToken _token;
    private readonly HashSet<ushort> _heldKeys = [];
    private readonly HashSet<MouseButton> _heldButtons = [];
    private readonly Queue<long> _inputTicks = new();
    private readonly object _gate = new();

    /// <summary>배율·표본을 지키는 잠금 - 조준 스레드(<see cref="AimLoop"/>)와 스크립트 스레드가 같이 본다.</summary>
    private readonly object _aimGate = new();

    /// <summary>조준 스레드. 처음 <c>조준(검출)</c> 을 부를 때 만든다.</summary>
    private AimLoop? _aim;

    public LiveScriptApi(LiveScriptHost host, CancellationToken token)
    {
        _host = host;
        _token = token;
    }

    /// <summary>어떻게 끝났는지. 엔진이 예외를 받은 뒤 이것을 보고 오류인지 정상 종료인지 가른다.</summary>
    public LiveScriptOutcome Outcome { get; private set; }

    /// <summary>안전장치가 막은 이유.</summary>
    public string? GuardMessage { get; private set; }

    // ── 계획 모드와 같은 이름들 - 곧바로 나간다 ─────────────────────────

    public void Type(string text) => Traced("Type", Quote(text), () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = text ?? string.Empty }));

    public void TypeLine(string text)
    {
        Type(text);
        Enter();
    }

    public void Enter() => Traced("Enter", "", () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.Enter }));

    public void ToggleHangul() => Traced("ToggleHangul", "", () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.ToggleHangul }));

    /// <param name="button">비우면 좌클릭. MouseButton.Right · 숫자 · "right" 를 받는다 - 언어마다 넘기는 모양이 다르다.</param>
    /// <summary>
    /// 마우스 버튼 한 번. <c>클릭()</c> 좌클릭 · <c>클릭(100)</c> 좌클릭을 100ms 누르고 있다가 뗌 ·
    /// <c>클릭("Right")</c> · <c>클릭("Right", 100)</c>.
    /// </summary>
    /// <param name="buttonOrHold">
    /// 숫자면 누르고 있을 시간(ms) - 게임에서 연사·차지처럼 누른 채로 있어야 할 때. 아니면 버튼(MouseButton · "Right").
    /// 버튼 번호를 숫자로 받던 것은 버렸다 - <c>클릭(100)</c> 이 더 자주 쓰인다.
    /// </param>
    /// <param name="holdMs">버튼을 먼저 줄 때의 누르는 시간(ms). 0 이면 보통 클릭(누르는 시간은 화면의 설정).</param>
    /// <remarks>누르고 있는 동안 중지가 먹고, 멈추거나 터져도 반드시 뗀다.</remarks>
    public void Click(object? buttonOrHold = null, int holdMs = 0)
    {
        var (button, hold) = buttonOrHold switch
        {
            int or long or double or float => (MouseButton.Left, (int)System.Convert.ToDouble(buttonOrHold, CultureInfo.InvariantCulture)),
            _ => (ToButton(buttonOrHold), holdMs)
        };

        ClickCore(button, hold);
    }

    private void ClickCore(MouseButton button, int holdMs)
        => Traced("Click", holdMs > 0 ? $"{button}, {holdMs}" : button.ToString(), () =>
        {
            EnsureCursorInsideTarget();

            if (holdMs <= 0)
            {
                Send(new SequenceStepDefinition { Kind = SequenceStepKind.Click, Button = button });
                return;
            }

            HoldButton(button, holdMs);
        });

    /// <summary>버튼을 그 시간만큼 누르고 있다가 뗀다. 비상 정지(<see cref="ReleaseAll"/>)도 뗄 수 있게 누른 것을 적어 둔다.</summary>
    private void HoldButton(MouseButton button, int holdMs)
    {
        BeforeInput();

        lock (_gate) _heldButtons.Add(button);
        _host.Service.Adapter.PressMouseButton(button);

        try
        {
            Wait(holdMs);
        }
        finally
        {
            lock (_gate) _heldButtons.Remove(button);
            _host.Service.Adapter.ReleaseMouseButton(button);
        }
    }

    /// <summary>우클릭. <c>우클릭(100)</c> 이면 100ms 누르고 있다가 뗀다.</summary>
    public void RightClick(int holdMs = 0) => ClickCore(MouseButton.Right, holdMs);

    public void ClickAt(int x, int y, object? button = null)
    {
        MoveTo(x, y);
        ClickCore(ToButton(button), 0);
    }

    public void MoveTo(int x, int y)
    {
        // 일반 모드는 커서를 보며 걸어간다(절대 이동이 안 먹는 메뉴). 커서 자리를 못 읽는 경로면 절대 이동으로.
        if (_normalMouse && _host.Service.Adapter.GetCursorPosition() is not null)
        {
            MoveCursor(x, y);
            return;
        }

        Traced("MoveTo", $"{x}, {y}", () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.MoveTo, X = x, Y = y }));
    }

    private bool _normalMouse;

    /// <summary>
    /// 마우스 움직임 방식을 고른다 - <c>"조준"</c>(기본, 커서를 잡는 게임: 시야를 돌린다) · <c>"일반"</c>(커서가 보이는 화면: 메뉴·창).
    /// </summary>
    /// <remarks>
    /// 일반 모드에서는 <c>이동()</c>·<c>이동클릭()</c>·<c>조준(x, y)</c>·<c>조준(검출)</c> 이 모두 실제 커서 자리를 보며 그 점까지 걸어간다
    /// (<see cref="MoveCursor"/>). 조준 모드에서는 지금까지처럼 조준 스레드·화면 가운데 기준이다. 스크립트 첫머리에서 한 번 정한다.
    /// </remarks>
    public void SetMouseMode(string mode)
    {
        _normalMouse = mode switch
        {
            "일반" or "Normal" or "normal" => true,
            "조준" or "Aim" or "aim" => false,
            _ => throw Guard($"마우스 모드는 \"일반\" 이나 \"조준\" 입니다: {mode}")
        };

        if (_normalMouse) ForgetAim();
    }

    public void 마우스모드(string 모드) => SetMouseMode(모드);

    private double _aimZone = AimLoop.DefaultOnTargetFraction;

    /// <summary>
    /// 조준(검출)이 "맞았다"(참)고 볼 몸 사각형의 안쪽 비율 - 0.6 이면 몸 가운데 60% 안에 조준점이 들어와야 쏜다. 작을수록 가운데서만 쏜다(0.1~1).
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-19) "타겟 사각형 조금 더 안쪽으로. 너무 끝에서 쏘니까 안 맞는 경우가 있네" - 예전 0.9 는 가장자리에 걸친 순간에 쏴 빗나갔다.
    /// 게임·무기(산탄·저격)마다 알맞은 값이 달라 스크립트 첫머리에서 정한다. 좁히면 쏘는 횟수가 준다(조준이 가운데로 올 때까지 기다린다).
    /// </remarks>
    public void SetAimZone(double fraction) => Traced("SetAimZone", fraction.ToString("0.00"), () =>
    {
        _aimZone = Math.Clamp(fraction, 0.1, 1.0);

        if (_aim is { } loop) loop.OnTargetFraction = _aimZone;
    });

    public void 조준범위(double 비율) => SetAimZone(비율);

    private int _aimLeadMs = AimLoop.DefaultLeadMs;
    private bool _aimConfirm = true;

    /// <summary>
    /// 움직이는 검출을 속도 × 이 시간(ms)만큼 앞서 겨눈다(기본 90, 0~400). 달리는 봇의 뒤를 쏘면 늘리고, 앞을 쏘면 줄인다.
    /// </summary>
    public void SetAimLead(int milliseconds) => Traced("SetAimLead", milliseconds.ToString(), () =>
    {
        _aimLeadMs = Math.Clamp(milliseconds, 0, 400);

        if (_aim is { } loop) loop.LeadMs = _aimLeadMs;
    });

    public void 앞질러겨누기(int 밀리초) => SetAimLead(밀리초);

    /// <summary>
    /// 조준(검출)이 참을 주려면 마지막으로 본 화면에서도 검출이 조준점 근처여야 하는가(기본 참). 끄면 예측만으로도 쏜다 - 빠르지만 배율이 틀리면 옆을 쏜다.
    /// </summary>
    public void SetAimConfirm(bool confirm) => Traced("SetAimConfirm", confirm.ToString(), () =>
    {
        _aimConfirm = confirm;

        if (_aim is { } loop) loop.ConfirmOnScreen = confirm;
    });

    public void 확인후쏘기(bool 켬) => SetAimConfirm(켬);

    public void Scroll(int notches) => Traced("Scroll", notches.ToString(), () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.Scroll, Notches = notches }));

    /// <summary>
    /// 그 화면 좌표를 향해 마우스를 <b>움직인 양</b>으로 옮긴다. 게임처럼 커서를 잡는 창에서 조준할 때.
    /// </summary>
    /// <remarks>
    /// 커서를 잡는 창은 절대 좌표 이동(<see cref="MoveTo"/>)을 무시한다 - 실제로 오버워치에서 이동()이 아무 일도 안 했다.
    /// 그런 창은 화면 가운데가 조준점이므로, 가운데에서 목표까지의 거리에 배율을 곱해 상대 이동으로 보낸다.
    /// 한 번에 딱 맞지 않을 수 있다 - 반복문에서 다시 찾고 다시 조준하면 점점 맞아 간다.
    /// </remarks>
    /// <returns>
    /// 맞았으면(겨눈 뒤의 새 화면에서 목표가 가운데 <see cref="LiveScriptHost.AimTolerancePx"/> 안) true - 이때 누르면 된다.
    /// 아직 멀어 움직였거나, 겨눈 뒤 새 화면이 아직 안 왔으면 false.
    /// </returns>
    public bool Aim(int x, int y) => _normalMouse ? MoveCursor(x, y) : Traced("Aim", $"{x}, {y}", () => { WarnIfAimScaleLow(); return AimCore(x, y, _host.AimTolerancePx, _host.AimTolerancePx); });

    /// <summary>
    /// 검출을 겨눈다 - <b>조준 스레드</b>(<see cref="AimLoop"/>)에 붙여 8ms 마다 멈추지 않고 <b>머리</b>를 따라가게 하고, <b>몸에 들어와 있으면</b> 맞은 것(true)으로 친다.
    /// </summary>
    /// <remarks>
    /// <b>부르면 어떻게 되나</b> - 처음 부르면 그 검출을 붙잡고 스레드가 움직이기 시작한다. 그 뒤로는 부를 때마다 <b>새 화면이 한 장 올 때까지</b>(검출 주기, 최대
    /// <see cref="AimFrameWaitMs"/>) 기다렸다가 맞았는지 돌려준다 - 그래서 <c>while { 검출 = 목표(); if (조준(검출)) 클릭(); }</c> 반복문이 검출 박자에 맞춰 돌고,
    /// 그동안 마우스는 스레드가 계속 움직인다. 맞았다는 답은 한 화면에 한 번만 준다(예측만으로 연달아 쏘지 않게).
    /// 붙잡은 것은 <c>목표풀기()</c>·다른 마우스 입력(상대이동·끌기·좌표 조준)·일시정지·중지에서 놓고, 400ms 넘게 못 보면 스스로 놓는다(그때 <c>목표()</c> 가 새로 고른다).
    ///
    /// <b>좌표로 겨누는 것과 무엇이 다른가</b> - <c>조준(x, y)</c> 는 한 번 움직이고 그 점에서 8px 안에 들어와야 맞았다고 한다.
    /// 총은 그렇게까지 정확할 필요가 없다. 사람도 머리를 보고 쏘되 몸에 걸치면 그냥 쏜다. 겨누는 곳은 <see cref="ScriptDetection.머리y"/>(위에서 22%)이고,
    /// 맞았다고 보는 범위는 <b>몸 사각형</b>(가운데에서 재야 한다 - 머리에서 높이의 반을 재면 머리 위 허공에서도 참이 된다).
    /// </remarks>
    public bool Aim(ScriptDetection mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        if (_normalMouse) return MoveCursor(mob.중심x, mob.중심y);

        WarnIfAimScaleLow();

        return Traced("Aim", mob.ToString(), () => AimTracked(mob));
    }

    /// <summary>이 배율(1.0 = 100%) 아래면 "느리다" 고 알린다. 로그의 오버워치 실측이 300~400% 라 30% 는 한참 낮다.</summary>
    private const double LowAimScale = 0.3;

    private bool _aimScaleWarned;

    /// <summary>
    /// 조준 모드로 처음 겨눌 때 배율이 너무 낮으면 한 번 말해 준다(사용자, 2026-09-19).
    /// </summary>
    /// <remarks>
    /// 메뉴 테스트로 낮춰 둔 10% 가 저장돼 사격장에서 100px 떨어진 몹에 10카운트만 보냈다 - 겨눈다고 하면서 몇 초씩 안 움직여 원인을 로그에서 한참 찾았다.
    /// 10% 는 배율의 하한(<see cref="MinAimScale"/>)이라 스스로 배울 수도 없다(보낸 양이 학습 문턱 15카운트 아래).
    /// 시작 배율만 보고 한 번 말하고, 도는 중 배우다 낮아진 것은 다시 말하지 않는다.
    /// </remarks>
    private void WarnIfAimScaleLow()
    {
        if (_aimScaleWarned) return;
        _aimScaleWarned = true;

        if (AimScale >= LowAimScale) return;

        _host.Print($"조준 배율이 {AimScale:P0} 로 낮아 조준이 아주 느리게 움직입니다 - 스크립트 화면 도구 줄의 「조준 배율(%)」 을 게임에 맞게 올리세요(처음 100%, 자동을 켜 두면 겨눈 결과를 보고 맞춥니다).");
    }

    /// <summary>새 화면을 기다리는 상한(ms). 검출이 0.1초에 한 번이라 보통 그 안에 온다. 넘으면 지금 예측으로 답한다.</summary>
    private const int AimFrameWaitMs = 400;

    private bool AimTracked(ScriptDetection mob)
    {
        ThrowIfStopping();
        EnsureForeground();

        var target = _host.Target() ?? throw Guard("대상 창이 없습니다 - 화면에서 창을 골라 시작(연결)하세요.");

        if (!CaptureTargetBounds.TryGet(target, out var bounds))
            throw Guard("대상 창의 자리를 알 수 없습니다 - 창이 닫혔거나 최소화됐습니다.");

        var loop = _aim ??= new AimLoop(new AimLoop.Host(
            Bounds: () => _host.Target() is { } t && CaptureTargetBounds.TryGet(t, out var b) ? b : null,
            Latest: () => _host.Hub.Latest,
            Scale: () => AimScale,
            Move: (dx, dy) => _host.Service.Adapter.MoveMouseBy(dx, dy),
            MayMove: IsTargetInFront,
            Learn: LearnSample,
            Pause: _host.PauseGate,
            Token: _token)) { OnTargetFraction = _aimZone, LeadMs = _aimLeadMs, ConfirmOnScreen = _aimConfirm };

        var frames = loop.FramesConsumed;

        loop.Engage(mob, _seenFrameTicks, bounds);

        // 다른 것이 시야를 움직인 뒤가 아니다 - 좌표 조준의 배율 배우기는 스레드가 도는 동안 쓰지 않는다.
        _lastAim = null;

        var deadline = Environment.TickCount64 + AimFrameWaitMs;

        while (true)
        {
            if (!loop.IsEngaged) return false;

            // 맞아 있으면 이 화면에서 아직 안 쐈을 때만 바로 참. 이미 쐈으면 다음 화면까지 기다린다.
            if (loop.IsOnTarget() && loop.TryClaimHit()) return true;

            if (loop.FramesConsumed != frames || Environment.TickCount64 >= deadline)
                return loop.IsOnTarget() && loop.TryClaimHit();

            Wait(4);
        }
    }

    /// <summary>입력을 보내도 되는가 - 앞 창 확인만(예외 없이). 조준 스레드가 박자마다 본다.</summary>
    private bool IsTargetInFront()
        => !_host.RequiresForeground
           || _host.Target() is not { Kind: CaptureTargetKind.Window, Handle: var handle }
           || handle == IntPtr.Zero
           || ForegroundWindow.IsInFront(handle);

    /// <summary>지금 자리에서 이만큼 움직인다. 배율 없이 그대로.</summary>
    public void MoveBy(int deltaX, int deltaY) => Traced("MoveBy", $"{deltaX}, {deltaY}", () =>
    {
        BeforeInput();
        ForgetAim();
        _host.Service.Adapter.MoveMouseBy(deltaX, deltaY);
        _aim?.NoteSent(deltaX, deltaY);
    });

    /// <summary>
    /// 커서를 그 화면 좌표까지 <b>실제 커서 자리를 보며</b> 걸어간다. 메뉴처럼 커서가 보이는 화면용.
    /// </summary>
    /// <remarks>
    /// 절대 이동이 안 먹는 게임 메뉴(Raw Input)에서, 작은 상대 이동을 여러 번 보내고 그때마다 커서가 실제로 얼마나 갔는지
    /// 읽어 픽셀당 카운트를 배워 간다(조준은 시야가 도는 게임 전용이라 메뉴에서는 화면 밖으로 튄다 - 실측).
    /// 커서 자리를 못 읽는 경로(창 메시지·조용한 경로)에서는 걸을 수 없어 그렇게 말한다.
    /// </remarks>
    /// <returns>목표에서 <c>tolerance</c> 픽셀 안에 닿았는가.</returns>
    public bool MoveCursor(int x, int y, int tolerance = 3) => Traced("MoveCursor", $"{x}, {y}", () =>
    {
        BeforeInput();
        ForgetAim();

        const int MaxStepCounts = 300;
        const int MaxIterations = 80;

        var adapter = _host.Service.Adapter;
        var scaleX = 1.0;
        var scaleY = 1.0;
        var walk = new System.Text.StringBuilder();
        var moved = false;
        var first = adapter.GetCursorPosition();

        for (var i = 0; i < MaxIterations; i++)
        {
            if (adapter.GetCursorPosition() is not { } cursor)
                throw Guard("이 입력 경로는 커서 자리를 읽을 수 없어 커서이동을 못 합니다. 입력 경로를 SendInput 이나 Interception 으로 바꿔 보세요.");

            var errX = x - cursor.X;
            var errY = y - cursor.Y;

            if (Math.Abs(errX) <= tolerance && Math.Abs(errY) <= tolerance) return true;

            var stepX = Math.Clamp((int)Math.Round(errX * scaleX), -MaxStepCounts, MaxStepCounts);
            var stepY = Math.Clamp((int)Math.Round(errY * scaleY), -MaxStepCounts, MaxStepCounts);

            // 남은 거리가 있는데 반올림으로 0 이 되면 안 간다 - 한 카운트는 보낸다.
            if (stepX == 0 && Math.Abs(errX) > tolerance) stepX = Math.Sign(errX);
            if (stepY == 0 && Math.Abs(errY) > tolerance) stepY = Math.Sign(errY);

            SendRelative(stepX, stepY);
            Wait(12);

            if (adapter.GetCursorPosition() is not { } after) continue;

            // 못 닿았을 때 원인을 볼 수 있게 걸음을 적어 둔다(앞 8걸음 + 마지막 4걸음).
            if (i < 8 || i >= MaxIterations - 4)
                walk.Append($"\n  {i}: ({cursor.X},{cursor.Y}) → ({after.X},{after.Y}) 보냄({stepX},{stepY}) 배율({scaleX:0.00},{scaleY:0.00})");

            // 보낸 카운트 / 실제로 간 픽셀 = 픽셀당 카운트. 안 움직였으면(커서가 안 따라옴) 배로 늘려 본다.
            scaleX = LearnScale(scaleX, stepX, after.X - cursor.X);
            scaleY = LearnScale(scaleY, stepY, after.Y - cursor.Y);

            // 네 걸음을 보내도 커서가 한 번도 안 움직였으면 더 걸어도 소용없다 - 입력이 게임(또는 커서)에 안 닿는 것이다.
            if (after != first) moved = true;

            if (i == 3 && !moved)
            {
                _host.Print($"커서이동 진단: {adapter}");
                throw Guard("커서이동: 입력을 보내도 커서가 전혀 움직이지 않습니다. 출력 창의 「커서이동 진단」 줄을 확인하고, 입력 경로를 다른 것으로 바꿔 보세요.");
            }
        }

        _host.Print($"커서이동 실패: 목표({x},{y}) 에 못 닿았다.{walk}");

        return false;
    });

    private static double LearnScale(double scale, int sentCounts, int movedPixels)
    {
        if (sentCounts == 0) return scale;

        // 움직임이 없거나 반대로 갔으면 더 크게 보내 본다.
        if (movedPixels == 0 || Math.Sign(movedPixels) != Math.Sign(sentCounts)) return Math.Min(scale * 2, 20);

        return Math.Clamp(Math.Abs((double)sentCounts / movedPixels), 0.05, 20);
    }

    public bool 커서이동(int x, int y, int 허용오차 = 3) => MoveCursor(x, y, 허용오차);

    /// <summary>버튼을 누른 채로 둔다. <c>버튼떼기</c> 전까지. 비상 정지도 뗄 수 있게 적어 둔다.</summary>
    public void MouseDown(object? button = null) => Traced("MouseDown", ToButton(button).ToString(), () =>
    {
        BeforeInput();

        var pressed = ToButton(button);
        lock (_gate) _heldButtons.Add(pressed);
        _host.Service.Adapter.PressMouseButton(pressed);
    });

    /// <summary>누르고 있던 버튼을 뗀다.</summary>
    public void MouseUp(object? button = null) => Traced("MouseUp", ToButton(button).ToString(), () =>
    {
        BeforeInput();

        var pressed = ToButton(button);
        lock (_gate) _heldButtons.Remove(pressed);
        _host.Service.Adapter.ReleaseMouseButton(pressed);
    });

    /// <summary>
    /// 버튼을 누른 채 그 화면 좌표로 끌었다가 뗀다. 커서가 보이는 창(RPG·보통 프로그램)에서.
    /// </summary>
    public void Drag(int x, int y, object? button = null)
        => Traced("Drag", $"{x}, {y}, {ToButton(button)}", () => DragCore(ToButton(button), () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.MoveTo, X = x, Y = y })));

    /// <summary>
    /// 버튼을 누른 채 이만큼 움직였다가 뗀다. RPG 의 우클릭 카메라 회전처럼 커서 자리가 아니라 <b>움직인 양</b>이 중요한 곳에.
    /// </summary>
    /// <remarks>
    /// <c>상대끌기(200, 0, "Right")</c> 면 오른쪽 버튼을 누른 채 오른쪽으로 200 카운트 돌린다. 조준과 같은 걸음 나누기를 쓴다.
    /// </remarks>
    public void DragBy(int deltaX, int deltaY, object? button = null)
        => Traced("DragBy", $"{deltaX}, {deltaY}, {ToButton(button)}", () => DragCore(ToButton(button), () => SendRelative(deltaX, deltaY)));

    /// <summary>누르고 → 움직이고 → 뗀다. 중간에 멈추거나 터져도 반드시 뗀다 - 누른 채 남으면 게임이 계속 끌린다.</summary>
    private void DragCore(MouseButton button, Action move)
    {
        BeforeInput();
        ForgetAim();

        lock (_gate) _heldButtons.Add(button);
        _host.Service.Adapter.PressMouseButton(button);

        try
        {
            // 누르자마자 움직이면 게임이 누름을 놓치는 일이 있다. 한 박자 둔다.
            Wait(_host.HoldTimeMs);
            move();
            Wait(_host.HoldTimeMs);
        }
        finally
        {
            lock (_gate) _heldButtons.Remove(button);
            _host.Service.Adapter.ReleaseMouseButton(button);
        }
    }

    /// <summary>
    /// 상대 이동을 <b>사람이 겨누듯</b> 나눠 보낸다 - 조준·상대이동·끌기가 같이 쓴다.
    /// </summary>
    /// <remarks>
    /// <b>왜 이렇게까지</b> - 예전에는 한 걸음 30카운트씩 최대 6걸음이었다. 멀리 겨누면 한 걸음이 200카운트라
    /// 시야가 뚝뚝 끊겨 돌았고("팍팍 이동"), 게임이 그 사이를 한 프레임도 못 봤다.
    ///
    /// 사람 손은 셋을 한다. 그대로 흉내 낸다.
    /// <list type="number">
    /// <item><b>천천히 떼고 천천히 멈춘다</b> - 가운데가 가장 빠른 S자(smoothstep). 등속으로 가면 시작과 끝이 튄다.</item>
    /// <item><b>잘게 자주</b> - 8ms 마다(약 125Hz) 조금씩. 게임이 그 사이사이를 보므로 움직임이 이어져 보인다.
    /// 윈도우 기본 눈금이 15.6ms 라 <see cref="PrecisionTimer"/> 로 1ms 로 당겨 두고 움직인다.</item>
    /// <item><b>완전한 직선이 아니다</b> - 손목은 조금 휜다. 가는 동안만 옆으로 살짝 벗어났다가 끝에서 0으로 돌아온다.
    /// 합은 정확히 delta 라 도착지는 그대로다.</item>
    /// </list>
    ///
    /// 걸리는 시간은 거리에 따라 늘지만 상한이 있다(<see cref="MaxMoveMs"/>). 조준은 새 화면을 기다렸다 다시 겨누므로
    /// 한 번에 다 맞힐 필요가 없다 - 오래 붙들고 있는 것이 더 나쁘다.
    /// </remarks>
    private void SendRelative(int deltaX, int deltaY)
    {
        if (deltaX == 0 && deltaY == 0) return;

        var distance = Math.Sqrt(((double)deltaX * deltaX) + ((double)deltaY * deltaY));
        var durationMs = Math.Clamp(MoveBaseMs + (MoveMsPerRoot * Math.Sqrt(distance)), MoveBaseMs, MaxMoveMs);
        var steps = Math.Clamp((int)Math.Round(durationMs / MoveStepMs), 1, MaxMoveSteps);
        var gapMs = durationMs / steps;

        // 옆으로 벗어나는 양(카운트). 거리에 비례하되 아주 작게 - 크면 조준이 흔들린 것처럼 보인다.
        var arc = Math.Min(distance * ArcFraction, MaxArcCounts) * ((_random.NextDouble() * 2) - 1);
        var arcX = distance > 0 ? -deltaY / distance * arc : 0;
        var arcY = distance > 0 ? deltaX / distance * arc : 0;

        var sentX = 0;
        var sentY = 0;

        // 움직이는 동안만 눈금을 당긴다. 이것이 없으면 8ms 를 부탁해도 15ms 를 쉬어 걸음이 절반으로 준다.
        using var precise = new PrecisionTimer();

        for (var i = 1; i <= steps; i++)
        {
            var t = Ease(i / (double)steps);

            // 부푼 만큼은 가는 길에만 있고 끝(t=1)에서는 0 이다.
            var bulge = Math.Sin(t * Math.PI);

            // 나눗셈 나머지가 끝에 몰리지 않게 누적으로 나눈다 - 합은 정확히 delta 다.
            var stepX = (int)Math.Round((deltaX * t) + (arcX * bulge)) - sentX;
            var stepY = (int)Math.Round((deltaY * t) + (arcY * bulge)) - sentY;

            if (stepX != 0 || stepY != 0)
            {
                _host.Service.Adapter.MoveMouseBy(stepX, stepY);
                sentX += stepX;
                sentY += stepY;
            }

            if (i < steps) WaitPrecise(gapMs);
        }

        // 휘어 간 것이 반올림으로 남았을 수 있다. 마지막에 딱 맞춘다.
        if (sentX != deltaX || sentY != deltaY)
            _host.Service.Adapter.MoveMouseBy(deltaX - sentX, deltaY - sentY);

        _aim?.NoteSent(deltaX, deltaY);
    }

    /// <summary>천천히 떼고 천천히 멈춘다(smoothstep). 0~1 을 0~1 로 옮기되 양 끝의 기울기가 0 이다.</summary>
    private static double Ease(double t) => t * t * (3 - (2 * t));

    /// <summary>
    /// 짧은 시간을 제대로 기다린다. 남은 시간이 넉넉하면 재우고, 1.5ms 아래로 남으면 시계를 보며 버틴다.
    /// </summary>
    /// <remarks>
    /// <see cref="Wait"/> 는 <c>WaitHandle</c> 이라 눈금(기본 15.6ms, <see cref="PrecisionTimer"/> 로 1ms)을 탄다.
    /// 걸음 간격이 8ms 인데 15ms 를 쉬면 움직임이 절반 속도로 늘어지고 걸음도 굵어진다. 대신 <b>토큰은 계속 본다</b> -
    /// 중지가 이 사이에 먹어야 한다.
    /// </remarks>
    private void WaitPrecise(double milliseconds)
    {
        ThrowIfStopping();

        if (milliseconds <= 0) return;

        var until = Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000.0);

        while (true)
        {
            var remaining = (until - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;

            if (remaining <= 0) return;

            if (remaining > 1.5)
            {
                if (_token.WaitHandle.WaitOne(1)) ThrowIfStopping();
            }
            else
            {
                Thread.SpinWait(80);
            }
        }
    }

    /// <summary>휘는 길에 쓰는 난수. 씨앗을 안 주면 부를 때마다 달라 사람 손처럼 같은 길을 두 번 안 간다.</summary>
    private readonly Random _random = new();

    /// <summary>아무리 짧아도 이만큼은 쓴다(ms). 한 걸음으로 끝나면 사람 손이 아니다.</summary>
    /// <remarks>
    /// 처음에는 70·6.5·280 이었는데 겨누는 맛이 굼떴다. 잘 하는 사람은 <b>멀어도 단호하게</b> 한 번에 꺾고
    /// 끝에서만 살짝 다듬는다 - 부드러움은 총 시간이 아니라 걸음 수(8ms 간격)가 지키므로, 시간만 줄이면
    /// 끊기지 않으면서 빨라진다. 거리 474 카운트가 212ms → 136ms, 그래도 17걸음이다.
    /// </remarks>
    private const double MoveBaseMs = 45;

    /// <summary>거리(카운트)의 제곱근에 곱하는 시간(ms). 멀수록 오래 걸리되 비례해서 늘지는 않는다 - 사람도 그렇다.</summary>
    private const double MoveMsPerRoot = 3.0;

    /// <summary>한 번의 이동에 쓰는 시간 상한(ms). 조준은 새 화면을 기다렸다 또 겨누므로 오래 붙들 이유가 없다.</summary>
    private const double MaxMoveMs = 130;

    /// <summary>걸음 사이 목표 간격(ms). 8ms 면 약 125Hz - 게이밍 마우스의 폴링과 비슷하다.</summary>
    private const double MoveStepMs = 5;

    /// <summary>걸음 수 상한. 눈금이 굵은 PC 에서 시간이 늘어지는 것을 막는다.</summary>
    private const int MaxMoveSteps = 60;

    /// <summary>옆으로 벗어나는 양을 거리의 몇 배로 할지. 손목이 휘는 만큼만 - 크면 겨눈 것이 흔들려 보인다.</summary>
    private const double ArcFraction = 0.012;

    /// <summary>벗어나는 양의 상한(카운트). 멀리 꺾을 때 옆으로 크게 돌면 겨눈 자리를 지나친다.</summary>
    private const double MaxArcCounts = 8;

    /// <summary>
    /// 한 번의 조준으로 보내는 양의 상한(카운트). 배율이 잘못 커지면 한 번에 2,000 이 넘게 나가 시야가 한 바퀴 돌았다(실측).
    /// 넘치면 잘라서 보낸다 - 모자란 만큼은 다음 화면에서 다시 겨눈다.
    /// </summary>
    private const int MaxAimCounts = 1200;

    /// <summary>
    /// 배율을 고치기 전에 모으는 표본 수. 평균이나 섞기가 아니라 <b>순서</b>로 본다(7개가 한쪽이어야 옮긴다 - <see cref="LearnSampleCore"/>).
    /// </summary>
    /// <remarks>
    /// <b>잡음이 한쪽으로만 튄다.</b> 검출이 스스로 움직이거나 화면이 덜 돌면 "보낸 것보다 덜 움직였다" 가 되어
    /// 잰 값이 커진다. 반대쪽("더 움직였다")은 거부 규칙(줄어든 비율 2.0 초과·멀어짐)에 걸러진다.
    /// 그래서 한 값씩 반영하면 <b>위로만 떠밀린다</b> - 실측에서 3.6 으로 시작해 734번 배우는 동안
    /// 상한 20 까지 올라가 붙었고(잰 값에 16.50 · 6.79 같은 것이 섞였다), 그러자 모든 조준이 상한에 잘려
    /// 화면이 제대로 돌지 못했다.
    ///
    /// 순서로 보면 그런 값 몇 개에 흔들리지 않는다. 가운뎃값도 한 무리가 다섯이 되면 넘어가서, 7개가 한쪽일 때만 옮긴다.
    /// </remarks>
    private const int AimSamples = 9;

    /// <summary>배율이 가질 수 있는 값의 범위(카운트/px).</summary>
    private const double MinAimScale = 0.1;

    /// <summary>기준값이 지금 배율의 이만큼 배 밖이면 "멀다" - 한 번에 크게(1.5배까지) 옮긴다. 안이면 <see cref="NearStep"/> 만큼만 다가간다.</summary>
    private const double FarRatio = 1.5;

    /// <summary>가까울 때 차이의 몇 할만큼 다가가나. 흩어진 표본에 배율이 튀지 않게.</summary>
    private const double NearStep = 0.35;


    private const double MaxAimScale = 20;

    private readonly List<double> _aimSamples = [];

    /// <summary>지금 배율보다 크게 높아 떼어 둔 표본 - <see cref="HighSampleRun"/> 개가 연달아 오면 받는다.</summary>
    private readonly List<double> _highSamples = [];

    /// <summary>표본이 지금 배율의 이 배를 넘으면 떼어 둔다.</summary>
    private const double HighSampleRatio = 1.6;

    /// <summary>떼어 둔 높은 표본이 이만큼 연달아 오면 배율이 정말 낮은 것으로 보고 받는다.</summary>
    private const int HighSampleRun = 5;

    /// <summary>이보다 가까우면 배율을 안 배운다(px). 검출 사각형의 떨림이 잰 값을 뒤집는다.</summary>
    private const double MinLearnOffsetPx = 40;

    /// <summary>
    /// 이보다 멀면 배율을 안 배운다(px). 상한에 잘리고, 화면 가장자리는 px 이 각도보다 빨리 늘어
    /// 가운데 근처에서 맞는 배율과 다른 값이 나온다.
    /// </summary>
    private const double MaxLearnOffsetPx = 400;

    /// <summary>
    /// 계산한 만큼의 몇 배를 실제로 보낼지. 1 보다 작게 두어 <b>일부러 조금 모자라게</b> 겨눈다.
    /// </summary>
    /// <remarks>
    /// 지나치면 반대편에서 다시 꺾어야 해서 화면이 좌우로 왕복한다(실측: 731 → -598 → 835 → -409 → 837 ...).
    /// 모자라면 다음 화면에서 마저 당기면 되고, 검출이 0.08초에 한 번이라 그 한 번이 비싸지 않다.
    /// 사람이 잘 겨눌 때도 한 번에 딱 붙이지 않고 살짝 못 미치게 꺾은 뒤 마지막을 다듬는다.
    /// </remarks>
    /// 값은 사용자가 "좌우 전환이 뚝뚝 끊긴다, 조금 더 빨리" 라고 해 0.85 에서 올렸다(2026-09-16) - 한 번에 더 당겨 걸음 수가 준다.
    private const double AimDamping = 0.93;

    /// <summary>마지막으로 겨눈 시각(TickCount64). 이보다 앞선 프레임으로 찾은 자리로는 다시 겨누지 않는다.</summary>
    private long _lastAimTicks;

    /// <summary>
    /// 겨눈 뒤 이만큼(ms) 안에 들어온 프레임도 버린다. 게임이 그리고, 화면에 오르고, 캡처가 받기까지 몇 프레임 걸려
    /// 그 사이의 프레임은 겨누기 전 화면이다(실측: 가끔 거리가 전혀 안 줄어든 프레임이 끼었다).
    /// </summary>
    private const int AimSettleMs = 80;

    /// <summary>지금 쓰는 배율(카운트/px). 처음엔 화면의 칸, 겨눌 때마다 배운다.</summary>
    private double? _aimScale;

    /// <summary>마지막 조준의 거리(px)와 보낸 양(카운트). 다음 새 화면에서 얼마나 줄었는지로 배율을 배운다.</summary>
    private (double OffsetX, double OffsetY, int CountX, int CountY)? _lastAim;

    private double AimScale
    {
        get
        {
            lock (_aimGate) return _aimScale ??= _host.AimScale;
        }
    }

    /// <summary>스크립트가 마지막으로 본 화면(검출들·가장가까운검출)의 프레임 시각. 0 이면 아직 안 봄.</summary>
    private long _seenFrameTicks;

    /// <summary>겨눈 뒤 새 화면을 이만큼(ms)까지 기다린다. 넘으면 false 로 돌아간다 - 검출이 멈췄을 수 있다.</summary>
    private const int AimWaitMs = 1500;

    /// <summary>이 프레임이 마지막 조준 뒤의 화면인가. 모르면(0) 그렇다고 본다.</summary>
    private bool IsAfterLastAim(long frameTicks) => frameTicks <= 0 || _lastAimTicks <= 0 || frameTicks > _lastAimTicks + AimSettleMs;

    /// <summary>
    /// 겨눈 뒤의 새 화면이 허브에 올라올 때까지 기다린다.
    /// </summary>
    /// <remarks>
    /// 쉬기 없는 반복문(<c>while (...) { if (조준(...)) 클릭(); }</c>)이 옛 화면에서 조준을 초당 수천 번 불러,
    /// 호출 로그가 1분에 10MB 를 넘고 화면 스레드가 밀려 검출이 0.7초에서 1.8초로 늦어졌다(실측). 기다려 주면
    /// 반복문이 저절로 검출 속도에 맞춰진다.
    /// </remarks>
    private void WaitForFreshFrame()
    {
        var deadline = Environment.TickCount64 + AimWaitMs;

        while (Environment.TickCount64 < deadline)
        {
            if (_host.Hub.Latest is { } latest && IsAfterLastAim(latest.FrameTicks)) return;

            Wait(15);
        }
    }

    /// <summary>조준 말고 다른 것이 시야를 움직였다. 다음 거리 변화는 배율 탓이 아니고, 조준 스레드도 놓는다(두 손이 한 마우스를 잡으면 안 된다).</summary>
    private void ForgetAim()
    {
        _lastAim = null;
        _aim?.Disengage();
    }

    /// <remarks>
    /// <b>같은 화면으로 두 번 겨누지 않는다</b> - 검출은 0.5~1초에 한 번인데 반복문은 0.1초마다 돈다. 같은 검출 자리로
    /// 예닐곱 번 겨누니 거리의 여섯 배를 돌아 검출을 지나쳐 흔들렸다(실측: 오버워치, 검출 자리가 0.7초마다 반대편으로 튐).
    /// 검출 자리를 찾은 프레임이 마지막 조준보다 앞이면, 그 자리는 조준 전의 것이라 건너뛴다.
    ///
    /// <b>큰 이동은 잘게</b> - 한 번에 수백 카운트를 넣으면 게임이 커서를 가운데로 되돌리기 전에 OS 커서가 창 밖으로
    /// 나가고, 이어진 클릭이 바탕 화면을 눌러 게임에서 빠져나왔다(실측: 앞 창이 "Program Manager" 가 됨).
    /// </remarks>
    /// <param name="toleranceX">맞았다고 볼 가로 범위(px). 좌표로 겨눌 때는 <see cref="LiveScriptHost.AimTolerancePx"/>.</param>
    /// <param name="hitX">맞았는지 잴 기준점. 안 주면 겨누는 곳과 같다 - 검출은 머리를 겨누되 몸 가운데에서 잰다.</param>
    private bool AimCore(int x, int y, int toleranceX, int toleranceY, int? hitX = null, int? hitY = null)
    {
        // 스크립트가 본 화면으로 판단한다. 아직 검출을 안 봤으면(좌표를 손으로 준 경우) 허브의 최신값으로.
        var seen = _seenFrameTicks > 0 ? _seenFrameTicks : _host.Hub.Latest?.FrameTicks ?? 0;

        if (!IsAfterLastAim(seen))
        {
            // 이 좌표는 겨누기 전 화면의 것이다. 새 화면을 기다렸다가, 움직이지 않고 돌아간다 - 다음 바퀴가 새 자리를 찾는다.
            WaitForFreshFrame();
            return false;
        }

        BeforeInput();

        // 좌표 조준은 한 번짜리 - 스레드가 잡고 있던 검출은 놓는다.
        _aim?.Disengage();

        var target = _host.Target() ?? throw Guard("대상 창이 없습니다 - 화면에서 창을 골라 시작(연결)하세요.");

        if (!CaptureTargetBounds.TryGet(target, out var bounds))
            throw Guard("대상 창의 자리를 알 수 없습니다 - 창이 닫혔거나 최소화됐습니다.");

        var centerX = bounds.Left + (bounds.Width / 2);
        var centerY = bounds.Top + (bounds.Height / 2);
        var offsetX = x - centerX;
        var offsetY = y - centerY;

        LearnAimScale(offsetX, offsetY);

        // 맞았는지는 따로 준 기준점에서 잰다 - 겨누는 곳(머리)과 다를 수 있다.
        var onTarget = Math.Abs((hitX ?? x) - centerX) <= toleranceX && Math.Abs((hitY ?? y) - centerY) <= toleranceY;

        // 필요한 만큼에서 조금 덜 보낸다(<see cref="AimDamping"/>). 지나치는 것이 모자란 것보다 나쁘다 -
        // 지나치면 반대편에서 다시 꺾어야 해서 화면이 좌우로 왕복한다. 모자라면 다음 화면에서 마저 당기면 된다.
        var deltaX = Math.Clamp((int)Math.Round(offsetX * AimScale * AimDamping), -MaxAimCounts, MaxAimCounts);
        var deltaY = Math.Clamp((int)Math.Round(offsetY * AimScale * AimDamping), -MaxAimCounts, MaxAimCounts);

        // 맞았어도 남은 몇 px 은 마저 당긴다. 다음 판정도 새 화면으로 하도록 시각은 늘 남긴다.
        // <b>자르고 나서 적는다</b> - 배율 배우기가 "보낸 양 ÷ 줄어든 거리" 로 재는데, 자르기 전 값을 적으면
        // 실제로 보낸 것보다 큰 값으로 재서 배율을 더 올린다. 실측에서 2,796 을 적고 1,200 을 보내고 있었다.
        _lastAimTicks = Environment.TickCount64;
        _lastAim = (offsetX, offsetY, deltaX, deltaY);

        // 이 조준으로 화면 속 물체가 몇 px 옮겨갈지. 목표 고정이 다음 화면에서 그만큼 옮겨 놓고 찾는다.
        _lockShiftX += deltaX / Math.Max(0.01, AimScale);
        _lockShiftY += deltaY / Math.Max(0.01, AimScale);

        if (deltaX == 0 && deltaY == 0) return onTarget;

        Logger.Debug($"조준: 거리({offsetX:0}, {offsetY:0}) × 배율 {AimScale:0.00} → 보냄({deltaX}, {deltaY}){(onTarget ? " · 맞음" : string.Empty)}");

        SendRelative(deltaX, deltaY);

        _lastAimTicks = Environment.TickCount64;
        return onTarget;
    }

    /// <summary>
    /// 지난 조준 뒤 거리가 얼마나 줄었는지로 배율(카운트/px)을 배운다.
    /// </summary>
    /// <remarks>
    /// <b>왜</b> - 필요한 배율은 게임·감도·해상도·시야각마다 다르다. 오버워치에서 100% 로 겨누니 한 번에 거리의 29% 만
    /// 줄었다(실측: 203→143→102, -228→-195→-106 ...). 사람이 340% 를 찾아 넣게 하지 말고, 겨눈 결과를 보고 맞춘다.
    /// 보낸 양 ÷ 실제로 줄어든 거리 = 배율. 지나친 값을 막으려고 한 번에 반만 따라가고, 한 축에서 15카운트·20px 넘게
    /// 움직였을 때만 배운다. 목표가 바뀌었을 수 있는 경우(거리가 거의 안 줄거나 반대로 두 배 넘게 넘어감)는 버린다.
    /// </remarks>
    private void LearnAimScale(double offsetX, double offsetY)
    {
        if (_lastAim is not { } last) return;

        // 많이 움직인 축으로 본다. 위아래는 대개 몇 px 이라 잡음이 크다.
        var (before, after, sent) = Math.Abs(last.CountX) >= Math.Abs(last.CountY)
            ? (last.OffsetX, offsetX, last.CountX)
            : (last.OffsetY, offsetY, last.CountY);

        LearnSample(before, after, sent);
    }

    /// <summary>
    /// 표본 하나 - 한 축에서 <paramref name="before"/>px 떨어져 있을 때 <paramref name="sent"/> 카운트를 보냈더니 <paramref name="after"/>px 가 됐다.
    /// 좌표 조준(한 번짜리)과 조준 스레드(프레임마다)가 같이 부른다. 스레드가 부르므로 잠금 아래.
    /// </summary>
    private void LearnSample(double before, double after, double sent)
    {
        // 일반(메뉴) 모드에서는 배율을 배우지도 저장하지도 않는다 - 그 배율은 게임 시야용이라 메뉴에서 잰 값이 섞이면 게임 조준이 틀어진다(사용자, 2026-09-19).
        if (_normalMouse) return;

        if (_host.AimScaleLearned is null || _host.IsAimScaleAuto?.Invoke() == false) return;

        lock (_aimGate) LearnSampleCore(before, after, sent);
    }

    private void LearnSampleCore(double before, double after, double sent)
    {
        // 너무 가깝거나 너무 먼 조준으로는 안 배운다 - 실측(오버워치 1920x1080)에서 잰 값이 이렇게 갈렸다.
        //   40~400px : 3.10 · 3.24 · 4.00 · 4.17 · 4.20  ← 일관된다
        //   21px     : 1.83   검출 사각형이 프레임마다 몇 px 씩 흔들려 잰 값이 통째로 뒤집힌다
        //   831px    : 1.33   상한(1,200)에 잘리고, 화면 가장자리는 원근 때문에 px 이 각도보다 빨리 는다
        // 저 둘이 섞이면 배율이 2.4 ↔ 3.6 으로 흔들리며 수렴하지 못한다(실측).
        if (Math.Abs(sent) < 15) return;
        if (Math.Abs(before) < MinLearnOffsetPx || Math.Abs(before) > MaxLearnOffsetPx) return;

        // 상한에 잘린 조준은 못 믿는다 - 보내려던 것을 다 못 보낸 것이라, 덜 움직인 이유가 배율 탓인지
        // 잘린 탓인지 가릴 수 없다. 배율이 한 번 커지면 모든 조준이 여기 걸려 서로를 키운다.
        if (Math.Abs(sent) >= MaxAimCounts) return;

        var moved = before - after;
        var fraction = moved / before;

        // 보낸 쪽과 줄어든 쪽이 반대면 같은 검출이 아니다 - 조준 스레드가 죽은 봇 옆의 다른 봇으로 이어 붙었을 때 "−198 → −8, 보낸 +203, 잰 값 0.10" 이 표본에 들어갔다(실측 2026-09-18).
        if (sent * moved <= 0)
        {
            Logger.Debug($"배율 배우기 버림: {before:0} → {after:0} (보낸 {sent:0} - 방향이 반대)");
            return;
        }

        // <b>가까워졌으면 믿는다 - 지나쳤더라도.</b> 예전에는 비율이 1을 넘으면(= 가운데를 지나쳐 반대편에 떨어짐)
        // 버렸는데, 그러면 배율이 모자랄 때는 올릴 수 있어도 <b>과할 때는 영영 못 내린다</b>. 실측: 배율 334% 로
        // 731px 을 겨누자 반대편 -598px 에 떨어졌고(비율 1.82) 그 값이 버려져, 화면이 좌우로 휙휙 왕복만 했다.
        //
        // 가르는 기준은 "비율이 1을 넘는가" 가 아니라 <b>"결국 가까워졌는가"</b> 다. 지나쳤어도 전보다 가까우면
        // 그 조준은 같은 목표를 향한 것이고 보낸 양도 믿을 만하다. 두 배 넘게 넘어갔거나(2.0 초과) 멀어졌으면
        // 목표가 다른 검출로 바뀐 것으로 보고 버린다 - 그런 값이 배율을 0.97→2.69→5.78 로 튀게 했었다.
        if (fraction < 0.2 || fraction > 2.0 || Math.Abs(after) >= Math.Abs(before))
        {
            Logger.Debug($"배율 배우기 버림: {before:0} → {after:0} (보낸 {sent}, 줄어든 비율 {fraction:0.00})");
            return;
        }

        var measured = Math.Clamp(sent / moved, MinAimScale, MaxAimScale);
        var scaleNow = _aimScale ??= _host.AimScale;

        // 지금 배율의 HighSampleRatio 배 넘는 표본은 곧바로 넣지 않고 떼어 둔다 - 달리는 봇을 따라가며 보낸 양이 섞인 표본은 늘 위로 틀려(5~9, 맞는 값 3.3~3.5)
        // 9개 중 4개쯤 섞이면서 배율을 4.4 까지 떠밀었다(사격장 실측 2026-09-19). 다만 <b>HighSampleRun 개가 연달아</b> 높으면 배율이 정말 낮은 것이다(처음 100% 로 시작 등) -
        // 떼어 둔 것을 한꺼번에 넣는다. 사이에 보통 표본이 하나라도 끼면 떼어 둔 것은 버린다.
        if (measured > scaleNow * HighSampleRatio)
        {
            _highSamples.Add(measured);

            if (_highSamples.Count < HighSampleRun)
            {
                Logger.Debug($"배율 표본 보류: 잰 값 {measured:0.00} 이 지금 배율 {scaleNow:0.00} 의 {HighSampleRatio}배를 넘는다 ({_highSamples.Count}/{HighSampleRun})");
                return;
            }

            Logger.Debug($"배율 표본 {HighSampleRun}개가 연달아 높다 - 배율이 낮은 것으로 보고 받는다 ([{string.Join(" ", _highSamples.Select(v => v.ToString("0.0")))}])");
            _aimSamples.AddRange(_highSamples);
            _highSamples.Clear();
        }
        else
        {
            _highSamples.Clear();

            // 잰 값 하나로 바꾸지 않는다. 여러 번 잰 것을 순서로 본다 - 이유는 AimSamples 에 적었다.
            _aimSamples.Add(measured);
        }

        while (_aimSamples.Count > AimSamples) _aimSamples.RemoveAt(0);
        if (_aimSamples.Count < AimSamples) return;

        var sorted = _aimSamples.OrderBy(v => v).ToArray();
        var current = _aimScale ??= _host.AimScale;

        // <b>9개 가운데 7개가 한쪽을 가리킬 때만 옮기고, 그 7번째 값까지만 간다</b> - 올릴 때는 아래에서 3번째, 내릴 때는 위에서 3번째. 가운뎃값은 한 무리가 다섯만 되면 넘어간다.
        // 이 게임의 표본은 거의 늘 위로 틀린다(검출이 움직이거나 입력이 덜 오른 화면이면 "보낸 만큼 안 좁혀졌다"). 실측(사격장 2026-09-19):
        //   [1.4 2.2 2.7 3.7 4.4 | 13.8 14.4 17.9 19.3 19.9] → 가운뎃값 13.8, 배율 3.8 → 5.7 → 8.6 → 12.9 로 뛰어 30초 가까이 못 쐈다.
        //   [3.1 3.4 3.5 | 6.0 6.5 6.5 7.5 7.7 8.9]           → 판을 시작할 때마다 3.5 → 5.3 으로 뛰었다가 천천히 돌아왔다. 맞는 값은 큰 꺾기들로 3.3~3.5.
        // 처음 배율이 크게 틀린 때는 표본이 다 한쪽에 모이니 그대로 배운다.
        var low = sorted[2];
        var high = sorted[^3];
        double estimate;

        if (low > current) estimate = low;
        else if (high < current) estimate = high;
        else
        {
            Logger.Debug($"배율 그대로 {current:0.00} - 표본이 양쪽에 있다 ([{string.Join(" ", sorted.Select(v => v.ToString("0.0")))}])");
            return;
        }

        // 그래도 한 번에 크게 바꾸지 않는다. 게임 안에서 감도가 바뀌는 일은 없으니 서둘 이유가 없다.
        // 가까우면(지금과 FarRatio 안) 차이의 일부만 다가간다 - 흩어진 표본에 배율이 그대로 따라 뛰어 4.75 로 84px 을 꺾다 한참 지나쳤다("조준이 너무 튀는데", 사용자 2026-09-19).
        // 멀면(처음 배율이 크게 틀림) 1.5배까지 한 번에 간다.
        var far = estimate > current * FarRatio || estimate < current / FarRatio;
        var target = far ? estimate : current + ((estimate - current) * NearStep);
        var next = Math.Clamp(Math.Clamp(target, current / 1.5, current * 1.5), MinAimScale, MaxAimScale);

        if (Math.Abs(next - current) / current < 0.02) return;

        Logger.Info($"배율 {current:0.00} → {next:0.00} ({before:0} → {after:0}, 보낸 {sent:0}, 잰 값 {measured:0.00}, " +
                    $"기준 {estimate:0.00} of [{string.Join(" ", sorted.Select(v => v.ToString("0.0")))}])");

        _aimScale = next;
        _host.AimScaleLearned!(next);
    }

    /// <summary>
    /// 누르기 전에 커서가 대상 창 안에 있는지. 밖이면 게임이 되돌릴 틈을 잠깐 주고, 그래도 밖이면 누르지 않는다.
    /// </summary>
    /// <remarks>
    /// 창 밖을 누르면 그 자리의 창(바탕 화면 등)이 앞으로 와 게임에서 빠져나간다. 입력이 앞 창에 들어가는 경로
    /// (SendInput·Interception)에서, 창을 잡고 있을 때만 본다.
    /// </remarks>
    private void EnsureCursorInsideTarget()
    {
        if (!_host.RequiresForeground || _host.Target() is not { Kind: CaptureTargetKind.Window } target) return;
        if (!CaptureTargetBounds.TryGet(target, out var bounds)) return;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (_host.Service.Adapter.GetCursorPosition() is not { } cursor) return;
            if (bounds.Contains(cursor.X, cursor.Y)) return;

            if (attempt == 9)
                throw Guard($"커서가 대상 창 밖({cursor.X}, {cursor.Y})이라 누르지 않았습니다 - 누르면 게임에서 빠져나갑니다. 조준 배율을 낮춰 보세요.");

            Wait(10);
        }
    }

    /// <summary>쉰다. 토큰으로 기다리므로 중지가 이 사이에 먹는다. 짧은 것은 로그에 안 남긴다 - 반복문이 초당 수십 줄을 만든다.</summary>
    public void Wait(int milliseconds)
    {
        ThrowIfStopping();

        if (milliseconds <= 0) return;

        if (milliseconds >= 500) _host.Trace?.Invoke(new ScriptCall(DateTime.Now, "Wait", milliseconds.ToString(), "", 0));

        // 쉬는 도중에 일시정지하면 남은 시간은 계속한 뒤에 마저 쉰다(멈춘 시간만큼 끝을 민다).
        var end = Environment.TickCount64 + milliseconds;

        while (true)
        {
            end += WaitWhilePaused();

            var left = end - Environment.TickCount64;
            if (left <= 0) return;

            if (_token.WaitHandle.WaitOne((int)Math.Min(left, 50))) ThrowIfStopping();
        }
    }

    public void 글자(string text) => Type(text);
    public void 줄입력(string text) => TypeLine(text);
    public void 엔터() => Enter();
    public void 한영() => ToggleHangul();
    public void 클릭(object? 버튼또는시간 = null, int 누르는시간 = 0) => Click(버튼또는시간, 누르는시간);
    public void 우클릭(int 누르는시간 = 0) => RightClick(누르는시간);
    public void 이동(int x, int y) => MoveTo(x, y);
    public void 이동클릭(int x, int y, object? button = null) => ClickAt(x, y, button);
    public void 휠(int notches) => Scroll(notches);
    public void 버튼누르기(object? 버튼 = null) => MouseDown(버튼);
    public void 버튼떼기(object? 버튼 = null) => MouseUp(버튼);
    public void 끌기(int x, int y, object? 버튼 = null) => Drag(x, y, 버튼);
    public void 상대끌기(int dx, int dy, object? 버튼 = null) => DragBy(dx, dy, 버튼);
    public bool 조준(int x, int y) => Aim(x, y);
    public bool 조준(ScriptDetection 검출) => Aim(검출);
    public void 상대이동(int deltaX, int deltaY) => MoveBy(deltaX, deltaY);
    public void 쉬기(int milliseconds) => Wait(milliseconds);

    // ── 화면 읽기 ────────────────────────────────────────────────────────

    /// <summary>지금 찾은 검출들. 화면 픽셀 자리로.</summary>
    public IReadOnlyList<ScriptDetection> Detections() => Traced("Detections", "", DetectionsCore);

    /// <summary>화면 가운데에서 가장 가까운 검출. 없으면 null.</summary>
    public ScriptDetection? NearestDetection() => Traced("NearestDetection", "", NearestDetectionCore);

    /// <summary>잡을 때까지 같은 검출만 본다. 놓치면 잠깐 기다렸다 새로 고른다. 없으면 null.</summary>
    public ScriptDetection? TargetDetection() => Traced("TargetDetection", "", TargetDetectionCore);

    /// <summary>고정한 목표를 놓는다. 잡은 뒤 다음 검출로 넘어갈 때.</summary>
    public void ReleaseTarget() => Traced("ReleaseTarget", "", ReleaseTargetCore);

    /// <summary>검출이 보일 때까지 최대 ms 기다린다. 50ms 마다 본다. 못 보면 null.</summary>
    public ScriptDetection? WaitDetection(int milliseconds) => Traced("WaitDetection", milliseconds.ToString(), () => WaitDetectionCore(milliseconds));

    /// <summary>그 자리(0~1 비율)의 글자를 읽는다.</summary>
    public string ReadText(double x, double y, double width, double height)
        => Traced("ReadText", $"{x:0.###}, {y:0.###}, {width:0.###}, {height:0.###}", () => ReadTextCore(x, y, width, height));

    private IReadOnlyList<ScriptDetection> DetectionsCore()
    {
        ThrowIfStopping();

        var hub = _host.Hub;

        if (!hub.IsCapturing) throw Guard("눈이 없습니다 - 화면에서 시작(연결)을 눌러 창을 잡아야 검출을 볼 수 있습니다.");
        if (!hub.IsDetecting) throw Guard("검출이 꺼져 있습니다 - 화면에서 검출을 켜세요.");

        var snapshot = hub.Latest;

        // 스크립트가 본 화면. 조준은 이 화면이 겨눈 뒤의 것인지로 판단한다 - 허브의 최신값으로 보면, 검출을 찾은 뒤
        // 조준하기 전 찰나에 새 화면이 올라온 경우 옛 자리로 한 번 더 겨눈다(실측: 같은 검출을 두 번 겨눠 지나침).
        _seenFrameTicks = snapshot?.FrameTicks ?? 0;

        if (snapshot is null || snapshot.Found.Count == 0) return [];

        var target = _host.Target();
        if (target is null || !CaptureTargetBounds.TryGet(target, out var bounds)) return [];

        var mobs = new List<ScriptDetection>(snapshot.Found.Count);
        _nameplates ??= new ScriptNameplateReader(ReadNameplateCore);

        for (var i = 0; i < snapshot.Found.Count; i++)
        {
            var d = snapshot.Found[i];
            var center = PreviewInputMapper.MapRatioToScreen(new Point(d.Box.CenterX, d.Box.CenterY), bounds);
            mobs.Add(new ScriptDetection(d.Label, d.Score,
                (int)Math.Round(center.X), (int)Math.Round(center.Y),
                (int)Math.Round(d.Box.Width * bounds.Width), (int)Math.Round(d.Box.Height * bounds.Height), string.Empty)
            {
                Box = d.Box,
                Reader = _nameplates
            });
        }

        return mobs;
    }

    /// <summary>
    /// <b>잡을 때까지 같은 검출만 본다.</b> 없으면 잠깐 기다렸다가 새로 고른다.
    /// </summary>
    /// <remarks>
    /// <b>왜</b> - <see cref="NearestDetection"/> 는 부를 때마다 그 순간 가장 가까운 것을 고른다. 검출이 둘이면
    /// A 로 돌다가 A 를 지나치는 순간 B 가 가까워져 B 로 돌고, 다시 A 가 가까워져 A 로 돈다 - 화면이 좌우로
    /// 휙휙 왕복만 하고 아무것도 못 잡는다(실측: 거리가 837 → -598 → 835 → -409 → 837 로 반복).
    ///
    /// <b>어려운 점은 "같은 검출인지" 를 가리는 것이다.</b> 겨누면 화면이 돌아서 검출의 화면 좌표가 크게 움직인다 -
    /// 자리가 비슷한 것을 찾으면 정작 크게 돌았을 때 놓친다. 그래서 <b>보낸 양만큼 옮겨 놓고</b> 찾는다
    /// (보낸 카운트 ÷ 배율 = 화면이 밀린 px). 배율이 틀려도 방향은 맞으므로 가까운 것을 고르는 데는 충분하다.
    ///
    /// 못 찾으면 바로 다른 검출로 갈아타지 않고 <see cref="LockGraceMs"/> 동안 null 을 준다 - 잠깐 가려진 것과
    /// 죽은 것을 구별할 길이 없으니, 그 사이 스크립트는 쉬었다 다시 부른다. 그 뒤에는 새로 고른다.
    /// 잡았으면 <see cref="ReleaseTarget"/>(목표풀기) 로 놓아 다음 검출로 넘어간다.
    /// </remarks>
    private ScriptDetection? TargetDetectionCore()
    {
        var mobs = DetectionsCore();

        // 조준 스레드가 붙잡고 있으면 그것이 목표다 - 스레드가 프레임마다 같은 검출을 잇고 예측하므로 여기서 따로 찾지 않는다(둘이 다른 검출을 고르면 안 된다).
        // 놓쳤으면(400ms 넘게 못 봄) 스레드가 스스로 놓고, 아래에서 새로 고른다.
        if (_aim is { IsEngaged: true } loop)
        {
            if (_host.Target() is { } target && CaptureTargetBounds.TryGet(target, out var bounds) && loop.CurrentDetection(bounds, _nameplates) is { } tracked)
            {
                _locked = tracked;
                return tracked;
            }

            ReleaseTargetCore();
        }

        if (_locked is { } locked)
        {
            // 지난 조준이 민 만큼 옮겨 놓고 그 자리에서 가장 가까운 것을 본다.
            var predictedX = locked.CenterX - _lockShiftX;
            var predictedY = locked.CenterY - _lockShiftY;
            var radius = Math.Max(locked.Width, locked.Height) * LockRadiusFactor;

            var best = mobs
                .Select(m => (Detection: m, Distance: Math.Sqrt(Sq(m.CenterX - predictedX) + Sq(m.CenterY - predictedY))))
                .OrderBy(t => t.Distance)
                .FirstOrDefault();

            if (best.Detection is not null && best.Distance <= radius)
            {
                _locked = Smooth(best.Detection, predictedX, predictedY, locked);
                _lockShiftX = 0;
                _lockShiftY = 0;
                _lockedMissTicks = 0;

                return _locked;
            }

            // 놓쳤다. 죽었는지 잠깐 가려졌는지 모르니 조금 기다려 본다.
            if (_lockedMissTicks == 0) _lockedMissTicks = Environment.TickCount64;

            if (Environment.TickCount64 - _lockedMissTicks < LockGraceMs)
            {
                Logger.Debug($"목표를 놓쳤다 - {LockGraceMs}ms 까지 기다린다 (예상 자리 {predictedX:0}, {predictedY:0} · 보인 것 {mobs.Count}마리)");

                return null;
            }

            Logger.Debug("목표를 놓친 채 시간이 지났다 - 새로 고른다.");
            ReleaseTargetCore();
        }

        var picked = NearestOf(mobs);

        // 멀리 있는 새 목표는 한 번 더 보고 움직인다 - 아래 Confirm 참고.
        var candidate = _candidate;
        if (picked is not null && !Confirmed(picked)) return null;

        // 두 번 본 먼 목표는 두 사각형의 가운데로 첫 조준을 한다 - 크게 도는 한 번이 한 장의 흔들림에 머리를 빗나가지 않게(Smooth 참고).
        // 화면은 그사이 안 돌았다(움직이지 않고 돌아갔으므로) - 그대로 섞어도 된다.
        if (picked is not null && candidate is not null && !ReferenceEquals(candidate, picked) && ReferenceEquals(_candidate, picked))
            picked = picked with
            {
                CenterX = (int)Math.Round((picked.CenterX + candidate.CenterX) / 2.0),
                CenterY = (int)Math.Round((picked.CenterY + candidate.CenterY) / 2.0),
                Width = (int)Math.Round((picked.Width + candidate.Width) / 2.0),
                Height = (int)Math.Round((picked.Height + candidate.Height) / 2.0)
            };

        _locked = picked;
        _lockShiftX = 0;
        _lockShiftY = 0;

        return _locked;
    }

    /// <summary>
    /// 새로 고른 목표를 믿어도 되는가. <b>멀리 있는 것은 두 번 연속 같은 자리에 보여야</b> 참이다.
    /// </summary>
    /// <remarks>
    /// <b>왜</b> - 한 프레임만 반짝한 헛것을 보고 화면이 확 돌아 버린다(실측: 0마리가 이어지다 한 프레임에
    /// 「일반 봇 78%」 가 창 오른쪽 위 구석에 떴고, 그 한 장으로 1200,-1200 을 보내 시야가 오른쪽 위로 튀었다.
    /// 다음 프레임은 다시 0마리였다). 추적(<c>DetectionTracker</c>)이 한 프레임짜리를 거르지만, 놓친 것을
    /// 두 프레임까지 이어 주기도 해서 이렇게 새는 것이 있다.
    ///
    /// <b>가까운 것은 그냥 믿는다.</b> 확인하느라 한 프레임(0.08초)을 버리는데, 코앞의 검출은 틀려도 조금 움직일
    /// 뿐이라 그 값이 아깝다. 크게 돌아야 하는 것만 - 헛것이 비싼 이유가 "크게 돈다" 는 것이므로 문턱도 거기 둔다.
    /// </remarks>
    private bool Confirmed(ScriptDetection mob)
    {
        var target = _host.Target();

        if (target is null || !CaptureTargetBounds.TryGet(target, out var bounds)) return true;

        var offsetX = mob.CenterX - (bounds.Left + (bounds.Width / 2));
        var offsetY = mob.CenterY - (bounds.Top + (bounds.Height / 2));

        if (Math.Sqrt(Sq(offsetX) + Sq(offsetY)) <= ConfirmDistancePx)
        {
            _candidate = null;
            return true;
        }

        // 지난번에도 거의 같은 자리에서 봤는가. 화면은 그사이 안 돌았다(움직이지 않고 돌아갔으므로).
        var near = _candidate is { } last
                   && Math.Sqrt(Sq(mob.CenterX - last.CenterX) + Sq(mob.CenterY - last.CenterY))
                      <= Math.Max(mob.Width, mob.Height) * LockRadiusFactor;

        _candidate = mob;

        if (!near) Logger.Debug($"먼 목표({offsetX:0}, {offsetY:0})를 처음 봤다 - 한 번 더 보고 움직인다.");

        return near;
    }

    /// <summary>
    /// 고정한 목표의 자리·크기를 프레임 사이에서 이어 준다 - 새로 본 사각형으로 바로 바꾸지 않고 예상 자리에서 일부만 따라간다.
    /// </summary>
    /// <remarks>
    /// <b>왜</b>(사용자, 2026-09-18 "화면 이동하고 나서 머리로 이동하는 게 부자연스럽다") - 검출 사각형은 같은 검출이 가만히 있어도
    /// 프레임마다 흔들린다. 실측(오버워치 사격장 로그, 조준·클릭 없이 이어 본 같은 목표 2,666쌍): 가운데가 세로 중간값 17px(표준편차 28)·
    /// 가로 11px. 머리 자리는 사각형 높이로 잡으니 높이 흔들림까지 더해진다. 그래서 크게 돌아 머리에 거의 닿은 뒤에도, 새 화면마다
    /// 흔들린 머리로 따로 한 번 더 움직여 "돌고 멈췄다가 머리로" 가 됐다.
    ///
    /// 세로·크기는 더 믿지 않는다(<see cref="SmoothVertical"/>) - 사격장 봇·사람은 주로 옆으로 움직이고 위아래로는 거의 안 움직인다.
    /// 가로는 검출이 실제로 움직이므로 많이 따라간다(<see cref="SmoothHorizontal"/>). 예상 자리는 조준이 민 만큼 옮긴 것이라 화면이 돌아도 뒤처지지 않는다.
    /// </remarks>
    private static ScriptDetection Smooth(ScriptDetection seen, double predictedX, double predictedY, ScriptDetection previous)
        => seen with
        {
            CenterX = (int)Math.Round(predictedX + ((seen.CenterX - predictedX) * SmoothHorizontal)),
            CenterY = (int)Math.Round(predictedY + ((seen.CenterY - predictedY) * SmoothVertical)),
            Width = (int)Math.Round(previous.Width + ((seen.Width - previous.Width) * SmoothVertical)),
            Height = (int)Math.Round(previous.Height + ((seen.Height - previous.Height) * SmoothVertical))
        };

    /// <summary>고정한 목표의 가로 자리를 새 사각형 쪽으로 이만큼 옮긴다(0~1). 검출이 옆으로 달리므로 크게.</summary>
    private const double SmoothHorizontal = 0.6;

    /// <summary>고정한 목표의 세로 자리·크기를 새 사각형 쪽으로 이만큼 옮긴다(0~1). 흔들림은 크고 실제 움직임은 작아 작게.</summary>
    private const double SmoothVertical = 0.35;

    /// <summary>두 번 연속 봐야 믿는 거리(px). 이 안쪽이면 바로 겨눈다.</summary>
    private const double ConfirmDistancePx = 300;

    /// <summary>지난번에 새로 고르려던 목표. 두 번 연속 같은 자리에 보이는지 견주는 데만 쓴다.</summary>
    private ScriptDetection? _candidate;

    /// <summary>고정한 목표를 놓는다. 잡은 뒤 다음 검출로 넘어갈 때 부른다.</summary>
    private void ReleaseTargetCore()
    {
        _locked = null;
        _lockedMissTicks = 0;
        _lockShiftX = 0;
        _lockShiftY = 0;
        _aim?.Disengage();
    }

    private ScriptDetection? NearestOf(IReadOnlyList<ScriptDetection> mobs)
    {
        var target = _host.Target();
        if (target is null || !CaptureTargetBounds.TryGet(target, out var bounds)) return null;

        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);

        return mobs.OrderBy(m => Sq(m.CenterX - cx) + Sq(m.CenterY - cy)).FirstOrDefault();
    }

    private static double Sq(double value) => value * value;

    /// <summary>고정한 목표. 같은 것을 계속 본다.</summary>
    private ScriptDetection? _locked;

    /// <summary>목표를 처음 놓친 시각. 0 이면 놓치지 않았다.</summary>
    private long _lockedMissTicks;

    /// <summary>지난 조준으로 화면이 밀린 양(px). 목표를 다시 찾을 때 예상 자리를 여기만큼 옮긴다.</summary>
    private double _lockShiftX;

    private double _lockShiftY;

    /// <summary>같은 검출로 볼 거리. 검출 크기의 몇 배까지 - 겨눈 뒤 자리가 꽤 움직이므로 넉넉해야 한다.</summary>
    private const double LockRadiusFactor = 2.0;

    /// <summary>목표를 놓친 뒤 새로 고르기까지 기다리는 시간(ms). 잠깐 가려진 것과 죽은 것을 구별할 길이 없다.</summary>
    private const int LockGraceMs = 400;

    private ScriptDetection? NearestDetectionCore()
    {
        var target = _host.Target();
        if (target is null || !CaptureTargetBounds.TryGet(target, out var bounds)) return null;

        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);

        return DetectionsCore().OrderBy(m => ((m.CenterX - cx) * (m.CenterX - cx)) + ((m.CenterY - cy) * (m.CenterY - cy))).FirstOrDefault();
    }

    private ScriptDetection? WaitDetectionCore(int milliseconds)
    {
        var deadline = Environment.TickCount64 + Math.Max(0, milliseconds);

        while (true)
        {
            var nearest = NearestDetectionCore();
            if (nearest is not null) return nearest;

            if (Environment.TickCount64 >= deadline) return null;

            Wait(50);
        }
    }

    /// <summary>프레임 한 장을 기다리는 시간(ms). 리드백이 꺼져 있다가 켜지는 중이면 그때부터 다시 이만큼 기다린다.</summary>
    private const int FrameWaitMs = 1500;

    private ScriptNameplateReader? _nameplates;

    /// <summary>검출 사각형 위 이름표를 지금 화면에서 읽는다(<c>검출.이름표</c>). 칸이 화면 밖이면 빈 글.</summary>
    private string ReadNameplateCore(Minguk.Tools.Vision.Labeling.LabelBox box)
        => Traced("Nameplate", $"{box.CenterX:0.###}, {box.CenterY:0.###}", () =>
        {
            var region = Minguk.Tools.Vision.Ocr.NameplateRegion.Above(box);

            return region.IsEmpty ? string.Empty : ReadTextCore(region.X, region.Y, region.Width, region.Height);
        });

    private string ReadTextCore(double x, double y, double width, double height)
    {
        // 자르기가 먼저다 - 메서드 인자 평가 순서상 Ocr() 을 먼저 부르면 프레임을 기다리기도 전에 "엔진이 없다" 로 먼저 실패한다.
        // 눈이 없다는 안내(CropFor 안)가 엔진이 없다는 안내보다 앞서야 한다 - 사람이 먼저 볼 문제가 그것이다.
        var crop = CropFor(new Rect(x, y, width, height));
        var outcome = Ocr().RecognizeAsync(crop, _token).GetAwaiter().GetResult();

        return outcome.Text.Replace(Environment.NewLine, " ").Trim();
    }

    // ── 글자 찾기 - 메뉴·버튼 ────────────────────────────────────────────

    /// <summary>화면 전체(또는 이름 붙인 자리)에서 그 글이 든 낱말·줄을 찾아 자리를 준다. 없으면 null.</summary>
    /// <remarks>
    /// 사용자(2026-09-18) "특정 메뉴를 눌러야 한다" - 게임 메뉴는 글자라 OCR 로 찾는다. <c>if (글자찾기("사격장") is { } 자리) 이동클릭(자리.x, 자리.y);</c>
    /// <b>화면 전체는 조각내어 읽는다</b>(<see cref="FindTextTiles"/>) - 줄 찾기 모델은 긴 변을 640 으로 줄이므로 1080p 를 통째로 넣으면 30px 글자가 10px 이 되어 놓친다.
    /// 조각마다 OCR 이 한 번씩이라(6조각이면 0.3~0.6초) 매 프레임이 아니라 메뉴가 떴을 때 부른다. 자리를 주면 그 자리만 읽어 빠르다.
    /// 여러 곳에 있으면 가장 위·왼쪽 것. 낱말 하나에 다 들어 있으면 그 낱말의 자리, 여러 낱말에 걸치면 그 줄의 자리.
    /// </remarks>
    public ScriptSpot? FindText(string text, string? regionName = null)
        => Traced("FindText", regionName is null ? Quote(text) : $"{Quote(text)}, {Quote(regionName)}", () => FindTextCore(text, regionName));

    /// <summary>글을 찾아 그 가운데를 누른다. 찾았으면 참.</summary>
    public bool PressText(string text, string? regionName = null, object? button = null)
        => Traced("PressText", regionName is null ? Quote(text) : $"{Quote(text)}, {Quote(regionName)}", () =>
        {
            if (FindTextCore(text, regionName) is not { } spot) return false;

            ClickAt(spot.CenterX, spot.CenterY, button);
            return true;
        });

    /// <summary>화면 전체를 이만큼씩 나눠 읽는다(가로 × 세로). 1080p 에서 조각 하나가 640×540 - 줄 찾기 모델(640)에 거의 그대로 들어간다.</summary>
    private const int FindTextTiles = 3;

    private const int FindTextRows = 2;

    /// <summary>이웃 조각과 겹치는 몫 - 경계에 걸린 글자가 어느 한쪽에는 통째로 들어가게.</summary>
    private const double FindTextOverlap = 0.15;

    private ScriptSpot? FindTextCore(string text, string? regionName)
    {
        if (string.IsNullOrWhiteSpace(text)) throw Guard("찾을 글이 비어 있습니다.");

        var target = _host.Target() ?? throw Guard("대상 창이 없습니다 - 화면에서 창을 골라 시작(연결)하세요.");

        if (!CaptureTargetBounds.TryGet(target, out var bounds))
            throw Guard("대상 창의 자리를 알 수 없습니다 - 창이 닫혔거나 최소화됐습니다.");

        var wanted = text.Trim();
        var tiles = new List<Rect>();

        if (regionName is not null)
        {
            var book = _host.Regions?.Invoke() ?? throw Guard("영역 목록이 없습니다 - 화면에서 데이터셋 폴더를 골라야 합니다.");
            var found = book.Resolve(regionName) ?? throw Guard(MissingRegion(book, regionName));

            // 칸의 상자(돌린 칸은 감싸는 상자) - 찾은 자리를 화면 좌표로 되돌리려면 자른 자리를 알아야 한다.
            _host.Hub.TryGetFrameSize(out var frameWidth, out var frameHeight);

            foreach (var region in RegionTargets.Of(found.Region, found.Cell)) tiles.Add(RegionTargets.Bounds(region, frameWidth, frameHeight));
        }
        else
        {
            var w = 1.0 / FindTextTiles;
            var h = 1.0 / FindTextRows;

            for (var row = 0; row < FindTextRows; row++)
                for (var col = 0; col < FindTextTiles; col++)
                {
                    var x = Math.Max(0, (col * w) - (w * FindTextOverlap));
                    var y = Math.Max(0, (row * h) - (h * FindTextOverlap));
                    var right = Math.Min(1, ((col + 1) * w) + (w * FindTextOverlap));
                    var bottom = Math.Min(1, ((row + 1) * h) + (h * FindTextOverlap));

                    tiles.Add(new Rect(x, y, right - x, bottom - y));
                }
        }

        ScriptSpot? best = null;

        foreach (var tile in tiles)
        {
            ThrowIfStopping();

            var crop = CropFor(tile);
            var outcome = Ocr().RecognizeAsync(crop, _token).GetAwaiter().GetResult();

            foreach (var line in outcome.Lines)
            {
                // 낱말 하나에 다 있으면 그 낱말, 아니면 줄 전체(띄어쓰기를 무시하고 견준다 - OCR 이 「사격 장」 으로 나누기도 한다).
                var word = line.Words.FirstOrDefault(w => Squash(w.Text).Contains(Squash(wanted), StringComparison.OrdinalIgnoreCase));
                var hit = word.Text is not null ? word : Squash(line.Text).Contains(Squash(wanted), StringComparison.OrdinalIgnoreCase) ? Union(line) : default;

                if (hit.Text is null) continue;

                var spot = ToSpot(hit, tile, bounds);

                if (best is null || spot.CenterY < best.CenterY - 4 || (Math.Abs(spot.CenterY - best.CenterY) <= 4 && spot.CenterX < best.CenterX)) best = spot;
            }
        }

        return best;
    }

    private static string Squash(string text) => text.Replace(" ", string.Empty);

    // ── 그림 찾기 - 그림으로 된 메뉴·버튼 ────────────────────────────────

    /// <summary>같은 그림으로 볼 닮음(0~1). 게임 메뉴는 마우스를 올리면 밝아져 1 이 되지 않는다 - 정규화 상호상관이라 0.8 이면 사실상 같은 그림이다.</summary>
    public const double DefaultImageScore = 0.8;

    /// <summary>
    /// 화면에서 본보기 그림을 찾아 자리를 준다. 못 찾거나 닮음이 문턱 아래면 null.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-18) "사격장 글이 아니고 큰 이미지인데" - 메뉴 버튼이 그림이면 <c>글자찾기</c> 로는 못 찾는다. 본보기는 프로젝트 <c>Resources</c> 의 PNG
    /// (영역 패널의 「본보기로 저장」 으로 만든다). 밝기·대비가 달라져도 견딘다(<see cref="TemplateMatch"/>) - 크기가 달라지면 못 찾는다(해상도를 바꿨으면 본보기도 다시).
    /// 자리 이름을 주면 그 자리 안에서만 찾아 훨씬 빠르다.
    /// </remarks>
    public ScriptSpot? FindImage(string resourceName, double minimumScore = DefaultImageScore, string? regionName = null)
        => Traced("FindImage", regionName is null ? Quote(resourceName) : $"{Quote(resourceName)}, {Quote(regionName)}", () => FindImageCore(resourceName, minimumScore, regionName));

    /// <summary>본보기가 화면(또는 그 자리)에 있는가.</summary>
    public bool HasImage(string resourceName, double minimumScore = DefaultImageScore, string? regionName = null)
        => Traced("HasImage", Quote(resourceName), () => FindImageCore(resourceName, minimumScore, regionName) is not null);

    /// <summary>본보기가 얼마나 닮았는지(0~1) - 문턱을 잡을 때 눈으로 본다. 못 찾으면 0.</summary>
    public double ImageScore(string resourceName, string? regionName = null)
        => Traced("ImageScore", Quote(resourceName), () => FindImageCore(resourceName, -1, regionName)?.Score ?? 0);

    /// <summary>본보기를 찾아 그 가운데를 누른다. 찾았으면 참.</summary>
    public bool PressImage(string resourceName, double minimumScore = DefaultImageScore, string? regionName = null)
        => Traced("PressImage", Quote(resourceName), () =>
        {
            if (FindImageCore(resourceName, minimumScore, regionName) is not { } spot) return false;

            ClickAt(spot.CenterX, spot.CenterY);
            return true;
        });

    /// <summary>읽어 둔 본보기 - 파일을 읽고 회색조로 바꾸는 값을 부를 때마다 치르지 않는다.</summary>
    private readonly Dictionary<string, Vision.Matching.GrayImage> _templates = new(StringComparer.OrdinalIgnoreCase);

    private ScriptSpot? FindImageCore(string resourceName, double minimumScore, string? regionName)
    {
        var target = _host.Target() ?? throw Guard("대상 창이 없습니다 - 화면에서 창을 골라 시작(연결)하세요.");

        if (!CaptureTargetBounds.TryGet(target, out var bounds))
            throw Guard("대상 창의 자리를 알 수 없습니다 - 창이 닫혔거나 최소화됐습니다.");

        var needle = Template(resourceName);

        // 찾을 자리 - 이름을 주면 그 자리, 아니면 화면 전체.
        var area = new Rect(0, 0, 1, 1);

        if (regionName is not null)
        {
            var book = _host.Regions?.Invoke() ?? throw Guard("영역 목록이 없습니다 - 화면에서 데이터셋 폴더를 골라야 합니다.");
            var found = book.Resolve(regionName) ?? throw Guard(MissingRegion(book, regionName));

            _host.Hub.TryGetFrameSize(out var frameWidth, out var frameHeight);
            area = RegionTargets.Bounds(RegionTargets.Of(found.Region, found.Cell)[0], frameWidth, frameHeight);
        }

        var haystack = Vision.Matching.GrayImage.From(CropFor(area));

        if (needle.Width > haystack.Width || needle.Height > haystack.Height)
            throw Guard($"본보기 「{resourceName}」({needle.Width}x{needle.Height})가 찾을 자리({haystack.Width}x{haystack.Height})보다 큽니다 - 게임 해상도가 바뀌었으면 본보기를 다시 만드세요.");

        if (Vision.Matching.TemplateMatch.Find(haystack, needle) is not { } hit || hit.Score < minimumScore) return null;

        // 자른 자리 안의 비율 → 프레임 비율 → 화면 픽셀.
        var cx = area.X + (hit.CenterX * area.Width);
        var cy = area.Y + (hit.CenterY * area.Height);
        var center = PreviewInputMapper.MapRatioToScreen(new Point(cx, cy), bounds);

        return new ScriptSpot((int)Math.Round(center.X), (int)Math.Round(center.Y),
            (int)Math.Round(hit.Width * area.Width * bounds.Width), (int)Math.Round(hit.Height * area.Height * bounds.Height), resourceName)
        {
            Score = hit.Score
        };
    }

    // ── 영역 그대로 누르기 ───────────────────────────────────────────────

    /// <summary>
    /// 이름 붙인 자리(또는 "자리.칸")의 <b>가운데</b> 자리를 준다 - 화면 픽셀. 자리가 없으면 멈춘다.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-21) "영역 그냥 누르고 싶은데" - 버튼이 늘 같은 데 있으면 글자·그림을 찾을 것 없이 그 자리를 그냥 누르면 된다.
    /// 자리는 0~1 비율로 저장되므로 창 크기가 바뀌어도 따라간다. 찾는 것이 없으니 <see cref="PressImage"/>·<see cref="PressText"/> 보다 빠르고 확실하다 -
    /// 대신 화면이 바뀌어 그 자리에 다른 것이 있어도 그냥 누른다(눌러도 되는지는 <c>글자있나</c>·<c>그림있나</c> 로 먼저 본다).
    /// </remarks>
    public ScriptSpot RegionSpot(string name) => Traced("RegionSpot", Quote(name), () => RegionSpotCore(name))!;

    /// <summary>이름 붙인 자리의 가운데를 누른다.</summary>
    public void PressRegion(string name, object? button = null)
        => Traced("PressRegion", Quote(name), () =>
        {
            var spot = RegionSpotCore(name);
            ClickAt(spot.CenterX, spot.CenterY, button);
        });

    public ScriptSpot 영역자리(string 자리) => RegionSpot(자리);

    public void 영역누르기(string 자리) => PressRegion(자리);

    public void 영역누르기(string 자리, object? 버튼) => PressRegion(자리, 버튼);

    private ScriptSpot RegionSpotCore(string name)
    {
        var target = _host.Target() ?? throw Guard("대상 창이 없습니다 - 화면에서 창을 골라 시작(연결)하세요.");

        if (!CaptureTargetBounds.TryGet(target, out var bounds))
            throw Guard("대상 창의 자리를 알 수 없습니다 - 창이 닫혔거나 최소화됐습니다.");

        var book = _host.Regions?.Invoke() ?? throw Guard("영역 목록이 없습니다 - 화면에서 데이터셋 폴더를 골라야 합니다.");
        var found = book.Resolve(name) ?? throw Guard(MissingRegion(book, name));

        _host.Hub.TryGetFrameSize(out var frameWidth, out var frameHeight);

        // 칸을 줬으면 그 칸, 자리만 줬으면 자리 전체(돌린 칸이면 그 칸을 감싸는 사각형)의 가운데.
        var area = RegionTargets.Bounds(RegionTargets.Of(found.Region, found.Cell)[0], frameWidth, frameHeight);
        var center = PreviewInputMapper.MapRatioToScreen(new Point(area.X + (area.Width / 2), area.Y + (area.Height / 2)), bounds);

        return new ScriptSpot((int)Math.Round(center.X), (int)Math.Round(center.Y),
            (int)Math.Round(area.Width * bounds.Width), (int)Math.Round(area.Height * bounds.Height), name);
    }

    // ── 체력바 - 명중 확인 ───────────────────────────────────────────────

    /// <summary>
    /// 그 검출 머리 위 체력바가 몇 할 찼는가(0~1). 체력바를 못 찾으면 null.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-19) "체력바로 해줘" - 히트 마커는 가늘고 반투명해 폭발·데미지 숫자와 섞여 못 갈랐다(연속 저장 17장). 체력바는 봇마다 머리 위 한 줄이고
    /// 칸 색이 뚜렷하다. 게임마다 다른 색·범위는 프로젝트 폴더의 healthbar.json(<see cref="Vision.HealthBars.HealthBarSpec"/>)에 두고, 찾고 재는 규칙은
    /// 모든 게임이 같다(<see cref="Vision.HealthBars.HealthBarReader"/>). 못 읽은 순간의 조각은 프로젝트 폴더 진단\체력바\ 에 남는다.
    /// </remarks>
    public double? HealthBar(ScriptDetection mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        return Traced("HealthBar", mob.ToString(), () => HealthBarCore(mob));
    }

    public double? 체력바(ScriptDetection 검출) => HealthBar(검출);

    /// <summary>
    /// 방금 쏜 것이 맞았는가 - 쏘기 전 체력바(<paramref name="before"/>)보다 <paramref name="waitMs"/> 안에 줄면 참, 그대로면 거짓.
    /// 쏘기 전 값을 모르거나, 쏜 뒤 못 읽었거나 늘어나 보이기만 했으면(겹친 효과) null - 빗나감으로 세지 않게.
    /// </summary>
    /// <remarks>
    /// <c>var 전 = 체력바(검출); 클릭(); if (명중했나(검출, 전) == false) 빗나감++;</c> - 쏜 뒤 화면에 반영되기까지 0.1~0.2초라 기다리며 몇 번 본다.
    /// 봇이 움직이면 지금 검출 가운데 원래 자리에서 가장 가까운 것의 체력바를 본다.
    /// </remarks>
    public bool? HitByHealthBar(ScriptDetection mob, double? before, int waitMs = 300)
    {
        ArgumentNullException.ThrowIfNull(mob);

        return Traced("HitByHealthBar", $"{mob}, {before?.ToString("0.00") ?? "모름"}", () => HitByHealthBarCore(mob, before, waitMs));
    }

    public bool? 명중했나(ScriptDetection 검출, double? 쏘기전) => HitByHealthBar(검출, 쏘기전);

    public bool? 명중했나(ScriptDetection 검출, double? 쏘기전, int 기다림) => HitByHealthBar(검출, 쏘기전, 기다림);

    /// <summary>쏜 뒤 이만큼(ms) 지난 화면부터 "그대로" 를 믿는다 - 입력이 화면에 오르기까지 0.1초 안팎(조준 실측).</summary>
    private const int ShotLatencyMs = 120;


    private bool? HitByHealthBarCore(ScriptDetection mob, double? before, int waitMs)
    {
        if (before is null) return null;

        var start = Environment.TickCount64;
        var deadline = start + Math.Max(0, waitMs);
        var sawSame = false;

        while (true)
        {
            // 봇이 걸었으면 원래 자리에서 가장 가까운 지금 검출로 - 없으면 원래 자리 그대로 본다.
            var now = NearestTo(mob) ?? mob;

            if (HealthBarCore(now) is { } after)
            {
                var drop = HealthBarSpecOrThrow().Drop;

                if (after < before.Value - drop) return true;

                // 늘어난 것은 믿지 않는다 - 맞힌 직후 빨간 데미지 숫자·처치 효과가 바에 겹쳐 찬 칸이 는 것처럼 보였다(실측). 그대로일 때만 "빗나감" 쪽으로 센다.
                // 쏜 직후(입력이 화면에 오르기 전) 화면은 당연히 그대로다 - 그것으로 빗나감을 세면 안 된다.
                if (after <= before.Value + drop && Environment.TickCount64 - start >= ShotLatencyMs) sawSame = true;
            }

            if (Environment.TickCount64 >= deadline) return sawSame ? false : null;

            Wait(40);
        }
    }

    /// <summary>지금 검출 가운데 이 검출 자리에서 가장 가까운 것(몸 크기 안). 검출이 꺼져 있거나 없으면 null.</summary>
    private ScriptDetection? NearestTo(ScriptDetection mob)
    {
        if (!_host.Hub.IsDetecting) return null;

        var reach = Math.Max(mob.Width, mob.Height);

        return DetectionsCore()
            .Select(d => (d, Distance: Math.Sqrt(Math.Pow(d.CenterX - mob.CenterX, 2) + Math.Pow(d.CenterY - mob.CenterY, 2))))
            .Where(p => p.Distance <= reach)
            .OrderBy(p => p.Distance)
            .Select(p => p.d)
            .FirstOrDefault();
    }

    private double? HealthBarCore(ScriptDetection mob)
    {
        var spec = HealthBarSpecOrThrow();
        var target = _host.Target() ?? throw Guard("대상 창이 없습니다 - 화면에서 창을 골라 시작(연결)하세요.");

        if (!CaptureTargetBounds.TryGet(target, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0) return null;

        // 찾을 띠 - 검출 사각형 기준 배수(healthbar.json). 봇이 멀어 작아져도 같은 값으로 맞는다.
        var top = mob.CenterY - (mob.Height / 2.0);
        var left = Math.Max(bounds.Left, mob.CenterX - (spec.Side * mob.Width));
        var right = Math.Min(bounds.Right, mob.CenterX + (spec.Side * mob.Width));
        var up = Math.Max(bounds.Top, top - (spec.Above * mob.Height));
        var down = Math.Min(bounds.Bottom, top + (spec.Below * mob.Height));

        if (right - left < 10 || down - up < 4) return null;

        var area = new Rect((left - bounds.Left) / bounds.Width, (up - bounds.Top) / bounds.Height, (right - left) / bounds.Width, (down - up) / bounds.Height);
        var crop = CropFor(area);

        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(crop, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);

        // 몸 너비(조각 픽셀로 바꿔서)의 이만큼보다 짧은 줄은 바로 안 본다.
        var minRun = (int)(spec.MinWidth * mob.Width * (width / Math.Max(1.0, right - left)));
        var reading = Vision.HealthBars.HealthBarReader.Read(pixels, width, height, spec, minRun);

        if (reading is not { } found || found.Fraction <= 0) SaveHealthBarDiagnostic(converted, reading);

        return reading?.Fraction;
    }

    // ── 체력바 설정·진단 - 프로젝트 폴더 ────────────────────────────────

    private Vision.HealthBars.HealthBarSpec? _healthBarSpec;
    private DateTime _healthBarSpecStamp;

    /// <summary>
    /// 프로젝트 폴더의 healthbar.json. 고치면 다음 호출부터 먹는다. 없거나 틀리면 무엇을 적어야 하는지 말하고 멈춘다.
    /// </summary>
    private Vision.HealthBars.HealthBarSpec HealthBarSpecOrThrow()
    {
        if (_host.ResourceRoot is not { } root)
            throw Guard("체력바는 프로젝트로 돌릴 때만 씁니다 - 색·범위를 프로젝트 폴더의 healthbar.json 에서 읽습니다.");

        var path = System.IO.Path.Combine(root, Vision.HealthBars.HealthBarSpec.FileName);

        if (!System.IO.File.Exists(path))
            throw Guard($"프로젝트 폴더에 {Vision.HealthBars.HealthBarSpec.FileName} 이 없습니다 - 체력바 색을 적으세요. 예: " +
                        "{ \"filled\": [255, 66, 107], \"empty\": [110, 75, 112], \"tolerance\": 45 } (찬 칸·빈 칸 색 R,G,B, 허용 거리). " +
                        "색은 영역 패널 「연속 저장」 으로 뜬 조각에서 잽니다.");

        var stamp = System.IO.File.GetLastWriteTimeUtc(path);

        if (_healthBarSpec is null || stamp != _healthBarSpecStamp)
        {
            try
            {
                _healthBarSpec = Vision.HealthBars.HealthBarSpec.Load(path);
                _healthBarSpecStamp = stamp;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.IO.IOException)
            {
                Logger.Warn(ex, $"체력바 설정을 못 읽었다: {path}");
                throw Guard($"{Vision.HealthBars.HealthBarSpec.FileName} 을 읽지 못했습니다 - 형식을 보세요(filled·empty 는 [R, G, B] 세 숫자).");
            }
        }

        return _healthBarSpec!;
    }

    /// <summary>한 번 실행에 남길 진단 조각 수.</summary>
    private const int MaxHealthBarDiagnostics = 40;

    private int _healthBarDiagnostics;

    /// <summary>
    /// 못 읽었거나 0 으로 읽은 순간의 탐색 조각을 프로젝트 폴더 <c>진단\체력바\</c> 에 남긴다 - 색·범위(healthbar.json)를 실제 화면에 맞추는 재료(사용자, 2026-09-19).
    /// 한 번 실행에 <see cref="MaxHealthBarDiagnostics"/> 장까지. 이름에 바로 본 줄(y)과 찬 몫을 적는다.
    /// </summary>
    private void SaveHealthBarDiagnostic(System.Windows.Media.Imaging.BitmapSource crop, Vision.HealthBars.HealthBarReading? reading)
    {
        if (_healthBarDiagnostics >= MaxHealthBarDiagnostics || _host.ResourceRoot is not { } root) return;

        try
        {
            var folder = System.IO.Path.Combine(root, "진단", "체력바");
            System.IO.Directory.CreateDirectory(folder);

            var what = reading is { } r ? $"y{r.Row}_{r.Fraction:0.00}" : "없음";
            var path = System.IO.Path.Combine(folder, $"{DateTime.Now:HHmmss_fff}_{what}.png");

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));

            using (var file = System.IO.File.Create(path)) encoder.Save(file);

            _healthBarDiagnostics++;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "체력바 진단 조각을 못 남겼다");
        }
    }

    // ── 명중 확인 - 히트 마커 ────────────────────────────────────────────

    /// <summary>
    /// 방금 쏜 것이 맞았는가 - 조준점 둘레에 히트 마커(맞히면 잠깐 뜨는 X 표시)가 <paramref name="waitMs"/> 안에 뜨면 참.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-19) "조준 후 맞췄는지 못맞췄는지 모르지?" - <c>조준(검출)</c> 의 참은 "조준점이 몸 안" 까지다. 쏜 것이 맞았는지는 게임이 보여 주는
    /// 히트 마커로만 안다. 본보기(Resources 의 PNG)는 영역 패널의 「연속 저장」 으로 쏘는 동안의 화면을 여러 장 떠서 마커가 찍힌 것을 고른다.
    /// 화면 가운데 작은 자리만 보므로 한 번에 몇 ms 다. 마커는 0.2초쯤 떴다 사라지고 화면은 0.1초마다 올라오므로 기다림은 0.3초가 알맞다.
    /// 조준점 자체가 마커와 닮으면 늘 참이 된다 - 본보기는 마커의 X 획만 담기게(조준점 한가운데는 빼고) 잡는다.
    /// </remarks>
    public bool HitConfirmed(string resourceName, int waitMs = 300, double minimumScore = 0.7)
        => Traced("HitConfirmed", Quote(resourceName), () => HitConfirmedCore(resourceName, waitMs, minimumScore));

    private bool HitConfirmedCore(string resourceName, int waitMs, double minimumScore)
    {
        var needle = Template(resourceName);

        _host.Hub.WantsFrames = true;
        var deadline = Environment.TickCount64 + Math.Max(0, waitMs);

        while (true)
        {
            if (_host.Hub.TryGetFrameSize(out var width, out var height) && width > 0 && height > 0)
            {
                // 조준점 둘레 - 본보기의 세 배(최소 160px) 네모. 조준점은 화면 가운데다.
                var side = Math.Max(160, 3 * Math.Max(needle.Width, needle.Height));
                var w = Math.Min(1, side / (double)width);
                var h = Math.Min(1, side / (double)height);
                var area = new Rect(0.5 - (w / 2), 0.5 - (h / 2), w, h);

                if (_host.Hub.TryCropFrame(area, out var crop) && crop is not null)
                {
                    var haystack = Vision.Matching.GrayImage.From(crop);

                    if (needle.Width <= haystack.Width && needle.Height <= haystack.Height
                        && Vision.Matching.TemplateMatch.Find(haystack, needle) is { } hit && hit.Score >= minimumScore)
                        return true;
                }
            }

            if (Environment.TickCount64 >= deadline) return false;

            Wait(30);
        }
    }

    public bool 명중확인(string 본보기) => HitConfirmed(본보기);

    public bool 명중확인(string 본보기, int 기다림) => HitConfirmed(본보기, 기다림);

    public bool 명중확인(string 본보기, int 기다림, double 문턱) => HitConfirmed(본보기, 기다림, 문턱);

    private Vision.Matching.GrayImage Template(string resourceName)
    {
        if (_templates.TryGetValue(resourceName, out var cached)) return cached;

        var path = FindResource(resourceName);
        var image = new System.Windows.Media.Imaging.BitmapImage();

        // 파일을 물고 있지 않게 통째로 읽어 둔다 - 스크립트가 도는 동안 사람이 본보기를 다시 저장할 수 있다.
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();

        // 알파가 마스크다 - 영역 이미지 저장이 구역의 다각형 밖을 투명으로 칠해 둔다(PolygonMask). 투명한 곳은 대조에서 빠진다.
        var gray = Vision.Matching.GrayImage.From(image, useAlpha: true);
        _templates[resourceName] = gray;

        return gray;
    }

    /// <summary>줄의 낱말들을 감싸는 자리.</summary>
    private static OcrWord Union(OcrLine line)
    {
        if (line.Words.Count == 0) return default;

        var left = line.Words.Min(w => w.Box.Left);
        var top = line.Words.Min(w => w.Box.Top);
        var right = line.Words.Max(w => w.Box.Right);
        var bottom = line.Words.Max(w => w.Box.Bottom);

        return new OcrWord(line.Text, Minguk.Tools.Vision.Labeling.LabelBox.FromCorners(0, left, top, right, bottom));
    }

    /// <summary>조각 안 0~1 자리 → 프레임 0~1 → 화면 픽셀.</summary>
    private static ScriptSpot ToSpot(OcrWord word, Rect tile, Rect bounds)
    {
        var cx = tile.X + (word.Box.CenterX * tile.Width);
        var cy = tile.Y + (word.Box.CenterY * tile.Height);
        var center = PreviewInputMapper.MapRatioToScreen(new Point(cx, cy), bounds);

        return new ScriptSpot((int)Math.Round(center.X), (int)Math.Round(center.Y),
            (int)Math.Round(word.Box.Width * tile.Width * bounds.Width), (int)Math.Round(word.Box.Height * tile.Height * bounds.Height), word.Text);
    }

    /// <summary>글자에서 숫자만. 없으면 null.</summary>
    public int? ReadNumber(double x, double y, double width, double height)
        => Traced("ReadNumber", $"{x:0.###}, {y:0.###}, {width:0.###}, {height:0.###}", () => ReadNumberCore(x, y, width, height));

    private int? ReadNumberCore(double x, double y, double width, double height)
    {
        var digits = new string(ReadTextCore(x, y, width, height).Where(char.IsDigit).ToArray());

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    // ── HUD 숫자 ─────────────────────────────────────────────────────────

    /// <summary>지금 탄약. 못 읽으면 null.</summary>
    public int? Ammo() => Traced("Ammo", "", () => HudNumber(HudRegions.Ammo, 0));

    /// <summary>탄약 최대치(재장전하면 이만큼 찬다). 못 읽으면 null.</summary>
    public int? AmmoMax() => Traced("AmmoMax", "", () => HudNumber(HudRegions.Ammo, 1));

    /// <summary>지금 체력. 못 읽으면 null.</summary>
    public int? Health() => Traced("Health", "", () => HudNumber(HudRegions.Health, 0));

    /// <summary>체력 최대치. 못 읽으면 null.</summary>
    public int? HealthMax() => Traced("HealthMax", "", () => HudNumber(HudRegions.Health, 1));

    /// <summary>궁극기 충전(%). <b>다 찼으면 숫자가 없어 null</b> - <see cref="UltimateReady"/> 쪽이 쓰기 낫다.</summary>
    public int? Ultimate() => Traced("Ultimate", "", () => HudNumber(HudRegions.Ultimate, 0));

    /// <summary>궁극기가 다 찼는가. 고리 안에 숫자가 없으면 찬 것으로 본다.</summary>
    /// <remarks>
    /// <b>못 읽은 것과 다 찬 것을 못 가른다.</b> 차는 동안만 % 를 적고 다 차면 아이콘만 보이기 때문이다.
    /// 자리가 어긋나 못 읽어도 "찼다" 가 나오므로, 쓰기 전에 차는 중에 <c>출력(궁극기())</c> 로 숫자가
    /// 나오는지 한 번 봐 둘 것.
    /// </remarks>
    public bool UltimateReady() => Ultimate() is null;

    /// <summary>
    /// HUD 의 한 자리를 읽어 <paramref name="index"/> 번째 숫자를 준다. 「17 24」 면 0 이 17, 1 이 24.
    /// </summary>
    /// <remarks>
    /// 「현재 | 최대」 처럼 둘이 붙어 나오므로 순서로 고른다. 구분선은 엔진이 1 이나 역슬래시로도 읽어 못 믿는다 -
    /// 숫자가 아닌 것은 다 버리고 남은 덩어리의 순서만 본다.
    /// </remarks>
    private int? HudNumber(HudSpot spot, int index)
    {
        var numbers = ReadNumbers(CropFor(spot.Region), out _);

        return index < numbers.Count ? numbers[index] : null;
    }

    /// <summary>그 자리의 화면 조각을 얻는다. 아직 프레임이 안 왔으면 잠깐 기다린다.</summary>
    private System.Windows.Media.Imaging.BitmapSource CropFor(Rect region)
        => CropWith((out System.Windows.Media.Imaging.BitmapSource? crop) => _host.Hub.TryCropFrame(region, out crop));

    /// <summary>칸의 화면 조각 - 돌린 칸은 똑바로 세워서(<see cref="RegionTargets.TryCrop"/>).</summary>
    private System.Windows.Media.Imaging.BitmapSource CropFor(RegionTarget target)
        => CropWith((out System.Windows.Media.Imaging.BitmapSource? crop) => RegionTargets.TryCrop(_host.Hub, target, out crop));

    private delegate bool CropAttempt(out System.Windows.Media.Imaging.BitmapSource? crop);

    /// <summary>프레임이 올 때까지 잠깐 기다리며 자른다.</summary>
    private System.Windows.Media.Imaging.BitmapSource CropWith(CropAttempt attempt)
    {
        ThrowIfStopping();

        var hub = _host.Hub;

        if (!hub.IsCapturing) throw Guard("눈이 없습니다 - 화면에서 시작(연결)을 눌러 창을 잡아야 화면을 읽을 수 있습니다.");

        // 처음 부를 때 프레임 복사를 켜고, 한 장 들어올 때까지 잠깐 기다린다.
        hub.WantsFrames = true;

        var deadline = Environment.TickCount64 + FrameWaitMs;
        System.Windows.Media.Imaging.BitmapSource? crop;

        while (!attempt(out crop) || crop is null)
        {
            // 리드백을 막 켰으면 캡처가 다시 시작되는 동안 더 기다린다 - 첫 읽기가 그 사이에 걸려 실패하던 것.
            if (hub.IsPreparingFrames) deadline = Environment.TickCount64 + FrameWaitMs;

            if (Environment.TickCount64 >= deadline) throw Guard("프레임이 들어오지 않습니다 - 캡처가 돌고 있는지, CPU 리드백이 켜져 있는지 보세요.");
            Wait(50);
        }

        return crop;
    }

    /// <summary>조각을 읽어 숫자 덩어리들을 왼쪽부터 순서대로 준다. 읽은 글도 같이 준다.</summary>
    private List<int> ReadNumbers(System.Windows.Media.Imaging.BitmapSource crop, out string text)
    {
        var outcome = Ocr().RecognizeAsync(crop, _token).GetAwaiter().GetResult();

        text = outcome.Text.Replace(Environment.NewLine, " ").Trim();

        // 칸을 이어 읽을 때와 같은 규칙으로 뽑는다(RegionTargets.NumbersIn) - 두 길이 다르게 끊으면 같은 화면에서 답이 갈린다.
        return [.. RegionTargets.NumbersIn(outcome.Text)];
    }

    // ── 이름 붙인 자리 ───────────────────────────────────────────────────

    /// <summary>화면에서 만들어 둔 자리의 글자를 읽는다.</summary>
    public string ReadAt(string name) => Traced("ReadAt", Quote(name), () => ReadAtCore(name).Text);

    /// <summary>화면에서 만들어 둔 자리의 숫자를 읽는다. 없으면 null.</summary>
    public int? ReadNumberAt(string name) => Traced("ReadNumberAt", Quote(name), () => ReadAtCore(name).First);

    /// <summary>그 자리에서 읽은 숫자를 <b>왼쪽에서 순서대로</b> 준다. 「30 40」 이면 [30, 40].</summary>
    /// <remarks>
    /// <b>왜 목록인가</b> - HUD 는 「현재 | 최대」 처럼 둘을 붙여 놓는 일이 많다. 앞 숫자만 읽으려고 자리를 좁히면
    /// 오히려 못 읽는다(실측 2026-09-16: 통째로 14장 중 13장, 앞쪽만 남기면 5장). 넓게 읽고 <b>스크립트가 고른다</b>.
    ///
    /// 믿을 범위(탄약 0~40, 체력 0~200)는 게임마다 달라 여기서 정하지 않는다 - 받아서 스크립트가 거른다.
    /// 구분선이 숫자로 읽히면(「24140」) 덩어리가 하나로 붙어 나오므로, 그런 값은 스크립트에서 버린다.
    /// </remarks>
    public int[] ReadNumbersAt(string name) => Traced("ReadNumbersAt", Quote(name), () => ReadAtCore(name).Numbers);

    /// <summary>그 자리의 <paramref name="index"/> 번째 숫자. 「30 40」 에서 0 이 30, 1 이 40. 없으면 null.</summary>
    // 형식을 못 박는다 - 블록 몸 람다는 Action 쪽으로 붙어 "값을 반환할 수 없다" 가 된다.
    public int? ReadNumberAt(string name, int index) => Traced<int?>("ReadNumberAt", $"{Quote(name)}, {index}", () =>
    {
        var numbers = ReadAtCore(name).Numbers;

        return index >= 0 && index < numbers.Length ? numbers[index] : null;
    });

    /// <summary>그 자리에 읽을 것이 있는가. 글자가 하나라도 나오면 참.</summary>
    /// <remarks>
    /// "재장전 중" 같은 표시가 떴는지 보는 데 쓴다. <b>글자가 없는 표시(아이콘·게이지)는 이걸로 못 본다</b> -
    /// OCR 은 글자만 읽는다.
    /// </remarks>
    public bool HasTextAt(string name) => Traced("HasTextAt", Quote(name), () => ReadAtCore(name).Text.Length > 0);

    private (string Text, int? First, int[] Numbers) ReadAtCore(string name)
    {
        var book = _host.Regions?.Invoke()
                   ?? throw Guard("영역 목록이 없습니다 - 화면에서 데이터셋 폴더를 골라야 합니다.");

        var found = book.Resolve(name) ?? throw Guard(MissingRegion(book, name));

        // 칸마다 따로 읽어 잇는다 - 자리를 부르면 칸 순서대로, 칸을 부르면 그 칸만. 자리가 엔진을 지정했으면 그것으로.
        var texts = RegionTargets.Of(found.Region, found.Cell)
            .Select(target => Ocr(found.Region).RecognizeAsync(Vision.Ocr.RegionPreprocess.Apply(CropFor(target), found.Region), _token).GetAwaiter().GetResult().Text)
            .ToList();

        var (text, numbers) = RegionTargets.Combine(texts);

        return (text, numbers.Length > 0 ? numbers[0] : null, numbers);
    }

    /// <summary>못 찾은 이름을 사람이 고칠 수 있게 - 자리가 없는지, 자리는 있는데 칸이 없는지.</summary>
    private static string MissingRegion(RegionBook book, string name)
    {
        var text = name?.Trim() ?? string.Empty;
        var dot = text.IndexOf('.');

        if (dot >= 0 && book.Find(text[..dot]) is { } region)
            return $"「{region.Name}」 영역에 「{text[(dot + 1)..].Trim()}」 구역이 없습니다. 있는 구역: {string.Join(" · ", region.Cells.Select(c => c.Name))}";

        return $"「{text}」 라는 영역이 없습니다. 스크립트 화면의 영역 패널에서 [새 영역] 으로 만들어 두세요 - 구역은 「영역.구역」 으로 부릅니다. " +
               (book.Regions.Count == 0
                   ? "지금 만들어 둔 영역이 하나도 없습니다."
                   : $"있는 것: {string.Join(" · ", book.Regions.Select(r => r.Name))}");
    }

    /// <summary>글자 읽기 엔진. 화면이 든 것을 같이 쓴다 - 한 모델이 한글·영문·숫자를 읽어 따로 둘 것이 없다.</summary>
    private IOcrEngine Ocr()
        => _host.Ocr?.Invoke()
           ?? throw Guard("글자 읽기 엔진이 없습니다 - 화면 상태 줄의 안내를 보세요(모델 파일이 없거나 엔진을 열지 못했습니다).");

    /// <summary>그 자리가 엔진을 지정했으면 그것으로, 아니면 <see cref="Ocr()"/> 과 같다.</summary>
    private IOcrEngine Ocr(Vision.Regions.NamedRegion? region)
        => (_host.OcrFor is { } ocrFor ? ocrFor(region) : _host.Ocr?.Invoke())
           ?? throw Guard("글자 읽기 엔진이 없습니다 - 화면 상태 줄의 안내를 보세요(모델 파일이 없거나 엔진을 열지 못했습니다).");

    public IReadOnlyList<ScriptDetection> 검출들() => Detections();
    public ScriptDetection? 가장가까운검출() => NearestDetection();
    public ScriptDetection? 목표() => TargetDetection();
    public void 목표풀기() => ReleaseTarget();
    public ScriptDetection? 검출기다리기(int milliseconds) => WaitDetection(milliseconds);
    public string 읽기(double x, double y, double width, double height) => ReadText(x, y, width, height);
    public int? 숫자읽기(double x, double y, double width, double height) => ReadNumber(x, y, width, height);
    public int? 탄약() => Ammo();
    public int? 탄약최대() => AmmoMax();
    public int? 체력() => Health();
    public int? 체력최대() => HealthMax();
    public int? 궁극기() => Ultimate();
    public bool 궁극기준비() => UltimateReady();
    public string 읽기(string 이름) => ReadAt(이름);
    public int? 숫자읽기(string 이름) => ReadNumberAt(이름);

    /// <summary>그 자리의 순번째 숫자. 「30 40」 에서 0 이 30, 1 이 40.</summary>
    public int? 숫자읽기(string 이름, int 순번) => ReadNumberAt(이름, 순번);

    /// <summary>그 자리에서 읽은 숫자를 왼쪽에서 순서대로. 믿을 범위는 스크립트가 고른다.</summary>
    public int[] 숫자들읽기(string 이름) => ReadNumbersAt(이름);
    public bool 글자있나(string 이름) => HasTextAt(이름);

    /// <summary>화면 전체에서 그 글을 찾아 자리를 준다(가운데 화면 픽셀). 없으면 null. 메뉴·버튼용 - 매 프레임이 아니라 메뉴가 떴을 때.</summary>
    public ScriptSpot? 글자찾기(string 글) => FindText(글);

    /// <summary>이름 붙인 자리 안에서만 찾는다 - 빠르다.</summary>
    public ScriptSpot? 글자찾기(string 글, string 자리) => FindText(글, 자리);

    /// <summary>글을 찾아 그 가운데를 누른다. 찾았으면 참. <c>글자누르기("사격장")</c>.</summary>
    public bool 글자누르기(string 글) => PressText(글);

    public bool 글자누르기(string 글, string 자리) => PressText(글, 자리);

    /// <summary>본보기 그림(프로젝트 Resources 의 PNG)을 화면에서 찾아 자리를 준다. 못 찾으면 null. 그림으로 된 메뉴·버튼용.</summary>
    public ScriptSpot? 그림찾기(string 본보기) => FindImage(본보기);

    /// <summary>닮음 문턱을 직접(기본 0.8).</summary>
    public ScriptSpot? 그림찾기(string 본보기, double 문턱) => FindImage(본보기, 문턱);

    /// <summary>이름 붙인 자리 안에서만 찾는다 - 훨씬 빠르다.</summary>
    public ScriptSpot? 그림찾기(string 본보기, double 문턱, string 자리) => FindImage(본보기, 문턱, 자리);

    /// <summary>본보기가 화면에 있는가.</summary>
    public bool 그림있나(string 본보기) => HasImage(본보기);

    public bool 그림있나(string 본보기, double 문턱) => HasImage(본보기, 문턱);

    /// <summary>그 자리에 본보기가 있는가 - 스킬 아이콘·버프 표시처럼 자리가 고정된 것.</summary>
    public bool 그림있나(string 본보기, double 문턱, string 자리) => HasImage(본보기, 문턱, 자리);

    /// <summary>본보기가 얼마나 닮았는지(0~1). 문턱을 잡을 때 눈으로 본다.</summary>
    public double 그림닮음(string 본보기) => ImageScore(본보기);

    public double 그림닮음(string 본보기, string 자리) => ImageScore(본보기, 자리);

    /// <summary>본보기를 찾아 그 가운데를 누른다. 찾았으면 참. <c>그림누르기("사격장.png")</c>.</summary>
    public bool 그림누르기(string 본보기) => PressImage(본보기);

    public bool 그림누르기(string 본보기, double 문턱) => PressImage(본보기, 문턱);

    public bool 그림누르기(string 본보기, double 문턱, string 자리) => PressImage(본보기, 문턱, 자리);

    // ── 키 ───────────────────────────────────────────────────────────────

    /// <summary>이름으로 키 한 번. "F" · "Space" · "Enter" · "Ctrl+Shift+1".</summary>
    public void Key(string name) => Traced("Key", Quote(name), () => KeyCore(name));

    /// <summary>키를 누른 채로 둔다. 비상 정지가 <see cref="ReleaseAll"/> 로 전부 뗀다.</summary>
    public void KeyDown(string name) => Traced("KeyDown", Quote(name), () => KeyDownCore(name));

    public void KeyUp(string name) => Traced("KeyUp", Quote(name), () => KeyUpCore(name));

    private void KeyCore(string name)
    {
        var (modifiers, key) = ParseKey(name);

        BeforeInput();

        foreach (var m in modifiers) _host.Service.Adapter.PressKey(m);

        try
        {
            _host.Service.Adapter.PressKey(key);
            Thread.Sleep(_host.HoldTimeMs);
            _host.Service.Adapter.ReleaseKey(key);
        }
        finally
        {
            foreach (var m in modifiers.AsEnumerable().Reverse()) _host.Service.Adapter.ReleaseKey(m);
        }
    }

    private void KeyDownCore(string name)
    {
        var (modifiers, key) = ParseKey(name);

        BeforeInput();

        foreach (var m in modifiers) Hold(m);
        Hold(key);
    }

    private void KeyUpCore(string name)
    {
        var (modifiers, key) = ParseKey(name);

        BeforeInput();

        Release(key);
        foreach (var m in modifiers.AsEnumerable().Reverse()) Release(m);
    }

    /// <summary>
    /// 키를 그 시간만큼 누르고 있다가 뗀다. 게임에서 걷기 - <c>걷기("W", 500)</c>, 대각선은 <c>걷기("W+A", 300)</c>.
    /// </summary>
    /// <remarks>
    /// 게임의 이동은 마우스가 아니라 W·A·S·D 를 누르고 있는 시간이다. 누르기·떼기를 따로 부르면 중간에 스크립트가
    /// 터졌을 때 키가 눌린 채 남는다 - 여기서는 finally 로 반드시 뗀다. 기다리는 동안 중지가 먹는다.
    /// </remarks>
    public void Walk(string keys, int milliseconds) => Traced("Walk", $"{Quote(keys)}, {milliseconds}", () => WalkCore(keys, milliseconds));

    private void WalkCore(string keys, int milliseconds)
    {
        if (string.IsNullOrWhiteSpace(keys)) throw new ArgumentException("키 이름이 비어 있습니다. \"W\" 나 \"W+A\" 처럼 적으세요.");

        var parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pressed = parts.Select(ToVirtualKey).ToArray();

        BeforeInput();

        try
        {
            foreach (var key in pressed) Hold(key);

            Wait(milliseconds);
        }
        finally
        {
            foreach (var key in pressed.AsEnumerable().Reverse()) Release(key);
        }
    }

    public void 키(string name) => Key(name);
    public void 걷기(string keys, int milliseconds) => Walk(keys, milliseconds);
    public void 누르기(string name) => KeyDown(name);
    public void 떼기(string name) => KeyUp(name);

    /// <summary>누르고 있던 키를 전부 뗀다. 중지·비상 정지 때 부른다 - 누른 채로 멈추면 게임이 계속 달린다.</summary>
    public void ReleaseAll()
    {
        ushort[] held;
        MouseButton[] buttons;

        // 조준 스레드도 멈춘다 - 멈춘 뒤에도 마우스가 돌면 안 된다.
        _aim?.Disengage();

        lock (_gate)
        {
            held = [.. _heldKeys];
            _heldKeys.Clear();
            buttons = [.. _heldButtons];
            _heldButtons.Clear();
        }

        foreach (var key in held)
        {
            try { _host.Service.Adapter.ReleaseKey(key); }
            catch (Exception) { /* 어댑터가 이미 닫혔을 수 있다. 떼려는 시도만 한다. */ }
        }

        // 누른 채로 멈추면 게임이 계속 쏜다.
        foreach (var button in buttons)
        {
            try { _host.Service.Adapter.ReleaseMouseButton(button); }
            catch (Exception) { }
        }

        // 묶어 둔 설정 쓰기를 낸다 - 멈춘 뒤 앱을 바로 꺼도 쓴 값이 남게.
        _settings?.Flush();
    }

    /// <summary>한 바퀴가 끝났다 - 조준 스레드를 내린다. 스크립트가 제 발로 끝나면 토큰이 안 취소돼 스레드가 남는다.</summary>
    public void Dispose()
    {
        _aim?.Dispose();
        _aim = null;
    }

    // ── 흐름 ─────────────────────────────────────────────────────────────

    public bool IsStopped() => _token.IsCancellationRequested;

    public bool 중지되었나() => IsStopped();

    /// <summary>스크립트를 여기서 끝낸다. 오류가 아니다.</summary>
    public void Stop()
    {
        Outcome = LiveScriptOutcome.Stopped;
        throw new ScriptStoppedException();
    }

    public void 끝() => Stop();

    public void Print(object? value) => _host.Print(value?.ToString() ?? "null");

    public void Watch(string name, object? value) => _host.Watch(name ?? string.Empty, value?.ToString() ?? "null");

    public void 출력(object? value) => Print(value);

    public void 보기(string name, object? value) => Watch(name, value);

    // ── 리소스(프로젝트) ─────────────────────────────────────────────────
    //    이름은 프로젝트 기준 경로("Resources/적.png")도, Resources 아래 이름("적.png")도 받는다.
    //    못 찾으면 null 을 주지 않고 멈춘다 - null 을 받아 한참 뒤 엉뚱한 줄에서 터지면 이름이 틀린 줄 모른다.

    public string ResourcePath(string name) => Traced(nameof(ResourcePath), Quote(name), () => FindResource(name));

    public string ResourceText(string name) => Traced(nameof(ResourceText), Quote(name), () => System.IO.File.ReadAllText(FindResource(name), System.Text.Encoding.UTF8));

    public byte[] ResourceBytes(string name) => Traced(nameof(ResourceBytes), Quote(name), () => System.IO.File.ReadAllBytes(FindResource(name)));

    /// <summary>wav 를 한 번 울린다. 끝나기를 기다리지 않는다 - 소리 때문에 조준이 밀리면 안 된다.</summary>
    public void PlaySound(string name) => Traced(nameof(PlaySound), Quote(name), () =>
    {
        var path = FindResource(name);
        var player = new System.Media.SoundPlayer(path);

        player.Play();
    });

    public string 리소스경로(string name) => ResourcePath(name);

    public string 리소스글(string name) => ResourceText(name);

    public byte[] 리소스바이트(string name) => ResourceBytes(name);

    public void 소리(string name) => PlaySound(name);

    // ── 다른 프로젝트로 ──────────────────────────────────────────────────
    //    같은 솔루션의 옆 프로젝트를 이어서 돌린다. 화면마다 무엇을 도는지 다르다(LiveScriptHost.RunProject 참고) -
    //    스크립트 화면은 소스를 연결해서, 플레이 화면은 빌드된 것(.mtsx)을 읽어서. 화면(플레이) 은 이 콜백을
    //    안 줄 수 있다 - 그러면 지원하지 않는다는 뜻으로 멈춘다.

    /// <summary>같은 솔루션의 옆 프로젝트를 이어서 돌린다.</summary>
    /// <remarks>
    /// 돌아오지 않는다 - 그 프로젝트의 시작 파일이 끝나야(또는 <c>끝()</c> 을 만나야) 다음 줄로 간다.
    /// 검출이 켜져 있으면 그 프로젝트 모델을 다 읽을 때까지(몇 초) 기다렸다가 돈다.
    /// </remarks>
    public void RunProject(string name) => Traced("RunProject", Quote(name), () => RunProjectCore(name));

    private void RunProjectCore(string name)
    {
        if (_host.RunProject is null)
            throw Guard("이 화면은 다른 프로젝트로 이어서 돌리는 것을 지원하지 않습니다.");

        var errors = _host.RunProject(name, _host, _token).GetAwaiter().GetResult();

        foreach (var error in errors)
            Print($"{name}: {error.Message}");

        // 부른 프로젝트 안에서 프로젝트이동을 불렀으면 여기(부른 쪽)도 끝낸다 - 쌓인 것을 모두 걷고 맨 바깥에서 넘어간다.
        if (_host.Moves.Pending is not null) Stop();
    }

    public void 프로젝트실행(string 이름) => RunProject(이름);

    /// <summary>
    /// 지금 스크립트를 끝내고 그 프로젝트로 넘어간다 - <c>프로젝트실행</c> 과 달리 돌아오지 않고 쌓이지도 않는다(사용자, 2026-09-19).
    /// </summary>
    /// <remarks>
    /// <c>끝()</c> 처럼 이 줄에서 끝나고, 실행기(맨 바깥)가 그 프로젝트의 시작 파일을 이어서 돌린다(<see cref="ProjectMoveRequest.RunWithMovesAsync"/>).
    /// 메인화면 → 영웅선택 → 플레이 → 사격장 → 다시 메인화면처럼 돌고 도는 흐름도 쌓이지 않는다. 검출 모델·이름 붙인 자리·리소스는 그 프로젝트 것이 된다.
    /// </remarks>
    public void MoveToProject(string name) => Traced("MoveToProject", Quote(name), () =>
    {
        if (_host.RunProject is null)
            throw Guard("이 화면은 다른 프로젝트로 이동하는 것을 지원하지 않습니다.");

        if (string.IsNullOrWhiteSpace(name))
            throw Guard("이동할 프로젝트 이름을 적으세요 - 프로젝트이동(\"사격장\").");

        _host.Moves.Request(name.Trim());
        Stop();
    });

    public void 프로젝트이동(string 이름) => MoveToProject(이름);

    // ── 설정(솔루션·프로젝트) ────────────────────────────────────────────
    //    설정 탭에서 사람이 만든 칸의 값. 부를 때마다 앱 안의 한 벌에서 읽는다 - 도는 중에 설정 탭에서 바꾸면 다음 호출부터 먹는다.
    //    찾는 순서·겹침 규칙은 SolutionSettings(docs/솔루션-설정.md). 없는 이름·형식 틀림은 멈추고 이유를 말한다.

    private Minguk.Tools.Projects.Settings.SolutionSettings? _settings;
    private readonly HashSet<string> _settingWarned = [];

    /// <summary>설정 값 - 숫자(정수 long·실수 double)·글·참거짓·목록(행들) 그대로.</summary>
    public object? Setting(string name) => Traced(nameof(Setting), Quote(name), () => Minguk.Tools.Projects.Settings.SettingsValue.ToObject(SettingEntry(name).Value));

    /// <summary>설정 값을 그 형식으로 - <c>Setting&lt;int&gt;("물약HP")</c>.</summary>
    public T Setting<T>(string name) => Traced(nameof(Setting), Quote(name), () =>
    {
        var entry = SettingEntry(name);

        return Minguk.Tools.Projects.Settings.SettingsValue.TryConvert(entry.Value, typeof(T), out var result)
            ? (T)result!
            : throw Guard($"설정 「{name}」 은(는) {Minguk.Tools.Projects.Settings.SolutionSettings.KindName(entry.Item.Kind)} 칸이라 {typeof(T).Name} 로 읽을 수 없습니다 (값 {entry.Value?.ToJsonString() ?? "null"}).");
    });

    /// <summary>목록 칸의 행들. 행은 <c>row["HP"]</c>·<c>row.Get&lt;int&gt;("HP")</c>.</summary>
    public IReadOnlyList<Minguk.Tools.Projects.Settings.SettingsRow> SettingList(string name) => Traced(nameof(SettingList), Quote(name), () =>
    {
        var entry = SettingEntry(name);

        return Minguk.Tools.Projects.Settings.SettingsValue.ToObject(entry.Value) as IReadOnlyList<Minguk.Tools.Projects.Settings.SettingsRow>
               ?? throw Guard($"설정 「{name}」 은(는) {Minguk.Tools.Projects.Settings.SolutionSettings.KindName(entry.Item.Kind)} 칸이라 목록으로 읽을 수 없습니다.");
    });

    /// <summary>값을 쓴다 - 지금 프로젝트 층에. 설정 탭에도 곧바로 보인다.</summary>
    public void SetSetting(string name, object? value) => Traced(nameof(SetSetting), $"{Quote(name)}, {value}", () =>
    {
        var settings = OpenSettings();

        try
        {
            settings.SetValue(name, Minguk.Tools.Projects.Settings.SettingsValue.FromObject(value));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidCastException)
        {
            throw Guard(ex.Message);
        }
    });

    /// <summary>그 이름의 칸이 있는가.</summary>
    public bool HasSetting(string name) => Traced(nameof(HasSetting), Quote(name), () => OpenSettings().Find(name) is not null);

    public object? 설정(string name) => Setting(name);

    public T 설정<T>(string name) => Setting<T>(name);

    public IReadOnlyList<Minguk.Tools.Projects.Settings.SettingsRow> 설정목록(string name) => SettingList(name);

    public void 설정저장(string name, object? value) => SetSetting(name, value);

    public bool 설정있나(string name) => HasSetting(name);

    private Minguk.Tools.Projects.Settings.SolutionSettings OpenSettings()
    {
        var root = _host.ResourceRoot
                   ?? throw Guard("설정을 쓰려면 프로젝트로 열어야 합니다 - 한 파일짜리 스크립트에는 솔루션·프로젝트 설정이 없습니다.");

        return _settings ??= Minguk.Tools.Projects.Settings.SolutionSettings.ForScriptRoot(root);
    }

    private Minguk.Tools.Projects.Settings.SettingsEntry SettingEntry(string name)
    {
        var entry = OpenSettings().Find(name ?? string.Empty)
                    ?? throw Guard(Minguk.Tools.Projects.Settings.SolutionSettings.MissingMessage(name ?? string.Empty));

        // 겹침·형식 틀림은 멈추지 않고 한 번만 말한다 - 파일 하나 때문에 돌던 런이 서면 안 된다.
        if (entry.Warning is { } warning && _settingWarned.Add(name + "\n" + warning))
            _host.Print($"설정 경고: {warning}");

        return entry;
    }

    private string FindResource(string name)
    {
        var root = _host.ResourceRoot
                   ?? throw Guard("리소스를 쓰려면 프로젝트로 열어야 합니다 - 한 파일짜리 스크립트에는 Resources 폴더가 없습니다.");

        return Projects.ScriptProject.ResolveResource(root, name)
               ?? throw Guard($"리소스 「{name}」 를 찾지 못했습니다. 프로젝트 폴더나 {Projects.ScriptProject.ResourceFolder} 폴더에 있는지 보세요 ({root}).");
    }

    // ── 호출 기록 ────────────────────────────────────────────────────────

    /// <summary>부른 것·인자·결과·걸린 시간을 남긴다. 터지면 그 사연도 남기고 그대로 던진다.</summary>
    private T Traced<T>(string name, string arguments, Func<T> body)
    {
        WaitWhilePaused();

        var watch = Stopwatch.StartNew();

        try
        {
            var result = body();
            _host.Trace?.Invoke(new ScriptCall(DateTime.Now, name, arguments, Describe(result), watch.Elapsed.TotalMilliseconds));
            return result;
        }
        catch (Exception ex)
        {
            _host.Trace?.Invoke(new ScriptCall(DateTime.Now, name, arguments, "! " + ex.Message, watch.Elapsed.TotalMilliseconds));
            throw;
        }
    }

    private void Traced(string name, string arguments, Action body)
        => Traced<object?>(name, arguments, () => { body(); return null; });

    private static string Describe(object? result) => result switch
    {
        null => "",
        string text => Quote(text),
        IReadOnlyList<ScriptDetection> mobs => mobs.Count == 0 ? "없음" : $"{mobs.Count}마리: {mobs[0]}",
        _ => result.ToString() ?? ""
    };

    private static string Quote(string? text) => text is null ? "null" : $"\"{text}\"";

    // ── 안쪽 ─────────────────────────────────────────────────────────────

    /// <summary>단계 하나를 계획 모드와 같은 길로 보낸다. 한글 처리·누름 시간이 그쪽과 같아야 한다.</summary>
    private void Send(SequenceStepDefinition definition)
    {
        BeforeInput();

        var sequence = new SequencePlan { Steps = [definition] }.Build(_host.Service, _host.HoldTimeMs);

        foreach (var step in sequence.Steps)
            step.RunAsync(null, _token).GetAwaiter().GetResult();
    }

    /// <summary>입력 전에 늘 거치는 것 - 중지 확인, 앞 창 확인, 초당 상한.</summary>
    private void BeforeInput()
    {
        ThrowIfStopping();
        EnsureForeground();
        Throttle();
    }

    /// <summary>대상 창이 앞에 없으면 멈춘다(SendInput·Interception 경로) - 엉뚱한 창에 입력이 들어가는 사고.</summary>
    private void EnsureForeground()
    {
        if (!IsTargetInFront())
            throw Guard($"대상 창이 앞에 없어 입력을 보내지 않았습니다(앞 창: {ForegroundWindow.Describe()}). 게임에서 F5 로 시작하거나, 시작 대기 안에 게임으로 넘어가세요.");
    }

    /// <summary>초당 상한을 넘으면 넘긴 만큼 기다린다.</summary>
    private void Throttle()
    {
        var limit = Math.Max(1, _host.MaxInputsPerSecond);

        while (true)
        {
            var now = Environment.TickCount64;

            lock (_gate)
            {
                while (_inputTicks.Count > 0 && now - _inputTicks.Peek() > 1000) _inputTicks.Dequeue();

                if (_inputTicks.Count < limit)
                {
                    _inputTicks.Enqueue(now);
                    return;
                }
            }

            Wait(20);
        }
    }

    private void Hold(ushort key)
    {
        lock (_gate) _heldKeys.Add(key);
        _host.Service.Adapter.PressKey(key);
    }

    private void Release(ushort key)
    {
        lock (_gate) _heldKeys.Remove(key);
        _host.Service.Adapter.ReleaseKey(key);
    }

    /// <summary>
    /// 화면이 일시정지를 걸었으면 풀릴 때까지 붙든다. 멈추기 전에 누르고 있던 키·버튼을 뗀다 - 누른 채 멈추면 게임 캐릭터가 계속 달리고 쏜다.
    /// </summary>
    /// <returns>멈춰 있던 시간(ms).</returns>
    private long WaitWhilePaused()
    {
        if (_host.PauseGate is not { } gate) return 0;

        var paused = gate.WaitWhilePaused(_token, () =>
        {
            ReleaseAll();
            _host.Print("일시정지 - 누르고 있던 키·버튼을 뗐습니다. 계속(F5)하면 여기서 이어 갑니다.");
        });

        ThrowIfStopping();
        return paused;
    }

    private void ThrowIfStopping()
    {
        if (!_token.IsCancellationRequested) return;

        Outcome = LiveScriptOutcome.Stopped;
        _token.ThrowIfCancellationRequested();
    }

    private ScriptGuardException Guard(string message)
    {
        Outcome = LiveScriptOutcome.Guarded;
        GuardMessage = message;
        return new ScriptGuardException(message);
    }

    /// <summary>"Ctrl+Shift+F" → 조합키들과 가상 키. 한 글자·숫자·Key 열거형 이름을 받는다.</summary>
    private static (ushort[] Modifiers, ushort Key) ParseKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("키 이름이 비어 있습니다.");

        var parts = name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = new List<ushort>();
        ushort? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers.Add(VirtualKeys.Control); continue;
                case "shift": modifiers.Add(VirtualKeys.Shift); continue;
                case "alt": modifiers.Add(VirtualKeys.Alt); continue;
            }

            key = ToVirtualKey(part);
        }

        if (key is null) throw new ArgumentException($"키 이름을 모르겠습니다: \"{name}\". \"F\", \"Space\", \"Enter\", \"Ctrl+1\" 처럼 적으세요.");

        return ([.. modifiers], key.Value);
    }

    private static ushort ToVirtualKey(string part)
    {
        var text = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;

        if (text.Length == 1 && char.IsLetter(text[0])) text = text.ToUpperInvariant();

        if (Enum.TryParse<System.Windows.Input.Key>(text, true, out var wpfKey) && wpfKey != System.Windows.Input.Key.None)
            return (ushort)KeyInterop.VirtualKeyFromKey(wpfKey);

        if (part.Length == 1 && VirtualKeys.TryGetKeyStroke(part[0], out var virtualKey, out _))
            return virtualKey;

        throw new ArgumentException($"키 이름을 모르겠습니다: \"{part}\".");
    }

    /// <summary>언어마다 다르게 넘어오는 버튼 값을 읽는다. 비우면 좌클릭.</summary>
    private static MouseButton ToButton(object? value) => value switch
    {
        null => MouseButton.Left,
        MouseButton button => button,
        int number => (MouseButton)number,
        long number => (MouseButton)number,
        double number => (MouseButton)(int)number,
        string text when Enum.TryParse<MouseButton>(text, true, out var parsed) => parsed,
        _ => MouseButton.Left
    };
}
