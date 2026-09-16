namespace Minguk.Tools.Capture;

/// <summary>
/// 멈출 수 있는 캡처 - 영상 파일(<see cref="VideoFileCaptureSession"/>)만. 창·모니터(WGC)는 화면이 제 시간대로 흘러 멈출 수 없다.
/// </summary>
/// <remarks>
/// 화면의 일시정지는 받은 프레임을 흘리기만 하는데, 영상은 그러면 재생 시계가 계속 가서 계속했을 때 그만큼 건너뛰었다(사용자, 2026-09-17).
/// 공유 손잡이(<see cref="SharedCaptureHub"/>)는 늘 이 인터페이스를 달고 안쪽 세션이 멈출 수 있을 때만 true 를 준다.
/// </remarks>
public interface IPausableCapture
{
    /// <summary>멈추거나(true) 잇는다(false). 안쪽이 멈출 수 없으면 false - 그때 화면은 프레임을 흘리기만 한다.</summary>
    bool TrySetPaused(bool paused);
}
