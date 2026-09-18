using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

using Minguk.Tools.Capture;
using Minguk.Tools.Vision.Inference;

namespace Minguk.Tools.Vision.Perception;

/// <summary>한 번 찾은 결과. 검출과 (있으면) 이름표, 그때의 프레임 크기와 대상, 시각.</summary>
public sealed record DetectionSnapshot(
    IReadOnlyList<Detection> Found,
    IReadOnlyList<string> Names,
    int FrameWidth,
    int FrameHeight,
    CaptureTarget? Target,
    long Ticks)
{
    /// <summary>
    /// 이 검출이 본 프레임이 들어온 시각(TickCount64). 0 이면 모름.
    /// </summary>
    /// <remarks>
    /// 올린 시각(<see cref="Ticks"/>)과 다르다 - 검출이 수백 ms 걸리므로, 올린 뒤라도 프레임은 그 전의 화면이다.
    /// 조준이 "내가 겨눈 뒤의 화면인가" 를 가릴 때 이것을 본다.
    /// </remarks>
    public long FrameTicks { get; init; }

    /// <summary>
    /// 이 프레임에서 검출기가 찾은 그대로 - 화면용 추적(<see cref="DetectionTracker"/>)을 안 거친 것. 안 실었으면 null(<see cref="Found"/> 가 곧 날것).
    /// </summary>
    /// <remarks>
    /// <b>조준 스레드는 이것을 본다</b>(사용자, 2026-09-18 "움직임이 너무 허술해"). <see cref="Found"/> 는 추적기가 직전 자리와 6:4 로 섞고, 못 이으면 옛 사각형을 두 번 더
    /// 그대로 내준다 - 화면이 가만히 있을 때는 떨림을 줄여 주지만, <b>조준이 화면을 돌리는 중</b>에는 사각형이 옛 화면 좌표에 끌려 늦는다. 휙 돌면(제 크기의 1.5배 넘게)
    /// 아예 못 이어 0.3초 동안 옛 자리를 주고, 새 자리는 두 번 보일 때까지 안 나온다. 실측 로그: 344 카운트를 보냈는데 본 자리가 -60 → -60 → -60 → +48 → +75,
    /// 조준은 "검출이 그만큼 달아났다" 로 읽고 더 밀어 -159px 에서 +75px 로 지나친 뒤 1초에 걸쳐 돌아왔다. 조준 스레드는 제가 돌린 만큼을 알고 스스로 잇고 섞는다.
    /// </remarks>
    public IReadOnlyList<Detection>? Raw { get; init; }

    /// <summary>조준이 볼 것 - 날것이 있으면 날것.</summary>
    public IReadOnlyList<Detection> ForAiming => Raw ?? Found;

    /// <summary>
    /// 바로 앞 검출(한 장만 - 그 앞은 끊어 둔다). 없으면 null.
    /// </summary>
    /// <remarks>
    /// 조준 스레드가 검출을 <b>붙이는 순간에 그 검출의 속도</b>를 알려고 본다(실측 2026-09-18): 사격장 봇은 늘 옆으로 걷는데(150px/s), 붙인 뒤에야 속도를 재기 시작하면 휙 꺾는 동안은
    /// 못 재서(우리가 돌리는 중의 화면은 못 믿는다) 0.3초쯤 선 봇인 줄 알고 겨눈다 - 꺾기가 늘 봇 뒤 20~35px 에 떨어졌다. 붙이기 전의 두 장은 우리가 가만히 있을 때라 깨끗하다.
    /// </remarks>
    public DetectionSnapshot? Previous { get; init; }
}

/// <summary>
/// 인식 허브. 화면이 찾은 것(검출·이름표·프레임)을 올려 두고 스크립트가 읽어 간다.
/// </summary>
/// <remarks>
/// <b>왜 있나</b> - 검출은 화면(ViewModel) 안에서 돌고 결과도 거기 있었다. 스크립트가 "검출이 보이면 클릭" 을
/// 하려면 그 결과를 읽을 통로가 필요하다. 화면이 스크립트에 직접 넘기면 화면과 스크립트가 서로를 알아야
/// 하고, 검증 하네스가 화면 없이 스크립트를 돌릴 수 없다. 허브 하나를 사이에 두면 화면은 올리기만,
/// 스크립트는 읽기만 한다.
///
/// 앱에 하나(<see cref="PerceptionHubFactory.Default"/>). 스크립트·플레이가 같이 켜져 있으면 나중에 올린 쪽이 보인다.
/// 읽는 쪽은 스레드가 다르다(스크립트 스레드). 올리는 것은 불변 스냅샷으로, 프레임은 잠금 아래 복사본으로.
/// </remarks>
public interface IPerceptionHub
{
    /// <summary>창을 잡고 있는가.</summary>
    bool IsCapturing { get; }

    /// <summary>검출이 켜져 있는가.</summary>
    bool IsDetecting { get; }

    /// <summary>잡고 있는 대상. 비율을 화면 픽셀로 바꿀 때 쓴다.</summary>
    CaptureTarget? Target { get; }

    /// <summary>마지막으로 찾은 것. 아직 없으면 null.</summary>
    DetectionSnapshot? Latest { get; }

    /// <summary>
    /// 스크립트가 프레임 픽셀을 원하는가(글자 읽기). 켜져 있을 때만 화면이 프레임을 복사해 올린다 -
    /// 8MB 를 0.25초마다 옮기는 일이라 늘 하지 않는다.
    /// </summary>
    bool WantsFrames { get; set; }

    /// <summary>
    /// 리드백을 막 켜서 프레임이 곧 올 예정인가. 켜면 캡처가 다시 시작되느라 1~2초 걸린다 -
    /// 그동안 스크립트의 읽기가 "프레임이 들어오지 않습니다" 로 끝나지 않게 더 기다리게 한다.
    /// </summary>
    bool IsPreparingFrames { get; }

    /// <summary>리드백을 켜는 중이라고 알린다. 몇 초 동안만 <see cref="IsPreparingFrames"/> 가 참이다.</summary>
    void PreparingFrames();

    void PublishState(bool capturing, bool detecting, CaptureTarget? target);

    /// <param name="frameTicks">검출이 본 프레임이 들어온 시각(TickCount64). 모르면 0.</param>
    /// <param name="raw">추적을 안 거친 이 프레임의 검출(<see cref="DetectionSnapshot.Raw"/>). <paramref name="found"/> 가 이미 날것이면 null.</param>
    void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0, IReadOnlyList<Detection>? raw = null);

    /// <summary>프레임 한 벌을 복사해 둔다. Bgra32, 줄 간격 = 너비*4.</summary>
    void PublishFrame(byte[] bgra, int width, int height);

    /// <summary>마지막 프레임의 한 부분(0~1 비율)을 그림으로 잘라 준다. 프레임이 없으면 false.</summary>
    bool TryCropFrame(Rect ratio, out BitmapSource? crop);

    /// <summary>마지막 프레임의 크기(픽셀). 돌린 칸을 감쌀 상자를 구할 때 쓴다(<c>RegionTargets.Bounds</c>). 프레임이 없으면 false.</summary>
    bool TryGetFrameSize(out int width, out int height);
}
