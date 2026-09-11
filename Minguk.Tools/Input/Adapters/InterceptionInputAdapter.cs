using System;
using System.Runtime.InteropServices;
using Minguk.Tools.Input.Interop;

namespace Minguk.Tools.Input.Adapters;

/// <summary>
/// Interception 커널 드라이버로 입력을 만든다.
///
/// <see cref="SendInputAdapter"/> 와 결정적으로 다른 점
///   드라이버 스택 아래에서 올라오므로 진짜 장치가 보낸 입력과 구분되지 않는다.
///   주입 입력을 걸러내는 대상(RawInput·DirectInput 을 쓰는 게임, 일부 안티치트)에도 통한다.
///   SendInput 은 그런 곳에서 무시될 수 있다.
///
/// 대신 필요한 것
///   드라이버 설치와 재부팅. 안 되어 있으면 <see cref="IsAvailable"/> 가 false 다.
///   부르는 쪽은 다른 경로로 내려앉아야 한다.
///
/// 키를 스캔코드로 보낸다
///   드라이버가 다루는 것이 스캔코드라 <see cref="IScanCodeInput"/> 이 본래 모습이다.
///   <see cref="PressKey"/> 로 가상 키를 받으면 MapVirtualKey 로 스캔코드로 옮겨 보낸다.
/// </summary>
public sealed class InterceptionInputAdapter : IInputAdapter, IScanCodeInput
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private IntPtr _context;

    public InterceptionInputAdapter()
    {
        try
        {
            _context = InterceptionNative.interception_create_context();

            if (_context == IntPtr.Zero)
            {
                UnavailableReason = "드라이버 컨텍스트를 만들지 못했다. 설치 후 재부팅했는지 확인할 것.";
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            // DLL 미배치 / 비트수 불일치 / 심볼 없음. 앱이 죽을 일은 아니므로 "쓸 수 없음" 으로 다룬다.
            UnavailableReason = ex.Message;
            _context = IntPtr.Zero;
        }

        if (UnavailableReason is not null)
            Logger.Info($"Interception 을 쓸 수 없다: {UnavailableReason}");
    }

    public string Name => "Interception";

    public bool IsAvailable => _context != IntPtr.Zero;

    public string? UnavailableReason { get; }

    /// <summary>커널 입력 큐로 가므로 포커스를 가진 창이 받는다.</summary>
    public bool RequiresForegroundTarget => true;

    public (int X, int Y)? GetCursorPosition() => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    public bool MoveMouseTo(int screenX, int screenY)
    {
        if (!VirtualScreen.TryNormalize(screenX, screenY, out var nx, out var ny))
            return false;

        return SendMouse(
            state: 0,
            flags: (ushort)(InterceptionNative.MouseMoveAbsolute | InterceptionNative.MouseVirtualDesktop),
            x: nx,
            y: ny);
    }

    /// <summary>상대 이동. 드라이버의 기본 이동이 이것이다 - 진짜 마우스가 움직인 것과 구분되지 않는다.</summary>
    public bool MoveMouseBy(int deltaX, int deltaY)
        => SendMouse(state: 0, flags: InterceptionNative.MouseMoveRelative, x: deltaX, y: deltaY);

    public bool PressMouseButton(MouseButton button) => SendMouse(DownState(button));

    public bool ReleaseMouseButton(MouseButton button) => SendMouse(UpState(button));

    /// <summary>누름과 뗌을 한 번에. 부르는 쪽이 중간에 끊겨도 버튼이 눌린 채 남지 않는다.</summary>
    public bool ClickMouseButton(MouseButton button)
        => PressMouseButton(button) && ReleaseMouseButton(button);

    /// <param name="delta">120 이 한 칸(WHEEL_DELTA).</param>
    public bool ScrollWheel(int delta)
        => SendMouse(InterceptionNative.MouseWheel, rolling: (short)delta);

    public bool PressKey(ushort virtualKey) => SendVirtualKey(virtualKey, isKeyUp: false);

    public bool ReleaseKey(ushort virtualKey) => SendVirtualKey(virtualKey, isKeyUp: true);

    public bool PressScanCode(ushort scanCode, bool extended) => SendScanCode(scanCode, extended, isKeyUp: false);

    public bool ReleaseScanCode(ushort scanCode, bool extended) => SendScanCode(scanCode, extended, isKeyUp: true);

    /// <summary>
    /// 가상 키를 스캔코드로 옮겨 보낸다. 드라이버는 스캔코드만 안다.
    /// </summary>
    private bool SendVirtualKey(ushort virtualKey, bool isKeyUp)
    {
        var scanCode = (ushort)MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC);

        if (scanCode == 0)
        {
            Logger.Warn($"가상 키 0x{virtualKey:X2} 에 대응하는 스캔코드가 없다.");
            return false;
        }

        return SendScanCode(scanCode, IsExtendedKey(virtualKey), isKeyUp);
    }

    /// <summary>
    /// E0 확장 플래그가 필요한 가상 키인지.
    /// MapVirtualKey 는 확장 여부를 알려 주지 않으므로 여기서 가려낸다.
    /// 빠뜨리면 방향키가 넘패드 키로 들어간다.
    /// </summary>
    private static bool IsExtendedKey(ushort virtualKey) => virtualKey switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true,   // PageUp PageDown End Home
        0x25 or 0x26 or 0x27 or 0x28 => true,   // Left Up Right Down
        0x2D or 0x2E => true,                   // Insert Delete
        0x5B or 0x5C or 0x5D => true,           // LWin RWin Apps
        0xA3 => true,                           // RControl
        0xA5 => true,                           // RMenu (오른쪽 Alt)
        _ => false
    };

    private bool SendScanCode(ushort scanCode, bool extended, bool isKeyUp)
    {
        if (!IsAvailable) return false;

        var state = isKeyUp ? InterceptionNative.KeyUp : InterceptionNative.KeyDown;
        if (extended) state |= InterceptionNative.KeyE0;

        var stroke = new InterceptionNative.Stroke
        {
            Key = new InterceptionNative.KeyStroke { Code = scanCode, State = state }
        };

        return Send(InterceptionNative.KeyboardFirst, ref stroke);
    }

    private static ushort DownState(MouseButton button) => button switch
    {
        MouseButton.Right => InterceptionNative.MouseRightDown,
        MouseButton.Middle => InterceptionNative.MouseMiddleDown,
        _ => InterceptionNative.MouseLeftDown
    };

    private static ushort UpState(MouseButton button) => button switch
    {
        MouseButton.Right => InterceptionNative.MouseRightUp,
        MouseButton.Middle => InterceptionNative.MouseMiddleUp,
        _ => InterceptionNative.MouseLeftUp
    };

    private bool SendMouse(ushort state, ushort flags = 0, short rolling = 0, int x = 0, int y = 0)
    {
        if (!IsAvailable) return false;

        var stroke = new InterceptionNative.Stroke
        {
            Mouse = new InterceptionNative.MouseStroke
            {
                State = state,
                Flags = flags,
                Rolling = rolling,
                X = x,
                Y = y
            }
        };

        return Send(InterceptionNative.MouseFirst, ref stroke);
    }

    /// <summary>
    /// 드라이버로 스트로크 하나를 보낸다.
    ///
    /// 보낸 개수가 1 이 아니면 드라이버가 받지 않은 것이다. 조용히 넘기면
    /// "입력이 안 들어간다" 는 것만 보이고 이유를 알 수 없어서 남긴다.
    /// </summary>
    private bool Send(int device, ref InterceptionNative.Stroke stroke)
    {
        var sent = InterceptionNative.interception_send(_context, device, ref stroke, 1);

        if (sent == 1) return true;

        Logger.Warn($"Interception 이 스트로크를 받지 않았다. 보낸 것 {sent}/1, 디바이스 {device}");
        return false;
    }

    public void Dispose()
    {
        if (_context == IntPtr.Zero) return;

        InterceptionNative.interception_destroy_context(_context);
        _context = IntPtr.Zero;
    }

    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
