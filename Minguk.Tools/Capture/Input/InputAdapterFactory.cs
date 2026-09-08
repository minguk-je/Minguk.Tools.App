namespace Minguk.Tools.Capture.Input;

/// <summary>
/// 쓸 입력 백엔드를 고른다.
///
/// 지금은 고를 것이 하나뿐이라 단순하지만, 백엔드가 늘면 이 자리에서만 판단하면 되도록
/// 만드는 곳을 한 군데로 모아 둔다. 부르는 쪽은 어느 구현인지 알 필요가 없다.
/// </summary>
public static class InputAdapterFactory
{
    public static IInputAdapter Create() => new SendInputAdapter();
}
