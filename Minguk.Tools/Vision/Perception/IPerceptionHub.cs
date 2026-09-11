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
}

/// <summary>
/// 인식 허브. 화면이 찾은 것(검출·이름표·프레임)을 올려 두고 스크립트가 읽어 간다.
/// </summary>
/// <remarks>
/// <b>왜 있나</b> - 검출은 화면(ViewModel) 안에서 돌고 결과도 거기 있었다. 스크립트가 "몹이 보이면 클릭" 을
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

    /// <summary>몹 찾기가 켜져 있는가.</summary>
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

    void PublishState(bool capturing, bool detecting, CaptureTarget? target);

    /// <param name="frameTicks">검출이 본 프레임이 들어온 시각(TickCount64). 모르면 0.</param>
    void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0);

    /// <summary>프레임 한 벌을 복사해 둔다. Bgra32, 줄 간격 = 너비*4.</summary>
    void PublishFrame(byte[] bgra, int width, int height);

    /// <summary>마지막 프레임의 한 부분(0~1 비율)을 그림으로 잘라 준다. 프레임이 없으면 false.</summary>
    bool TryCropFrame(Rect ratio, out BitmapSource? crop);
}
