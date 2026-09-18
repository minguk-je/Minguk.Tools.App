namespace Minguk.Tools.Capture;

/// <summary>
/// 캡처 세션 허브를 만든다. 앱은 <see cref="Default"/> 하나를 쓰고, 검증은 가짜 세션 공장을 꽂아 제 것을 만든다.
/// </summary>
public static class CaptureSessionHubFactory
{
    private static ICaptureSessionHub? _default;

    /// <summary>앱 전체가 함께 쓰는 허브. 화면들이 같은 창을 잡으면 세션 하나를 나눠 쓴다.</summary>
    public static ICaptureSessionHub Default => _default ??= Create(ScreenCaptureAdapterFactory.Create);

    public static ICaptureSessionHub Create(System.Func<CaptureTarget, bool, IScreenCaptureAdapter> create)
        => new SharedCaptureHub(create);
}
