using System;

namespace Minguk.Tools.Input;

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
/// 지금 구현은 <see cref="Adapters.SendInputAdapter"/> 하나다. 인터페이스로 갈라 둔 이유는
/// 나중에 다른 경로(예: 접근성 도구용 필터 드라이버, 하드웨어 HID 장치)를 붙일 때
/// 이 위의 좌표 계산과 화면 배선을 건드리지 않으려는 것이다.
///
/// 좌표 규약
///   화면 절대 좌표(픽셀)를 받는다. 정규화는 각 구현이 알아서 한다.
///
/// 반환값 규약
///   실제로 입력이 들어갔으면 true. false 는 OS 가 거부했다는 뜻이다 —
///   대개 대상이 관리자 권한으로 떠 있어서(UIPI) 막힌 경우다.
///   부르는 쪽이 사용자에게 이유를 알려 줄 수 있어야 해서 void 로 두지 않았다.
/// </summary>
public interface IInputAdapter : IDisposable
{
    /// <summary>사람이 읽을 이름. 어느 경로로 나가는지 화면에 보여 주려고 둔다.</summary>
    string Name { get; }

    /// <summary>
    /// 지금 이 경로로 입력을 보낼 수 있는지.
    ///
    /// 대부분의 경로는 늘 true 다 — OS 가 항상 주는 API 를 쓰기 때문이다.
    /// 드라이버가 있어야 도는 경로(Interception)는 설치·재부팅 전에는 false 가 된다.
    /// 고른 경로가 안 되면 다른 경로로 내려앉아야 하므로, 만들어 보기 전에는 알 수 없다.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 쓸 수 없는 이유. 쓸 수 있으면 null.
    /// 조용히 다른 경로로 바꾸면 안 되므로 사용자에게 보여 줄 문장이 필요하다.
    /// </summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// 입력이 들어가려면 대상이 앞에 나와 있어야 하는지.
    ///
    /// 커널 입력 큐를 쓰는 경로(SendInput)는 true 다 — 입력이 포커스를 가진 창으로 가므로
    /// 보내기 전에 대상을 끌어올려야 한다. 창 메시지를 직접 넣는 경로(PostMessage)는 false 고,
    /// 그래서 이 앱에 포커스를 둔 채로 대상을 조작할 수 있다.
    /// </summary>
    bool RequiresForegroundTarget { get; }

    /// <summary>
    /// 지금 커서가 있는 화면 좌표. 못 읽으면 null.
    /// 입력을 넘긴 뒤 커서를 제자리로 돌려놓으려고 둔다.
    /// </summary>
    (int X, int Y)? GetCursorPosition();

    bool MoveMouseTo(int screenX, int screenY);

    /// <summary>
    /// 지금 자리에서 이만큼 움직인다(상대 이동).
    /// </summary>
    /// <remarks>
    /// 게임처럼 커서를 붙잡는 창은 절대 좌표 이동을 무시하고 움직인 양만 본다(Raw Input 의 상대 이동).
    /// 그런 창에서 조준하려면 이것이어야 한다. 보통 창에서는 <see cref="MoveMouseTo"/> 가 낫다.
    /// </remarks>
    bool MoveMouseBy(int deltaX, int deltaY);

    bool PressMouseButton(MouseButton button);

    bool ReleaseMouseButton(MouseButton button);

    /// <summary>
    /// 누르고 떼기를 한 번에.
    ///
    /// 미리보기에서는 이걸 쓴다. 누름과 뗌을 따로 보내면 뗌이 영영 안 온다 —
    /// 누르는 순간 진짜 커서가 대상 창 위로 옮겨 가서, 사용자가 버튼을 떼는 것을
    /// 이 앱이 못 보기 때문이다. 그러면 대상 창에서는 버튼이 눌린 채로 남는다.
    /// </summary>
    bool ClickMouseButton(MouseButton button);

    /// <summary>휠. 120 이 한 칸이다(WHEEL_DELTA).</summary>
    bool ScrollWheel(int delta);

    bool PressKey(ushort virtualKey);

    bool ReleaseKey(ushort virtualKey);

    // IDisposable 을 무는 이유
    //   드라이버 컨텍스트처럼 놓아 주어야 하는 자원을 든 구현이 있다(Interception).
    //   경로를 갈아끼울 때 이전 것을 버리지 않으면 그대로 샌다.
    //   자원이 없는 구현은 빈 Dispose 를 둔다.
}
