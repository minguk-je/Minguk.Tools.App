using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Minguk.Tools.Capture.Input;

/// <summary>
/// 캡처 대상이 화면의 어디를 차지하는지 알아낸다.
///
/// 미리보기에서 누른 자리를 실제 입력으로 바꾸려면 "캡처된 그림의 (x, y) 가
/// 화면 좌표로는 어디인가" 를 알아야 한다. 그 기준 사각형을 여기서 구한다.
///
/// 모니터와 창은 구하는 방법이 다르다.
///   모니터 : GetMonitorInfo 가 주는 화면상의 사각형 그대로다.
///   창     : GetWindowRect 가 아니라 DWM 의 확장 프레임 경계를 쓴다.
///            Windows 10 부터 창 주위에 보이지 않는 여백이 붙어 있어서
///            GetWindowRect 는 실제로 보이는 것보다 큰 사각형을 준다.
///            WGC 가 캡처하는 범위는 DWM 경계 쪽에 맞다.
/// </summary>
public static class CaptureTargetBounds
{
    /// <summary>대상이 차지하는 화면 사각형. 구하지 못하면 false.</summary>
    public static bool TryGet(CaptureTarget target, out Rect bounds)
    {
        bounds = default;

        // 영상은 화면 자리가 없다 - 영상 픽셀 그대로(0,0 에서 시작)를 자리로 삼는다. 검출 좌표가 영상 픽셀로 나온다.
        // 입력은 이 자리로 보내지 않는다(PreviewInputRouter·실시간 스크립트가 영상이면 막는다) - 보내면 진짜 화면 왼쪽 위를 누른다.
        if (target.Kind == CaptureTargetKind.Video)
        {
            if (target.FilePath is null || !VideoFileCaptureSession.TryGetFrameSize(target.FilePath, out var width, out var height))
                return false;

            bounds = new Rect(0, 0, width, height);
            return true;
        }

        if (target.Handle == IntPtr.Zero)
            return false;

        return target.Kind == CaptureTargetKind.Monitor
            ? TryGetMonitorBounds(target.Handle, out bounds)
            : TryGetWindowBounds(target.Handle, out bounds);
    }

    private static bool TryGetMonitorBounds(IntPtr monitorHandle, out Rect bounds)
    {
        bounds = default;

        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref info))
            return false;

        bounds = ToRect(info.Monitor);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool TryGetWindowBounds(IntPtr windowHandle, out Rect bounds)
    {
        bounds = default;

        // 먼저 DWM 경계를 물어본다. 실패하면(구형 창 등) 창 사각형으로 물러난다.
        if (NativeMethods.DwmGetWindowAttribute(
                windowHandle,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out var frame,
                Marshal.SizeOf<NativeMethods.Rect>()) == 0)
        {
            bounds = ToRect(frame);
        }
        else if (NativeMethods.GetWindowRect(windowHandle, out var windowRect))
        {
            bounds = ToRect(windowRect);
        }

        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static Rect ToRect(NativeMethods.Rect rect)
        => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static class NativeMethods
    {
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo info);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr windowHandle, int attribute, out Rect value, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }
    }
}
