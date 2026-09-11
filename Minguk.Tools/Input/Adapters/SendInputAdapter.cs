using System;
using System.Runtime.InteropServices;

namespace Minguk.Tools.Input.Adapters;

/// <summary>
/// Win32 <c>SendInput</c> 으로 입력을 만들어 넣는다. 드라이버 설치가 필요 없는 기본 경로다.
///
/// 왜 SendInput 인가
///   PostMessage/SendMessage 로 WM_LBUTTONDOWN 같은 창 메시지를 보내는 방법도 있지만,
///   그건 "창에 쪽지를 넣는" 것이라 게임처럼 원시 입력(Raw Input)이나 DirectInput 을
///   직접 읽는 프로그램은 무시한다.
///   SendInput 은 커널의 입력 큐에 넣어서 실제 HID 장치가 만든 입력과 같은 경로를 탄다.
///
/// 그래서 생기는 제약
///   - 입력은 "지금 포커스를 가진 창" 으로 간다. 특정 창에 보내려면 그 창을 먼저 앞으로 가져와야 한다.
///   - 화면 좌표계로 움직인다. 대상 창의 클라이언트 좌표가 아니다.
///   - 관리자 권한으로 뜬 창에는 일반 권한 프로세스가 입력을 넣을 수 없다(UIPI).
/// </summary>
public sealed class SendInputAdapter : IInputAdapter, IScanCodeInput
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public string Name => "SendInput";

    /// <summary>OS 가 항상 주는 API 라 준비할 것이 없다.</summary>
    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    /// <summary>커널 입력 큐는 포커스를 가진 창으로 간다. 그래서 대상을 앞으로 가져와야 한다.</summary>
    public bool RequiresForegroundTarget => true;

    public (int X, int Y)? GetCursorPosition()
        => NativeMethods.GetCursorPos(out var point) ? (point.X, point.Y) : null;

    /// <summary>
    /// 마우스를 화면 절대 좌표로 옮긴다.
    ///
    /// SendInput 의 절대 좌표는 픽셀이 아니라 0~65535 로 정규화된 값이다.
    /// 여러 모니터를 함께 쓰려면 가상 화면(모든 모니터를 감싸는 사각형) 기준으로 환산해야 한다.
    /// </summary>
    public bool MoveMouseTo(int screenX, int screenY)
    {
        // 환산은 VirtualScreen 에 모여 있다. Interception 경로도 같은 계산을 쓴다.
        if (!VirtualScreen.TryNormalize(screenX, screenY, out var normalizedX, out var normalizedY))
            return false;

        return Send(NativeMethods.MouseInput(
            NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
            normalizedX,
            normalizedY));
    }

    /// <summary>
    /// 0~65535 정규화 값을 환산 없이 그대로 보낸다.
    /// </summary>
    /// <remarks>
    /// Windows 가 정규화 좌표를 픽셀로 되돌리는 규칙은 문서에 없다. 그 규칙을 실측하려면
    /// 우리 환산을 거치지 않고 값을 그대로 넣어 봐야 해서 열어 둔다.
    /// 평소 쓰는 것은 <see cref="MoveMouseTo"/> 다.
    /// </remarks>
    public bool MoveMouseToNormalized(int normalizedX, int normalizedY)
        => Send(NativeMethods.MouseInput(
            NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
            normalizedX,
            normalizedY));

    /// <summary>상대 이동. 절대 플래그 없이 dx·dy 만 보내면 움직인 양으로 들어간다 - 커서를 잡는 게임이 보는 것이 이것이다.</summary>
    public bool MoveMouseBy(int deltaX, int deltaY)
        => Send(NativeMethods.MouseInput(NativeMethods.MOUSEEVENTF_MOVE, deltaX, deltaY));

    public bool PressMouseButton(MouseButton button) => Send(NativeMethods.MouseInput(DownFlag(button)));

    public bool ReleaseMouseButton(MouseButton button) => Send(NativeMethods.MouseInput(UpFlag(button)));

    /// <summary>누르고 떼기를 한 번에. 두 이벤트를 한 번의 SendInput 으로 보내 사이에 끼어들 틈을 줄인다.</summary>
    public bool ClickMouseButton(MouseButton button)
        => Send(NativeMethods.MouseInput(DownFlag(button)), NativeMethods.MouseInput(UpFlag(button)));

    /// <summary>휠. <paramref name="delta"/> 는 120 이 한 칸이다(WHEEL_DELTA).</summary>
    public bool ScrollWheel(int delta)
        => Send(NativeMethods.MouseInput(NativeMethods.MOUSEEVENTF_WHEEL, mouseData: delta));

    public bool PressKey(ushort virtualKey) => Send(NativeMethods.KeyInput(virtualKey, isKeyUp: false));

    public bool ReleaseKey(ushort virtualKey) => Send(NativeMethods.KeyInput(virtualKey, isKeyUp: true));

    public bool PressScanCode(ushort scanCode, bool extended) => SendScanCode(scanCode, extended, isKeyUp: false);

    public bool ReleaseScanCode(ushort scanCode, bool extended) => SendScanCode(scanCode, extended, isKeyUp: true);

    /// <summary>
    /// 한/영(0xF2)·한자(0xF1)만 가상 키로 바꿔 보낸다.
    ///
    /// 이 둘은 스캔코드 표(0x00~0x7F) 바깥 값이라 스캔코드로 주입하면 키보드 레이아웃이
    /// 가상 키로 번역하지 못한다. 드라이버 수준으로 넣는 경로(Interception)는 진짜 키보드가
    /// 보내는 것과 같아서 그대로 통하지만, SendInput 은 레이아웃 번역을 거치므로 안 통한다.
    /// </summary>
    private static bool SendScanCode(ushort scanCode, bool extended, bool isKeyUp)
    {
        var virtualKey = scanCode switch
        {
            HangulScanCode => VK_HANGUL,
            HanjaScanCode => VK_HANJA,
            _ => (ushort)0
        };

        return virtualKey != 0
            ? Send(NativeMethods.KeyInput(virtualKey, isKeyUp))
            : Send(NativeMethods.ScanCodeInput(scanCode, extended, isKeyUp));
    }

    private const ushort HangulScanCode = 0xF2;
    private const ushort HanjaScanCode = 0xF1;
    private const ushort VK_HANGUL = 0x15;
    private const ushort VK_HANJA = 0x19;

    private static uint DownFlag(MouseButton button) => button switch
    {
        MouseButton.Right => NativeMethods.MOUSEEVENTF_RIGHTDOWN,
        MouseButton.Middle => NativeMethods.MOUSEEVENTF_MIDDLEDOWN,
        _ => NativeMethods.MOUSEEVENTF_LEFTDOWN
    };

    private static uint UpFlag(MouseButton button) => button switch
    {
        MouseButton.Right => NativeMethods.MOUSEEVENTF_RIGHTUP,
        MouseButton.Middle => NativeMethods.MOUSEEVENTF_MIDDLEUP,
        _ => NativeMethods.MOUSEEVENTF_LEFTUP
    };

    /// <summary>
    /// 실제로 커널 입력 큐에 넣는다.
    ///
    /// SendInput 은 넣은 개수를 돌려준다. 요청한 것보다 적으면 OS 가 막은 것이다.
    /// 거의 항상 UIPI — 대상 창이 이 앱보다 높은 권한으로 떠 있는 경우다(오류 코드 5).
    /// 조용히 넘기면 "클릭이 안 된다" 는 것만 보이고 이유를 알 수 없어서 남긴다.
    /// </summary>
    private static bool Send(params NativeMethods.Input[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());

        if (sent == inputs.Length)
            return true;

        Logger.Warn($"SendInput 이 막혔다. 보낸 것 {sent}/{inputs.Length}, 오류 코드 {Marshal.GetLastWin32Error()}");
        return false;
    }

    /// <summary>커널 입력 큐에 넣기만 하므로 놓아 줄 자원이 없다.</summary>
    public void Dispose()
    {
    }

    private static class NativeMethods
    {

        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;

        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;

        public static Input MouseInput(uint flags, int x = 0, int y = 0, int mouseData = 0) => new()
        {
            Type = INPUT_MOUSE,
            Data = new InputUnion
            {
                Mouse = new MouseInputData
                {
                    X = x,
                    Y = y,
                    MouseData = mouseData,
                    Flags = flags
                }
            }
        };

        /// <summary>
        /// 가상 키로 보내되 스캔코드도 같이 싣는다.
        /// </summary>
        /// <remarks>
        /// wScan 을 비우면 Raw Input 으로 받는 쪽(게임)에는 MakeCode 가 0 인 키가 들어가 W·A·S·D 가 안 먹는다.
        /// 가상 키 모드에서도 wScan 은 그대로 Raw Input 의 MakeCode 가 되므로 채워 둔다. 보통 창에는 아무 차이가 없다.
        /// </remarks>
        public static Input KeyInput(ushort virtualKey, bool isKeyUp)
        {
            var flags = isKeyUp ? KEYEVENTF_KEYUP : 0;
            if (VirtualKeys.IsExtendedKey(virtualKey)) flags |= KEYEVENTF_EXTENDEDKEY;

            return new Input
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInputData
                    {
                        VirtualKey = virtualKey,
                        ScanCode = (ushort)MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC),
                        Flags = flags
                    }
                }
            };
        }

        /// <summary>
        /// 가상 키 자리를 비우고 스캔코드로 보낸다. KEYEVENTF_SCANCODE 가 그 뜻이다.
        /// </summary>
        public static Input ScanCodeInput(ushort scanCode, bool extended, bool isKeyUp)
        {
            var flags = KEYEVENTF_SCANCODE;
            if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
            if (isKeyUp) flags |= KEYEVENTF_KEYUP;

            return new Input
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInputData
                    {
                        VirtualKey = 0,
                        ScanCode = scanCode,
                        Flags = flags
                    }
                }
            };
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, Input[] inputs, int size);

        public const uint MAPVK_VK_TO_VSC = 0;

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);


        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out ScreenPoint point);

        [StructLayout(LayoutKind.Sequential)]
        public struct ScreenPoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public uint Type;
            public InputUnion Data;
        }

        // MOUSEINPUT / KEYBDINPUT / HARDWAREINPUT 이 같은 자리를 나눠 쓴다.
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MouseInputData Mouse;
            [FieldOffset(0)] public KeyboardInputData Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInputData
        {
            public int X;
            public int Y;
            public int MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeyboardInputData
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;

            // 키보드 구조체가 마우스 구조체보다 작다. 공용체 크기를 맞추려고 채워 둔다.
            private readonly int _padding;
        }
    }
}
