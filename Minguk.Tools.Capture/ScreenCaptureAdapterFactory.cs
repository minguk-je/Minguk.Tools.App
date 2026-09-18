namespace Minguk.Tools.Capture;

/// <summary>
/// 쓸 캡처 어댑터를 고른다.
///
/// 지금은 WGC 하나뿐이라 단순하지만, 고르는 판단을 한 군데로 모아 두면
/// 나중에 "WGC 를 못 쓰는 환경이면 다른 방식으로" 같은 규칙이 생겨도 여기만 고치면 된다.
/// 부르는 쪽은 어느 구현인지 알 필요가 없다.
/// </summary>
public static class ScreenCaptureAdapterFactory
{
    /// <param name="target">잡을 대상.</param>
    /// <param name="cpuReadback">픽셀을 CPU 로 내릴지. 프레임 저장이나 CPU 미리보기에 필요하다.</param>
    public static IScreenCaptureAdapter Create(CaptureTarget target, bool cpuReadback)
        => target.Kind == CaptureTargetKind.Video
            ? new VideoFileCaptureSession(target, cpuReadback)
            : new WgcCaptureSession(target, cpuReadback);
}
