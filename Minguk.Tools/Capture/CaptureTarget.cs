using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Minguk.Base.Utilities;

namespace Minguk.Tools.Capture;

/// <summary>캡처 대상의 종류.</summary>
public enum CaptureTargetKind
{
    /// <summary>창 하나(HWND). 게임이 창/테두리 없는 창 모드일 때 쓴다.</summary>
    Window,

    /// <summary>모니터 하나(HMONITOR). 독점 전체화면까지 확실히 잡으려면 이쪽이다.</summary>
    Monitor,

    /// <summary>
    /// 녹화한 영상 파일(<see cref="CaptureTarget.FilePath"/>). 게임 창 대신 영상 프레임이 흐른다 - 몹 찾기·글자 읽기·스크립트 흐름 시험용.
    /// 입력은 받지 않는다(<see cref="Capture.Input.InputForwardResult.VideoTarget"/>). 화면 자리가 없어 좌표는 영상 픽셀 그대로다.
    /// </summary>
    Video
}

/// <summary>
/// WGC 가 캡처할 대상 하나.
///
/// 창과 모니터를 같은 타입으로 다루는 이유는, 게임이 전체화면으로 넘어가면
/// 창 캡처가 멈추거나 검은 화면이 되는 경우가 있어서 모니터 캡처로 갈아타야 하기 때문이다.
/// (<see cref="WgcCaptureSession"/> 의 자동 폴백 참조)
/// </summary>
public sealed class CaptureTarget
{
    public required CaptureTargetKind Kind { get; init; }

    /// <summary><see cref="CaptureTargetKind.Window"/> 면 HWND, <see cref="CaptureTargetKind.Monitor"/> 면 HMONITOR. 영상이면 0.</summary>
    public required IntPtr Handle { get; init; }

    public required string Title { get; init; }

    /// <summary>창일 때만 채운다. 어느 게임인지 눈으로 고르라고 붙여 둔다.</summary>
    public string? ProcessName { get; init; }

    /// <summary>영상일 때만 채운다 - 틀 파일의 전체 경로.</summary>
    public string? FilePath { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>
    /// 같은 대상인지 가르는 열쇠. 창·모니터는 핸들, 영상은 경로(핸들이 모두 0 이라 핸들로 가르면 영상끼리 세션이 섞인다).
    /// </summary>
    public string Key => Kind == CaptureTargetKind.Video
        ? $"Video:{FilePath?.ToUpperInvariant()}"
        : $"{Kind}:{Handle}";

    /// <summary>
    /// 콤보에 보이는 글. 저장하는 선택도 이것이라 영상은 크기를 넣지 않는다 - 목록을 만들 때 파일을 열어 크기를 읽지 않으려고.
    /// </summary>
    public string Display => Kind switch
    {
        CaptureTargetKind.Window => $"[창] {Title}  ({ProcessName}, {Width}×{Height})",
        CaptureTargetKind.Video => $"[영상] {Title}",
        _ => $"[모니터] {Title}  ({Width}×{Height})"
    };

    public override string ToString() => Display;

    /// <summary>
    /// 캡처할 만한 최상위 창 목록.
    ///
    /// 걸러 내는 것들:
    ///   - 보이지 않는 창, 제목 없는 창, 자식/소유된 창
    ///   - WS_EX_TOOLWINDOW (툴 팔레트류)
    ///   - DWM 이 cloak 한 창 — UWP 앱의 유령 창이 여기 걸린다. 이걸 안 거르면 목록이 쓰레기로 찬다.
    /// </summary>
    public static List<CaptureTarget> EnumerateWindows()
    {
        var result = new List<CaptureTarget>();
        var self = Process.GetCurrentProcess().Id;

        WinApi32.EnumWindows((hWnd, _) =>
        {
            if (!WinApi32.IsWindowVisible(hWnd))
                return true;

            if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
                return true;

            var exStyle = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true;

            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            var title = WinApi32.GetWindowText(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            var rect = default(WinApi32.Rect);
            if (!WinApi32.GetWindowRect(hWnd, out rect) || rect.Width <= 1 || rect.Height <= 1)
                return true;

            // 자기 자신은 캡처 대상에서 뺀다. 자기 화면을 캡처하면 거울 되먹임이 생긴다.
            WinApi32.GetWindowThreadProcessId(hWnd, out int pid);
            if (pid == self)
                return true;

            result.Add(new CaptureTarget
            {
                Kind = CaptureTargetKind.Window,
                Handle = hWnd,
                Title = title,
                ProcessName = SafeProcessName(pid),
                Width = rect.Width,
                Height = rect.Height
            });

            return true;
        }, 0);

        return result;
    }

    /// <summary>연결된 모니터 목록.</summary>
    public static List<CaptureTarget> EnumerateMonitors()
    {
        var result = new List<CaptureTarget>();
        var index = 0;

        // 델리게이트를 지역 변수로 잡아 두지 않으면 열거 도중 GC 가 가져갈 수 있다.
        WinApi32.MonitorEnumDelegate callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref WinApi32.Rect rect, int data) =>
        {
            index++;
            result.Add(new CaptureTarget
            {
                Kind = CaptureTargetKind.Monitor,
                Handle = hMonitor,
                Title = $"디스플레이 {index}",
                Width = rect.Width,
                Height = rect.Height
            });

            return true;
        };

        WinApi32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, 0);
        GC.KeepAlive(callback);

        return result;
    }

    /// <summary>
    /// 폴더의 녹화 영상(mp4) - 새것부터. 폴더가 없으면 빈 목록. 파일은 열지 않는다(크기는 틀 때 안다).
    /// </summary>
    public static List<CaptureTarget> EnumerateVideos(string? folder)
    {
        var result = new List<CaptureTarget>();

        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
            return result;

        foreach (var file in new System.IO.DirectoryInfo(folder).EnumerateFiles("*.mp4").OrderByDescending(file => file.LastWriteTimeUtc))
        {
            result.Add(new CaptureTarget
            {
                Kind = CaptureTargetKind.Video,
                Handle = IntPtr.Zero,
                Title = file.Name,
                FilePath = file.FullName
            });
        }

        return result;
    }

    /// <summary>창 하나가 올라가 있는 모니터. 전체화면에서 창 캡처가 죽었을 때 갈아탈 대상이다.</summary>
    public static CaptureTarget? MonitorOf(IntPtr hWnd)
    {
        var hMonitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
            return null;

        foreach (var monitor in EnumerateMonitors())
        {
            if (monitor.Handle == hMonitor)
                return monitor;
        }

        return null;
    }

    private static string SafeProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            // 열거하는 사이에 죽은 프로세스. 목록 한 줄 때문에 예외를 띄울 일은 아니다.
            return "?";
        }
    }

    /// <summary>WinApi32 에 없는 것만 여기에 둔다.</summary>
    private static class NativeMethods
    {
        public const int GW_OWNER = 4;
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const int DWMWA_CLOAKED = 14;
        public const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, int dwFlags);

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    }
}
