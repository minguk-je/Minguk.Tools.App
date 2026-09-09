using System;
using System.Runtime.InteropServices;

namespace Minguk.Tools.Input.Adapters;

/// <summary>
/// 대상 창의 메시지 큐에 입력 메시지를 직접 넣는다.
///
/// <see cref="SendInputAdapter"/> 와 결정적으로 다른 점
///   커널 입력 큐를 거치지 않는다. 그래서 진짜 커서가 움직이지 않고, 대상 창을
///   앞으로 가져올 필요도 없다. 이 앱에 포커스를 둔 채로 대상만 조작할 수 있다.
///   자체 개발 중인 앱·키오스크를 자동화할 때 쓰라고 만든 경로다.
///
/// 안 통하는 곳
///   창 메시지를 안 보고 입력을 직접 읽는 프로그램에는 아무 일도 일어나지 않는다.
///   Raw Input(WM_INPUT), DirectInput, XInput, GetAsyncKeyState 가 여기 해당한다.
///   그런 대상에는 <see cref="SendInputAdapter"/> 를 써야 한다.
///
/// 어디로 보내는가
///   마우스는 그 좌표 아래에 있는 자식 컨트롤까지 찾아 들어가서 보낸다.
///   WinForms·Win32 는 컨트롤마다 HWND 가 따로라 최상위 창에만 보내면 안 먹는다.
///   키보드는 대상 스레드에서 지금 포커스를 가진 HWND 로 보낸다
///   (GetGUIThreadInfo 로 읽는다 — AttachThreadInput 없이 알 수 있다).
///   WPF 는 창 하나가 전부라 어느 쪽이든 같은 곳으로 간다.
/// </summary>
public sealed class PostMessageInputAdapter : IInputAdapter
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Func<IntPtr> _targetWindowProvider;

    /// <summary>마지막으로 옮긴 화면 좌표. 버튼 메시지에 실을 자리를 여기서 가져온다.</summary>
    private (int X, int Y) _lastScreenPoint;

    /// <param name="targetWindowProvider">메시지를 받을 최상위 창. 대상이 바뀌면 다음 호출부터 반영된다.</param>
    public PostMessageInputAdapter(Func<IntPtr> targetWindowProvider)
        => _targetWindowProvider = targetWindowProvider;

    public string Name => "PostMessage";

    /// <summary>
    /// 창 메시지는 OS 기본 기능이라 준비할 것이 없다.
    /// 대상 창이 없는 상황은 이 값이 아니라 각 호출의 false 로 알린다 — 대상은 매번 바뀔 수 있다.
    /// </summary>
    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    /// <summary>대상을 앞으로 가져올 필요가 없다. 이 경로의 존재 이유가 그것이다.</summary>
    public bool RequiresForegroundTarget => false;

    /// <summary>진짜 커서를 안 건드리므로 되돌릴 것도 없다.</summary>
    public (int X, int Y)? GetCursorPosition() => null;

    public bool MoveMouseTo(int screenX, int screenY)
    {
        _lastScreenPoint = (screenX, screenY);

        return PostMouse(NativeMethods.WM_MOUSEMOVE, wParam: 0);
    }

    public bool PressMouseButton(MouseButton button)
        => PostMouse(DownMessage(button), ButtonFlag(button));

    public bool ReleaseMouseButton(MouseButton button)
        => PostMouse(UpMessage(button), wParam: 0);

    public bool ClickMouseButton(MouseButton button)
        => PressMouseButton(button) && ReleaseMouseButton(button);

    /// <summary>
    /// 휠. WM_MOUSEWHEEL 의 lParam 만은 클라이언트가 아니라 화면 좌표다(Win32 규약).
    /// </summary>
    public bool ScrollWheel(int delta)
    {
        var window = ResolveMouseTarget(out _);
        if (window == IntPtr.Zero)
            return false;

        return Post(
            window,
            NativeMethods.WM_MOUSEWHEEL,
            MakeParam(0, delta),
            MakeParam(_lastScreenPoint.X, _lastScreenPoint.Y));
    }

    public bool PressKey(ushort virtualKey) => PostKey(virtualKey, isKeyUp: false);

    public bool ReleaseKey(ushort virtualKey) => PostKey(virtualKey, isKeyUp: true);

    // ── 마우스 ────────────────────────────────────────────────────────────

    private bool PostMouse(uint message, int wParam)
    {
        var window = ResolveMouseTarget(out var clientPoint);
        if (window == IntPtr.Zero)
            return false;

        // 좌표가 어긋날 때 어디서 틀어졌는지 보려면 이 세 가지가 필요하다.
        // 화면 좌표는 맞는데 창 좌표가 이상하면 변환이,
        // 창 자체가 다르면 자식 컨트롤 탐색이 문제다.
        Logger.Debug($"PostMessage 0x{message:X} → 창 0x{window.ToInt64():X}"
                     + $" (최상위 0x{ResolveRootWindow().ToInt64():X})"
                     + $", 화면 {_lastScreenPoint.X},{_lastScreenPoint.Y}"
                     + $" → 창 기준 {clientPoint.X},{clientPoint.Y}");

        return Post(window, message, wParam, MakeParam(clientPoint.X, clientPoint.Y));
    }

    /// <summary>
    /// 마지막 좌표 아래에 있는 창을 찾고, 그 창 기준 좌표로 바꿔 돌려준다.
    ///
    /// 최상위 창에서 시작해 자식으로 계속 파고든다. 더 내려갈 곳이 없으면 멈춘다.
    /// 보이지 않거나 투명한 컨트롤은 건너뛴다 — 그런 것에 보내면 아무 일도 안 일어난다.
    /// </summary>
    private IntPtr ResolveMouseTarget(out (int X, int Y) clientPoint)
    {
        clientPoint = default;

        var root = ResolveRootWindow();
        if (root == IntPtr.Zero)
            return IntPtr.Zero;

        var window = root;

        // 깊이 제한을 두는 이유는 컨트롤이 자기 자신을 돌려주는 경우를 막기 위해서다.
        for (var depth = 0; depth < 16; depth++)
        {
            var point = new NativeMethods.ScreenPoint { X = _lastScreenPoint.X, Y = _lastScreenPoint.Y };

            if (!NativeMethods.ScreenToClient(window, ref point))
                return IntPtr.Zero;

            var child = NativeMethods.ChildWindowFromPointEx(
                window,
                point,
                NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPTRANSPARENT);

            if (child == IntPtr.Zero || child == window)
            {
                clientPoint = (point.X, point.Y);
                return window;
            }

            window = child;
        }

        // 여기까지 왔으면 자식을 16겹 넘게 파고든 것이다. 정상적인 화면에서는 안 나온다.
        // 좌표를 안 채운 채로 내보내면 (0,0) 을 누른 것이 되므로 실패로 처리한다.
        Logger.Warn("자식 컨트롤이 너무 깊다. 좌표를 확정하지 못했다.");
        return IntPtr.Zero;
    }

    private static uint DownMessage(MouseButton button) => button switch
    {
        MouseButton.Right => NativeMethods.WM_RBUTTONDOWN,
        MouseButton.Middle => NativeMethods.WM_MBUTTONDOWN,
        _ => NativeMethods.WM_LBUTTONDOWN
    };

    private static uint UpMessage(MouseButton button) => button switch
    {
        MouseButton.Right => NativeMethods.WM_RBUTTONUP,
        MouseButton.Middle => NativeMethods.WM_MBUTTONUP,
        _ => NativeMethods.WM_LBUTTONUP
    };

    /// <summary>버튼 누름 메시지의 wParam 에 실리는 "지금 눌려 있는 버튼" 표시.</summary>
    private static int ButtonFlag(MouseButton button) => button switch
    {
        MouseButton.Right => NativeMethods.MK_RBUTTON,
        MouseButton.Middle => NativeMethods.MK_MBUTTON,
        _ => NativeMethods.MK_LBUTTON
    };

    // ── 키보드 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 키 하나를 보낸다.
    ///
    /// lParam 을 제대로 채워야 하는 대상이 있다. 반복 횟수·스캔 코드·전환 상태가
    /// 거기 들어간다. 0 으로 두면 무시하는 컨트롤이 있어서 실제 키보드와 같게 맞춘다.
    /// </summary>
    private bool PostKey(ushort virtualKey, bool isKeyUp)
    {
        var window = ResolveKeyboardTarget();
        if (window == IntPtr.Zero)
            return false;

        var scanCode = NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MAPVK_VK_TO_VSC);

        // 0~15 반복 횟수(1), 16~23 스캔 코드, 30 이전 상태, 31 전환 상태.
        var lParam = 1 | (int)(scanCode << 16);
        if (isKeyUp)
            lParam |= unchecked((int)0xC0000000);

        return Post(window, isKeyUp ? NativeMethods.WM_KEYUP : NativeMethods.WM_KEYDOWN, virtualKey, lParam);
    }

    /// <summary>
    /// 메시지를 넣을 최상위 창.
    ///
    /// 대상이 창이면 그 핸들을 그대로 쓴다.
    /// 대상이 모니터면 핸들이 모니터라 보낼 곳이 없으므로, 마지막 좌표 아래에 있는
    /// 창을 찾아 쓴다. 그렇게 하지 않으면 모니터를 보고 있을 때 아무것도 안 나간다.
    /// </summary>
    private IntPtr ResolveRootWindow()
    {
        var target = _targetWindowProvider();
        if (target != IntPtr.Zero)
            return target;

        var hit = NativeMethods.WindowFromPoint(new NativeMethods.ScreenPoint
        {
            X = _lastScreenPoint.X,
            Y = _lastScreenPoint.Y
        });

        return hit == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
    }

    /// <summary>
    /// 키를 받을 창. 대상 스레드에서 지금 포커스를 가진 HWND 를 쓴다.
    /// 못 알아내면 최상위 창으로 보낸다 — WPF 는 그게 곧 같은 곳이다.
    /// </summary>
    private IntPtr ResolveKeyboardTarget()
    {
        var root = ResolveRootWindow();
        if (root == IntPtr.Zero)
            return IntPtr.Zero;

        var threadId = NativeMethods.GetWindowThreadProcessId(root, IntPtr.Zero);
        if (threadId == 0)
            return root;

        var info = new NativeMethods.GuiThreadInfo { Size = Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };

        if (NativeMethods.GetGUIThreadInfo(threadId, ref info) && info.FocusWindow != IntPtr.Zero)
            return info.FocusWindow;

        return root;
    }

    // ── 공통 ──────────────────────────────────────────────────────────────

    private static int MakeParam(int low, int high) => (low & 0xFFFF) | (high << 16);

    private static bool Post(IntPtr window, uint message, int wParam, int lParam)
    {
        if (NativeMethods.PostMessage(window, message, (IntPtr)wParam, (IntPtr)lParam))
            return true;

        Logger.Warn($"PostMessage 실패. 메시지 0x{message:X}, 오류 코드 {Marshal.GetLastWin32Error()}");
        return false;
    }

    private static class NativeMethods
    {
        public const uint WM_MOUSEMOVE = 0x0200;
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_RBUTTONDOWN = 0x0204;
        public const uint WM_RBUTTONUP = 0x0205;
        public const uint WM_MBUTTONDOWN = 0x0207;
        public const uint WM_MBUTTONUP = 0x0208;
        public const uint WM_MOUSEWHEEL = 0x020A;

        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;

        public const int MK_LBUTTON = 0x0001;
        public const int MK_RBUTTON = 0x0002;
        public const int MK_MBUTTON = 0x0010;

        public const uint CWP_SKIPINVISIBLE = 0x0001;
        public const uint CWP_SKIPTRANSPARENT = 0x0004;

        public const uint MAPVK_VK_TO_VSC = 0;

        public const uint GA_ROOT = 2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr window, ref ScreenPoint point);

        [DllImport("user32.dll")]
        public static extern IntPtr ChildWindowFromPointEx(IntPtr parent, ScreenPoint point, uint flags);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

        [DllImport("user32.dll")]
        public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(ScreenPoint point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct ScreenPoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GuiThreadInfo
        {
            public int Size;
            public uint Flags;
            public IntPtr ActiveWindow;
            public IntPtr FocusWindow;
            public IntPtr CaptureWindow;
            public IntPtr MenuOwnerWindow;
            public IntPtr MoveSizeWindow;
            public IntPtr CaretWindow;
            public int CaretLeft;
            public int CaretTop;
            public int CaretRight;
            public int CaretBottom;
        }
    }
}
