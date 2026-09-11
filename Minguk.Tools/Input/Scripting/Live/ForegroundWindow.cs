using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>지금 앞에 있는 창. 입력을 보내기 전에 대상이 앞에 있는지 볼 때 쓴다.</summary>
internal static class ForegroundWindow
{
    public static IntPtr Handle => GetForegroundWindow();

    /// <summary>
    /// 그 창이 앞에 있다고 볼 수 있는가 - 같은 창이거나, 앞 창의 최상위 소유자거나, 같은 프로세스의 창.
    /// </summary>
    /// <remarks>
    /// 게임은 잡은 창 말고 다른 창(IME·오버레이·자식)을 앞에 두기도 한다. 핸들만 비교하면 게임이 분명히
    /// 앞에 있는데도 "앞에 없다" 가 된다. 같은 프로세스면 입력이 그 게임으로 가므로 통과시킨다.
    /// </remarks>
    public static bool IsInFront(IntPtr target)
    {
        var front = GetForegroundWindow();

        if (front == IntPtr.Zero || target == IntPtr.Zero) return false;
        if (front == target) return true;
        if (GetAncestor(front, GA_ROOTOWNER) == target) return true;

        GetWindowThreadProcessId(front, out var frontProcess);
        GetWindowThreadProcessId(target, out var targetProcess);

        return frontProcess != 0 && frontProcess == targetProcess;
    }

    /// <summary>앞 창을 사람이 알아볼 말로 - "크롬 - 제목". 왜 막혔는지 보여 줄 때.</summary>
    public static string Describe()
    {
        var front = GetForegroundWindow();
        if (front == IntPtr.Zero) return "(없음)";

        var title = new StringBuilder(256);
        GetWindowText(front, title, title.Capacity);

        GetWindowThreadProcessId(front, out var processId);
        string process;

        try { process = Process.GetProcessById((int)processId).ProcessName; }
        catch { process = "?"; }

        return title.Length > 0 ? $"{process} - {title}" : process;
    }

    private const uint GA_ROOTOWNER = 3;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
}
