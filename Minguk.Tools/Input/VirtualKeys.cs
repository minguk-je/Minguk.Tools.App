using System.Runtime.InteropServices;

namespace Minguk.Tools.Input;

/// <summary>
/// 글자를 <b>가상 키</b>로 옮긴다. 스캔코드를 못 넣는 경로(PostMessage)가 쓰는 길이다.
/// </summary>
/// <remarks>
/// 스캔코드는 "키보드의 어느 자리" 이고 가상 키는 "어느 글쇠" 다. 둘은 다른 층이라
/// <see cref="ScanCodes"/> 와 따로 둔다.
///
/// <b>왜 이것이 필요한가</b>
///   PostMessage 는 스캔코드를 못 넣지만 <c>PressKey</c>/<c>ReleaseKey</c> 는 할 수 있다.
///   그래서 영문·숫자·문장부호·Enter 는 이 길로 들어간다. 한때 스캔코드가 안 되면 통째로
///   버렸는데, 보낼 수 있는 것을 버리고 있었던 것이다.
///
/// <b>못 하는 것</b>
///   한글. 자모를 눌러 넣는 것은 대상 창의 IME 가 처리해야 하는데, 부친 키 메시지로는
///   한/영 전환이 먹지 않는다. 한글은 스캔코드 경로에서만 된다.
/// </remarks>
public static class VirtualKeys
{
    public const ushort Enter = 0x0D;
    public const ushort Shift = 0x10;
    public const ushort Control = 0x11;
    public const ushort Alt = 0x12;

    /// <summary>
    /// 이 글자를 지금 자판에서 어느 가상 키로 넣는지.
    /// </summary>
    /// <remarks>
    /// <c>VkKeyScan</c> 은 현재 입력 언어 기준으로 답한다. 낮은 바이트가 가상 키,
    /// 높은 바이트가 함께 눌러야 할 조합키다. -1 이면 지금 자판으로는 넣을 수 없다는 뜻이다.
    ///
    /// Ctrl·Alt 가 필요한 글자(유럽어 자판의 일부)는 다루지 않는다. 이 앱이 보내는 것은
    /// 영문·숫자·문장부호라 Shift 만으로 충분하고, 조합키를 늘리면 대상마다 다르게 먹는다.
    /// </remarks>
    public static bool TryGetKeyStroke(char c, out ushort virtualKey, out bool needsShift)
    {
        virtualKey = 0;
        needsShift = false;

        var scan = VkKeyScanW(c);

        if (scan == -1) return false;

        var modifiers = (scan >> 8) & 0xFF;

        // Ctrl(2)·Alt(4) 가 필요하면 다루지 않는다.
        if ((modifiers & 0x06) != 0) return false;

        virtualKey = (ushort)(scan & 0xFF);
        needsShift = (modifiers & 0x01) != 0;

        return true;
    }

    /// <summary>가상 키로 넣을 수 있는 글자인지.</summary>
    public static bool CanType(char c) => TryGetKeyStroke(c, out _, out _);

    /// <summary>
    /// E0 확장 플래그가 필요한 가상 키인지. MapVirtualKey 는 확장 여부를 알려 주지 않으므로 여기서 가려낸다.
    /// 빠뜨리면 방향키가 넘패드 키로 들어간다.
    /// </summary>
    public static bool IsExtendedKey(ushort virtualKey) => virtualKey switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true,   // PageUp PageDown End Home
        0x25 or 0x26 or 0x27 or 0x28 => true,   // Left Up Right Down
        0x2D or 0x2E => true,                   // Insert Delete
        0x5B or 0x5C or 0x5D => true,           // LWin RWin Apps
        0xA3 => true,                           // RControl
        0xA5 => true,                           // RMenu (오른쪽 Alt)
        _ => false
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanW(char ch);
}
