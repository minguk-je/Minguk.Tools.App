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
/// <b>움직임 - 잘 겨누는 사람의 손처럼</b>(사용자, 2026-09-18 "움직임이 너무 허술해 · 프로게이머처럼"). 박자마다 남은 거리에서 낼 수 있는 속도를 정한다:
/// 빨라질 때 가속 상한(<see cref="MaxAccelPx"/>) → 속도 상한(<see cref="MaxSpeedPxPerMs"/>) → <b>일정 감속</b>(<see cref="DecelPxPerMs2"/>, √(2·감속·거리)) → 마지막 수십 px 만
/// 1차 접근(<see cref="TauFarMs"/>, 붙은 뒤에는 <see cref="TauNearMs"/>). 속도가 종 모양이라 빠르게 올렸다 딱 선다 - 1차 접근만 쓰던 때(시간 상수 95ms)는 끝이 지수 꼬리라
/// 300px 을 20px 까지 344ms 걸려 "거의 다 왔는데 안 붙는" 시간이 길었다. 느려지는 쪽은 안 막는다 - 새 화면에서 목표가 가까이 나타났을 때 관성으로 지나치면 안 된다.
/// 소수 카운트는 이월해 작은 걸음도 합이 맞다.
/// <b>크게 꺾을 때는 두 번에 나눈다</b> - 첫 움직임은 일부러 8% 못 미치게 던지고(<see cref="ShortfallFraction"/>), 그 결과가 담긴 화면을 본 뒤 남긴 것을 풀어 다듬는다. 0.13초짜리
/// 꺾기는 도중에 화면으로 고칠 수 없어(입력이 화면에 오르기까지 0.1초) 정확도가 배율에 달렸는데, 잰 배율은 ±15% 흩어진다 - 끝까지 가면 300px 에서 45px 를 지나쳐 되돌아온다.
///
/// <b>새 화면</b> - 허브의 새 검출이 오면 프레임 시각의 예상 자리에서 가장 가까운 사각형을 같은 몹으로 보고(반지름 = 크기×2 + 속도 여유), 예상과의 차이를 일부만
/// 받아들인다(가로 <see cref="GainX"/>·세로 <see cref="GainY"/> - 검출 사각형은 가만히 있는 몹도 세로 17px 흔들린다, 실측).
/// <b>화면용 추적기를 거친 사각형이 아니라 날것을 본다</b>(<see cref="DetectionSnapshot.Raw"/>) - 추적기는 옛 화면 좌표와 섞고 못 이으면 옛 사각형을 내줘, 화면을 돌리는 동안에는
/// 늦은 자리다. 그것을 보던 때는 "몹이 달아났다" 로 읽고 더 밀어 -159px 에서 +75px 로 지나친 뒤 1초에 걸쳐 돌아왔다(실측 로그) - "휙휙" 과 "허술" 의 뿌리가 그것이었다.
/// 속도는 두 프레임 사이의 자리 변화에서 우리가 돌린 만큼을 되돌려 재고 지수 평균한다 - <b>우리가 휙 꺾는 중이던 쌍으로는 안 잰다</b>(<see cref="MobVelocity"/>, 지연 20ms 차이가
/// 가짜 속도 0.78px/ms 가 되어 앞먹임이 조준을 목표 옆에 1초씩 걸어 뒀다). 못 찾으면 <see cref="LockGraceMs"/> 동안은 예측으로 이어 가고, 넘으면 놓는다(<see cref="IsEngaged"/> false).
///
/// <b>배율 배우기</b>는 예전 규칙 그대로 <see cref="LiveScriptApi"/> 에 맡긴다 - 표본은 둘: 크게 꺾은 뒤의 확인 화면(꺾기 전 본 자리 → 지금, 그 사이 보낸 전부 - 첫 움직임 뒤로는
/// 거의 안 보내 지연과 무관하다)과, 멈춰 서 있던 프레임(닻)에서 지금까지 길게 재어 지연을 모르는 부분이 총량의 15% 아래일 때 닻마다 한 번.
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

    /// <summary>가까이 붙어 따라갈 때의 시간 상수(ms) - <see cref="NearPx"/> 안. 검출 떨림(가만히 선 몹도 몇 px)에 마우스가 따라 떨지 않을 만큼 느긋하게.</summary>
    private const double TauNearMs = 45;

    /// <summary>떨어져 있을 때의 시간 상수(ms) - <see cref="FarPx"/> 밖. 감속 구간이 끝난 마지막 수십 px 을 또렷하게 붙인다.</summary>
    private const double TauFarMs = 22;

    private const double NearPx = 8;

    private const double FarPx = 30;

    /// <summary>
    /// 꺾는 속도의 상한(px/ms) - 300px 을 0.13초쯤에. 화면 px 로 둔다 - 카운트로 두면 배율(게임 감도)에 따라 꺾는 속도가 달라진다.
    /// </summary>
    private const double MaxSpeedPxPerMs = 3.9;

    /// <summary>빨라질 때 박자 사이 걸음 변화 상한(px). 정지에서 상한 속도까지 5박자(40ms) - 첫 박자부터 상한으로 튀어 나가지는 않게.</summary>
    private const double MaxAccelPx = 6.3;

    /// <summary>
    /// 멈출 때의 감속(px/ms²) - 남은 거리 d 에서 낼 수 있는 속도는 √(2·감속·d). 상한 속도에서 100px 안에 선다.
    /// </summary>
    /// <remarks>
    /// 남은 거리의 일정 비율만 보내는 1차 접근은 끝이 지수 꼬리라 "거의 다 왔는데 안 붙는" 시간이 길다(시간 상수 95ms 일 때 300px → 20px 에 344ms, 45ms 로 줄여도 190ms).
    /// 잘 겨누는 사람의 손은 빠르게 올렸다가 <b>일정하게 줄여서 딱 선다</b>(종 모양 속도) - 가속 상한 → 속도 상한 → 일정 감속 → 마지막 몇 px 만 1차 접근.
    /// </remarks>
    private const double DecelPxPerMs2 = 0.076;

    /// <summary>움직일 때 한 박자의 최소 걸음(px). 마지막 몇 px 을 1차 접근의 꼬리로 0.3초씩 끌지 않게.</summary>
    private const double MinStepPx = 0.5;

    /// <summary>
    /// 크게 꺾을 때 첫 움직임이 일부러 남기는 비율. <see cref="FlickNearPx"/> 부터 늘어 <see cref="FlickFarPx"/> 넘으면 이만큼.
    /// </summary>
    /// <remarks>
    /// 0.15초짜리 꺾기는 도중에 화면으로 고칠 수 없다(보낸 입력이 화면에 오르기까지 0.1초) - 정확도는 오로지 배율이 얼마나 맞느냐다. 그런데 게임에서 잰 배율은
    /// 3.1~4.2 로 ±15% 흩어진다(가장자리 원근·몹의 움직임). 모형만 믿고 끝까지 가면 300px 에서 45px 를 지나쳐 되돌아온다 - 그게 허술해 보인다.
    /// 사람(프로)도 크게 꺾을 때는 살짝 못 미치게 던지고(첫 움직임), 눈으로 확인한 뒤 짧게 다듬는다(둘째 움직임). 첫 움직임이 끝나고 그것이 다 반영된 화면
    /// (<see cref="SettleLatencyMs"/>)이 오면 남긴 것을 풀고 그 화면을 그대로 믿는다. 예전 값(0.1)은 박자마다 남은 거리에 곱해져 속도만 10% 늦출 뿐 남기는 것이 없었다.
    /// </remarks>
    private const double ShortfallFraction = 0.08;

    /// <summary>이보다 멀면 "크게 꺾기" 로 본다 - 끝난 뒤 그 결과가 담긴 화면(확인 화면)을 가려 그대로 믿고 배율 표본을 만든다.</summary>
    private const double FlickNearPx = 60;

    /// <summary>
    /// 남기는 것은 이보다 먼 꺾기부터(<see cref="HoldFarPx"/> 에서 <see cref="ShortfallFraction"/> 전부). 그 아래는 남기지 않고 한 번에 간다.
    /// <b>60 이었다가 올렸다</b>(실측 2026-09-18): 사격장 봇은 옆으로 걷는데(150px/s) 확인 화면을 기다리며 서 있는 동안(중간값 141ms, 90% 235ms) 봇이 20~35px 달아나,
    /// 100~200px 꺾기 33번 가운데 32번이 거리의 20%(중간값)를 모자란 채 멈춰 있었다. 그 거리에서 배율 15% 가 만드는 오차(15~30px)는 몸 안이고 기다리는 값과 비슷하다 -
    /// 남기는 것은 그 오차가 몸 밖으로 나가는 먼 꺾기에서만 값을 한다.
    /// </summary>
    private const double HoldNearPx = 200;

    private const double HoldFarPx = 300;

    /// <summary>첫 움직임이 끝났다고 볼 남은 거리(px) - 남긴 자리까지.</summary>
    private const double PrimaryDonePx = 4;

    /// <summary>첫 움직임이 다 반영된 화면으로 볼 지연(ms). 프레임 시각 − 이 값이 첫 움직임 끝보다 뒤면 그 화면은 꺾은 결과를 다 담고 있다(실측 지연 80~100).</summary>
    private const int SettleLatencyMs = 100;

    /// <summary>
    /// 확인하는 화면이 안 와도 이만큼(ms) 지나면 남긴 것을 푼다 - 몹 찾기가 밀려도 못 미친 채 서 있지 않게. 첫 움직임 0.13초 + 지연 0.1초 + 프레임 주기 0.1초보다는 길어야 한다
    /// (300 이었을 때 0.1초 프레임에서 확인 화면보다 먼저 풀려 버리는 일이 잦았다).
    /// </summary>
    private const int HoldTimeoutMs = 450;

    /// <summary>이 안(px)에 붙으면 다음 큰 꺾기를 다시 준비한다.</summary>
    private const double RearmPx = 30;

    /// <summary>이보다 가까우면 안 움직인다(px). 검출 떨림에 마우스가 떨지 않게.</summary>
    private const double DeadbandPx = 1.5;

    /// <summary>
    /// 붙은 뒤에는 가만히 - 남은 거리가 <see cref="RestInPx"/> 안에 들면 쉬고, <see cref="RestOutPx"/> 를 넘어야 다시 움직인다(히스테리시스).
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-18) "화면이 너무 떨려·헛발이 많다". 붙어 있는 프레임에서 프레임마다 가로 중간값 14카운트(4px)·90% 72카운트(20px)를 보내고 있었다 - 검출 사각형이
    /// 몇 px 씩 흔들리는 것을 그때그때 따라간 것이다(1.5px 문턱은 거의 늘 넘는다). 사람은 몸 안에 들어오면 손을 세운다. 몸 폭 60px 이니 5px 안은 그냥 둔다.
    /// </remarks>
    private const double RestInPx = 4;

    private const double RestOutPx = 8;

    /// <summary>이보다 느린 속도(px/ms)는 0 으로 본다 - 붙어 있는 동안 잰 속도의 중간값이 33px/s 였는데 떨림이지 움직임이 아니다. 80px/s 아래는 앞먹임·앞서 겨누기에서 뺀다.</summary>
    private const double VelocityDeadPxPerMs = 0.08;

    /// <summary>
    /// 보낸 입력이 캡처된 화면에 보이기까지(ms). 프레임 시각에서 이만큼 앞선 뒤의 입력은 그 프레임에 아직 없는 것으로 본다.
    /// 실측은 80~100ms 쯤(붙인 뒤 움직임이 화면에 처음 보이기까지 중간값 164ms − 프레임 주기 절반 − 가속) - 틀려도 안전한 쪽으로 조금 길게.
    /// </summary>
    private readonly int _latencyMs = 110;

    /// <summary>몹의 자리를 앞서 잡는 시간(ms) - 화면이 늦은 만큼. 입력 지연(<see cref="_latencyMs"/>)보다 조금 짧게 - 입력은 게임이 받아 그리기까지가 더 든다.</summary>
    private const int MobLeadMs = 90;

    /// <summary>지연이 이 사이 어디인지 모른다고 본다(ms). 이 띠 안에 보낸 양이 프레임 사이에 바뀐 만큼이 그 화면의 불확실함이다.</summary>
    private const int LatencyMinMs = 40;

    private const int LatencyMaxMs = 200;

    /// <summary>불확실함(px)이 이만큼이면 화면을 절반만 믿는다.</summary>
    private const double UncertaintyHalfPx = 24;

    /// <summary>예측과의 차이가 이보다 작으면(px) 검출 떨림으로 보고 조금만 받아들인다(<see cref="Calm"/>) - 붙은 뒤 좌우로 떨지 않게.</summary>
    private const double JitterPx = 8;

    /// <summary>예측과의 차이가 이보다 크면(px) 떨림이 아니라 진짜 움직임으로 본다.</summary>
    private const double RealMovePx = 24;

    /// <summary>못 본 채 예측으로 이어 가는 시간(ms). 넘으면 놓는다 - 잠깐 가려진 것과 죽은 것을 구별할 길이 없다.</summary>
    private const int LockGraceMs = 400;

    /// <summary>못 본 채 속도로 밀어 주는 시간 상한(ms). 그 뒤로는 마지막 예측 자리에 선다.</summary>
    private const int StaleVelocityMs = 160;

    /// <summary>
    /// 속도 상한(px/ms) - 400px/s. 화면에서 그보다 빠른 몹은 없다(사격장 봇의 옆걸음은 150px/s 쯤). 1.5 였을 때 검출 사각형이 한 장 40px 튄 것을 −363·+619px/s 로 받아,
    /// 앞서 겨누기·앞먹임이 그쪽으로 밀어 100~180px 꺾기가 반대편으로 87~89px 지나쳤다(실측 2026-09-18).
    /// </summary>
    private const double MaxVelocityPxPerMs = 0.4;

    /// <summary>새 사각형과 예측의 차이를 받아들이는 비율. 몹은 옆으로 달리므로 가로는 크게, 세로·크기는 흔들림이 커 작게.</summary>
    private const double GainX = 0.75;

    private const double GainY = 0.45;

    private const double GainSize = 0.35;

    /// <summary>속도 지수 평균의 새 값 비중.</summary>
    private const double VelocityAlphaX = 0.4;

    /// <summary>예측과의 차이(px) ÷ 프레임 간격을 속도에 보태는 비율.</summary>
    private const double VelocityBeta = 0.15;

    /// <summary>처음 잰 속도의 비중(가로. 세로는 절반 - 세로는 사각형 흔들림이 크다).</summary>
    private const double FirstVelocityAlpha = 0.8;

    private const double VelocityAlphaY = 0.25;

    /// <summary>붙일 때 "이미 따라가던 그 몹인가" 를 가르는 거리 - 크기의 몇 배. 스크립트가 준 자리는 화면용 추적기 것이라 넉넉히.</summary>
    private const double LockRadiusFactor = 2.0;

    /// <summary>새 화면에서 같은 몹으로 볼 거리의 바탕 - 크기의 몇 배(머리 자리 기준).</summary>
    private const double GateSizeFactor = 0.6;

    /// <summary>속도를 모르는 몹이 움직일 수 있다고 보는 빠르기(px/ms) - 사격장 봇의 옆걸음이 150px/s 쯤.</summary>
    private const double UnknownMobSpeedPxPerMs = 0.15;

    /// <summary>마지막으로 본 뒤 우리가 돌린 양 가운데 모른다고 보는 몫 - 배율 ±15% 와 지연.</summary>
    private const double GateTurnFraction = 0.25;

    /// <summary>같은 몹이면 사각형 높이가 이 비율 안이다.</summary>
    private const double GateMinSizeRatio = 0.6;

    private const double GateMaxSizeRatio = 1.6;

    /// <summary>가만히 겨누는 중(최근 0.2초에 돌린 것이 <see cref="QuietTurnPx"/> 아래)에 이만큼 연달아·이만큼 오래 안 보이면 사라진 것으로 보고 놓는다.</summary>
    private const int GoneFrames = 3;

    private const int GoneMs = 150;

    private const double QuietTurnPx = 12;

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

    /// <summary>붙어서 쉬는 중인가(<see cref="RestInPx"/>).</summary>
    private bool _resting;

    /// <summary>첫 움직임이 남기는 양(px, 목표 쪽 벡터). 0 이면 남기는 것 없음.</summary>
    private double _holdX;
    private double _holdY;

    /// <summary>큰 꺾기를 시작할 수 있는가 - 새로 붙였거나 한 번 붙은 뒤.</summary>
    private bool _flickArmed = true;

    /// <summary>크게 꺾는 중인가 - 확인 화면이 오거나 시간이 지나면 끝난다.</summary>
    private bool _flicking;

    private long _holdStartTicks;

    /// <summary>첫 움직임이 끝난 시각. 0 이면 아직 가는 중.</summary>
    private long _primaryDoneTicks;

    /// <summary>꺾기 시작할 때 마지막으로 본 날것 자리(px)와 그 프레임 시각 - 배율 표본의 "앞".</summary>
    private double _flickFromX;
    private double _flickFromY;
    private long _flickFromTicks;

    /// <summary>마지막 확인 화면의 프레임 시각 - 그 직후의 화면은 조심해서 받는다.</summary>
    private long _settledFrameTicks = long.MinValue / 2;

    /// <summary>꺾기 전 본 자리에 반영됐는지 모르는 입력(카운트, 절댓값).</summary>
    private double _flickUnsureX;
    private double _flickUnsureY;

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

        /// <summary>연달아 못 본 프레임 수.</summary>
        public int MissFrames;

        /// <summary>속도를 잰 횟수 - 처음 잰 값은 크게 받아들인다.</summary>
        public int VelocitySamples;

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
            ResetFlick();
            SeedVelocity(_track, bounds);

            Logger.Debug($"조준 붙임: {mob.Name} 거리({offX:0}, {offY:0}) 크기 {mob.Width}x{mob.Height} 속도({_track.Vx * 1000:0}, {_track.Vy * 1000:0})px/s");

            _thread ??= Start();
        }

        _wake.Set();
    }

    /// <summary>
    /// 붙이는 순간의 속도 - 허브의 최근 두 장(날것)에서 이 몹을 찾아 잰다. 못 찾거나 그사이 우리가 돌리고 있었으면 그대로 0 이다. _sync 안에서.
    /// </summary>
    /// <remarks>이유는 <see cref="DetectionSnapshot.Previous"/>. 찾았으면 자리도 날것의 최신 장으로 옮긴다 - 스크립트가 준 자리는 화면용 추적기가 섞은 것이라 걷는 봇이면 몇 px 늦다.</remarks>
    private void SeedVelocity(Track t, Rect bounds)
    {
        if (_host.Latest() is not { Previous: { } before } latest) return;

        var frame = latest.FrameTicks > 0 ? latest.FrameTicks : latest.Ticks;
        var beforeFrame = before.FrameTicks > 0 ? before.FrameTicks : before.Ticks;
        var gap = frame - beforeFrame;

        if (gap <= 0 || gap > MaxVelocityGapMs || frame < t.SeenTicks) return;

        var scale = Math.Max(0.01, _host.Scale());
        var size = Math.Max(t.W, t.H);

        if (Nearest(latest.ForAiming, bounds, t.OffX, t.OffY, t.H) is not var (nowX, nowY, nowDistance) || nowDistance > size * GateSizeFactor) return;

        if (Nearest(before.ForAiming, bounds, nowX, nowY, t.H) is not var (wasX, wasY, wasDistance)
            || wasDistance > (size * GateSizeFactor) + (TeleportPxPerMs * gap))
            return;

        // 새 자리는 늘 옮긴다. 속도는 우리가 가만히 있던 쌍일 때만.
        t.OffX = t.ObsX = t.AnchorX = nowX;
        t.OffY = t.ObsY = t.AnchorY = nowY;
        t.SeenTicks = t.AnchorTicks = frame;
        t.HasObservation = true;
        _consumedFrameTicks = frame;

        foreach (var latency in VelocityLatenciesMs)
        {
            var (sx, sy) = SentBetween(beforeFrame - latency, frame - latency);

            if (Math.Sqrt(Sq(sx) + Sq(sy)) / scale / gap > OwnQuietPxPerMs) return;
        }

        var (sentX, sentY) = SentBetween(beforeFrame - _latencyMs, frame - _latencyMs);
        var vx = (nowX - wasX + (sentX / scale)) / gap;
        var vy = (nowY - wasY + (sentY / scale)) / gap;

        if (Math.Sqrt(Sq(vx) + Sq(vy)) > TeleportPxPerMs) return;

        // 우리가 가만히 있던 쌍이라 가장 깨끗한 표본이다 - 바로 다 믿는다(VelocityConfidence). 붙인 뒤 첫 표본은 반만.
        t.Vx = vx * FirstVelocityAlpha;
        t.Vy = vy * FirstVelocityAlpha * 0.5;
        t.VelocitySamples = 2;
    }

    /// <summary>그 자리(화면 가운데 기준 머리 거리)에서 가장 가까운 검출 - 크기가 비슷한 것만. 없으면 null.</summary>
    private static (double X, double Y, double Distance)? Nearest(IReadOnlyList<Minguk.Tools.Vision.Inference.Detection> seen, Rect bounds, double x, double y, double height)
    {
        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);
        (double X, double Y, double Distance)? best = null;

        foreach (var detection in seen)
        {
            var box = detection.Box;
            var boxHeight = box.Height * bounds.Height;

            if (boxHeight < height * GateMinSizeRatio || boxHeight > height * GateMaxSizeRatio) continue;

            var headX = bounds.Left + (box.CenterX * bounds.Width) - cx;
            var headY = bounds.Top + ((box.CenterY - (box.Height / 2) + (box.Height * ScriptMob.HeadFraction)) * bounds.Height) - cy;
            var distance = Math.Sqrt(Sq(headX - x) + Sq(headY - y));

            if (best is null || distance < best.Value.Distance) best = (headX, headY, distance);
        }

        return best;
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
            ResetFlick();
        }
    }

    /// <summary>남긴 것을 버리고 다음 큰 꺾기를 준비한다. _sync 안에서.</summary>
    private void ResetFlick()
    {
        _holdX = _holdY = 0;
        _flicking = false;
        _resting = false;
        _flickArmed = true;
        _primaryDoneTicks = 0;
        _flickFromTicks = 0;
        _settledFrameTicks = long.MinValue / 2;
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

        // 화면용 추적을 안 거친 날것을 본다 - 추적기는 옛 화면 좌표와 섞고 못 이으면 옛 사각형을 그대로 내줘서, 화면을 돌리는 중에는 늦은 자리다(DetectionSnapshot.Raw).
        var seen = snapshot.ForAiming;

        if (_host.Bounds() is not { } bounds) return;

        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);
        var scale = Math.Max(0.01, _host.Scale());

        lock (_sync)
        {
            if (!_engaged || _track is not { } t) return;

            // 프레임 시각의 예상 자리 - 그때까지(지연만큼 앞서) 보낸 것만 뺀다.
            var (px, py) = PredictAt(t, frame);

            // 같은 몹으로 볼 거리. 날것에는 옆의 다른 봇도 그대로 들어 있어 넉넉히(크기×2) 잡으면 붙잡은 봇이 한 장 안 보일 때 옆 봇으로 건너뛴다 - 실측(2026-09-18): 가만히 있던
            // 프레임 쌍의 7% 에서 본 자리가 60px 넘게 뛰었고(한 번은 650px), 그 쌍으로 잰 배율 표본(7.0·7.4·9.6)이 배율을 3.3↔4.3 으로 흔들었다.
            // 몸 크기의 0.6배 + 몹이 그사이 갈 수 있는 만큼(아는 속도 + 모르는 0.15px/ms) + 우리가 돌린 양의 25%(배율·지연을 모르는 몫)만 연다.
            var sinceSeen = Math.Max(0, frame - t.SeenTicks);
            var (turnedX, turnedY) = SentBetween(t.SeenTicks - _latencyMs, frame);
            var radius = (Math.Max(t.W, t.H) * GateSizeFactor)
                         + ((Math.Sqrt(Sq(t.Vx) + Sq(t.Vy)) + UnknownMobSpeedPxPerMs) * Math.Min(sinceSeen, LockGraceMs))
                         + (Math.Sqrt(Sq(turnedX) + Sq(turnedY)) / scale * GateTurnFraction);

            var bestDistance = double.MaxValue;
            var found = false;
            double obsX = 0, obsY = 0, w = 0, h = 0;
            var bestIndex = -1;

            for (var i = 0; i < seen.Count; i++)
            {
                var box = seen[i].Box;
                var headX = bounds.Left + (box.CenterX * bounds.Width) - cx;
                var headY = bounds.Top + ((box.CenterY - (box.Height / 2) + (box.Height * ScriptMob.HeadFraction)) * bounds.Height) - cy;
                var distance = Math.Sqrt(Sq(headX - px) + Sq(headY - py));
                var boxHeight = box.Height * bounds.Height;

                if (distance >= bestDistance) continue;

                // 크기가 많이 다르면 다른 거리에 선 다른 봇이다.
                if (boxHeight < t.H * GateMinSizeRatio || boxHeight > t.H * GateMaxSizeRatio) continue;

                bestDistance = distance;
                obsX = headX;
                obsY = headY;
                w = box.Width * bounds.Width;
                h = boxHeight;
                bestIndex = i;
                found = distance <= radius;
            }

            Interlocked.Increment(ref _framesConsumed);

            if (!found)
            {
                if (t.FirstMissTicks == 0) t.FirstMissTicks = now;

                t.MissFrames++;

                // 가만히 겨누고 있는데 연달아 안 보이면 죽은 것이다 - 바로 놓아 스크립트가 다음 몹을 고르게 한다(실측: 붙인 142번 가운데 49번이 400ms 를 다 기다리고 끝났다 - 죽은 봇마다 0.4초).
                // 휙 꺾는 중에는 화면이 번져 몇 장 안 보일 수 있으니 그때는 끝까지 기다린다.
                var (movingX, movingY) = SentBetween(frame - LatencyMaxMs, frame);
                var quiet = (Math.Abs(movingX) + Math.Abs(movingY)) / scale <= QuietTurnPx;
                var gone = quiet && t.MissFrames >= GoneFrames && now - t.FirstMissTicks >= GoneMs;

                if (!gone && now - t.FirstMissTicks <= LockGraceMs) return;

                var nearest = bestIndex < 0 ? "없음" : $"{bestDistance:0}px";
                var why = gone ? $"가만히 겨누는데 {t.MissFrames}장째 안 보인다" : $"{LockGraceMs}ms 넘게 못 봤다";

                Logger.Debug($"조준 놓침: {why} (예상 거리 {px:0}, {py:0} · 보인 것 {seen.Count}마리 · 가장 가까운 것 {nearest} · 문 {radius:0}px)");
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

            // 첫 움직임(크게 꺾기)이 끝난 뒤 그것이 다 반영된 화면이다 - 그 뒤로는 거의 안 보냈으니 지연이 얼마든 답이 같다. 그대로 믿고, 남긴 것을 풀어 마저 다듬는다.
            var settled = _flicking && _primaryDoneTicks > 0 && frame - SettleLatencyMs >= _primaryDoneTicks;

            if (settled)
            {
                trustX = trustY = 1;
                _flicking = false;
                _holdX = _holdY = 0;

                // 배율 표본 - 꺾기 전 본 자리 → 지금 본 자리, 그 사이 보낸 전부. 첫 움직임 뒤로는 거의 안 보냈으니 지연이 얼마든 "이 화면에 다 반영됐다" - 닻 방식보다 깨끗하고
                // 큰 꺾기마다 하나씩 나온다(닻 방식은 배율이 많이 틀린 동안 조금씩 고쳐 가느라 조용한 프레임이 늦게 와, 그 전에 몹이 죽으면 표본이 없었다).
                // 달리던 몹(꺾기 전에 잰 속도가 있을 때)이면 줄어든 거리에 몹의 움직임이 섞여 안 준다. 막 붙인 몹은 속도를 모르지만 다가오는 몹과 달아나는 몹이 반반이라 가운뎃값이 거른다.
                var sampled = false;

                _settledFrameTicks = frame;

                if (_flickFromTicks > 0 && Math.Abs(t.Vx) <= StillPxPerMs && Math.Abs(t.Vy) <= StillPxPerMs)
                {
                    var (flickX, flickY) = SentBetween(_flickFromTicks - LatencyMinMs, long.MaxValue);
                    var horizontal = Math.Abs(flickX) >= Math.Abs(flickY);
                    var total = horizontal ? flickX : flickY;

                    if ((horizontal ? _flickUnsureX : _flickUnsureY) <= Math.Abs(total) * 0.15)
                    {
                        _host.Learn(horizontal ? _flickFromX : _flickFromY, horizontal ? obsX : obsY, total);
                        sampled = true;

                        // 같은 움직임을 닻 방식이 한 번 더 재지 않게.
                        t.AnchorUsed = true;
                    }
                }

                Logger.Debug($"확인 화면: 본 것({obsX:0}, {obsY:0}) · 첫 움직임이 끝나고 {frame - _primaryDoneTicks}ms 뒤의 프레임 · 속도({t.Vx * 1000:0}, {t.Vy * 1000:0})px/s{(sampled ? " · 배율 표본" : string.Empty)}");

                _flickFromTicks = 0;
            }

            // 속도 - 두 프레임 사이 자리 변화에서 우리가 돌린 만큼을 되돌린다(보낸 카운트는 거리를 줄였으니 더한다). 못 믿을 화면으로는 덜 고친다.
            if (t.HasObservation && gap > 0 && gap <= MaxVelocityGapMs)
            {
                // 우리가 휙 꺾는 중이던 쌍이면 안 잰다 - 알던 속도를 그대로 둔다.
                if (MobVelocity(t, obsX, obsY, frame, gap, scale) is var (rawVx, rawVy))
                {
                    // 한 프레임에 이만큼 뛰었으면 달린 것이 아니라 다른 몹으로 이어진 것이다(죽은 봇 옆의 새 봇) - 속도로 읽으면 앞먹임이 몇 프레임 동안 엉뚱한 쪽으로 민다.
                    if (Math.Sqrt(Sq(rawVx) + Sq(rawVy)) > TeleportPxPerMs)
                    {
                        rawVx = rawVy = t.Vx = t.Vy = 0;
                        t.VelocitySamples = 0;
                    }

                    // 처음 잰 값은 크게 받아들인다 - 사격장 봇은 늘 옆으로 걷는데(150px/s), 평균에 0.35 씩 섞으면 제 속도를 아는 데 0.3초가 걸려 그동안 20~30px 뒤에서 쫓는다.
                    // 크게 받는 것은 조용한 쌍일 때만 - 남긴 것을 푸는 움직임(0.4px/ms)이 낀 쌍의 가짜 속도를 0.8 로 받으면 붙은 직후 12px 을 흔들렸다.
                    var first = t.VelocitySamples == 0 && _velocityPairWasQuiet;
                    var alphaX = first ? FirstVelocityAlpha : VelocityAlphaX;
                    var alphaY = first ? FirstVelocityAlpha * 0.5 : VelocityAlphaY;

                    t.Vx = Math.Clamp(t.Vx + ((rawVx - t.Vx) * alphaX * trustX), -MaxVelocityPxPerMs, MaxVelocityPxPerMs);
                    t.Vy = Math.Clamp(t.Vy + ((rawVy - t.Vy) * alphaY * trustY), -MaxVelocityPxPerMs, MaxVelocityPxPerMs);
                    t.VelocitySamples++;
                }
            }
            else
            {
                t.Vx = t.Vy = 0;
                t.VelocitySamples = 0;
            }

            // 배율 표본 - <b>닻(멈춰 서 있던 프레임) → 지금</b> 보낸 총량 ÷ 줄어든 거리. 프레임마다 재면 지연 안의 입력이 반영됐는지 몰라 못 믿는데, 닻에서부터 길게 재면
            // 그 모르는 부분(띠 안에 보낸 양)이 총량에 견줘 작아진다 - 15% 아래일 때 한 번 준다. 완전히 멈춰 서기를 기다리면 배율이 많이 틀린 동안에는(끝없이 조금씩
            // 고쳐 가느라) 표본이 영영 안 나온다. 몹이 달리는 중(속도 120px/s 넘게)에는 줄어든 거리에 몹의 움직임이 섞여 안 준다. 닻은 다시 멈춰 서면 새로 놓는다.
            {
                var (totalX, totalY) = SentBetween(t.AnchorTicks - _latencyMs, frame - _latencyMs);
                var horizontal = Math.Abs(totalX) >= Math.Abs(totalY);
                var total = horizontal ? totalX : totalY;
                var band = horizontal ? bandX : bandY;
                var still = Math.Abs(t.Vx) <= StillPxPerMs && Math.Abs(t.Vy) <= StillPxPerMs;

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

            // 예측과의 차이. <b>남긴 것을 푼 직후</b>(<see cref="ReleaseCautionMs"/>)에는 지연을 짧게·길게 가정해도 남는 만큼만 받아들인다(Cautious) - 그 짧은 움직임(24px, 0.4px/ms)이
            // 화면에 어디까지 올랐는지는 지연 20ms 차이로 8px 이 갈리는데, 기준 지연 하나로만 재면 "지나쳤다" 로 잘못 읽고 뒤로 물러났다(검사 궤적: 2 → −2 → 12 → 18 → 17, 0.5초에 걸쳐 복귀).
            // 늘 그렇게 하지는 않는다 - 달리는 몹을 따라가는 중에는 우리가 계속 움직여 가정들이 늘 12px 쯤 갈리고, 그만큼이 죽은 띠가 되어 몹을 10px 뒤에서 쫓았다.
            var innovationX = obsX - px;
            var innovationY = obsY - py;

            if (!settled && frame - _settledFrameTicks <= ReleaseCautionMs)
            {
                var (shortX, shortY) = PredictAt(t, frame, InnovationLatencyMinMs);
                var (longX, longY) = PredictAt(t, frame, InnovationLatencyMaxMs);

                innovationX = Cautious(innovationX, obsX - shortX, obsX - longX);
                innovationY = Cautious(innovationY, obsY - shortY, obsY - longY);
            }

            // 차이가 한쪽으로 이어지면 속도가 모자란 것이다 - 속도에도 조금 보탠다(α-β). 자리만 고치면 떨림을 누르느라(Calm) 달아나는 몹의 자리를 늘 10px 쯤 늦게 알아,
            // 속도를 맞게 알고도 20~30px 뒤에서 1초 넘게 쫓았다(가짜 게임 궤적: 27 → 34 → 30 → 27 → 17 …). 우리가 휙 꺾는 중이던 화면으로는 안 한다.
            if (!settled && t.VelocitySamples > 0 && gap > 0 && gap <= MaxVelocityGapMs && MobVelocity(t, obsX, obsY, frame, gap, scale) is not null)
            {
                t.Vx = Math.Clamp(t.Vx + (VelocityBeta * innovationX / gap * trustX), -MaxVelocityPxPerMs, MaxVelocityPxPerMs);
                t.Vy = Math.Clamp(t.Vy + (VelocityBeta * 0.5 * innovationY / gap * trustY), -MaxVelocityPxPerMs, MaxVelocityPxPerMs);
            }

            t.OffX = settled ? obsX : px + (innovationX * GainX * trustX * Calm(innovationX));
            t.OffY = settled ? obsY : py + (innovationY * GainY * trustY * Calm(innovationY, JitterPx * 1.5, RealMovePx * 1.75));
            t.W += (w - t.W) * GainSize;
            t.H += (h - t.H) * GainSize;
            t.SeenTicks = frame;
            t.HasObservation = true;
            t.ObsX = obsX;
            t.ObsY = obsY;
            t.FirstMissTicks = 0;
            t.MissFrames = 0;
            t.Name = seen[bestIndex].Label;
            t.Score = seen[bestIndex].Score;
            t.Box = seen[bestIndex].Box;

            Logger.Debug($"추적: 거리({t.OffX:0}, {t.OffY:0}) 본 것({obsX:0}, {obsY:0}) 속도({t.Vx * 1000:0}, {t.Vy * 1000:0})px/s 보냄({sentX}, {sentY}) 크기 {t.W:0}x{t.H:0} 믿음 {trustX:0.00} · 날것 {w:0}x{h:0} 점수 {t.Score:0.00} 후보 {seen.Count} 문 {radius:0}px");
        }
    }

    /// <summary>
    /// 두 프레임 사이 몹의 속도(px/ms) - 자리 변화에서 우리가 돌린 만큼을 되돌린 것. <b>우리가 휙 꺾는 중이던 프레임 쌍으로는 재지 않는다</b>(null).
    /// </summary>
    /// <remarks>
    /// 우리가 돌린 만큼은 "프레임 − 지연" 까지 보낸 양인데 지연을 정확히 모른다. 천천히 따라가는 중에는 20ms 틀려도 몇 px 이지만, 휙 꺾는 중에는
    /// 3.9px/ms × 20ms = 78px - 0.1초 프레임 간격이면 0.78px/ms 짜리 <b>가짜 속도</b>가 된다. 그 속도를 앞먹임이 그대로 밀어서, 꺾은 뒤 조준이 목표 옆 10~15px 에
    /// 1초 가까이 걸려 있었다(검사 궤적: 32 → 3 → −11 → −14 → −16 … −6). 지연을 여러 값으로 가정해 그 사이 우리가 돌린 속도가 <see cref="OwnFlickPxPerMs"/> 를
    /// 넘는 것이 하나라도 있으면 꺾는 중으로 본다 - 달리는 몹을 따라가는 속도(0.2~0.6px/ms)는 그 아래다.
    /// 가정들이 엇갈린 폭만큼 속도를 줄이는 방식은 안 됐다 - 속도를 작게 알면 프레임마다 몰아서 보내게 되고, 그러면 폭이 더 커져 끝내 0 으로 붙는다(달리는 몹을 20px 뒤에서 쫓았다).
    /// </remarks>
    private (double Vx, double Vy)? MobVelocity(Track t, double obsX, double obsY, long frame, long gap, double scale)
    {
        _velocityPairWasQuiet = true;

        foreach (var latency in VelocityLatenciesMs)
        {
            var (sx, sy) = SentBetween(t.SeenTicks - latency, frame - latency);
            var own = Math.Sqrt(Sq(sx) + Sq(sy)) / scale / gap;

            if (own > OwnFlickPxPerMs) return null;
            if (own > OwnQuietPxPerMs) _velocityPairWasQuiet = false;
        }

        var (sentX, sentY) = SentBetween(t.SeenTicks - _latencyMs, frame - _latencyMs);

        return ((obsX - t.ObsX + (sentX / scale)) / gap, (obsY - t.ObsY + (sentY / scale)) / gap);
    }

    /// <summary>두 프레임 사이 자리 변화가 이보다 빠르면(px/ms) 움직인 것이 아니라 다른 몹이거나 사각형이 튄 것으로 본다 - 화면에서 450px/s 로 달리는 몹은 없다.</summary>
    private const double TeleportPxPerMs = 0.45;

    /// <summary>몹이 서 있다고 볼 속도(px/ms) - 120px/s. 이보다 빠르면 줄어든 거리에 몹의 움직임이 섞여 배율 표본으로 못 쓴다.</summary>
    private const double StillPxPerMs = 0.12;

    /// <summary>방금 <see cref="MobVelocity"/> 가 본 쌍이 조용했는가(우리가 <see cref="OwnQuietPxPerMs"/> 아래로 돌았는가).</summary>
    private bool _velocityPairWasQuiet;

    /// <summary>두 프레임 사이 우리가 돌린 속도가 이보다 빠르면(px/ms) 그 쌍으로는 몹의 속도를 안 잰다.</summary>
    private const double OwnFlickPxPerMs = 0.9;

    /// <summary>우리가 이보다 느리게 돌던 쌍(px/ms)이어야 처음 잰 속도를 크게 받고, 붙일 때 속도를 미리 잰다.</summary>
    private const double OwnQuietPxPerMs = 0.3;

    /// <summary>꺾는 중인지 볼 때 가정해 보는 지연들(ms) - 실측 80~100 을 넉넉히 감싼다.</summary>
    private static readonly int[] VelocityLatenciesMs = [60, 85, 110, 135, 160];

    /// <summary>세 가정이 다 같은 쪽을 가리키면 그 가운데 가장 작은 것, 부호가 갈리면 0 - 지연을 어떻게 잡아도 설명 안 되는 만큼만 남긴다.</summary>
    private static double Cautious(double a, double b, double c)
    {
        if ((a > 0 && b > 0 && c > 0) || (a < 0 && b < 0 && c < 0))
            return Math.Sign(a) * Math.Min(Math.Abs(a), Math.Min(Math.Abs(b), Math.Abs(c)));

        return 0;
    }

    /// <summary>예측과의 차이를 잴 때 가정해 보는 지연의 양 끝(ms) - 실측 80~100 을 감싼다.</summary>
    private const int InnovationLatencyMinMs = 70;

    private const int InnovationLatencyMaxMs = 130;

    /// <summary>확인 화면 뒤 이만큼(ms)은 예측과의 차이를 조심해서 받는다 - 남긴 것을 푸는 움직임(약 80ms)이 지연 띠를 다 지나갈 때까지.</summary>
    private const int ReleaseCautionMs = 300;

    /// <summary>불확실함(px) → 이 화면을 받아들이는 비율(0~1). <see cref="UncertaintyHalfPx"/> 에서 절반.</summary>
    private static double Trust(double uncertaintyPx) => 1 / (1 + (uncertaintyPx / UncertaintyHalfPx));

    /// <summary>
    /// 예측과의 차이가 떨림 크기(<see cref="JitterPx"/> 아래)면 0.4 만, <see cref="RealMovePx"/> 넘으면 그대로, 그 사이는 고르게 - 검출 사각형은 가만히 있는 몹도 가로 11px·세로 17px 흔들린다(실측).
    /// 받아들이는 비율(가로 0.75)을 올리면서 같이 바꿨다 - 8px 에서 0.5 → 1 로 뚝 끊기면 떨림 10px 짜리를 통째로 받아, 붙은 직후 조준이 ±10px 을 오갔다(검사 궤적 2 → −2 → 7 → 11 → 9).
    /// 달리는 몹은 차이가 한쪽으로 이어지므로 속도가 받아 준다.
    /// </summary>
    private static double Calm(double innovationPx) => Calm(innovationPx, JitterPx, RealMovePx);

    /// <summary>세로는 띠를 넓게 쓴다 - 가만히 겨눈 프레임 쌍에서 본 자리가 가로 중간값 3px·90% 30px, 세로 6px·35px 흔들렸다(실측 2026-09-18, 971쌍).</summary>
    private static double Calm(double innovationPx, double jitterPx, double realMovePx)
        => 0.3 + (0.7 * Math.Clamp((Math.Abs(innovationPx) - jitterPx) / (realMovePx - jitterPx), 0, 1));

    /// <summary>한 박자 - 지금 예측한 남은 거리의 일정 비율을 보낸다.</summary>
    private void Step(long now)
    {
        int ix, iy;

        lock (_sync)
        {
            if (!_engaged || _track is not { } t) return;

            var (ex, ey) = Predict(t, now);
            var scale = Math.Max(0.01, _host.Scale());
            var distance = Math.Sqrt(Sq(ex) + Sq(ey));

            // 크게 꺾기 - 첫 움직임은 일부러 조금 못 미친 자리까지만 간다(ShortfallFraction). 남긴 것은 꺾은 결과가 담긴 화면이 오면 ConsumeFrame 이 푼다.
            if (_flickArmed && distance > FlickNearPx)
            {
                var fraction = ShortfallFraction * Math.Clamp((distance - HoldNearPx) / (HoldFarPx - HoldNearPx), 0, 1);

                _holdX = ex * fraction;
                _holdY = ey * fraction;
                _flicking = true;
                _flickArmed = false;
                _holdStartTicks = now;
                _primaryDoneTicks = 0;

                // 배율 표본의 "앞" - 마지막으로 본 날것 자리. 막 붙인 몹은 아직 날것이 없다 - 붙일 때 받은 자리가 곧 본 자리다.
                // 그 프레임에 반영됐는지 모르는 입력(지연 띠 40~200ms 안에 보낸 것)은 적어 뒀다가, 확인 화면에서 꺾은 양의 15% 를 넘으면 표본을 버린다(닻 방식과 같은 기준).
                // 띠 뒤(프레임 − 40ms 부터)에 보낸 것은 그 프레임에 없는 것이 분명하니 꺾은 양에 그대로 들어간다.
                var (unsureX, unsureY) = SentBetween(t.SeenTicks - LatencyMaxMs, t.SeenTicks - LatencyMinMs);

                _flickFromX = t.HasObservation ? t.ObsX : t.OffX;
                _flickFromY = t.HasObservation ? t.ObsY : t.OffY;
                _flickUnsureX = Math.Abs(unsureX);
                _flickUnsureY = Math.Abs(unsureY);
                _flickFromTicks = t.SeenTicks;

                Logger.Debug($"꺾기 시작: 거리({ex:0}, {ey:0}) 남김({_holdX:0}, {_holdY:0})");
            }
            else if (!_flicking && distance < RearmPx)
            {
                _flickArmed = true;
            }

            if (_flicking)
            {
                if (_primaryDoneTicks == 0 && Math.Sqrt(Sq(ex - _holdX) + Sq(ey - _holdY)) <= PrimaryDonePx) _primaryDoneTicks = now;

                if (now - _holdStartTicks > HoldTimeoutMs)
                {
                    _flicking = false;
                    _holdX = _holdY = 0;
                }
            }

            ex -= _holdX;
            ey -= _holdY;

            // 붙었으면 쉰다 - 문턱 둘로 들락거리지 않게.
            var offBy = Math.Sqrt(Sq(ex) + Sq(ey));

            if (_resting ? offBy > RestOutPx : offBy <= RestInPx) _resting = !_resting;

            if (_resting)
            {
                ex = ey = 0;
            }
            else
            {
                // 떨림 크기 안쪽 축은 안 움직인다.
                if (Math.Abs(ex) < DeadbandPx) ex = 0;
                if (Math.Abs(ey) < DeadbandPx) ey = 0;
            }

            // 이번 박자의 걸음(px) - 남은 거리에서 낼 수 있는 속도: 1차 접근(가까울수록 느긋하게) · 일정 감속으로 설 수 있는 속도 · 상한 가운데 가장 느린 것.
            var remaining = Math.Sqrt(Sq(ex) + Sq(ey));
            double wantX = 0, wantY = 0;

            if (remaining > 0)
            {
                var tau = TauNearMs + ((TauFarMs - TauNearMs) * Math.Clamp((remaining - NearPx) / (FarPx - NearPx), 0, 1));
                var speed = Math.Min(Math.Min(remaining * (1 - Math.Exp(-TickMs / tau)) / TickMs, Math.Sqrt(2 * DecelPxPerMs2 * remaining)), MaxSpeedPxPerMs);
                var stepPx = Math.Min(remaining, Math.Max(speed * TickMs, MinStepPx));

                wantX = ex / remaining * stepPx * scale;
                wantY = ey / remaining * stepPx * scale;
            }

            // 몹이 가는 만큼(속도 앞먹임) - 남은 거리만 쫓으면 달리는 몹을 늘 속도×시간 상수만큼 뒤에서 쫓는다.
            var follow = now - t.SeenTicks <= StaleVelocityMs ? TickMs * scale : 0;

            // 한 번 잰 속도는 반만 - 한 쌍이 튄 것일 수 있다. 두 번 맞으면 다 믿는다. 느린 속도는 떨림이라 0.
            follow *= VelocityConfidence(t);
            wantX += Dead(t.Vx) * follow;
            wantY += Dead(t.Vy) * follow;

            var magnitude = Math.Sqrt(Sq(wantX) + Sq(wantY));

            // 빨라질 때만 가속을 막는다. 느려지는 것은 바로 - 새 화면에서 목표가 가까이 나타났는데 관성으로 지나치면 안 된다.
            var ax = wantX - _stepX;
            var ay = wantY - _stepY;
            var accel = Math.Sqrt(Sq(ax) + Sq(ay));
            var maxAccel = MaxAccelPx * scale;

            if (accel > maxAccel && magnitude > Math.Sqrt(Sq(_stepX) + Sq(_stepY)))
            {
                wantX = _stepX + (ax * maxAccel / accel);
                wantY = _stepY + (ay * maxAccel / accel);
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

        // 화면에 찍힌 몹의 자리도 지연만큼 옛것이다 - 우리 입력만 늦게 보이는 것이 아니다. 그만큼 앞서 겨눈다(150px/s 옆걸음 봇이면 14px - 안 하면 늘 그만큼 뒤를 쏜다).
        var coast = (Math.Min(age, StaleVelocityMs) + (age <= StaleVelocityMs ? MobLeadMs : 0)) * VelocityConfidence(t);
        var (sx, sy) = SentBetween(t.SeenTicks - _latencyMs, long.MaxValue);
        var scale = Math.Max(0.01, _host.Scale());

        return (t.OffX + (Dead(t.Vx) * coast) - (sx / scale), t.OffY + (Dead(t.Vy) * coast) - (sy / scale));
    }

    /// <summary>느린 속도는 0 으로(<see cref="VelocityDeadPxPerMs"/>).</summary>
    private static double Dead(double velocity) => Math.Abs(velocity) < VelocityDeadPxPerMs ? 0 : velocity;

    /// <summary>속도를 얼마나 믿나 - 한 번 잰 것은 반, 두 번부터 다.</summary>
    private static double VelocityConfidence(Track t) => Math.Min(1, t.VelocitySamples / 2.0);

    /// <summary>그 프레임 시각의 예상 거리 - 프레임에 이미 반영된 입력(프레임 − 지연 앞)까지만 뺀다.</summary>
    private (double X, double Y) PredictAt(Track t, long frame) => PredictAt(t, frame, _latencyMs);

    /// <summary>지연을 <paramref name="latencyMs"/> 로 가정한 예상 거리. 섞은 자리(<see cref="Track.OffX"/>)는 기준 지연으로 만든 것이라 앞쪽 끝은 그대로 둔다.</summary>
    private (double X, double Y) PredictAt(Track t, long frame, int latencyMs)
    {
        var gap = Math.Clamp(frame - t.SeenTicks, 0, StaleVelocityMs) * VelocityConfidence(t);
        var (sx, sy) = SentBetween(t.SeenTicks - _latencyMs, frame - latencyMs);
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
