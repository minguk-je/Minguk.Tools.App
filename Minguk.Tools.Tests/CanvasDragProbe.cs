using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Minguk.Tools.Markup;
using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Tests;

/// <summary>
/// 라벨 캔버스를 <b>실제 마우스</b>로 끌어 본다. 옮기기·크기 조절·새로 그리기.
/// </summary>
/// <remarks>
/// 계산은 <c>--vision</c> 이 숫자로 본다. 여기서 보는 것은 그 계산이 마우스 이벤트에
/// 실제로 이어져 있는지다 - 캡처를 안 잡거나, 손잡이를 못 짚거나, 놓을 때 되돌리는 식의
/// 실수는 오프스크린으로는 안 드러난다.
///
/// 커서를 몇 초 가져간다. 그래서 <c>--views</c> 에 넣지 않고 따로 돌린다. 제 창(TopMost)
/// 위에서만 움직이고, 끝나면 커서를 원래 자리로 돌려놓는다.
/// </remarks>
internal static class CanvasDragProbe
{
    private const int Size = 400;

    public static int Run()
    {
        var failures = 0;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            try
            {
                failures = await DragAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 캔버스 끌기 — {ex.GetType().Name}: {ex.Message}");
                failures = 1;
            }

            app.Shutdown();
        });

        app.Run();

        Console.WriteLine(failures == 0 ? "== 캔버스 끌기 통과 ==" : $"== 캔버스 끌기 실패 {failures}건 ==");

        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> DragAsync()
    {
        var failures = 0;

        var image = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
        image.WritePixels(new Int32Rect(0, 0, Size, Size), new byte[Size * Size * 4], Size * 4, 0);
        image.Freeze();

        var boxes = new ObservableCollection<LabelBox>
        {
            LabelBox.FromCorners(0, 0.25, 0.25, 0.5, 0.5)   // 화면 100..200
        };

        var canvas = new LabelCanvas { Width = Size, Height = Size, ImageSource = image, Boxes = boxes };

        var window = new Window
        {
            Title = "LabelCanvas 끌기 시험",
            Content = canvas,
            SizeToContent = SizeToContent.WidthAndHeight,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 80,
            Top = 80,
            ResizeMode = ResizeMode.NoResize
        };

        // 고정 시간만 기다리면 첫 실행(JIT 가 찬 상태)에서는 창이 그려지기 전에 끌기 시작해
        // 사각형을 못 잡았다 - 실제로 첫 돌림만 4건이 어긋났다. 그려졌다는 신호를 기다린다.
        var rendered = new TaskCompletionSource();
        window.ContentRendered += (_, _) => rendered.TrySetResult();

        window.Show();
        window.Activate();

        await Task.WhenAny(rendered.Task, Task.Delay(5000));
        await Task.Delay(400);

        GetCursorPos(out var original);

        try
        {
            // ── 1) 안쪽을 잡고 끌면 옮겨진다 (60, 20 픽셀) ──
            await DragAsync(canvas, 150, 150, 210, 170);

            var moved = boxes[0];
            failures += Check("안쪽을 끌면 옮겨진다",
                Near(moved.Left, 0.40) && Near(moved.Top, 0.30) && Near(moved.Width, 0.25) && Near(moved.Height, 0.25),
                LabelFile.Format(moved));
            failures += Check("끌기 시작한 것이 골라진다", canvas.SelectedIndex == 0, canvas.SelectedIndex.ToString());

            // ── 2) 고른 것의 오른쪽 아래 모서리를 끌면 그쪽만 늘어난다 ──
            // 옮긴 뒤 화면 자리는 160..260 x 120..220 이다.
            await DragAsync(canvas, 260, 220, 300, 260);

            var grown = boxes[0];
            failures += Check("모서리를 끌면 반대편은 그대로",
                Near(grown.Left, 0.40) && Near(grown.Top, 0.30) && Near(grown.Right, 0.75) && Near(grown.Bottom, 0.65),
                LabelFile.Format(grown));

            // ── 3) 빈 자리를 끌면 새로 그려진다 ──
            await DragAsync(canvas, 20, 300, 80, 360);

            failures += Check("빈 자리를 끌면 새 사각형", boxes.Count == 2, $"{boxes.Count}개");

            if (boxes.Count == 2)
            {
                var drawn = boxes[1];
                failures += Check("새 사각형 자리",
                    Near(drawn.Left, 0.05) && Near(drawn.Top, 0.75) && Near(drawn.Right, 0.20) && Near(drawn.Bottom, 0.90),
                    LabelFile.Format(drawn));
            }

            // ── 4) 클릭만 하면 옮겨지지 않는다 ──
            var before = boxes[0];
            await DragAsync(canvas, 200, 180, 201, 181);

            failures += Check("클릭(1px 떨림)으로는 안 움직인다", boxes[0] == before, LabelFile.Format(boxes[0]));
        }
        finally
        {
            SetCursorPos(original.X, original.Y);
            window.Close();
        }

        return failures;
    }

    /// <summary>캔버스 좌표 (x1,y1) 에서 (x2,y2) 까지 실제 마우스로 끈다. 중간 이동을 여러 번 보내 WPF 가 MouseMove 를 받게 한다.</summary>
    private static async Task DragAsync(LabelCanvas canvas, double x1, double y1, double x2, double y2)
    {
        var from = canvas.PointToScreen(new Point(x1, y1));
        var to = canvas.PointToScreen(new Point(x2, y2));

        SetCursorPos((int)from.X, (int)from.Y);
        await Task.Delay(80);
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(80);

        const int steps = 6;
        for (var i = 1; i <= steps; i++)
        {
            var x = from.X + ((to.X - from.X) * i / steps);
            var y = from.Y + ((to.Y - from.Y) * i / steps);
            SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
            await Task.Delay(40);
        }

        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(150);

        // 틀렸을 때 어디에 떨어졌는지 보이게. 실패 줄만으로는 커서가 빗나갔는지 잡기가 안 됐는지 모른다.
        var dpi = VisualTreeHelper.GetDpi(canvas);
        var now = canvas.PointFromScreen(to);
        Console.WriteLine($"      끌기 ({x1},{y1})→({x2},{y2}) 화면 ({from.X:0},{from.Y:0})→({to.X:0},{to.Y:0}) 되돌림 ({now.X:0},{now.Y:0}) dpi {dpi.DpiScaleX:0.##}"
                          + $" 사각형 {canvas.Boxes?.Count}개 고른 것 {canvas.SelectedIndex}"
                          + (canvas.Boxes is { } b ? " : " + string.Join(" | ", b.Select(LabelFile.Format)) : string.Empty));
    }

    private static int Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name} — {detail}");
        return ok ? 0 : 1;
    }

    // 1픽셀은 1/400 = 0.0025 다. 두 픽셀까지는 봐준다.
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 0.006;

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
