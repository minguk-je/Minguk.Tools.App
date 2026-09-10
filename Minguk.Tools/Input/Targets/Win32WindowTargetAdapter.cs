using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Minguk.Tools.Input.Targets;

/// <summary>
/// <c>EnumWindows</c> · <c>WindowFromPoint</c> 로 창을 찾는다.
/// </summary>
/// <remarks>
/// 목록에서 빼는 것들
///   보이지 않는 창, 제목이 없는 창, 도구 창(<c>WS_EX_TOOLWINDOW</c>) - 사용자가 "저 창" 이라고
///   가리킬 수 있는 것만 남긴다. 안 그러면 목록이 수백 개가 되고 대부분 눌러도 소용이 없다.
///
/// 프로세스 이름을 함께 다는 이유는 제목이 같거나 빈 창이 흔하기 때문이다.
/// 이름을 못 읽는 경우(권한이 높은 프로세스)에는 조용히 "?" 로 둔다 - 목록에서 빼면
/// 사용자는 왜 자기 창이 안 보이는지 알 수 없다.
/// </remarks>
public sealed class Win32WindowTargetAdapter : IWindowTargetAdapter
{
    public string Name => "EnumWindows";

    public IReadOnlyList<WindowTarget> List(IntPtr exclude = default)
    {
        var found = new List<WindowTarget>();

        EnumWindows((handle, _) =>
        {
            if (handle == exclude) return true;
            if (!IsWindowVisible(handle)) return true;

            // 도구 창은 작업 표시줄에도 안 나오는 것들이다. 사용자가 가리킬 대상이 아니다.
            if ((GetWindowLong(handle, GwlExStyle) & WsExToolWindow) != 0) return true;

            var title = GetTitle(handle);
            if (title.Length == 0) return true;

            found.Add(new WindowTarget(handle, title, GetProcessName(handle)));
            return true;
        }, IntPtr.Zero);

        return found;
    }

    public WindowTarget? FromPoint(int x, int y)
    {
        var hit = WindowFromPoint(new Point { X = x, Y = y });

        if (hit == IntPtr.Zero) return null;

        // 자식 컨트롤이 잡히므로 최상위까지 올라간다. 메시지는 최상위로 보낸다.
        var root = GetAncestor(hit, GaRoot);
        if (root == IntPtr.Zero) return null;

        return new WindowTarget(root, GetTitle(root), GetProcessName(root));
    }

    public bool IsAlive(IntPtr handle) => handle != IntPtr.Zero && IsWindow(handle);

    public void Dispose()
    {
        // 들고 있는 자원이 없다. 인터페이스가 IDisposable 인 것은 다른 구현(UI Automation 처럼
        // 연결을 여는 것)을 위해서다.
    }

    private static string GetTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0) return string.Empty;

        var buffer = new StringBuilder(length + 1);
        GetWindowText(handle, buffer, buffer.Capacity);

        return buffer.ToString();
    }

    private static string GetProcessName(IntPtr handle)
    {
        try
        {
            GetWindowThreadProcessId(handle, out var pid);

            if (pid == 0) return "?";

            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            // 권한이 높은 프로세스는 못 읽는다. 그래도 창은 목록에 남긴다.
            return "?";
        }
    }

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    private static int GetWindowLong(IntPtr handle, int index) => (int)GetWindowLongPtr(handle, index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
}
