using System;
using System.Collections.Generic;
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
        {
            Logger.Info($"Interception 을 쓸 수 없다: {UnavailableReason}");
            return;
        }

        _firstKeyboard = FirstAttached(InterceptionNative.KeyboardFirst, InterceptionNative.MaxKeyboard) ?? InterceptionNative.KeyboardFirst;
        _firstMouse = FirstAttached(InterceptionNative.MouseFirst, InterceptionNative.MaxMouse) ?? InterceptionNative.MouseFirst;

        // 사람이 마지막으로 쓴 장치를 따라가려면 Raw Input 을 받을 창이 필요하다 - 메시지 루프가 있는 UI 스레드에서만.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is not null && dispatcher.CheckAccess())
        {
            try { _tracker = new RawInputDeviceTracker(); }
            catch (Exception ex) { Logger.Warn(ex, "입력 장치 추적을 못 켰다. 붙은 첫 자리로 보낸다."); }
        }

        Logger.Info($"Interception 장치: {DescribeDevices()}");
    }

    private readonly int _firstKeyboard = InterceptionNative.KeyboardFirst;
    private readonly int _firstMouse = InterceptionNative.MouseFirst;
    private readonly RawInputDeviceTracker? _tracker;
    private int _lastKeyboardDevice;
    private int _lastMouseDevice;

    /// <summary>키 스트로크를 보내는 자리(1~10). 사람이 마지막으로 누른 키보드, 모르면 붙은 첫 자리.</summary>
    public int KeyboardDevice => Resolve(_tracker?.LastKeyboardName, InterceptionNative.KeyboardFirst, InterceptionNative.MaxKeyboard, _firstKeyboard, ref _lastKeyboardDevice);

    /// <summary>마우스 스트로크를 보내는 자리(11~20). 사람이 마지막으로 움직인 마우스, 모르면 붙은 첫 자리.</summary>
    public int MouseDevice => Resolve(_tracker?.LastMouseName, InterceptionNative.MouseFirst, InterceptionNative.MaxMouse, _firstMouse, ref _lastMouseDevice);

    /// <summary>
    /// 사람이 마지막으로 쓴 장치의 자리. 게임은 Raw Input 으로 장치를 구분하므로, 사람이 쓰는 바로 그 장치로 넣어야 게임이 본다.
    /// </summary>
    /// <remarks>
    /// 마우스가 둘 붙은 PC 에서 첫 자리(무선 콤보의 마우스 인터페이스)로 보냈더니 오버워치가 무시했다(실측 - SendInput 은 됐다).
    /// 자리가 바뀌면 한 번 남긴다 - 매 스트로크마다 적으면 로그가 넘친다.
    /// </remarks>
    private int Resolve(string? rawName, int first, int count, int fallback, ref int remembered)
    {
        var chosen = fallback;

        if (rawName is not null)
        {
            for (var device = first; device < first + count; device++)
            {
                if (RawInputDeviceTracker.SameDevice(rawName, InterceptionNative.HardwareId(_context, device)))
                {
                    chosen = device;
                    break;
                }
            }
        }

        if (chosen != remembered)
        {
            remembered = chosen;
            Logger.Info($"Interception 보낼 자리 {chosen} ({(rawName is null ? "아직 사람 입력 없음 - 붙은 첫 자리" : "사람이 마지막으로 쓴 장치")})");
        }

        return chosen;
    }

    /// <summary>
    /// 장치가 붙은 첫 자리. 없으면 null.
    /// </summary>
    /// <remarks>
    /// 드라이버는 자리(1~10 키보드, 11~20 마우스)마다 장치 개체를 미리 만들어 두고, 실제 장치가 붙을 때 그 자리에
    /// 연결한다. 빈 자리로 보내면 드라이버는 받았다고 하지만(보낸 수 1) 아래에 넘겨 줄 장치가 없어 아무 일도
    /// 안 일어난다 - 오버워치에서 조준·걷기가 조용히 안 먹은 것이 이것이다(실측: 키보드 5개·마우스 2개가 붙은 PC).
    /// 하드웨어 ID 가 있는 자리가 붙은 자리다.
    /// </remarks>
    private int? FirstAttached(int first, int count)
    {
        for (var device = first; device < first + count; device++)
            if (InterceptionNative.HardwareId(_context, device) is not null) return device;

        return null;
    }

    /// <summary>
    /// 사람이 마우스를 움직이는 것을 본 적이 있는가. 아니면 붙은 첫 자리로 보내는데, 그 자리가 게임이 보는 마우스가
    /// 아닐 수 있다 - 실제로 아무 일도 안 일어났다(실측).
    /// </summary>
    public bool SawHumanMouse => _tracker?.LastMouseName is not null;

    /// <summary>자리마다 무엇이 붙었는지. 진단용.</summary>
    public string DescribeDevices()
    {
        if (!IsAvailable) return "(드라이버 없음)";

        var parts = new List<string>();

        for (var device = InterceptionNative.KeyboardFirst; device < InterceptionNative.MouseFirst + InterceptionNative.MaxMouse; device++)
        {
            var id = InterceptionNative.HardwareId(_context, device);
            if (id is null) continue;

            var role = device < InterceptionNative.MouseFirst ? "키보드" : "마우스";
            var chosen = device == KeyboardDevice || device == MouseDevice ? "*" : string.Empty;
            parts.Add($"{device}{chosen}={role} {id}");
        }

        return parts.Count == 0 ? "붙은 장치 없음" : string.Join(" · ", parts);
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

        return SendScanCode(scanCode, VirtualKeys.IsExtendedKey(virtualKey), isKeyUp);
    }

    private bool SendScanCode(ushort scanCode, bool extended, bool isKeyUp)
    {
        if (!IsAvailable) return false;

        var state = isKeyUp ? InterceptionNative.KeyUp : InterceptionNative.KeyDown;
        if (extended) state |= InterceptionNative.KeyE0;

        var stroke = new InterceptionNative.Stroke
        {
            Key = new InterceptionNative.KeyStroke { Code = scanCode, State = state }
        };

        return Send(KeyboardDevice, ref stroke);
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

        return Send(MouseDevice, ref stroke);
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
        _tracker?.Dispose();

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
