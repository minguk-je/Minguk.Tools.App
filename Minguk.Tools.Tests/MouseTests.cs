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
            Skip("절대 좌표 이동", $"{_adapter.Name} 은 진짜 커서를 움직이지 않는다");
            Skip("부드러운 이동", $"{_adapter.Name} 은 진짜 커서를 움직이지 않는다");
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

    /// <summary>
    /// 상대 이동(<see cref="IInputAdapter.MoveMouseBy"/>)이 진짜 커서를 움직이는지, 그리고 보낸 카운트와 움직인 픽셀의 비를 적는다.
    /// </summary>
    /// <remarks>
    /// 게임은 Raw Input 으로 <b>보낸 카운트</b>를 보지만, OS 커서는 포인터 속도·정밀도 향상을 거쳐 다른 만큼 움직인다.
    /// 그 비를 적어 두면 "조준 배율" 이야기를 할 때 커서 기준으로 잰 값과 게임 기준 값을 섞지 않는다.
    /// </remarks>
    private static async Task TestRelativeMoveAsync()
    {
        if (_adapter.GetCursorPosition() is not { } origin)
        {
            Skip("상대 이동", $"{_adapter.Name} 은 진짜 커서를 움직이지 않는다");
            Skip("끌기 뒤 버튼이 안 눌린 채 남는가", $"{_adapter.Name} 은 진짜 커서를 움직이지 않는다");
            return;
        }

        var screen = VirtualScreen.GetBounds();

        // 화면 한가운데에서 잰다 - 모서리에서는 커서가 벽에 막혀 덜 움직인다.
        _adapter.MoveMouseTo(screen.Left + (int)screen.Width / 2, screen.Top + (int)screen.Height / 2);
        await Task.Delay(120);

        var before = _adapter.GetCursorPosition() ?? (0, 0);
        _adapter.MoveMouseBy(60, 40);
        await Task.Delay(150);
        var after = _adapter.GetCursorPosition() ?? (0, 0);

        var movedX = after.Item1 - before.Item1;
        var movedY = after.Item2 - before.Item2;

        Check("상대 이동", movedX > 0 && movedY > 0,
              $"보낸 (60, 40) → 커서 ({movedX}, {movedY}) · 커서/카운트 {(movedX / 60.0):0.00}배 "
              + "(게임은 카운트를 그대로 본다)");

        // 끌기: 누른 채 움직였다 떼고, 버튼이 남아 있지 않은지 본다. 남으면 다음 클릭이 드래그가 된다.
        _adapter.PressMouseButton(MouseButton.Left);
        await Task.Delay(30);
        _adapter.MoveMouseBy(-30, -20);
        await Task.Delay(30);
        _adapter.ReleaseMouseButton(MouseButton.Left);
        await Task.Delay(150);

        Check("끌기 뒤 버튼이 안 눌린 채 남는가", (GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0,
              (GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0 ? "떼어졌다" : "아직 눌려 있다");

        _adapter.MoveMouseTo(origin.X, origin.Y);
        await Task.Delay(150);
    }

    private const int VK_LBUTTON = 0x01;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    /// <summary>버튼 위로 커서를 옮겨 좌클릭했을 때 Click 이벤트가 오르는지.</summary>
    private static async Task TestClickAsync(TestWindow ui)
    {
        var origin = _adapter.GetCursorPosition();

        Post(() => ui.ClickCount = 0);
        var messagesBefore = Read(() => ui.MouseDownMessages);
        var target = Read(() => CenterOnScreen(ui.Target));

        await MoveOverAsync((int)target.X, (int)target.Y);

        await _service.ClickAsync(MouseButton.Left, holdTimeMs: 30);
        await Task.Delay(250);

        var clicks = Read(() => ui.ClickCount);
        var messages = Read(() => ui.MouseDownMessages) - messagesBefore;

        if (clicks == 1)
        {
            Pass("마우스 좌클릭", $"버튼 Click 이벤트 {clicks}회");
        }
        else if (messages > 0)
        {
            // 메시지는 창까지 왔다. WPF 는 창 하나가 전부라 자식 HWND 가 없고,
            // 마우스 입력을 lParam 이 아니라 실제 커서 위치로 판단한다.
            // 그래서 부친 메시지가 어느 요소에도 닿지 않는다. 어댑터 문제가 아니다.
            Skip("마우스 좌클릭", $"WM_LBUTTONDOWN {messages}건이 창에 도착했지만 WPF 가 요소로 넘기지 않았다 "
                                + "- 이 경로는 Win32/WinForms 대상용이다");
        }
        else
        {
            Fail("마우스 좌클릭", $"창에 마우스 메시지가 오지 않았다 (Click {clicks}회)");
        }

        await RestoreCursorAsync(origin);
    }

    /// <summary>스크롤 영역 위에서 휠을 굴렸을 때 실제로 스크롤되는지.</summary>
    private static async Task TestWheelAsync(TestWindow ui)
    {
        var origin = _adapter.GetCursorPosition();

        var target = Read(() => CenterOnScreen(ui.Scroller));
        await MoveOverAsync((int)target.X, (int)target.Y);

        var before = Read(() => ui.Scroller.VerticalOffset);
        var wheelBefore = Read(() => ui.WheelMessages);
        _service.Scroll(-3);            // 음수가 아래로
        await Task.Delay(300);
        var after = Read(() => ui.Scroller.VerticalOffset);

        var wheelMessages = Read(() => ui.WheelMessages) - wheelBefore;

        if (after > before)
        {
            Pass("휠 스크롤", $"세로 오프셋 {before} → {after}");
        }
        else if (wheelMessages > 0)
        {
            Skip("휠 스크롤", $"WM_MOUSEWHEEL {wheelMessages}건이 창에 도착했지만 WPF 가 요소로 넘기지 않았다 "
                            + "- 이 경로는 Win32/WinForms 대상용이다");
        }
        else
        {
            Fail("휠 스크롤", $"창에 휠 메시지가 오지 않았다 (오프셋 {before} → {after})");
        }

        await RestoreCursorAsync(origin);
    }

    /// <summary>
    /// 버튼·휠을 보낼 자리로 옮긴다.
    /// 진짜 커서를 움직이는 경로는 도착까지 확인하고, 그렇지 않은 경로(PostMessage)는
    /// 좌표만 알려 준다 - 그쪽은 이 좌표를 다음 버튼 메시지에 실을 뿐이다.
    /// </summary>
    private static async Task MoveOverAsync(int x, int y)
    {
        if (_adapter.GetCursorPosition() is null) _adapter.MoveMouseTo(x, y);
        else await _service.MoveToExactAsync(x, y);

        await Task.Delay(200);
    }

    private static async Task RestoreCursorAsync((int X, int Y)? origin)
    {
        if (origin is not { } p) return;

        _adapter.MoveMouseTo(p.X, p.Y);
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
