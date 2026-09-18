using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace Minguk.Tools.Input.Interop;

/// <summary>
/// 사람이 마지막으로 실제로 쓴 마우스·키보드가 어느 장치인지. Raw Input 을 뒤에서 받아(INPUTSINK) 장치 이름을 기억한다.
/// </summary>
/// <remarks>
/// <b>왜</b> - Interception 은 장치 자리(1~10 키보드, 11~20 마우스)로 보낸다. 이 PC 처럼 마우스가 둘(무선 콤보의
/// 마우스 인터페이스 + 실제 쓰는 마우스) 붙어 있으면 첫 자리가 사람이 쓰는 마우스가 아닐 수 있다. 게임은 Raw Input 으로
/// 장치를 구분하므로 안 쓰는 장치로 넣은 움직임은 조용히 무시되기도 한다 - 오버워치에서 SendInput 은 되는데
/// Interception 은 안 됐다(실측). 사람이 마지막으로 움직인 그 장치로 보내면 게임이 보는 것과 같아진다.
///
/// 메시지 전용 창을 만들어 WM_INPUT 을 받는다. 메시지 루프가 있는 스레드(UI)에서 만들어야 한다.
/// 장치 이름은 <c>\\?\HID#VID_046D&amp;PID_C547&amp;MI_00#...</c> 꼴이라, Interception 의 하드웨어 ID 와 VID·PID·MI 로 맞춘다.
/// </remarks>
public sealed class RawInputDeviceTracker : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly HwndSource _source;
    private IntPtr _lastMouse;
    private IntPtr _lastKeyboard;

    public RawInputDeviceTracker()
    {
        _source = new HwndSource(new HwndSourceParameters("Minguk.Tools 입력 장치 추적")
        {
            Width = 0,
            Height = 0,
            ParentWindow = HwndMessage,
            WindowStyle = 0
        });

        _source.AddHook(OnMessage);

        RawInputDevice[] devices =
        [
            new() { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = _source.Handle },   // 마우스
            new() { UsagePage = 1, Usage = 6, Flags = RIDEV_INPUTSINK, Target = _source.Handle }    // 키보드
        ];

        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
            Logger.Warn($"Raw Input 등록에 실패했다. 오류 {Marshal.GetLastWin32Error()}");
    }

    /// <summary>마지막으로 움직인 마우스의 장치 이름. 아직 없으면 null.</summary>
    public string? LastMouseName => DeviceName(_lastMouse);

    /// <summary>마지막으로 누른 키보드의 장치 이름. 아직 없으면 null.</summary>
    public string? LastKeyboardName => DeviceName(_lastKeyboard);

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_INPUT) return IntPtr.Zero;

        var size = (uint)Marshal.SizeOf<RawInputHeader>();

        if (GetRawInputData(lParam, RID_HEADER, out var header, ref size, size) == size)
        {
            if (header.Type == RIM_TYPEMOUSE) _lastMouse = header.Device;
            else if (header.Type == RIM_TYPEKEYBOARD) _lastKeyboard = header.Device;
        }

        return IntPtr.Zero;
    }

    private static string? DeviceName(IntPtr device)
    {
        if (device == IntPtr.Zero) return null;

        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, null, ref size);
        if (size == 0) return null;

        var buffer = new StringBuilder((int)size + 1);
        return GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref size) > 0 ? buffer.ToString() : null;
    }

    /// <summary>
    /// Raw Input 장치 이름과 Interception 하드웨어 ID 가 같은 장치인가. VID·PID 가 같고, 이름에 MI 가 있으면 그것도 같아야 한다.
    /// </summary>
    public static bool SameDevice(string? rawName, string? hardwareId)
    {
        if (string.IsNullOrEmpty(rawName) || string.IsNullOrEmpty(hardwareId)) return false;

        var vidPid = Between(rawName, "VID_", '#', 18);
        if (vidPid is null) return false;

        var upper = hardwareId.ToUpperInvariant();
        if (!upper.Contains("VID_" + vidPid.Split('&')[0].ToUpperInvariant())) return false;

        // "VID_046D&PID_C547&MI_00" 에서 PID 와 MI 를 낱개로 견준다. 없는 것은 안 본다.
        foreach (var part in vidPid.ToUpperInvariant().Split('&'))
        {
            if (part.StartsWith("PID_") || part.StartsWith("MI_"))
                if (!upper.Contains(part)) return false;
        }

        return true;
    }

    /// <summary>"…#VID_046D&amp;PID_C547&amp;MI_00#…" 에서 "046D&amp;PID_C547&amp;MI_00" 을 꺼낸다.</summary>
    private static string? Between(string text, string start, char end, int maxLength)
    {
        var at = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        at += start.Length;
        var stop = text.IndexOf(end, at);
        if (stop < 0) stop = text.Length;

        var value = text[at..stop];
        return value.Length > 0 ? value : null;
    }

    public void Dispose()
    {
        _source.RemoveHook(OnMessage);
        _source.Dispose();
    }

    private const int WM_INPUT = 0x00FF;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RID_HEADER = 0x10000005;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;

    private static readonly IntPtr HwndMessage = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, out RawInputHeader header, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder? data, ref uint size);
}
