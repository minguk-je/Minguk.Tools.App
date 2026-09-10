using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Minguk.Tools.Input.Korean;

/// <summary>한/영 전환을 어떤 입력으로 보낼지 결정하는 방식.</summary>
public enum HangulKeyMode
{
    /// <summary>한글 103/106키: 전용 스캔코드 0xF2 (메이크 온리).</summary>
    HangulScanCode = 0,

    /// <summary>한글 101키 종류 1·3: 오른쪽 Alt (E0 0x38).</summary>
    RightAlt = 1,

    /// <summary>한글 101키 종류 2: Shift + Space.</summary>
    ShiftSpace = 2
}

/// <summary>
/// Windows에 설정된 키보드 종류(Type/Subtype)를 조회해 한/영 전환 방식을 판별합니다.
/// 키보드가 보내는 스캔코드 자체는 모델과 무관하게 0xF2로 통일되어 있지만,
/// 커널이 "무엇을 한/영으로 해석할지"는 이 Subtype 설정이 결정합니다.
/// </summary>
public static class KoreanKeyboardInfo
{
    /// <summary>한국어 키보드의 Type 값.</summary>
    public const int KoreanKeyboardType = 8;

    [DllImport("user32.dll")]
    private static extern int GetKeyboardType(int nTypeFlag);

    /// <param name="Type">GetKeyboardType(0). 한국어 키보드면 8.</param>
    /// <param name="Subtype">GetKeyboardType(1). 3=103/106키, 4~6=101키 종류 1~3.</param>
    public readonly record struct Detection(int Type, int Subtype, HangulKeyMode Mode, string Description)
    {
        public bool IsKorean => Type == KoreanKeyboardType;
    }

    public static Detection Detect()
    {
        int type = GetKeyboardType(0);
        int subtype = GetKeyboardType(1);
        string source = "API";

        // USB(HID) 키보드는 subtype을 0(미지정)으로 보고하는 경우가 많습니다.
        // i8042prt의 재정의 값은 PS/2 키보드에만 적용되므로 API가 0을 주면
        // kbdhid -> i8042prt 순으로 레지스트리 재정의 값을 참고합니다.
        if (subtype == 0)
        {
            int overridden = ReadOverride("OverrideKeyboardSubtype");
            if (overridden != 0)
            {
                subtype = overridden;
                source = "레지스트리";
            }
        }

        if (type == 0)
        {
            type = ReadOverride("OverrideKeyboardType");
        }

        var (mode, name) = subtype switch
        {
            3 => (HangulKeyMode.HangulScanCode, "한글 103/106키"),
            4 => (HangulKeyMode.RightAlt, "한글 101키 (종류 1)"),
            5 => (HangulKeyMode.ShiftSpace, "한글 101키 (종류 2)"),
            6 => (HangulKeyMode.RightAlt, "한글 101키 (종류 3)"),
            0 => (HangulKeyMode.HangulScanCode, "종류 미지정 (103/106키 기본 동작)"),
            _ => (HangulKeyMode.HangulScanCode, "알 수 없는 종류")
        };

        // 한국어 키보드로 설정되지 않았다면 어떤 방식을 써도 IME가 반응하지 않습니다.
        var description = type == KoreanKeyboardType
            ? $"{name} · Type {type} / Subtype {subtype} ({source})"
            : $"한국어 키보드 아님 · Type {type} / Subtype {subtype} ({source})";

        return new Detection(type, subtype, mode, description);
    }

    // ─────────────── 현재 IME 상태 (한글 입력 중인지) ───────────────

    private const int WM_IME_CONTROL = 0x0283;
    private const int IMC_GETCONVERSIONMODE = 0x0001;
    private const int IMC_SETCONVERSIONMODE = 0x0002;
    private const int IME_CMODE_NATIVE = 0x0001;   // 한글 모드 비트
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hwnd);

    /// <summary>
    /// 입력이 실제로 들어갈 창. 최상위 창이 아니라 그 안에서 포커스를 가진 창이다.
    /// </summary>
    /// <remarks>
    /// 최상위 창으로 읽으면 안 되는 이유
    ///   Win32·WinForms 는 컨트롤마다 HWND 가 따로다. 메모장이 그렇다.
    ///   최상위 창에 물으면 IME 상태가 늘 0(영문)으로 돌아온다 - 실제로 한글 모드여도 그렇다.
    ///   그 값을 믿으면 이미 한글인데도 한/영 을 눌러 영문으로 뒤집고, 글자마다 이것을 반복해
    ///   "안sud하tp요" 처럼 한 글자 걸러 한 글자가 영문으로 찍힌다.
    ///
    ///   WPF 는 창 하나가 전부라 최상위와 포커스 창이 같다. 그래서 WPF 대상만 보면 멀쩡해 보인다.
    /// </remarks>
    private static IntPtr GetFocusedWindow()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return IntPtr.Zero;

        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };

        // AttachThreadInput 없이도 다른 스레드의 포커스 창을 알 수 있다.
        if (!GetGUIThreadInfo(GetWindowThreadProcessId(foreground, IntPtr.Zero), ref info))
            return foreground;

        return info.FocusWindow != IntPtr.Zero ? info.FocusWindow : foreground;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr ActiveWindow;
        public IntPtr FocusWindow;
        public IntPtr CaptureWindow;
        public IntPtr MenuOwnerWindow;
        public IntPtr MoveSizeWindow;
        public IntPtr CaretWindow;
        public Rect CaretRect;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    /// <summary>
    /// 지금 입력 포커스를 가진 창이 한글 입력 모드인지 확인합니다.
    /// 창마다 IME 상태가 따로이므로 "입력이 들어갈 창"을 기준으로 읽습니다.
    /// </summary>
    /// <returns>상태를 읽지 못하면 false (판별 불가).</returns>
    public static bool TryGetForegroundHangulMode(out bool isHangul)
    {
        isHangul = false;

        IntPtr target = GetFocusedWindow();
        if (target == IntPtr.Zero) return false;

        // IME 상태는 창의 기본 IME 윈도우에 물어봐야 하며, 다른 프로세스에도 통합니다.
        IntPtr ime = ImmGetDefaultIMEWnd(target);
        if (ime == IntPtr.Zero) return false;

        // 응답 없는 창에 걸려 루프가 멈추지 않도록 타임아웃을 둡니다.
        if (SendMessageTimeout(ime, WM_IME_CONTROL, IMC_GETCONVERSIONMODE, IntPtr.Zero,
                               SMTO_ABORTIFHUNG, 300, out IntPtr result) == IntPtr.Zero)
        {
            return false;
        }

        isHangul = (result.ToInt32() & IME_CMODE_NATIVE) != 0;
        return true;
    }

    /// <summary>
    /// 정해진 창의 IME 가 한글 모드인지 읽습니다.
    /// </summary>
    /// <remarks>
    /// 포커스가 아니라 <b>창을 지정해</b> 읽습니다. 창 메시지 경로는 대상을 직접 정하므로
    /// 포커스를 기준으로 읽으면 엉뚱한 창의 상태를 보게 됩니다.
    /// </remarks>
    public static bool TryGetHangulMode(IntPtr window, out bool isHangul)
    {
        isHangul = false;

        if (window == IntPtr.Zero) return false;

        IntPtr ime = ImmGetDefaultIMEWnd(window);
        if (ime == IntPtr.Zero) return false;

        if (SendMessageTimeout(ime, WM_IME_CONTROL, IMC_GETCONVERSIONMODE, IntPtr.Zero,
                               SMTO_ABORTIFHUNG, 300, out IntPtr result) == IntPtr.Zero)
        {
            return false;
        }

        isHangul = (result.ToInt32() & IME_CMODE_NATIVE) != 0;
        return true;
    }

    /// <summary>
    /// 정해진 창의 IME 를 한글/영문 모드로 <b>바꿉니다</b>.
    /// </summary>
    /// <remarks>
    /// 한/영 키를 누르는 것과 결과는 같지만 경로가 다릅니다. 키는 커널 입력 큐를 거쳐야
    /// 하므로 스캔코드를 넣을 수 있는 경로에서만 되는데, 이것은 창의 기본 IME 윈도우에
    /// 메시지를 보내는 것이라 창 메시지 경로에서도 됩니다.
    ///
    /// <c>WM_INPUTLANGCHANGE</c> 와 혼동하지 마십시오. 그쪽은 <b>입력 언어</b>(자판)를 바꾸는
    /// 것이고, 이것은 그 언어 안에서의 <b>변환 모드</b>(한글이냐 영문이냐)입니다.
    /// 한국어 자판을 쓰는 중에 한/영 을 누르는 것은 후자입니다.
    ///
    /// 이 메시지는 답을 받아야 하므로 부치면 안 되고 보내야 합니다. 대상이 멈춰 있을 때를
    /// 대비해 타임아웃을 둡니다.
    /// </remarks>
    /// <returns>바꿨으면 true. 창이 IME 를 안 쓰거나 답이 없으면 false.</returns>
    public static bool TrySetHangulMode(IntPtr window, bool hangul)
    {
        if (window == IntPtr.Zero) return false;

        IntPtr ime = ImmGetDefaultIMEWnd(window);
        if (ime == IntPtr.Zero) return false;

        // 지금 값을 읽어 NATIVE 비트만 켜고 끕니다. 통째로 덮어쓰면 전각/한자 같은
        // 다른 비트가 함께 날아갑니다.
        if (SendMessageTimeout(ime, WM_IME_CONTROL, IMC_GETCONVERSIONMODE, IntPtr.Zero,
                               SMTO_ABORTIFHUNG, 300, out IntPtr current) == IntPtr.Zero)
        {
            return false;
        }

        int mode = current.ToInt32();
        int wanted = hangul ? mode | IME_CMODE_NATIVE : mode & ~IME_CMODE_NATIVE;

        if (wanted == mode) return true;

        return SendMessageTimeout(ime, WM_IME_CONTROL, IMC_SETCONVERSIONMODE, (IntPtr)wanted,
                                  SMTO_ABORTIFHUNG, 300, out _) != IntPtr.Zero;
    }

    public static string ModeLabel(HangulKeyMode mode) => mode switch
    {
        HangulKeyMode.RightAlt => "오른쪽 Alt",
        HangulKeyMode.ShiftSpace => "Shift+Space",
        _ => "한/영 키 (0xF2)"
    };

    /// <summary>키보드 종류 재정의 값이 들어가는 서비스 키. USB(kbdhid)를 먼저 확인합니다.</summary>
    private static readonly string[] ParameterKeys =
    [
        @"SYSTEM\CurrentControlSet\Services\kbdhid\Parameters",   // USB / HID 키보드
        @"SYSTEM\CurrentControlSet\Services\i8042prt\Parameters"  // PS/2 키보드
    ];

    private static int ReadOverride(string valueName)
    {
        foreach (var path in ParameterKeys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);

                if (key?.GetValue(valueName) is int value && value != 0)
                {
                    return value;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                // 접근 권한이 없는 키는 건너뜁니다.
            }
        }

        return 0;
    }
}
