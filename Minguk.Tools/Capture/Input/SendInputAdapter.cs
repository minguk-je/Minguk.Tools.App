using System;
using System.Runtime.InteropServices;

namespace Minguk.Tools.Capture.Input;

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
public sealed class SendInputAdapter : IInputAdapter
{
    public string Name => "SendInput";

    /// <summary>
    /// 마우스를 화면 절대 좌표로 옮긴다.
    ///
    /// SendInput 의 절대 좌표는 픽셀이 아니라 0~65535 로 정규화된 값이다.
    /// 여러 모니터를 함께 쓰려면 가상 화면(모든 모니터를 감싸는 사각형) 기준으로 환산해야 한다.
    /// </summary>
    public void MoveMouseTo(int screenX, int screenY)
    {
        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        if (virtualWidth <= 0 || virtualHeight <= 0)
            return;

        // -1 을 빼는 이유: 65535 는 마지막 픽셀의 "오른쪽 끝" 이라 그대로 쓰면 한 칸 넘어간다.
        var normalizedX = (int)Math.Round((screenX - virtualLeft) * 65535.0 / (virtualWidth - 1));
        var normalizedY = (int)Math.Round((screenY - virtualTop) * 65535.0 / (virtualHeight - 1));

        Send(NativeMethods.MouseInput(
            NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
            normalizedX,
            normalizedY));
    }

    public void PressMouseButton(MouseButton button) => Send(NativeMethods.MouseInput(DownFlag(button)));

    public void ReleaseMouseButton(MouseButton button) => Send(NativeMethods.MouseInput(UpFlag(button)));

    /// <summary>누르고 떼기를 한 번에. 두 이벤트를 한 번의 SendInput 으로 보내 사이에 끼어들 틈을 줄인다.</summary>
    public void ClickMouseButton(MouseButton button)
        => Send(NativeMethods.MouseInput(DownFlag(button)), NativeMethods.MouseInput(UpFlag(button)));

    /// <summary>휠. <paramref name="delta"/> 는 120 이 한 칸이다(WHEEL_DELTA).</summary>
    public void ScrollWheel(int delta)
        => Send(NativeMethods.MouseInput(NativeMethods.MOUSEEVENTF_WHEEL, mouseData: delta));

    public void PressKey(ushort virtualKey) => Send(NativeMethods.KeyInput(virtualKey, isKeyUp: false));

    public void ReleaseKey(ushort virtualKey) => Send(NativeMethods.KeyInput(virtualKey, isKeyUp: true));

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

    private static void Send(params NativeMethods.Input[] inputs)
        => NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());

    private static class NativeMethods
    {
        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

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

        public const uint KEYEVENTF_KEYUP = 0x0002;

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

        public static Input KeyInput(ushort virtualKey, bool isKeyUp) => new()
        {
            Type = INPUT_KEYBOARD,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = virtualKey,
                    Flags = isKeyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, Input[] inputs, int size);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

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
