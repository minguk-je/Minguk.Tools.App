using System.Runtime.InteropServices;
using System.Text;

namespace Minguk.Base.Utilities;

public class WinApi32
{
    public const uint MAX_PATH = 260;

    #region Hooks - user32.dll

    /// <summary>
    /// Defines the layout of the MouseHookStruct
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public Point Point;
        public int MouseData;
        public int Flags;
        public int Time;
        public int ExtraInfo;
    }

    /// <summary>
    ///  EventArgs class for use as parameters by HookEventHandler delegates
    /// </summary>
    public class HookEventArgs : EventArgs
    {
        private int _code;
        private IntPtr _wParam;
        private IntPtr _lParam;

        /// <summary>
        /// Initializes a new instance of the HookEventArgs class
        /// </summary>
        /// <param name="code">the hook code</param>
        /// <param name="wParam">hook specific information</param>
        /// <param name="lParam">hook specific information</param>
        public HookEventArgs(int code, IntPtr wParam, IntPtr lParam)
        {
            _code = code;
            _wParam = wParam;
            _lParam = lParam;
        }

        /// <summary>
        /// The hook code
        /// </summary>
        public int Code
        {
            get
            {
                return _code;
            }
        }

        /// <summary>
        /// A pointer to data
        /// </summary>
        public IntPtr wParam
        {
            get
            {
                return _wParam;
            }
        }

        /// <summary>
        /// A pointer to data
        /// </summary>
        public IntPtr lParam
        {
            get
            {
                return _lParam;
            }
        }
    }

    /// <summary>
    /// Event delegate for use with the HookEventArgs class
    /// </summary>
    public delegate void HookEventHandler(object sender, HookEventArgs e);

    /// <summary>
    /// Defines the various types of hooks that are available in Windows
    /// </summary>
    public enum HookTypes : int
    {
        WH_JOURNALRECORD = 0,
        WH_JOURNALPLAYBACK = 1,
        WH_KEYBOARD = 2,
        WH_GETMESSAGE = 3,
        WH_CALLWNDPROC = 4,
        WH_CBT = 5,
        WH_SYSMSGFILTER = 6,
        WH_MOUSE = 7,
        WH_HARDWARE = 8,
        WH_DEBUG = 9,
        WH_SHELL = 10,
        WH_FOREGROUNDIDLE = 11,
        WH_CALLWNDPROCRET = 12,
        WH_KEYBOARD_LL = 13,
        WH_MOUSE_LL = 14
    }

    public enum ShellHookMessages
    {
        HSHELL_WINDOWCREATED = 1,
        HSHELL_WINDOWDESTROYED = 2,
        HSHELL_ACTIVATESHELLWINDOW = 3,
        HSHELL_WINDOWACTIVATED = 4,
        HSHELL_GETMINRECT = 5,
        HSHELL_REDRAW = 6,
        HSHELL_TASKMAN = 7,
        HSHELL_LANGUAGE = 8,
        HSHELL_ACCESSIBILITYSTATE = 11
    }

    public delegate int HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowsHookEx(HookTypes hookType, HookProc hookProc, IntPtr hInstance, int nThreadId);

    [DllImport("user32.dll")]
    public static extern int UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    public static extern int CallNextHookEx(IntPtr hookHandle, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int RegisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int DeregisterShellHookWindow(IntPtr hWnd);

    #endregion Hooks

    #region System - Kernel32.dll

    [DllImport("kernel32.dll", EntryPoint = "GetLastError", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern Int32 GetLastError();

    public enum BinaryType : uint
    {
        SCS_32BIT_BINARY = 0, // A 32-bit Windows-based application
        SCS_64BIT_BINARY = 6, // A 64-bit Windows-based application.
        SCS_DOS_BINARY = 1, // An MS-DOS – based application
        SCS_OS216_BINARY = 5, // A 16-bit OS/2-based application
        SCS_PIF_BINARY = 3, // A PIF file that executes an MS-DOS – based application
        SCS_POSIX_BINARY = 4, // A POSIX – based application
        SCS_WOW_BINARY = 2 // A 16-bit Windows-based application
    }

    [DllImport("kernel32.dll")]
    public static extern bool GetBinaryType(string lpApplicationName, out BinaryType lpBinaryType);

    #endregion

    #region Windows - user32.dll

    public const int GWL_HWNDPARENT = (-8);
    public const int GWL_EXSTYLE = (-20);
    public const int GWL_STYLE = (-16);
    public const int GCL_HICON = (-14);
    public const int GCL_HICONSM = (-34);
    public const int WM_QUERYDRAGICON = 0x37;
    public const int WM_GETICON = 0x7F;
    public const int WM_SETICON = 0x80;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int SMTO_ABORTIFHUNG = 0x2;
    public const int TRUE = 1;
    public const int FALSE = 0;

    public const int WHITE_BRUSH = 0;
    public const int LTGRAY_BRUSH = 1;
    public const int GRAY_BRUSH = 2;
    public const int DKGRAY_BRUSH = 3;
    public const int BLACK_BRUSH = 4;
    public const int NULL_BRUSH = 5;
    public const int HOLLOW_BRUSH = NULL_BRUSH;
    public const int WHITE_PEN = 6;
    public const int BLACK_PEN = 7;
    public const int NULL_PEN = 8;
    public const int OEM_FIXED_FONT = 10;
    public const int ANSI_FIXED_FONT = 11;
    public const int ANSI_VAR_FONT = 12;
    public const int SYSTEM_FONT = 13;
    public const int DEVICE_DEFAULT_FONT = 14;
    public const int DEFAULT_PALETTE = 15;
    public const int SYSTEM_FIXED_FONT = 16;


    public const int RDW_INVALIDATE = 0x0001;
    public const int RDW_INTERNALPAINT = 0x0002;
    public const int RDW_ERASE = 0x0004;

    public const int RDW_VALIDATE = 0x0008;
    public const int RDW_NOINTERNALPAINT = 0x0010;
    public const int RDW_NOERASE = 0x0020;

    public const int RDW_NOCHILDREN = 0x0040;
    public const int RDW_ALLCHILDREN = 0x0080;

    public const int RDW_UPDATENOW = 0x0100;
    public const int RDW_ERASENOW = 0x0200;

    public const int RDW_FRAME = 0x0400;
    public const int RDW_NOFRAME = 0x0800;



    public enum ShowWindowCmds
    {
        SW_HIDE = 0,
        SW_SHOWNORMAL = 1,
        SW_NORMAL = 1,
        SW_SHOWMINIMIZED = 2,
        SW_SHOWMAXIMIZED = 3,
        SW_MAXIMIZE = 3,
        SW_SHOWNOACTIVATE = 4,
        SW_SHOW = 5,
        SW_MINIMIZE = 6,
        SW_SHOWMINNOACTIVE = 7,
        SW_SHOWNA = 8,
        SW_RESTORE = 9,
        SW_SHOWDEFAULT = 10,
        SW_FORCEMINIMIZE = 11,
        SW_MAX = 11
    }

    public const int HIDE_WINDOW = 0;
    public const int SHOW_OPENWINDOW = 1;
    public const int SHOW_ICONWINDOW = 2;
    public const int SHOW_FULLSCREEN = 3;
    public const int SHOW_OPENNOACTIVATE = 4;
    public const int SW_PARENTCLOSING = 1;
    public const int SW_OTHERZOOM = 2;
    public const int SW_PARENTOPENING = 3;
    public const int SW_OTHERUNZOOM = 4;

    public const int SWP_NOSIZE = 0x0001;
    public const int SWP_NOMOVE = 0x0002;
    public const int SWP_NOZORDER = 0x0004;
    public const int SWP_NOREDRAW = 0x0008;
    public const int SWP_NOACTIVATE = 0x0010;
    public const int SWP_FRAMECHANGED = 0x0020; /* The frame changed: send WM_NCCALCSIZE */
    public const int SWP_SHOWWINDOW = 0x0040;
    public const int SWP_HIDEWINDOW = 0x0080;
    public const int SWP_NOCOPYBITS = 0x0100;
    public const int SWP_NOOWNERZORDER = 0x0200; /* Don't do owner Z ordering */
    public const int SWP_NOSENDCHANGING = 0x0400;  /* Don't send WM_WINDOWPOSCHANGING */
    public const int SWP_DRAWFRAME = SWP_FRAMECHANGED;
    public const int SWP_NOREPOSITION = SWP_NOOWNERZORDER;
    public const int SWP_DEFERERASE = 0x2000;
    public const int SWP_ASYNCWINDOWPOS = 0x4000;

    public const int HWND_TOP = 0;
    public const int HWND_BOTTOM = 1;
    public const int HWND_TOPMOST = -1;
    public const int HWND_NOTOPMOST = -2;

    public enum PeekMessageFlags
    {
        PM_NOREMOVE = 0,
        PM_REMOVE = 1,
        PM_NOYIELD = 2
    }

    public enum WindowMessages
    {
        WM_NULL = 0x0000,
        WM_CREATE = 0x0001,
        WM_DESTROY = 0x0002,
        WM_MOVE = 0x0003,
        WM_SIZE = 0x0005,
        WM_ACTIVATE = 0x0006,
        WM_SETFOCUS = 0x0007,
        WM_KILLFOCUS = 0x0008,
        WM_ENABLE = 0x000A,
        WM_SETREDRAW = 0x000B,
        WM_SETTEXT = 0x000C,
        WM_GETTEXT = 0x000D,
        WM_GETTEXTLENGTH = 0x000E,
        WM_PAINT = 0x000F,
        WM_CLOSE = 0x0010,
        WM_QUERYENDSESSION = 0x0011,
        WM_QUIT = 0x0012,
        WM_QUERYOPEN = 0x0013,
        WM_ERASEBKGND = 0x0014,
        WM_SYSCOLORCHANGE = 0x0015,
        WM_ENDSESSION = 0x0016,
        WM_SHOWWINDOW = 0x0018,
        WM_CTLCOLOR = 0x0019,
        WM_WININICHANGE = 0x001A,
        WM_SETTINGCHANGE = 0x001A,
        WM_DEVMODECHANGE = 0x001B,
        WM_ACTIVATEAPP = 0x001C,
        WM_FONTCHANGE = 0x001D,
        WM_TIMECHANGE = 0x001E,
        WM_CANCELMODE = 0x001F,
        WM_SETCURSOR = 0x0020,
        WM_MOUSEACTIVATE = 0x0021,
        WM_CHILDACTIVATE = 0x0022,
        WM_QUEUESYNC = 0x0023,
        WM_GETMINMAXINFO = 0x0024,
        WM_PAINTICON = 0x0026,
        WM_ICONERASEBKGND = 0x0027,
        WM_NEXTDLGCTL = 0x0028,
        WM_SPOOLERSTATUS = 0x002A,
        WM_DRAWITEM = 0x002B,
        WM_MEASUREITEM = 0x002C,
        WM_DELETEITEM = 0x002D,
        WM_VKEYTOITEM = 0x002E,
        WM_CHARTOITEM = 0x002F,
        WM_SETFONT = 0x0030,
        WM_GETFONT = 0x0031,
        WM_SETHOTKEY = 0x0032,
        WM_GETHOTKEY = 0x0033,
        WM_QUERYDRAGICON = 0x0037,
        WM_COMPAREITEM = 0x0039,
        WM_GETOBJECT = 0x003D,
        WM_COMPACTING = 0x0041,
        WM_COMMNOTIFY = 0x0044,
        WM_WINDOWPOSCHANGING = 0x0046,
        WM_WINDOWPOSCHANGED = 0x0047,
        WM_POWER = 0x0048,
        WM_COPYDATA = 0x004A,
        WM_CANCELJOURNAL = 0x004B,
        WM_NOTIFY = 0x004E,
        WM_INPUTLANGCHANGEREQUEST = 0x0050,
        WM_INPUTLANGCHANGE = 0x0051,
        WM_TCARD = 0x0052,
        WM_HELP = 0x0053,
        WM_USERCHANGED = 0x0054,
        WM_NOTIFYFORMAT = 0x0055,
        WM_CONTEXTMENU = 0x007B,
        WM_STYLECHANGING = 0x007C,
        WM_STYLECHANGED = 0x007D,
        WM_DISPLAYCHANGE = 0x007E,
        WM_GETICON = 0x007F,
        WM_SETICON = 0x0080,
        WM_NCCREATE = 0x0081,
        WM_NCDESTROY = 0x0082,
        WM_NCCALCSIZE = 0x0083,
        WM_NCHITTEST = 0x0084,
        WM_NCPAINT = 0x0085,
        WM_NCACTIVATE = 0x0086,
        WM_GETDLGCODE = 0x0087,
        WM_SYNCPAINT = 0x0088,
        WM_NCMOUSEMOVE = 0x00A0,
        WM_NCLBUTTONDOWN = 0x00A1,
        WM_NCLBUTTONUP = 0x00A2,
        WM_NCLBUTTONDBLCLK = 0x00A3,
        WM_NCRBUTTONDOWN = 0x00A4,
        WM_NCRBUTTONUP = 0x00A5,
        WM_NCRBUTTONDBLCLK = 0x00A6,
        WM_NCMBUTTONDOWN = 0x00A7,
        WM_NCMBUTTONUP = 0x00A8,
        WM_NCMBUTTONDBLCLK = 0x00A9,
        WM_KEYDOWN = 0x0100,
        WM_KEYUP = 0x0101,
        WM_CHAR = 0x0102,
        WM_DEADCHAR = 0x0103,
        WM_SYSKEYDOWN = 0x0104,
        WM_SYSKEYUP = 0x0105,
        WM_SYSCHAR = 0x0106,
        WM_SYSDEADCHAR = 0x0107,
        WM_KEYLAST = 0x0108,
        WM_IME_STARTCOMPOSITION = 0x010D,
        WM_IME_ENDCOMPOSITION = 0x010E,
        WM_IME_COMPOSITION = 0x010F,
        WM_IME_KEYLAST = 0x010F,
        WM_INITDIALOG = 0x0110,
        WM_COMMAND = 0x0111,
        WM_SYSCOMMAND = 0x0112,
        WM_TIMER = 0x0113,
        WM_HSCROLL = 0x0114,
        WM_VSCROLL = 0x0115,
        WM_INITMENU = 0x0116,
        WM_INITMENUPOPUP = 0x0117,
        WM_MENUSELECT = 0x011F,
        WM_MENUCHAR = 0x0120,
        WM_ENTERIDLE = 0x0121,
        WM_MENURBUTTONUP = 0x0122,
        WM_MENUDRAG = 0x0123,
        WM_MENUGETOBJECT = 0x0124,
        WM_UNINITMENUPOPUP = 0x0125,
        WM_MENUCOMMAND = 0x0126,
        WM_CTLCOLORMSGBOX = 0x0132,
        WM_CTLCOLOREDIT = 0x0133,
        WM_CTLCOLORLISTBOX = 0x0134,
        WM_CTLCOLORBTN = 0x0135,
        WM_CTLCOLORDLG = 0x0136,
        WM_CTLCOLORSCROLLBAR = 0x0137,
        WM_CTLCOLORSTATIC = 0x0138,
        WM_MOUSEMOVE = 0x0200,
        WM_LBUTTONDOWN = 0x0201,
        WM_LBUTTONUP = 0x0202,
        WM_LBUTTONDBLCLK = 0x0203,
        WM_RBUTTONDOWN = 0x0204,
        WM_RBUTTONUP = 0x0205,
        WM_RBUTTONDBLCLK = 0x0206,
        WM_MBUTTONDOWN = 0x0207,
        WM_MBUTTONUP = 0x0208,
        WM_MBUTTONDBLCLK = 0x0209,
        WM_MOUSEWHEEL = 0x020A,
        WM_PARENTNOTIFY = 0x0210,
        WM_ENTERMENULOOP = 0x0211,
        WM_EXITMENULOOP = 0x0212,
        WM_NEXTMENU = 0x0213,
        WM_SIZING = 0x0214,
        WM_CAPTURECHANGED = 0x0215,
        WM_MOVING = 0x0216,
        WM_DEVICECHANGE = 0x0219,
        WM_MDICREATE = 0x0220,
        WM_MDIDESTROY = 0x0221,
        WM_MDIACTIVATE = 0x0222,
        WM_MDIRESTORE = 0x0223,
        WM_MDINEXT = 0x0224,
        WM_MDIMAXIMIZE = 0x0225,
        WM_MDITILE = 0x0226,
        WM_MDICASCADE = 0x0227,
        WM_MDIICONARRANGE = 0x0228,
        WM_MDIGETACTIVE = 0x0229,
        WM_MDISETMENU = 0x0230,
        WM_ENTERSIZEMOVE = 0x0231,
        WM_EXITSIZEMOVE = 0x0232,
        WM_DROPFILES = 0x0233,
        WM_MDIREFRESHMENU = 0x0234,
        WM_IME_SETCONTEXT = 0x0281,
        WM_IME_NOTIFY = 0x0282,
        WM_IME_CONTROL = 0x0283,
        WM_IME_COMPOSITIONFULL = 0x0284,
        WM_IME_SELECT = 0x0285,
        WM_IME_CHAR = 0x0286,
        WM_IME_REQUEST = 0x0288,
        WM_IME_KEYDOWN = 0x0290,
        WM_IME_KEYUP = 0x0291,
        WM_MOUSEHOVER = 0x02A1,
        WM_MOUSELEAVE = 0x02A3,
        WM_CUT = 0x0300,
        WM_COPY = 0x0301,
        WM_PASTE = 0x0302,
        WM_CLEAR = 0x0303,
        WM_UNDO = 0x0304,
        WM_RENDERFORMAT = 0x0305,
        WM_RENDERALLFORMATS = 0x0306,
        WM_DESTROYCLIPBOARD = 0x0307,
        WM_DRAWCLIPBOARD = 0x0308,
        WM_PAINTCLIPBOARD = 0x0309,
        WM_VSCROLLCLIPBOARD = 0x030A,
        WM_SIZECLIPBOARD = 0x030B,
        WM_ASKCBFORMATNAME = 0x030C,
        WM_CHANGECBCHAIN = 0x030D,
        WM_HSCROLLCLIPBOARD = 0x030E,
        WM_QUERYNEWPALETTE = 0x030F,
        WM_PALETTEISCHANGING = 0x0310,
        WM_PALETTECHANGED = 0x0311,
        WM_HOTKEY = 0x0312,
        WM_PRINT = 0x0317,
        WM_PRINTCLIENT = 0x0318,
        WM_HANDHELDFIRST = 0x0358,
        WM_HANDHELDLAST = 0x035F,
        WM_AFXFIRST = 0x0360,
        WM_AFXLAST = 0x037F,
        WM_PENWINFIRST = 0x0380,
        WM_PENWINLAST = 0x038F,
        WM_APP = 0x8000,
        WM_USER = 0x0400,
        WM_REFLECT = WM_USER + 0x1c00
    }

    /// <summary>
    /// Defines the style bits that a window can have
    /// </summary>
    public enum WindowStyles : uint
    {
        WS_BORDER = 0x800000,
        WS_CAPTION = 0xC00000,//               '  WS_BORDER Or WS_DLGFRAME
        WS_CHILD = 0x40000000,
        WS_CHILDWINDOW = (WS_CHILD),
        WS_CLIPCHILDREN = 0x2000000,
        WS_CLIPSIBLINGS = 0x4000000,
        WS_DISABLED = 0x8000000,
        WS_DLGFRAME = 0x400000,
        WS_EX_ACCEPTFILES = 0x10,
        WS_EX_DLGMODALFRAME = 0x1,
        WS_EX_NOPARENTNOTIFY = 0x4,
        WS_EX_TOPMOST = 0x8,
        WS_EX_TRANSPARENT = 0x20,
        WS_EX_TOOLWINDOW = 0x80,
        WS_EX_APPWINDOW = 0x40000,
        WS_GROUP = 0x20000,
        WS_HSCROLL = 0x100000,
        WS_MAXIMIZE = 0x1000000,
        WS_MAXIMIZEBOX = 0x10000,
        WS_MINIMIZE = 0x20000000,
        WS_MINIMIZEBOX = 0x20000,
        WS_OVERLAPPED = 0x0,
        WS_POPUP = 0x80000000,
        WS_SYSMENU = 0x80000,
        WS_TABSTOP = 0x10000,
        WS_THICKFRAME = 0x40000,
        WS_VISIBLE = 0x10000000,
        WS_VSCROLL = 0x200000,
        WS_ICONIC = WS_MINIMIZE,
        WS_POPUPWINDOW = (WS_POPUP | WS_BORDER | WS_SYSMENU),
        WS_SIZEBOX = WS_THICKFRAME,
        WS_TILED = WS_OVERLAPPED,
        WS_OVERLAPPEDWINDOW = (WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)
    }

    public enum GetWindow_Cmd : int
    {
        GW_HWNDFIRST = 0,
        GW_HWNDLAST = 1,
        GW_HWNDNEXT = 2,
        GW_HWNDPREV = 3,
        GW_OWNER = 4,
        GW_CHILD = 5,
        GW_ENABLEDPOPUP = 6
    }

    public struct WindowInfo
    {
        public int size;
        public Rectangle window;
        public Rectangle client;
        public int style;
        public int exStyle;
        public int windowStatus;
        public uint xWindowBorders;
        public uint yWindowBorders;
        public short atomWindowtype;
        public short creatorVersion;
    }

    public struct WindowPlacement
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point minPosition;
        public Point maxPosition;
        public Rectangle normalPosition;
    }

    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;

        public bool Contains(Rect other)
        {
            return Left <= other.Right && Right >= other.Left && Top <= other.Bottom && Bottom >= other.Top;
        }

        public Rectangle ToRectangle()
        {
            return new Rectangle(Left, Top, Right - Left, Bottom - Top);
        }
    }
    public delegate bool EnumWindowEventHandler(IntPtr hWnd, Int32 lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowModuleFileName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement position);

    [DllImport("User32.dll")]
    public static extern int RegisterWindowMessage(string message);

    [DllImport("User32.dll")]
    public static extern void EnumWindows(EnumWindowEventHandler callback, int lParam);

    [DllImport("User32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("User32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, int wCmd);

    [DllImport("User32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("User32.dll")]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("User32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("User32.dll")]
    public static extern int SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("User32.dll")]
    public static extern WindowStyles GetWindowLong(IntPtr hWnd, int index);

    [DllImport("User32.dll")]
    public static extern int SendMessageTimeout(IntPtr hWnd, int uMsg, int wParam, int lParam, int fuFlags, int uTimeout, out int lpdwResult);

    [DllImport("User32.dll")]
    public static extern int GetClassLong(IntPtr hWnd, int index);

    [DllImport("User32.dll")]
    public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("User32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("User32.dll")]
    public static extern int PostMessage(IntPtr hWnd, WindowMessages uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("User32.dll")]
    public static extern void SwitchToThisWindow(IntPtr hWnd, int altTabActivated);

    [DllImport("User32.dll")]
    public static extern int ShowWindowAsync(IntPtr hWnd, int command);

    [DllImport("User32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool GetWindowInfo(IntPtr hwnd, ref WindowInfo info);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern Int32 ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rectangle);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hwnd, ref Rect rectangle);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(Point pt);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseCapture();

    //[DllImport("user32.dll")]
    //public static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    //[DllImport("user32.dll")]
    //public static extern IntPtr GetStockObject(int nObject);

    [DllImport("user32.dll")]
    public static extern int InvalidateRect(IntPtr hWnd, IntPtr lpRect, int bErase);

    [DllImport("user32.dll")]
    public static extern int UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr handle, ref Point point);

    [DllImport("user32.dll")]
    public static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, Point pt, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hwnd, ref Point lpPoint);

    [DllImport("user32.dll")]
    public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true)]
    public static extern IntPtr FindWindowByCaption(IntPtr zeroOnly, string lpWindowName);
    
    [DllImport("user32.dll")]
    public static extern int FindWindowEx(int hWnd1, int hWnd2, string lpsz1, string lpsz2);

    //Import the SetForeground API to activate it
    [DllImport("User32.dll")]
    public static extern IntPtr SetForegroundWindow(int hWnd);

    public const int KEYEVENTF_EXTENDEDKEY = 0x1;
    public const int KEYEVENTF_KEYUP = 0x2;

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte vVk, byte bScan, int dwFlags, int dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern int MapVirtualKey(int wCode, int wMapType);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int flags);



    // http://msdn.microsoft.com/en-us/library/windows/desktop/ms646260(v=vs.85).aspx

    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000; // The dx and dy parameters contain normalized absolute coordinates. If not set, 
                                                     // those parameters contain relative data: the change in position since the last reported position. 
                                                     // This flag can be set, or not set, regardless of what kind of mouse or mouse-like device, if any, is connected to the system. 
                                                     // For further information about relative mouse motion, see the following Remarks section.
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002; // 왼쪽 마우스 버튼 눌림
    public const uint MOUSEEVENTF_LEFTUP = 0x0004; // 왼쪽 마우스 버튼 떼어짐
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020; // 가운데 마우스 버튼 눌림
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040; // 가운데 마우스 버튼 떼어짐
    public const uint MOUSEEVENTF_MOVE = 0x0001; // 마우스 이동
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008; // 오른쪽 마우스 버튼 눌림
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010; // 오른쪽 마우스 버튼 떼어짐
    public const uint MOUSEEVENTF_XDOWN = 0x0080; // An X button was pressed.
    public const uint MOUSEEVENTF_XUP = 0x0100; // An X button was released.
    public const uint MOUSEEVENTF_WHEEL = 0x0800; // The wheel button is rotated.
    public const uint MOUSEEVENTF_HWHEEL = 0x01000; // The wheel button is tilted.

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern int SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(ref Point point);



    public enum EnumGetSystemMetrics : int
    {
        SM_CXBORDER = 5,
        SM_CXFULLSCREEN = 16,
        SM_CYFULLSCREEN = 17
    }

    public const int GCL_HMODULE = -16;
    
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int smIndex);


    // helper function return directly a Rectangle object
    public static Rectangle GetWindowRect(IntPtr hWnd)
    {
        //Debug.Assert(hWnd != IntPtr.Zero);
        if (GetWindowRect(hWnd, out Rect rect) == false)
            throw new Exception("GetWindowRect failed");
        return rect.ToRectangle();
    }

    public static string GetWindowText(IntPtr hWnd)
    {
        //Debug.Assert(hWnd != IntPtr.Zero);
        StringBuilder WindowText = new StringBuilder(GetWindowTextLength(hWnd) + 1);
        GetWindowText(hWnd, WindowText, WindowText.Capacity);
        return WindowText.ToString();
    }
    #endregion Windows

    #region GDI - gdi32.dll

    public enum RopMode : int
    {
        R2_NOT = 6
    }

    public enum PenStyles : int
    {
        PS_INSIDEFRAME = 6
    }

    public enum StockObjects : int
    {
        NULL_BRUSH = 5
    }

    public enum BinaryRasterOperations
    {
        R2_BLACK = 1,   /*  0       */
        R2_NOTMERGEPEN = 2,   /* DPon     */
        R2_MASKNOTPEN = 3,   /* DPna     */
        R2_NOTCOPYPEN = 4,   /* PN       */
        R2_MASKPENNOT = 5,   /* PDna     */
        R2_NOT = 6,   /* Dn       */
        R2_XORPEN = 7,   /* DPx      */
        R2_NOTMASKPEN = 8,   /* DPan     */
        R2_MASKPEN = 9,   /* DPa      */
        R2_NOTXORPEN = 10,  /* DPxn     */
        R2_NOP = 11,  /* D        */
        R2_MERGENOTPEN = 12,  /* DPno     */
        R2_COPYPEN = 13,  /* P        */
        R2_MERGEPENNOT = 14,  /* PDno     */
        R2_MERGEPEN = 15,  /* DPo      */
        R2_WHITE = 16,  /*  1       */
        R2_LAST = 16
    }

    public enum TernaryRasterOperations
    {
        SRCCOPY = 0x00CC0020, /* dest = source                   */
        SRCPAINT = 0x00EE0086, /* dest = source OR dest           */
        SRCAND = 0x008800C6, /* dest = source AND dest          */
        SRCINVERT = 0x00660046, /* dest = source XOR dest          */
        SRCERASE = 0x00440328, /* dest = source AND (NOT dest )   */
        NOTSRCCOPY = 0x00330008, /* dest = (NOT source)             */
        NOTSRCERASE = 0x001100A6, /* dest = (NOT src) AND (NOT dest) */
        MERGECOPY = 0x00C000CA, /* dest = (source AND pattern)     */
        MERGEPAINT = 0x00BB0226, /* dest = (NOT source) OR dest     */
        PATCOPY = 0x00F00021, /* dest = pattern                  */
        PATPAINT = 0x00FB0A09, /* dest = DPSnoo                   */
        PATINVERT = 0x005A0049, /* dest = pattern XOR dest         */
        DSTINVERT = 0x00550009, /* dest = (NOT dest)               */
        BLACKNESS = 0x00000042, /* dest = BLACK                    */
        WHITENESS = 0x00FF0062 /* dest = WHITE                    */

    }

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDst, int xDst, int yDst, int cx, int cy, IntPtr hdcSrc, int xSrc, int ySrc, uint ulRop);

    [DllImport("gdi32.dll")]
    public static extern bool StretchBlt(IntPtr hdcDst, int xDst, int yDst, int cx, int cy, IntPtr hdcSrc, int xSrc, int ySrc, int cxSrc, int cySrc, uint ulRop);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDC(IntPtr lpszDriver, string lpszDevice, IntPtr lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    public static extern IntPtr DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern int SetROP2(IntPtr hdc, int fnDrawMode);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int fnObject);

    [DllImport("gdi32.dll")]
    public static extern uint Rectangle(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hObjectSource, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    #endregion

    #region Shlwapi.dll

    [DllImport("Shlwapi.dll")]
    public static extern string PathGetArgs(string path);

    public static string SafePathGetArgs(string path)
    {
        try
        {
            return WinApi32.PathGetArgs(path);
        }
        catch
        {
        }
        return string.Empty;
    }

    [DllImport("Shlwapi.dll")]
    public static extern int PathCompactPathEx(
        System.Text.StringBuilder pszOut, /* Address of the string that has been altered */
        System.Text.StringBuilder pszSrc, /* Pointer to a null-terminated string of max length (MAX_PATH) that contains the path to be altered */
        uint cchMax,                      /* Maximum number of chars to be contained in the new string, including the null character. Example: cchMax = 8, then 7 chars will be returned, the last for the null character. */
        uint dwFlags);                    /* Reserved */

    public static string PathCompactPathEx(string source, uint maxChars)
    {
        StringBuilder pszOut = new StringBuilder((int)WinApi32.MAX_PATH);
        StringBuilder pszSrc = new StringBuilder(source);

        int result = WinApi32.PathCompactPathEx(pszOut, pszSrc, maxChars, (uint)0);
        if (result == 1)
            return pszOut.ToString();
        else
        {
            System.Diagnostics.Debug.WriteLine(String.Format("Win32.PathCompactPathEx failed to compact the path '{0}' down to '{1}' characters.", source, maxChars));
            return string.Empty;
        }
    }

    #endregion

    #region Hotkeys

    [Flags()]
    public enum HotkeyModifiers
    {
        MOD_ALT = 0x0001,
        MOD_CONTROL = 0x0002,
        MOD_SHIFT = 0x0004,
        MOD_WIN = 0x0008
    }

    [DllImport("User32")]
    public static extern int RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualkeyCode);

    [DllImport("User32")]
    public static extern int UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("Kernel32")]
    public static extern short GlobalAddAtom(string atomName);

    [DllImport("Kernel32")]
    public static extern short GlobalDeleteAtom(short atom);

    #endregion

    #region dwmapi.dll

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out Rect pvAttribute, int cbAttribute);

    #endregion

    [DllImport("User32")]
    public static extern int LockWindowUpdate(IntPtr windowHandle);

    public static short MAKEWORD(byte a, byte b)
    {
        return ((short)(((byte)(a & 0xff)) | ((short)((byte)(b & 0xff))) << 8));
    }

    public static byte LOBYTE(short a)
    {
        return ((byte)(a & 0xff));
    }

    public static byte HIBYTE(short a)
    {
        return ((byte)(a >> 8));
    }

    public static int MAKELONG(short a, short b)
    {
        return (((int)(a & 0xffff)) | (((int)(b & 0xffff)) << 16));
    }

    public static short HIWORD(int a)
    {
        return ((short)(a >> 16));
    }

    public static short LOWORD(int a)
    {
        return ((short)(a & 0xffff));
    }

    [DllImport("Kernel32")]
    public static extern int CopyFile(string source, string destination, int failIfExists);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(System.Windows.Forms.Keys key);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rect, MonitorEnumDelegate callback, int data);
    public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect rect, int data);


    public static Point GetSystemDPI()
    {
        Point result = new Point();
        IntPtr hDC = GetDC(IntPtr.Zero);
        result.X = GetDeviceCaps(hDC, 0x58);
        result.Y = GetDeviceCaps(hDC, 0x5A);
        ReleaseDC(IntPtr.Zero, hDC);
        return result;
    }

    public enum DpiFlags
    {
        Effective = 0,
        Angular = 1,
        Raw = 2,
    }

    [DllImport("Shcore.dll")]
    private static extern IntPtr GetDpiForMonitor(IntPtr hMonitor, DpiFlags dpiType, out uint dpiX, out uint dpiY);
    public static Point GetMonitorDPI(IntPtr hMonitor)
    {
        GetDpiForMonitor(hMonitor, DpiFlags.Effective, out uint dpiX, out uint dpiY);
        return new Point((int)dpiX, (int)dpiY);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();
    
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    public static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);

    public delegate Boolean ConsoleCtrlDelegate(CtrlTypes CtrlType);
    public enum CtrlTypes : uint
    {
        CTRL_C_EVENT = 0,
        CTRL_BREAK_EVENT,
        CTRL_CLOSE_EVENT,
        CTRL_LOGOFF_EVENT = 5,
        CTRL_SHUTDOWN_EVENT
    }

    [DllImport("user32.dll")]
    public static extern short VkKeyScan(char ch);

    public static Keys ConvertCharToVirtualKey(char ch)
    {
        short vkey = VkKeyScan(ch);
        Keys retval = (Keys)(vkey & 0xFF); // Low-order byte is the virtual-key code
        int modifiers = vkey >> 8;        // High-order byte contains shift state

        if ((modifiers & 1) != 0) // Shift key
        {
            retval |= Keys.Shift;
        }
        if ((modifiers & 2) != 0) // Control key
        {
            retval |= Keys.Control;
        }
        if ((modifiers & 4) != 0) // Alt key
        {
            retval |= Keys.Alt;
        }

        return retval;
    }

    public const uint MAPVK_VK_TO_CHAR = 0x02;

    [DllImport("user32.dll")]
    public static extern int MapVirtualKey(int uCode, uint uMapType);


    public delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumThreadWindows(int dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);
}
