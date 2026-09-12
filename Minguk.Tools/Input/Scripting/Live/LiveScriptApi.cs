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
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>실시간 스크립트가 어떻게 끝났는지.</summary>
public enum LiveScriptOutcome
{
    /// <summary>끝까지 돌았거나 아직 도는 중.</summary>
    None,

    /// <summary>중지·F9·<c>끝()</c> 으로 멈췄다. 오류가 아니다.</summary>
    Stopped,

    /// <summary>안전장치가 막았다. 이유는 <see cref="LiveScriptApi.GuardMessage"/>.</summary>
    Guarded
}

/// <summary>
/// 실시간 모드에서 스크립트가 부르는 것들. <b>부르면 곧바로 나간다.</b>
/// </summary>
/// <remarks>
/// 계획 모드(<see cref="SequenceScriptApi"/>)와 같은 이름을 쓴다 - 같은 글을 두 모드에서 돌릴 수 있어야
/// 사람이 두 벌을 배우지 않는다. 거기에 화면을 읽는 것(몹들·읽기)과 흐름(중지되었나·끝)이 더 있다.
/// 이름은 <see cref="ScriptApiCatalog"/> 표에 있고, 검증이 이 클래스에 그 이름이 다 있는지 센다.
///
/// <b>안전장치</b> - 전부 여기서 건다. 엔진이나 화면에 두면 언어마다 다르게 새어 나간다.
///   - 대상 창이 앞에 없으면 입력을 보내지 않고 멈춘다(<see cref="ScriptGuardException"/>). 엉뚱한 창에 타이핑하는 사고.
///   - 초당 입력 상한. 넘으면 기다린다(멈추지 않는다) - 빠른 반복문이 입력을 쏟지 않게.
///   - 모든 호출이 중지 토큰을 본다. <c>쉬기()</c> 도 토큰으로 기다려서 중지가 바로 먹는다.
///   - 눈이 없으면(캡처 안 돎, 몹 찾기 꺼짐) <c>몹들()</c> 은 빈 목록이 아니라 멈추고 이유를 말한다.
///
/// 스크립트 스레드에서 돈다. UI 스레드가 아니라서 입력을 기다려도(await) 화면이 멈추지 않는다.
/// </remarks>
public sealed class LiveScriptApi
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly LiveScriptHost _host;
    private readonly CancellationToken _token;
    private readonly HashSet<ushort> _heldKeys = [];
    private readonly HashSet<MouseButton> _heldButtons = [];
    private readonly Queue<long> _inputTicks = new();
    private readonly object _gate = new();

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

    public void MoveTo(int x, int y) => Traced("MoveTo", $"{x}, {y}", () => Send(new SequenceStepDefinition { Kind = SequenceStepKind.MoveTo, X = x, Y = y }));

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
    public bool Aim(int x, int y) => Traced("Aim", $"{x}, {y}", () => AimCore(x, y));

    /// <summary>지금 자리에서 이만큼 움직인다. 배율 없이 그대로.</summary>
    public void MoveBy(int deltaX, int deltaY) => Traced("MoveBy", $"{deltaX}, {deltaY}", () =>
    {
        BeforeInput();
        ForgetAim();
        _host.Service.Adapter.MoveMouseBy(deltaX, deltaY);
    });

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
    private const double MoveBaseMs = 70;

    /// <summary>거리(카운트)의 제곱근에 곱하는 시간(ms). 멀수록 오래 걸리되 비례해서 늘지는 않는다 - 사람도 그렇다.</summary>
    private const double MoveMsPerRoot = 6.5;

    /// <summary>한 번의 이동에 쓰는 시간 상한(ms). 조준은 새 화면을 기다렸다 또 겨누므로 오래 붙들 이유가 없다.</summary>
    private const double MaxMoveMs = 280;

    /// <summary>걸음 사이 목표 간격(ms). 8ms 면 약 125Hz - 게이밍 마우스의 폴링과 비슷하다.</summary>
    private const double MoveStepMs = 8;

    /// <summary>걸음 수 상한. 눈금이 굵은 PC 에서 시간이 늘어지는 것을 막는다.</summary>
    private const int MaxMoveSteps = 40;

    /// <summary>옆으로 벗어나는 양을 거리의 몇 배로 할지. 2% 면 눈에 안 띄고 직선도 아니다.</summary>
    private const double ArcFraction = 0.02;

    /// <summary>벗어나는 양의 상한(카운트). 멀리 겨눌 때 옆으로 크게 돌면 조준이 흔들린 것으로 보인다.</summary>
    private const double MaxArcCounts = 12;

    /// <summary>
    /// 한 번의 조준으로 보내는 양의 상한(카운트). 배율이 잘못 커지면 한 번에 2,000 이 넘게 나가 시야가 한 바퀴 돌았다(실측).
    /// 넘치면 잘라서 보낸다 - 모자란 만큼은 다음 화면에서 다시 겨눈다.
    /// </summary>
    private const int MaxAimCounts = 1200;

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

    private double AimScale => _aimScale ??= _host.AimScale;

    /// <summary>스크립트가 마지막으로 본 화면(몹들·가장가까운몹)의 프레임 시각. 0 이면 아직 안 봄.</summary>
    private long _seenFrameTicks;

    /// <summary>겨눈 뒤 새 화면을 이만큼(ms)까지 기다린다. 넘으면 false 로 돌아간다 - 몹 찾기가 멈췄을 수 있다.</summary>
    private const int AimWaitMs = 1500;

    /// <summary>이 프레임이 마지막 조준 뒤의 화면인가. 모르면(0) 그렇다고 본다.</summary>
    private bool IsAfterLastAim(long frameTicks) => frameTicks <= 0 || _lastAimTicks <= 0 || frameTicks > _lastAimTicks + AimSettleMs;

    /// <summary>
    /// 겨눈 뒤의 새 화면이 허브에 올라올 때까지 기다린다.
    /// </summary>
    /// <remarks>
    /// 쉬기 없는 반복문(<c>while (...) { if (조준(...)) 클릭(); }</c>)이 옛 화면에서 조준을 초당 수천 번 불러,
    /// 호출 로그가 1분에 10MB 를 넘고 화면 스레드가 밀려 몹 찾기가 0.7초에서 1.8초로 늦어졌다(실측). 기다려 주면
    /// 반복문이 저절로 몹 찾기 속도에 맞춰진다.
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

    /// <summary>조준 말고 다른 것이 시야를 움직였다. 다음 거리 변화는 배율 탓이 아니다.</summary>
    private void ForgetAim() => _lastAim = null;

    /// <remarks>
    /// <b>같은 화면으로 두 번 겨누지 않는다</b> - 검출은 0.5~1초에 한 번인데 반복문은 0.1초마다 돈다. 같은 몹 자리로
    /// 예닐곱 번 겨누니 거리의 여섯 배를 돌아 몹을 지나쳐 흔들렸다(실측: 오버워치, 몹 자리가 0.7초마다 반대편으로 튐).
    /// 몹 자리를 찾은 프레임이 마지막 조준보다 앞이면, 그 자리는 조준 전의 것이라 건너뛴다.
    ///
    /// <b>큰 이동은 잘게</b> - 한 번에 수백 카운트를 넣으면 게임이 커서를 가운데로 되돌리기 전에 OS 커서가 창 밖으로
    /// 나가고, 이어진 클릭이 바탕 화면을 눌러 게임에서 빠져나왔다(실측: 앞 창이 "Program Manager" 가 됨).
    /// </remarks>
    private bool AimCore(int x, int y)
    {
        // 스크립트가 본 화면으로 판단한다. 아직 몹을 안 봤으면(좌표를 손으로 준 경우) 허브의 최신값으로.
        var seen = _seenFrameTicks > 0 ? _seenFrameTicks : _host.Hub.Latest?.FrameTicks ?? 0;

        if (!IsAfterLastAim(seen))
        {
            // 이 좌표는 겨누기 전 화면의 것이다. 새 화면을 기다렸다가, 움직이지 않고 돌아간다 - 다음 바퀴가 새 자리를 찾는다.
            WaitForFreshFrame();
            return false;
        }

        BeforeInput();

        var target = _host.Target() ?? throw Guard("대상 창이 없습니다 - 화면에서 창을 골라 시작(연결)하세요.");

        if (!CaptureTargetBounds.TryGet(target, out var bounds))
            throw Guard("대상 창의 자리를 알 수 없습니다 - 창이 닫혔거나 최소화됐습니다.");

        var centerX = bounds.Left + (bounds.Width / 2);
        var centerY = bounds.Top + (bounds.Height / 2);
        var offsetX = x - centerX;
        var offsetY = y - centerY;

        LearnAimScale(offsetX, offsetY);

        var onTarget = Math.Abs(offsetX) <= _host.AimTolerancePx && Math.Abs(offsetY) <= _host.AimTolerancePx;
        var deltaX = (int)Math.Round(offsetX * AimScale);
        var deltaY = (int)Math.Round(offsetY * AimScale);

        // 맞았어도 남은 몇 px 은 마저 당긴다. 다음 판정도 새 화면으로 하도록 시각은 늘 남긴다.
        _lastAimTicks = Environment.TickCount64;
        _lastAim = (offsetX, offsetY, deltaX, deltaY);

        if (deltaX == 0 && deltaY == 0) return onTarget;

        deltaX = Math.Clamp(deltaX, -MaxAimCounts, MaxAimCounts);
        deltaY = Math.Clamp(deltaY, -MaxAimCounts, MaxAimCounts);

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
        if (_host.AimScaleLearned is null || _lastAim is not { } last) return;

        // 많이 움직인 축으로 본다. 위아래는 대개 몇 px 이라 잡음이 크다.
        var (before, after, sent) = Math.Abs(last.CountX) >= Math.Abs(last.CountY)
            ? (last.OffsetX, offsetX, last.CountX)
            : (last.OffsetY, offsetY, last.CountY);

        if (Math.Abs(sent) < 15 || Math.Abs(before) < 20) return;

        var moved = before - after;
        var fraction = moved / before;

        // 가까워졌고 가운데를 지나치지 않았을 때만 믿는다. 지나쳤거나(1 초과) 거의 안 줄었으면(0.2 미만)
        // 다른 몹으로 목표가 바뀌었을 때가 많다 - 실측(노트북)에서 그런 값이 배율을 0.97→2.69→5.78 로 튀게 했다.
        if (fraction < 0.2 || fraction > 1.0 || Math.Abs(after) >= Math.Abs(before))
        {
            Logger.Debug($"배율 배우기 버림: {before:0} → {after:0} (보낸 {sent}, 줄어든 비율 {fraction:0.00})");
            return;
        }

        var measured = Math.Clamp(sent / moved, 0.1, 20);

        // 한 번에 크게 바꾸지 않는다. 한 번 잘못 잰 값이 배율을 몇 배로 끌고 가면 다음 조준이 화면 밖까지 돈다.
        var bounded = Math.Clamp(measured, AimScale / 1.5, AimScale * 1.5);
        var next = Math.Clamp((AimScale * 0.5) + (bounded * 0.5), 0.1, 20);

        if (Math.Abs(next - AimScale) / AimScale < 0.02) return;

        Logger.Info($"배율 {AimScale:0.00} → {next:0.00} ({before:0} → {after:0}, 보낸 {sent}, 잰 값 {measured:0.00})");

        _aimScale = next;
        _host.AimScaleLearned(next);
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

        if (_token.WaitHandle.WaitOne(milliseconds)) ThrowIfStopping();
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
    public void 상대이동(int deltaX, int deltaY) => MoveBy(deltaX, deltaY);
    public void 쉬기(int milliseconds) => Wait(milliseconds);

    // ── 화면 읽기 ────────────────────────────────────────────────────────

    /// <summary>지금 찾은 몹들. 화면 픽셀 자리로.</summary>
    public IReadOnlyList<ScriptMob> Mobs() => Traced("Mobs", "", MobsCore);

    /// <summary>화면 가운데에서 가장 가까운 몹. 없으면 null.</summary>
    public ScriptMob? NearestMob() => Traced("NearestMob", "", NearestMobCore);

    /// <summary>몹이 보일 때까지 최대 ms 기다린다. 50ms 마다 본다. 못 보면 null.</summary>
    public ScriptMob? WaitMob(int milliseconds) => Traced("WaitMob", milliseconds.ToString(), () => WaitMobCore(milliseconds));

    /// <summary>그 자리(0~1 비율)의 글자를 읽는다.</summary>
    public string ReadText(double x, double y, double width, double height)
        => Traced("ReadText", $"{x:0.###}, {y:0.###}, {width:0.###}, {height:0.###}", () => ReadTextCore(x, y, width, height));

    private IReadOnlyList<ScriptMob> MobsCore()
    {
        ThrowIfStopping();

        var hub = _host.Hub;

        if (!hub.IsCapturing) throw Guard("눈이 없습니다 - 화면에서 시작(연결)을 눌러 창을 잡아야 몹을 볼 수 있습니다.");
        if (!hub.IsDetecting) throw Guard("몹 찾기가 꺼져 있습니다 - 화면에서 몹 찾기를 켜세요.");

        var snapshot = hub.Latest;

        // 스크립트가 본 화면. 조준은 이 화면이 겨눈 뒤의 것인지로 판단한다 - 허브의 최신값으로 보면, 몹을 찾은 뒤
        // 조준하기 전 찰나에 새 화면이 올라온 경우 옛 자리로 한 번 더 겨눈다(실측: 같은 몹을 두 번 겨눠 지나침).
        _seenFrameTicks = snapshot?.FrameTicks ?? 0;

        if (snapshot is null || snapshot.Found.Count == 0) return [];

        var target = _host.Target();
        if (target is null || !CaptureTargetBounds.TryGet(target, out var bounds)) return [];

        var mobs = new List<ScriptMob>(snapshot.Found.Count);

        for (var i = 0; i < snapshot.Found.Count; i++)
        {
            var d = snapshot.Found[i];
            var center = PreviewInputMapper.MapRatioToScreen(new Point(d.Box.CenterX, d.Box.CenterY), bounds);
            var caption = i < snapshot.Names.Count ? snapshot.Names[i] ?? string.Empty : string.Empty;

            mobs.Add(new ScriptMob(d.Label, d.Score,
                (int)Math.Round(center.X), (int)Math.Round(center.Y),
                (int)Math.Round(d.Box.Width * bounds.Width), (int)Math.Round(d.Box.Height * bounds.Height), caption));
        }

        return mobs;
    }

    private ScriptMob? NearestMobCore()
    {
        var target = _host.Target();
        if (target is null || !CaptureTargetBounds.TryGet(target, out var bounds)) return null;

        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);

        return MobsCore().OrderBy(m => ((m.CenterX - cx) * (m.CenterX - cx)) + ((m.CenterY - cy) * (m.CenterY - cy))).FirstOrDefault();
    }

    private ScriptMob? WaitMobCore(int milliseconds)
    {
        var deadline = Environment.TickCount64 + Math.Max(0, milliseconds);

        while (true)
        {
            var nearest = NearestMobCore();
            if (nearest is not null) return nearest;

            if (Environment.TickCount64 >= deadline) return null;

            Wait(50);
        }
    }

    private string ReadTextCore(double x, double y, double width, double height)
    {
        ThrowIfStopping();

        var hub = _host.Hub;
        if (!hub.IsCapturing) throw Guard("눈이 없습니다 - 화면에서 시작(연결)을 눌러 창을 잡아야 글자를 읽을 수 있습니다.");

        var ocr = _host.Ocr?.Invoke() ?? throw Guard("글자 읽기 엔진이 없습니다 - Windows OCR 언어 팩을 확인하세요.");

        // 처음 부를 때 프레임 복사를 켜고, 한 장 들어올 때까지 잠깐 기다린다.
        hub.WantsFrames = true;

        var deadline = Environment.TickCount64 + 1500;
        var region = new Rect(x, y, width, height);
        System.Windows.Media.Imaging.BitmapSource? crop;

        while (!hub.TryCropFrame(region, out crop) || crop is null)
        {
            if (Environment.TickCount64 >= deadline) throw Guard("프레임이 들어오지 않습니다 - 캡처가 돌고 있는지, CPU 리드백이 켜져 있는지 보세요.");
            Wait(50);
        }

        var outcome = ocr.RecognizeAsync(crop, _token).GetAwaiter().GetResult();

        return outcome.Text.Replace(Environment.NewLine, " ").Trim();
    }

    /// <summary>글자에서 숫자만. 없으면 null.</summary>
    public int? ReadNumber(double x, double y, double width, double height)
        => Traced("ReadNumber", $"{x:0.###}, {y:0.###}, {width:0.###}, {height:0.###}", () => ReadNumberCore(x, y, width, height));

    private int? ReadNumberCore(double x, double y, double width, double height)
    {
        var digits = new string(ReadTextCore(x, y, width, height).Where(char.IsDigit).ToArray());

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public IReadOnlyList<ScriptMob> 몹들() => Mobs();
    public ScriptMob? 가장가까운몹() => NearestMob();
    public ScriptMob? 몹기다리기(int milliseconds) => WaitMob(milliseconds);
    public string 읽기(double x, double y, double width, double height) => ReadText(x, y, width, height);
    public int? 숫자읽기(double x, double y, double width, double height) => ReadNumber(x, y, width, height);

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

    // ── 호출 기록 ────────────────────────────────────────────────────────

    /// <summary>부른 것·인자·결과·걸린 시간을 남긴다. 터지면 그 사연도 남기고 그대로 던진다.</summary>
    private T Traced<T>(string name, string arguments, Func<T> body)
    {
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
        IReadOnlyList<ScriptMob> mobs => mobs.Count == 0 ? "없음" : $"{mobs.Count}마리: {mobs[0]}",
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

        if (_host.RequiresForeground && _host.Target() is { Kind: CaptureTargetKind.Window, Handle: var handle } && handle != IntPtr.Zero
            && !ForegroundWindow.IsInFront(handle))
            throw Guard($"대상 창이 앞에 없어 입력을 보내지 않았습니다(앞 창: {ForegroundWindow.Describe()}). 게임에서 F5 로 시작하거나, 시작 대기 안에 게임으로 넘어가세요.");

        Throttle();
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
