using System.Runtime.InteropServices;

namespace Minguk.Tools.Input;

/// <summary>
/// 지금 화면 어디에 커서가 있는지 묻는다.
/// </summary>
/// <remarks>
/// <b>왜 어댑터에 묻지 않는가</b>
///
/// <see cref="IInputAdapter.GetCursorPosition"/> 는 "이 경로가 진짜 커서를 다루는가" 를 말한다.
/// 창 메시지 경로는 커서를 움직이지 않으므로 null 을 돌려준다 — 그것이 맞다.
///
/// 하지만 좌표를 <b>집는</b> 일은 다르다. 사용자가 대상 위에 커서를 올려 두고 누르는 것이라,
/// 어느 경로로 보낼지와 상관없이 OS 에게 물으면 된다. 어댑터에 물었더니 창 메시지 경로에서
/// 좌표 담기가 아무 일도 안 하는 채로 조용히 지나갔다.
/// </remarks>
public static class ScreenCursor
{
    public static (int X, int Y)? TryGetPosition()
        => GetCursorPos(out var point) ? (point.X, point.Y) : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);
}
