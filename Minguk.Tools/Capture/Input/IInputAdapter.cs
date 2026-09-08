namespace Minguk.Tools.Capture.Input;

/// <summary>어느 마우스 버튼인지.</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle
}

/// <summary>
/// 입력을 실제로 만들어 내는 곳.
///
/// 지금 구현은 <see cref="SendInputAdapter"/> 하나다. 인터페이스로 갈라 둔 이유는
/// 나중에 다른 경로(예: 접근성 도구용 필터 드라이버, 하드웨어 HID 장치)를 붙일 때
/// 이 위의 좌표 계산과 화면 배선을 건드리지 않으려는 것이다.
///
/// 좌표 규약
///   화면 절대 좌표(픽셀)를 받는다. 정규화는 각 구현이 알아서 한다.
/// </summary>
public interface IInputAdapter
{
    /// <summary>사람이 읽을 이름. 어느 경로로 나가는지 화면에 보여 주려고 둔다.</summary>
    string Name { get; }

    void MoveMouseTo(int screenX, int screenY);

    void PressMouseButton(MouseButton button);

    void ReleaseMouseButton(MouseButton button);

    /// <summary>휠. 120 이 한 칸이다(WHEEL_DELTA).</summary>
    void ScrollWheel(int delta);

    void PressKey(ushort virtualKey);

    void ReleaseKey(ushort virtualKey);
}
