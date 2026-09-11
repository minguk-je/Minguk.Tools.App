namespace Minguk.Tools.Vision.Perception;

/// <summary>
/// 인식 허브를 만든다. 앱은 <see cref="Default"/> 하나를 쓰고, 검증은 <see cref="Create"/> 로 제 것을 만든다.
/// </summary>
public static class PerceptionHubFactory
{
    private static IPerceptionHub? _default;

    /// <summary>앱 전체가 함께 보는 허브. 화면이 올리고 스크립트가 읽는다.</summary>
    public static IPerceptionHub Default => _default ??= Create();

    public static IPerceptionHub Create() => new PerceptionHub();
}
