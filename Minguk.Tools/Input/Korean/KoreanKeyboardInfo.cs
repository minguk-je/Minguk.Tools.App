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
    private const int IME_CMODE_NATIVE = 0x0001;   // 한글 모드 비트
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hwnd);

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

        IntPtr target = GetForegroundWindow();
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
