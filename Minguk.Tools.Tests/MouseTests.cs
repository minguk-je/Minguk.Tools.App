using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Minguk.Tools.Input;

namespace Minguk.Tools.Tests;

internal static partial class Program
{
    /// <summary>
    /// 붙어 있는 모든 모니터의 모서리·중앙에 정확히 착지하는지. 끝나면 커서를 원래 자리로 돌려놓는다.
    /// </summary>
    private static async Task TestMouseMoveAsync()
    {
        if (_adapter.GetCursorPosition() is not { } origin)
        {
            Fail("마우스 이동", "커서 좌표를 읽지 못했다");
            return;
        }

        var screen = VirtualScreen.GetBounds();
        var displays = EnumerateMonitors();

        Console.WriteLine($"  가상 화면: X {screen.Left}~{screen.Right - 1}, Y {screen.Top}~{screen.Bottom - 1} "
                          + $"({screen.Width}x{screen.Height}), 모니터 {displays.Count}대");

        // 실제로 존재하는 모니터의 모서리·중앙만 검증한다.
        // 가상 화면 사각형의 모서리는 어느 모니터에도 속하지 않는 빈 공간일 수 있고,
        // 그런 좌표에 커서를 놓을 수 없는 것은 Windows 의 정상 동작이다.
        List<(string Name, int X, int Y)> targets = [];

        for (var i = 0; i < displays.Count; i++)
        {
            var (left, top, right, bottom) = displays[i];
            var tag = $"모니터{i + 1}";

            targets.Add(($"{tag} 좌상", left, top));
            targets.Add(($"{tag} 우하", right - 1, bottom - 1));
            targets.Add(($"{tag} 중앙", left + (right - left) / 2, top + (bottom - top) / 2));
        }

        var worst = 0;
        var offenders = new List<string>();
        var unreachable = new List<string>();
        var centerX = screen.Left + screen.Width / 2;
        var centerY = screen.Top + screen.Height / 2;

        foreach (var (name, x, y) in targets)
        {
            // 매번 같은 출발점에서 시작해야 직전 위치의 영향을 배제할 수 있다.
            _adapter.MoveMouseTo(centerX, centerY);
            await Task.Delay(60);

            var exact = await _service.MoveToExactAsync(x, y);
            var got = _adapter.GetCursorPosition() ?? (0, 0);

            var error = Math.Max(Math.Abs(got.Item1 - x), Math.Abs(got.Item2 - y));
            var note = exact ? "" : "  <- 표시 영역 밖(되튕김)";

            Console.WriteLine($"  {name,-18} 목표({x},{y}) -> 실제({got.Item1},{got.Item2})  오차 {error}px{note}");

            if (exact)
            {
                worst = Math.Max(worst, error);
                if (error > 0) offenders.Add($"{name} {error}px");
            }
            else
            {
                unreachable.Add(name);
            }
        }

        Check($"절대 좌표 이동 (모니터 {displays.Count}대, {targets.Count}개 지점)",
              offenders.Count == 0,
              offenders.Count == 0
                  ? $"도달 가능한 지점 모두 오차 {worst}px"
                    + (unreachable.Count == 0 ? "" : $" / 표시 영역 밖 {unreachable.Count}곳: {string.Join(", ", unreachable)}")
                  : string.Join(", ", offenders));

        // 부드러운 이동: 중간 단계를 거쳐도 목적지에 정확히 도착해야 한다.
        _service.MoveSpeedPxPerSec = 4000;
        var smoothX = screen.Left + screen.Width / 4;
        var smoothY = screen.Top + screen.Height / 4;

        await _service.MoveSmoothAsync(smoothX, smoothY);
        var end = _adapter.GetCursorPosition() ?? (0, 0);
        var smoothError = Math.Max(Math.Abs(end.Item1 - smoothX), Math.Abs(end.Item2 - smoothY));

        Check("부드러운 이동", smoothError == 0,
              $"목표 ({smoothX},{smoothY}) / 실제 ({end.Item1},{end.Item2}) / 오차 {smoothError}px");

        _adapter.MoveMouseTo(origin.X, origin.Y);
        await Task.Delay(150);
    }

    /// <summary>버튼 위로 커서를 옮겨 좌클릭했을 때 Click 이벤트가 오르는지.</summary>
    private static async Task TestClickAsync(TestWindow ui)
    {
        if (_adapter.GetCursorPosition() is not { } origin)
        {
            Fail("마우스 클릭", "커서 좌표를 읽지 못했다");
            return;
        }

        Post(() => ui.ClickCount = 0);
        var target = Read(() => CenterOnScreen(ui.Target));

        await _service.MoveToExactAsync((int)target.X, (int)target.Y);
        await Task.Delay(200);

        await _service.ClickAsync(MouseButton.Left, holdTimeMs: 30);
        await Task.Delay(250);

        var clicks = Read(() => ui.ClickCount);
        Check("마우스 좌클릭", clicks == 1, $"버튼 Click 이벤트 {clicks}회 (기대 1회)");

        _adapter.MoveMouseTo(origin.X, origin.Y);
        await Task.Delay(150);
    }

    /// <summary>스크롤 영역 위에서 휠을 굴렸을 때 실제로 스크롤되는지.</summary>
    private static async Task TestWheelAsync(TestWindow ui)
    {
        if (_adapter.GetCursorPosition() is not { } origin)
        {
            Fail("휠 스크롤", "커서 좌표를 읽지 못했다");
            return;
        }

        var target = Read(() => CenterOnScreen(ui.Scroller));
        await _service.MoveToExactAsync((int)target.X, (int)target.Y);
        await Task.Delay(200);

        var before = Read(() => ui.Scroller.VerticalOffset);
        _service.Scroll(-3);            // 음수가 아래로
        await Task.Delay(300);
        var after = Read(() => ui.Scroller.VerticalOffset);

        Check("휠 스크롤", after > before, $"세로 오프셋 {before} → {after}");

        _adapter.MoveMouseTo(origin.X, origin.Y);
        await Task.Delay(150);
    }

    /// <summary>요소의 중앙을 화면 물리 픽셀 좌표로 바꾼다.</summary>
    private static Point CenterOnScreen(FrameworkElement element)
        => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    /// <summary>실제로 붙어 있는 모니터들의 사각형을 열거한다.</summary>
    private static List<(int Left, int Top, int Right, int Bottom)> EnumerateMonitors()
    {
        List<(int, int, int, int)> found = [];

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                found.Add((info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom));
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
