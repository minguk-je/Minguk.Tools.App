using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;

using Minguk.Tools.Input.Interop;
using Minguk.Tools.Vision.Perception;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 조준 스레드 - 붙잡은 몹을 향해 <b>8ms 마다 조금씩, 멈추지 않고</b> 마우스를 움직인다. 스크립트는 <c>조준(몹)</c> 으로 붙이고 맞았을 때 쏘기만 한다.
/// </summary>
/// <remarks>
/// <b>왜 따로 도는가</b>(사용자, 2026-09-18 "부자연스러워") - 예전 조준은 한 번 부르면 남은 거리의 93% 를 76ms 에 보내고 <b>멈춘 채</b> 새 화면(몹 찾기 0.1초 주기 +
/// 80ms 안정)을 기다렸다 다시 보냈다. 로그 434회 중간값: 움직임 76ms · 멈춤 249ms - 조준하는 동안 움직이는 시간이 22% 라 초당 3번 「툭 - 멈춤 - 툭」 이었다.
/// 사람은 새 정보가 없어도 멈추지 않는다 - 마지막으로 본 자리와 속도로 계속 따라가다가 새로 보이면 고친다. 그것을 이 스레드가 한다.
///
/// <b>모형</b> - 목표의 자리는 <b>화면 가운데(조준점) 기준 거리(px)</b>로 든다. 우리가 보낸 마우스 카운트는 화면을 돌려 그 거리를 <c>카운트 ÷ 배율</c> 만큼 줄인다.
/// <c>지금 거리 = 본 거리 + 속도 × 지난 시간 − (그 뒤 보낸 카운트) ÷ 배율</c>. 보낸 카운트는 시각과 함께 적어 둔다(<see cref="_sent"/>) - 프레임은 보낸 입력이
/// 화면에 오르기까지의 지연(<see cref="_latencyMs"/>) 앞의 것까지만 반영하므로, 프레임 시각 − 지연 뒤에 보낸 것만 뺀다.
/// <b>지연은 넉넉히 잡고, 흔들리는 동안의 화면은 덜 믿는다</b>(사용자, 2026-09-18 "너무 휙휙" → "조금 더 스무스하게"). 지연을 짧게 잡으면(40ms) 아직 안 보인 입력을
/// 반영된 줄 알고 더 보내 지나치고, 길게 잡으면 덜 보내 느릴 뿐이다 - 틀려도 안전한 쪽(<see cref="_latencyMs"/> = 110)으로 못 박는다. 후보를 견줘 스스로 고르게
/// 했더니(오차가 8·9·13 처럼 비슷해) 40↔70↔100 을 오가며 예측이 튀어 큰 조준의 79% 가 지나쳤다(실측). 대신 <b>보내는 양이 막 바뀌는 중</b>(휙 돌기 시작·끝)에 온 화면은
/// 지연이 40 인지 200 인지에 따라 답이 크게 갈리므로(<see cref="Trust"/>) 그만큼 덜 받아들이고 제 모형(보낸 양 ÷ 배율)을 믿는다. 꾸준히 따라가는 중에는 어느 지연이든 같아 그대로 믿는다.
///
/// <b>움직임</b> - 남은 거리를 시간 상수 <see cref="TauMs"/> 로 줄이는 1차 접근(박자마다 남은 것의 일정 비율). 멀면 빠르고 가까울수록 느려져 스스로 지나치지 않는다.
/// 한 박자 상한(<see cref="MaxStepCounts"/>)과 <b>빨라질 때만</b> 가속 상한(<see cref="MaxAccelCounts"/>) - 느려지는 쪽을 막으면 새 화면에서 목표가 가까이
/// 나타났을 때 관성으로 지나친다. 소수 카운트는 이월해 작은 걸음도 합이 맞다.
///
/// <b>새 화면</b> - 허브의 새 검출이 오면 프레임 시각의 예상 자리에서 가장 가까운 사각형을 같은 몹으로 보고(반지름 = 크기×2 + 속도 여유), 예상과의 차이를 일부만
/// 받아들인다(가로 <see cref="GainX"/>·세로 <see cref="GainY"/> - 검출 사각형은 가만히 있는 몹도 세로 17px 흔들린다, 실측). 속도는 두 프레임 사이의 자리 변화에서
/// 우리가 돌린 만큼을 되돌려 재고 지수 평균한다. 못 찾으면 <see cref="LockGraceMs"/> 동안은 예측으로 이어 가고, 넘으면 놓는다(<see cref="IsEngaged"/> false).
///
/// <b>배율 배우기</b>는 예전 규칙 그대로 <see cref="LiveScriptApi"/> 에 맡긴다 - 표본은 멈춰 서 있던 프레임(닻)에서 지금까지 길게 재어, 지연을 모르는 부분이 총량의 15% 아래일 때 닻마다 한 번 준다.
///
/// 스레드는 처음 붙일 때 만들고, 붙인 것이 없으면 잔다. 일시정지·중지·앞 창 바뀜이면 움직이지 않는다. 입력 어댑터는 스크립트 스레드와 같이 쓴다(SendInput·드라이버 모두 스레드에 안전).
/// </remarks>
internal sealed class AimLoop : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>바깥에서 빌려 쓰는 것들 - 대상 자리, 검출, 배율, 보내기, 앞 창 확인, 배율 표본, 일시정지, 중지.</summary>
    internal sealed record Host(
        Func<Rect?> Bounds,
        Func<DetectionSnapshot?> Latest,
        Func<double> Scale,
        Func<int, int, bool> Move,
        Func<bool> MayMove,
        Action<double, double, double> Learn,
        ScriptPauseGate? Pause,
        CancellationToken Token);

    /// <summary>박자(ms). 약 125Hz - 게이밍 마우스 폴링과 비슷하다.</summary>
    private const double TickMs = 8;

    /// <summary>남은 거리를 줄이는 시간 상수(ms). 한 박자에 남은 것의 약 12% - 100ms 뒤 19% 남는다.</summary>
    private const double TauMs = 95;

    /// <summary>한 박자에 보내는 상한(카운트). 옛 조준의 상한(130ms 에 1,200)과 비슷한 속도다.</summary>
    private const double MaxStepCounts = 45;

    /// <summary>빨라질 때 박자 사이 걸음 변화 상한(카운트). 정지에서 상한 속도까지 6박자(48ms) - 휙 튀어 나가지 않게.</summary>
    private const double MaxAccelCounts = 8;

    /// <summary>멀리서 겨눌 때 일부러 남기는 비율. 20px 부터 늘어 80px 넘으면 이만큼 - 사람도 크게 꺾을 때는 살짝 못 미치게 꺾고 끝에서 다듬는다.</summary>
    private const double ShortfallFraction = 0.1;

    /// <summary>이보다 가까우면 안 움직인다(px). 검출 떨림에 마우스가 떨지 않게.</summary>
    private const double DeadbandPx = 1.5;

    /// <summary>
    /// 보낸 입력이 캡처된 화면에 보이기까지(ms). 프레임 시각에서 이만큼 앞선 뒤의 입력은 그 프레임에 아직 없는 것으로 본다.
    /// 실측은 80~100ms 쯤(붙인 뒤 움직임이 화면에 처음 보이기까지 중간값 164ms − 프레임 주기 절반 − 가속) - 틀려도 안전한 쪽으로 조금 길게.
    /// </summary>
    private readonly int _latencyMs = 110;

    /// <summary>지연이 이 사이 어디인지 모른다고 본다(ms). 이 띠 안에 보낸 양이 프레임 사이에 바뀐 만큼이 그 화면의 불확실함이다.</summary>
    private const int LatencyMinMs = 40;

    private const int LatencyMaxMs = 200;

    /// <summary>불확실함(px)이 이만큼이면 화면을 절반만 믿는다.</summary>
    private const double UncertaintyHalfPx = 12;

    /// <summary>예측과의 차이가 이보다 작으면(px) 검출 떨림으로 보고 절반만 받아들인다 - 붙은 뒤 좌우로 떨지 않게.</summary>
    private const double JitterPx = 8;

    /// <summary>못 본 채 예측으로 이어 가는 시간(ms). 넘으면 놓는다 - 잠깐 가려진 것과 죽은 것을 구별할 길이 없다.</summary>
    private const int LockGraceMs = 400;

    /// <summary>못 본 채 속도로 밀어 주는 시간 상한(ms). 그 뒤로는 마지막 예측 자리에 선다.</summary>
    private const int StaleVelocityMs = 160;

    /// <summary>속도 상한(px/ms). 그보다 빠른 값은 다른 몹으로 잘못 이은 것이다.</summary>
    private const double MaxVelocityPxPerMs = 1.5;

    /// <summary>새 사각형과 예측의 차이를 받아들이는 비율. 몹은 옆으로 달리므로 가로는 크게, 세로·크기는 흔들림이 커 작게.</summary>
    private const double GainX = 0.55;

    private const double GainY = 0.4;

    private const double GainSize = 0.35;

    /// <summary>속도 지수 평균의 새 값 비중.</summary>
    private const double VelocityAlphaX = 0.35;

    private const double VelocityAlphaY = 0.25;

    /// <summary>같은 몹으로 볼 거리 - 크기의 몇 배.</summary>
    private const double LockRadiusFactor = 2.0;

    /// <summary>두 프레임 사이가 이보다 멀면 속도를 재지 않는다(ms).</summary>
    private const int MaxVelocityGapMs = 300;

    /// <summary>마지막으로 본 지 이보다 오래됐으면 맞았다고 하지 않는다(ms) - 예측만으로는 쏘지 않는다.</summary>
    private const int OnTargetMaxAgeMs = 300;

    /// <summary>맞았다고 볼 몸 사각형의 비율. 가장자리에 걸친 것은 빼고.</summary>
    private const double OnTargetFraction = 0.9;

    /// <summary>보낸 기록을 남기는 시간(ms).</summary>
    private const int SentHistoryMs = 1000;

    /// <summary>앞 창을 몇 박자마다 보는지.</summary>
    private const int ForegroundCheckEvery = 10;

    private readonly Host _host;
    private readonly object _sync = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Queue<(long Ticks, int Dx, int Dy)> _sent = new();

    private Thread? _thread;
    private volatile bool _disposed;
    private volatile bool _engaged;
    private Track? _track;
    private ScriptMob? _given;
    private double _carryX;
    private double _carryY;
    private double _stepX;
    private double _stepY;
    private int _framesConsumed;
    private int _lastTrueFrame = -1;
    private long _consumedFrameTicks;
    private int _tickCount;

    public AimLoop(Host host)
    {
        _host = host;
    }

    /// <summary>붙잡은 몹의 상태. 자리는 화면 가운데 기준 머리의 거리(px).</summary>
    private sealed class Track
    {
        /// <summary>마지막으로 본(섞은) 프레임 시각.</summary>
        public long SeenTicks;

        public double OffX;
        public double OffY;

        /// <summary>px/ms.</summary>
        public double Vx;
        public double Vy;

        public double W;
        public double H;

        public string Name = string.Empty;
        public double Score;
        public Minguk.Tools.Vision.Labeling.LabelBox Box;

        /// <summary>마지막 프레임의 날것 자리(섞기 전) - 속도·배율 배우기용.</summary>
        public bool HasObservation;
        public double ObsX;
        public double ObsY;

        /// <summary>처음 못 본 시각. 0 이면 보고 있다.</summary>
        public long FirstMissTicks;

        /// <summary>지난 프레임 때 불확실한 띠(지연 40~200ms) 안에 보낸 양(카운트).</summary>
        public double BandX;
        public double BandY;

        /// <summary>배율 배우기의 닻 - 마지막으로 멈춰 서 있던 프레임의 시각과 날것 자리. 붙인 순간에 처음 놓는다.</summary>
        public long AnchorTicks;
        public double AnchorX;
        public double AnchorY;

        /// <summary>이 닻으로 이미 표본을 줬는가 - 닻 하나에 표본 하나.</summary>
        public bool AnchorUsed;
    }

    /// <summary>붙잡고 따라가는 중인가. 놓쳤거나(<see cref="LockGraceMs"/>) 뗐으면 false.</summary>
    public bool IsEngaged => _engaged;

    /// <summary>새 화면을 받아들인 횟수. <c>조준()</c> 이 "프레임마다 한 번" 을 지키는 데 쓴다.</summary>
    public int FramesConsumed => Volatile.Read(ref _framesConsumed);

    /// <summary>
    /// 이 몹을 붙잡고 움직이기 시작한다. 이미 같은 몹(우리가 준 것이거나 예상 자리 안의 것)을 따라가는 중이면 그대로 잇는다 - 속도·섞은 자리를 잃지 않게.
    /// </summary>
    /// <param name="frameTicks">이 몹을 찾은 프레임 시각. 모르면 0(지금으로 본다).</param>
    public void Engage(ScriptMob mob, long frameTicks, Rect bounds)
    {
        var now = Environment.TickCount64;
        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);
        var offX = mob.HeadX - cx;
        var offY = mob.HeadY - cy;

        lock (_sync)
        {
            if (_engaged && _track is { } current)
            {
                if (ReferenceEquals(mob, _given)) return;

                var (px, py) = Predict(current, now);
                var radius = Math.Max(current.W, current.H) * LockRadiusFactor;

                if (Math.Sqrt(Sq(offX - px) + Sq(offY - py)) <= radius)
                {
                    _given = mob;
                    return;
                }
            }

            _track = new Track
            {
                SeenTicks = frameTicks > 0 ? frameTicks : now,
                OffX = offX,
                OffY = offY,
                W = Math.Max(1, mob.Width),
                H = Math.Max(1, mob.Height),
                Name = mob.Name,
                Score = mob.Score,
                Box = mob.Box,
                AnchorTicks = frameTicks > 0 ? frameTicks : now,
                AnchorX = offX,
                AnchorY = offY
            };

            _given = mob;
            _consumedFrameTicks = _track.SeenTicks;
            _lastTrueFrame = -1;
            _engaged = true;

            Logger.Debug($"조준 붙임: {mob.Name} 거리({offX:0}, {offY:0}) 크기 {mob.Width}x{mob.Height}");

            _thread ??= Start();
        }

        _wake.Set();
    }

    /// <summary>놓는다 - 움직임을 멈춘다. 다른 입력(상대이동·끌기·좌표 조준)·목표풀기·일시정지·중지 때.</summary>
    public void Disengage()
    {
        lock (_sync)
        {
            if (_engaged) Logger.Debug("조준 뗌");

            _engaged = false;
            _track = null;
            _given = null;
            _stepX = _stepY = 0;
            _carryX = _carryY = 0;
        }
    }

    /// <summary>스크립트 스레드가 따로 보낸 상대 이동을 적는다 - 그만큼 화면이 돌았으니 예측에서 빼야 한다.</summary>
    public void NoteSent(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;

        lock (_sync) Record(Environment.TickCount64, dx, dy);
    }

    /// <summary>지금 예측한 자리의 몹. 스크립트의 <c>목표()</c> 가 준다. 놓았거나 놓쳤으면 null.</summary>
    public ScriptMob? CurrentMob(Rect bounds, ScriptNameplateReader? reader)
    {
        lock (_sync)
        {
            if (!_engaged || _track is not { } t) return null;

            var (ex, ey) = Predict(t, Environment.TickCount64);
            var cx = bounds.Left + (bounds.Width / 2);
            var cy = bounds.Top + (bounds.Height / 2);
            var height = (int)Math.Round(t.H);

            // 머리 거리에서 사각형 가운데로 - 머리는 위에서 HeadFraction 만큼 내려온 곳이다.
            var centerX = (int)Math.Round(cx + ex);
            var centerY = (int)Math.Round(cy + ey + ((0.5 - ScriptMob.HeadFraction) * t.H));

            var mob = new ScriptMob(t.Name, t.Score, centerX, centerY, (int)Math.Round(t.W), height, string.Empty) { Box = t.Box, Reader = reader };

            _given = mob;
            return mob;
        }
    }

    /// <summary>조준점이 몸 사각형 안(<see cref="OnTargetFraction"/>)에 있는가 - 최근에 본 것일 때만.</summary>
    public bool IsOnTarget()
    {
        lock (_sync)
        {
            if (!_engaged || _track is not { } t) return false;

            var now = Environment.TickCount64;
            if (now - t.SeenTicks > OnTargetMaxAgeMs) return false;

            var (ex, ey) = Predict(t, now);
            var bodyY = ey + ((0.5 - ScriptMob.HeadFraction) * t.H);

            if (Math.Abs(ex) > t.W / 2 * OnTargetFraction || Math.Abs(bodyY) > t.H / 2 * OnTargetFraction) return false;

            // 마지막 화면 뒤로 몸 반쪽 넘게 움직였으면 아직 화면으로 확인된 자리가 아니다 - 배율이 틀리면 예측은 붙었다는데 실제로는 멀어, 허공에 쏜다.
            var (sx, sy) = SentBetween(t.SeenTicks - _latencyMs, long.MaxValue);
            var scale = Math.Max(0.01, _host.Scale());

            return Math.Abs(sx) / scale <= t.W / 2 && Math.Abs(sy) / scale <= t.H / 2;
        }
    }

    /// <summary>이 프레임에서 아직 "맞았다" 를 안 줬으면 주고 참. 한 프레임에 한 번만 쏘게 - 예측만으로 연달아 누르지 않는다.</summary>
    public bool TryClaimHit()
    {
        lock (_sync)
        {
            if (_lastTrueFrame == _framesConsumed) return false;

            _lastTrueFrame = _framesConsumed;
            return true;
        }
    }

    private Thread Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "조준", Priority = ThreadPriority.AboveNormal };

        thread.Start();
        return thread;
    }

    private void Run()
    {
        PrecisionTimer? timer = null;

        try
        {
            while (!_disposed && !_host.Token.IsCancellationRequested)
            {
                if (!_engaged || _host.Pause is { IsPaused: true })
                {
                    // 붙잡은 것이 없으면 잔다. 시계 눈금도 돌려준다 - 시스템 전체 값이다.
                    timer?.Dispose();
                    timer = null;

                    if (_host.Pause is { IsPaused: true }) Disengage();

                    WaitHandle.WaitAny([_wake, _host.Token.WaitHandle], 250);
                    continue;
                }

                timer ??= new PrecisionTimer();

                if (++_tickCount % ForegroundCheckEvery == 0 && !_host.MayMove())
                {
                    Logger.Debug("조준 뗌 - 대상 창이 앞에 없다.");
                    Disengage();
                    continue;
                }

                var now = Environment.TickCount64;

                ConsumeFrame(now);
                Step(now);
                WaitPrecise(TickMs);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "조준 스레드가 멈췄다");
        }
        finally
        {
            timer?.Dispose();
        }
    }

    /// <summary>허브에 새 검출이 올라왔으면 받아들인다 - 같은 몹을 찾아 자리·속도·크기를 고친다.</summary>
    private void ConsumeFrame(long now)
    {
        var snapshot = _host.Latest();
        if (snapshot is null) return;

        var frame = snapshot.FrameTicks > 0 ? snapshot.FrameTicks : snapshot.Ticks;
        if (frame <= _consumedFrameTicks) return;

        _consumedFrameTicks = frame;

        if (_host.Bounds() is not { } bounds) return;

        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);
        var scale = Math.Max(0.01, _host.Scale());

        lock (_sync)
        {
            if (!_engaged || _track is not { } t) return;

            // 프레임 시각의 예상 자리 - 그때까지(지연만큼 앞서) 보낸 것만 뺀다.
            var (px, py) = PredictAt(t, frame);
            var radius = (Math.Max(t.W, t.H) * LockRadiusFactor) + (Math.Sqrt(Sq(t.Vx) + Sq(t.Vy)) * (frame - t.SeenTicks));

            var bestDistance = double.MaxValue;
            var found = false;
            double obsX = 0, obsY = 0, w = 0, h = 0;
            var bestIndex = -1;

            for (var i = 0; i < snapshot.Found.Count; i++)
            {
                var box = snapshot.Found[i].Box;
                var headX = bounds.Left + (box.CenterX * bounds.Width) - cx;
                var headY = bounds.Top + ((box.CenterY - (box.Height / 2) + (box.Height * ScriptMob.HeadFraction)) * bounds.Height) - cy;
                var distance = Math.Sqrt(Sq(headX - px) + Sq(headY - py));

                if (distance >= bestDistance) continue;

                bestDistance = distance;
                obsX = headX;
                obsY = headY;
                w = box.Width * bounds.Width;
                h = box.Height * bounds.Height;
                bestIndex = i;
                found = distance <= radius;
            }

            Interlocked.Increment(ref _framesConsumed);

            if (!found)
            {
                if (t.FirstMissTicks == 0) t.FirstMissTicks = now;

                if (now - t.FirstMissTicks <= LockGraceMs) return;

                Logger.Debug($"조준 놓침: {LockGraceMs}ms 넘게 못 봤다 (예상 거리 {px:0}, {py:0} · 보인 것 {snapshot.Found.Count}마리)");
                _engaged = false;
                _track = null;
                _given = null;
                _stepX = _stepY = 0;
                return;
            }

            var gap = frame - t.SeenTicks;
            var (sentX, sentY) = SentBetween(t.SeenTicks - _latencyMs, frame - _latencyMs);

            // 이 화면을 얼마나 믿을까 - 지연을 모르는 띠(40~200ms 앞) 안에 보낸 양이 지난 프레임 때와 달라진 만큼이 불확실함이다.
            // 휙 돌기 시작·끝에서 크고(지연에 따라 답이 갈린다), 가만히 있거나 꾸준히 따라가는 중에는 0 에 가깝다.
            var (bandX, bandY) = SentBetween(frame - LatencyMaxMs, frame - LatencyMinMs);
            var trustX = Trust(Math.Abs(bandX - t.BandX) / scale);
            var trustY = Trust(Math.Abs(bandY - t.BandY) / scale);

            t.BandX = bandX;
            t.BandY = bandY;

            // 속도 - 두 프레임 사이 자리 변화에서 우리가 돌린 만큼을 되돌린다(보낸 카운트는 거리를 줄였으니 더한다). 못 믿을 화면으로는 덜 고친다.
            if (t.HasObservation && gap > 0 && gap <= MaxVelocityGapMs)
            {
                var rawVx = (obsX - t.ObsX + (sentX / scale)) / gap;
                var rawVy = (obsY - t.ObsY + (sentY / scale)) / gap;

                t.Vx = Math.Clamp(t.Vx + ((rawVx - t.Vx) * VelocityAlphaX * trustX), -MaxVelocityPxPerMs, MaxVelocityPxPerMs);
                t.Vy = Math.Clamp(t.Vy + ((rawVy - t.Vy) * VelocityAlphaY * trustY), -MaxVelocityPxPerMs, MaxVelocityPxPerMs);
            }
            else
            {
                t.Vx = t.Vy = 0;
            }

            // 배율 표본 - <b>닻(멈춰 서 있던 프레임) → 지금</b> 보낸 총량 ÷ 줄어든 거리. 프레임마다 재면 지연 안의 입력이 반영됐는지 몰라 못 믿는데, 닻에서부터 길게 재면
            // 그 모르는 부분(띠 안에 보낸 양)이 총량에 견줘 작아진다 - 15% 아래일 때 한 번 준다. 완전히 멈춰 서기를 기다리면 배율이 많이 틀린 동안에는(끝없이 조금씩
            // 고쳐 가느라) 표본이 영영 안 나온다. 몹이 달리는 중(속도 120px/s 넘게)에는 줄어든 거리에 몹의 움직임이 섞여 안 준다. 닻은 다시 멈춰 서면 새로 놓는다.
            {
                var (totalX, totalY) = SentBetween(t.AnchorTicks - _latencyMs, frame - _latencyMs);
                var horizontal = Math.Abs(totalX) >= Math.Abs(totalY);
                var total = horizontal ? totalX : totalY;
                var band = horizontal ? bandX : bandY;
                var still = Math.Abs(t.Vx) <= 0.12 && Math.Abs(t.Vy) <= 0.12;

                if (!t.AnchorUsed && still && Math.Abs(total) >= 60 && Math.Abs(band) <= Math.Abs(total) * 0.15)
                {
                    _host.Learn(horizontal ? t.AnchorX : t.AnchorY, horizontal ? obsX : obsY, total);
                    t.AnchorUsed = true;
                }

                if (Math.Abs(bandX) + Math.Abs(bandY) <= 6)
                {
                    t.AnchorTicks = frame;
                    t.AnchorX = obsX;
                    t.AnchorY = obsY;
                    t.AnchorUsed = false;
                }
            }

            t.OffX = px + ((obsX - px) * GainX * trustX * Calm(obsX - px));
            t.OffY = py + ((obsY - py) * GainY * trustY * Calm(obsY - py));
            t.W += (w - t.W) * GainSize;
            t.H += (h - t.H) * GainSize;
            t.SeenTicks = frame;
            t.HasObservation = true;
            t.ObsX = obsX;
            t.ObsY = obsY;
            t.FirstMissTicks = 0;
            t.Name = snapshot.Found[bestIndex].Label;
            t.Score = snapshot.Found[bestIndex].Score;
            t.Box = snapshot.Found[bestIndex].Box;

            Logger.Debug($"추적: 거리({t.OffX:0}, {t.OffY:0}) 본 것({obsX:0}, {obsY:0}) 속도({t.Vx * 1000:0}, {t.Vy * 1000:0})px/s 보냄({sentX}, {sentY}) 크기 {t.W:0}x{t.H:0} 믿음 {trustX:0.00}");
        }
    }

    /// <summary>불확실함(px) → 이 화면을 받아들이는 비율(0~1). <see cref="UncertaintyHalfPx"/> 에서 절반.</summary>
    private static double Trust(double uncertaintyPx) => 1 / (1 + (uncertaintyPx / UncertaintyHalfPx));

    /// <summary>예측과의 차이가 떨림 크기면 절반만 - 검출 사각형은 가만히 있는 몹도 가로 11px·세로 17px 흔들린다(실측).</summary>
    private static double Calm(double innovationPx) => Math.Abs(innovationPx) < JitterPx ? 0.5 : 1;

    /// <summary>한 박자 - 지금 예측한 남은 거리의 일정 비율을 보낸다.</summary>
    private void Step(long now)
    {
        int ix, iy;

        lock (_sync)
        {
            if (!_engaged || _track is not { } t) return;

            var (ex, ey) = Predict(t, now);
            var scale = Math.Max(0.01, _host.Scale());
            // 멀리서는 일부러 조금 못 미치게 겨눈다(최대 10%) - 배율이 과하면 모형만 믿고 간 만큼 지나치는데, 못 미친 것은 느려진 뒤의 화면으로 마저 당기면 된다.
            var distance = Math.Sqrt(Sq(ex) + Sq(ey));
            var reach = 1 - (ShortfallFraction * Math.Clamp((distance - 20) / 60, 0, 1));
            var dx = Math.Abs(ex) < DeadbandPx ? 0 : ex * scale * reach;
            var dy = Math.Abs(ey) < DeadbandPx ? 0 : ey * scale * reach;

            var alpha = 1 - Math.Exp(-TickMs / TauMs);

            // 남은 거리의 일정 비율 + 몹이 가는 만큼(속도 앞먹임) - 비율만으로는 달리는 몹을 늘 속도×시간 상수만큼 뒤에서 쫓는다.
            var follow = now - t.SeenTicks <= StaleVelocityMs ? TickMs * scale : 0;
            var wantX = (dx * alpha) + (t.Vx * follow);
            var wantY = (dy * alpha) + (t.Vy * follow);

            var magnitude = Math.Sqrt(Sq(wantX) + Sq(wantY));

            if (magnitude > MaxStepCounts)
            {
                wantX *= MaxStepCounts / magnitude;
                wantY *= MaxStepCounts / magnitude;
                magnitude = MaxStepCounts;
            }

            // 빨라질 때만 가속을 막는다. 느려지는 것은 바로 - 새 화면에서 목표가 가까이 나타났는데 관성으로 지나치면 안 된다.
            var ax = wantX - _stepX;
            var ay = wantY - _stepY;
            var accel = Math.Sqrt(Sq(ax) + Sq(ay));

            if (accel > MaxAccelCounts && magnitude > Math.Sqrt(Sq(_stepX) + Sq(_stepY)))
            {
                wantX = _stepX + (ax * MaxAccelCounts / accel);
                wantY = _stepY + (ay * MaxAccelCounts / accel);
            }

            _stepX = wantX;
            _stepY = wantY;
            _carryX += wantX;
            _carryY += wantY;

            ix = (int)Math.Round(_carryX);
            iy = (int)Math.Round(_carryY);
            _carryX -= ix;
            _carryY -= iy;

            if (ix == 0 && iy == 0) return;

            Record(now, ix, iy);
        }

        _host.Move(ix, iy);
    }

    /// <summary>지금 시각의 예상 거리(px). 마지막 본 자리 + 속도 × 시간(상한) − 그 뒤 보낸 것 ÷ 배율.</summary>
    private (double X, double Y) Predict(Track t, long now)
    {
        var age = Math.Max(0, now - t.SeenTicks);
        var coast = Math.Min(age, StaleVelocityMs);
        var (sx, sy) = SentBetween(t.SeenTicks - _latencyMs, long.MaxValue);
        var scale = Math.Max(0.01, _host.Scale());

        return (t.OffX + (t.Vx * coast) - (sx / scale), t.OffY + (t.Vy * coast) - (sy / scale));
    }

    /// <summary>그 프레임 시각의 예상 거리 - 프레임에 이미 반영된 입력(프레임 − 지연 앞)까지만 뺀다.</summary>
    private (double X, double Y) PredictAt(Track t, long frame)
    {
        var gap = Math.Clamp(frame - t.SeenTicks, 0, StaleVelocityMs);
        var (sx, sy) = SentBetween(t.SeenTicks - _latencyMs, frame - _latencyMs);
        var scale = Math.Max(0.01, _host.Scale());

        return (t.OffX + (t.Vx * gap) - (sx / scale), t.OffY + (t.Vy * gap) - (sy / scale));
    }

    /// <summary>(after, until] 사이에 보낸 카운트 합. _sync 안에서.</summary>
    private (double X, double Y) SentBetween(long after, long until)
    {
        double x = 0, y = 0;

        foreach (var (ticks, dx, dy) in _sent)
        {
            if (ticks <= after || ticks > until) continue;

            x += dx;
            y += dy;
        }

        return (x, y);
    }

    private void Record(long now, int dx, int dy)
    {
        _sent.Enqueue((now, dx, dy));

        while (_sent.Count > 0 && now - _sent.Peek().Ticks > SentHistoryMs) _sent.Dequeue();
    }

    /// <summary>짧은 시간을 제대로 기다린다 - 넉넉하면 1ms 씩 재우고 1.5ms 아래는 시계를 보며 버틴다. 중지 토큰을 본다.</summary>
    private void WaitPrecise(double milliseconds)
    {
        var until = Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000.0);

        while (!_disposed)
        {
            var remaining = (until - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;

            if (remaining <= 0) return;

            if (remaining > 1.5)
            {
                if (_host.Token.WaitHandle.WaitOne(1)) return;
            }
            else
            {
                Thread.SpinWait(80);
            }
        }
    }

    private static double Sq(double value) => value * value;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Disengage();
        _wake.Set();

        if (_thread is { } thread && thread != Thread.CurrentThread) thread.Join(300);

        _wake.Dispose();
    }
}
