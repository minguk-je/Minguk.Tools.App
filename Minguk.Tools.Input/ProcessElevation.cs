using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Minguk.Tools.Input;

/// <summary>
/// 누가 관리자 권한(승격)으로 떠 있는지. 이 앱과 대상 창을 견줘 입력이 막힐지 미리 안다.
/// </summary>
/// <remarks>
/// <b>왜</b> - Windows 는 낮은 무결성의 프로세스가 높은 쪽에 입력을 넣지 못하게 막는다(UIPI). 대상 게임이
/// 관리자로 떠 있고 이 앱은 아니면 SendInput 은 조용히 버려지고, RegisterHotKey 로 쥔 전역 단축키(F5)도
/// 그 창이 앞에 있는 동안은 오지 않는다. 오버워치는 관리자로 떠 있었다(실측) - 앱을 일반 권한으로 띄우면 이 경우다.
/// 드라이버(Interception)는 입력은 넣지만 단축키는 여전히 안 온다.
/// </remarks>
public static class ProcessElevation
{
    /// <summary>이 앱이 관리자로 떠 있는가.</summary>
    public static bool IsCurrentElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// 그 창의 프로세스가 관리자로 떠 있는가. 모르면 null - 열어 볼 수조차 없으면 더 높은 쪽이라 보고 true 를 준다.
    /// </summary>
    public static bool? IsWindowElevated(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return null;

        return IsProcessElevated(processId);
    }

    /// <summary>그 프로세스가 관리자로 떠 있는가. 열어 볼 수조차 없으면 더 높은 쪽이라 보고 true.</summary>
    public static bool? IsProcessElevated(uint processId)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return true;

        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token))
                return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED ? true : null;

            try
            {
                return GetTokenInformation(token, TokenElevation, out var elevation, sizeof(uint), out _) ? elevation != 0 : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>같은 실행 파일을 관리자로 다시 띄운다. 사용자가 UAC 에서 거절하면 false.</summary>
    public static bool TryRestartElevated(out string? problem)
    {
        problem = null;

        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            problem = "실행 파일 경로를 모르겠습니다.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            problem = "관리자 권한 요청을 취소했습니다.";
            return false;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_CANCELLED = 1223;

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, out uint information, uint length, out uint returned);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
