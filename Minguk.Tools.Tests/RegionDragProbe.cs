using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using DevExpress.Mvvm;

using Minguk.Tools.Markup.Regions;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 영역·구역 손잡이를 <b>실제 마우스</b>로 끌면서, 끄는 동안 손잡이가 커서를 따라오는지 잰다 - <c>--region-drag</c>.
/// </summary>
/// <remarks>
/// 사용자, 2026-09-16 "어도너 포인터랑 마우스 포인터가 안 맞는다". <c>--script-screen</c> 은 캔버스의 <c>MoveBy</c>·<c>ResizeBy</c> 에 숫자를 넣어
/// 계산만 본다 - Thumb 이 주는 변위를 어떻게 쌓는지(끄는 동안 어긋남)는 진짜 마우스 이벤트가 와야 드러난다.
///
/// 미리보기와 같은 짜임으로 띄운다: ScrollViewer 안에 LayoutTransform 으로 키운 판(그림 + <see cref="RegionCanvas"/>). 어도너 층은 확대 밖이다.
/// 끌기는 여러 걸음으로 나눠 보내고 <b>걸음마다</b> "커서 - 잡은 점" 이 처음과 얼마나 달라졌는지 잰다(캔버스 좌표 → 화면 픽셀로 적는다).
/// 놓은 뒤에는 저장값(비율)이 커서가 간 만큼인지 본다.
///
/// 커서를 몇 초 가져간다(<c>--canvas-drag</c> 와 같다). 제 창(TopMost) 위에서만 움직이고 끝나면 커서를 되돌린다.
/// </remarks>
internal static class RegionDragProbe
{
    /// <summary>그림(원본) 크기. 판도 같은 크기라 배율 1 에서 캔버스 1 = 그림 1 픽셀.</summary>
    private const int ImageWidth = 800;
    private const int ImageHeight = 450;

    /// <summary>끄는 동안 커서와 잡은 점이 이만큼(화면 픽셀) 넘게 벌어지면 실패.</summary>
    private const double TolerancePx = 2.5;

    /// <summary>
    /// <c>--busy</c>: 미리보기처럼 그림을 60fps 로 갈아 끼우고 한 장마다 UI 스레드를 12ms 붙든다 - 실제 앱은 캡처 그림을 그리느라 바쁘다.
    /// 한가한 창에서는 배치가 입력을 곧바로 따라와 어긋남이 안 드러날 수 있다.
    /// </summary>
    private static bool _busy;

    public static int Run(string[] args)
    {
        _busy = args.Contains("--busy");
        var failures = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            try
            {
                failures = await RunAllAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 영역 손잡이 끌기 — {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                failures = 1;
            }

            app.Shutdown();
        });

        app.Run();

        Console.WriteLine(failures == 0 ? "== 영역 손잡이 끌기 통과 ==" : $"== 영역 손잡이 끌기 실패 {failures}건 ==");

        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> RunAllAsync()
    {
        GetCursorPos(out var original);
        var failures = 0;

        try
        {
            foreach (var zoom in new[] { 1.0, 2.0 })
                failures += await RunZoomAsync(zoom);
        }
        finally
        {
            SetCursorPos(original.X, original.Y);
        }

        return failures;
    }

    private static async Task<int> RunZoomAsync(double zoom)
    {
        var failures = 0;
        var fixture = await Fixture.OpenAsync(zoom);

        try
        {
            Console.WriteLine($"[INFO] 배율 {zoom:0.#} - 판 {fixture.Canvas.ActualWidth:0}x{fixture.Canvas.ActualHeight:0} · dpi {VisualTreeHelper.GetDpi(fixture.Canvas).DpiScaleX:0.##}");

            // ── 1) 영역 크기 손잡이(오른쪽 아래) ── 영역을 목록에서 고른 것처럼 SelectedRegion 으로 고른다.
            fixture.Canvas.SelectedCell = null;
            fixture.Canvas.SelectedRegion = fixture.Region;
            await Settle();

            var regionItem = fixture.Canvas.Items.Single();
            var start = RectOf(regionItem);
            var target = new Point(start.Right + 90, start.Bottom + 45);

            var error = await DragAsync(fixture, start.BottomRight, target, () => RectOf(regionItem).BottomRight, zoom);
            var expectRegion = new Rect(start.X / ImageWidth, start.Y / ImageHeight, (target.X - start.X) / ImageWidth, (target.Y - start.Y) / ImageHeight);
            failures += Check($"배율 {zoom:0.#} · 영역 오른쪽 아래 손잡이가 끄는 동안 커서를 따라온다", error <= TolerancePx, $"가장 벌어짐 {error:0.0}px");
            failures += Check($"배율 {zoom:0.#} · 놓은 영역 크기가 커서가 간 만큼이다", Near(fixture.Region.Rect, expectRegion), $"{Describe(fixture.Region.Rect)} / 기대 {Describe(expectRegion)}");

            // ── 2) 구역 옮기기(안쪽 잡기) ── 반쪽 구역을 고르고 안쪽을 끈다.
            fixture.Canvas.SelectedCell = fixture.HalfCell;
            await Settle();

            var cellItem = fixture.Canvas.CellItems.Single(c => ReferenceEquals(c.Cell, fixture.HalfCell));
            var cellStart = RectOf(cellItem);
            var press = new Point(cellStart.X + (cellStart.Width * 0.5), cellStart.Y + (cellStart.Height * 0.5));
            var owner = RectOf(regionItem);

            // 영역 안에서만 움직인다 - 끝까지 가면 막혀 커서와 벌어지는 것이 맞다.
            var move = new Vector(-cellStart.X + owner.X + 4, 0);
            error = await DragAsync(fixture, press, press + move, () => RectOf(cellItem).TopLeft, zoom);
            failures += Check($"배율 {zoom:0.#} · 구역 안쪽을 끄는 동안 구역이 커서를 따라온다", error <= TolerancePx, $"가장 벌어짐 {error:0.0}px");

            // ── 3) 구역 크기 손잡이(오른쪽 변 가운데) ──
            await Settle();
            cellStart = RectOf(cellItem);
            var edge = new Point(cellStart.Right, cellStart.Y + (cellStart.Height / 2));
            error = await DragAsync(fixture, edge, edge + new Vector(50, 0), () => new Point(RectOf(cellItem).Right, edge.Y), zoom);
            failures += Check($"배율 {zoom:0.#} · 구역 오른쪽 손잡이가 끄는 동안 커서를 따라온다", error <= TolerancePx, $"가장 벌어짐 {error:0.0}px");
        }
        finally
        {
            fixture.Window.Close();
        }

        return failures;
    }

    /// <summary>
    /// 캔버스 좌표 <paramref name="from"/> 에서 <paramref name="to"/> 까지 실제 마우스로 끈다. 걸음마다 "커서 - 잡은 점" 이 처음보다 얼마나 벌어졌는지 재어 가장 큰 값(화면 픽셀)을 준다.
    /// </summary>
    private static async Task<double> DragAsync(Fixture fixture, Point from, Point to, Func<Point> anchor, double zoom)
    {
        var canvas = fixture.Canvas;
        var screenFrom = canvas.PointToScreen(from);
        var screenTo = canvas.PointToScreen(to);

        SetCursorPos((int)Math.Round(screenFrom.X), (int)Math.Round(screenFrom.Y));
        await Task.Delay(80);

        GetCursorPos(out var actual);
        if (Math.Abs(actual.X - screenFrom.X) > 2 || Math.Abs(actual.Y - screenFrom.Y) > 2)
            throw new InvalidOperationException(
                $"커서를 ({screenFrom.X:0},{screenFrom.Y:0}) 로 보냈는데 ({actual.X},{actual.Y}) 에 있다. 다른 창이 커서를 가두고 있다 - 게임을 내리고 다시 돌린다.");

        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(80);

        var startOffset = CursorInCanvas(canvas) - anchor();
        var worst = 0d;
        var samples = new List<string>();

        // 빠르게 여러 걸음 - 사람이 휙 끌 때처럼. 한 걸음 사이에 배치가 따라오지 못하면 어긋남이 쌓인다.
        const int steps = 24;
        for (var i = 1; i <= steps; i++)
        {
            var x = screenFrom.X + ((screenTo.X - screenFrom.X) * i / steps);
            var y = screenFrom.Y + ((screenTo.Y - screenFrom.Y) * i / steps);
            SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
            await Task.Delay(i % 4 == 0 ? 30 : 4);

            if (i % 4 != 0) continue;

            // 캔버스 좌표 차이를 화면 픽셀로(배율 × dpi).
            var drift = (CursorInCanvas(canvas) - anchor()) - startOffset;
            var px = drift.Length * zoom * VisualTreeHelper.GetDpi(canvas).DpiScaleX;
            worst = Math.Max(worst, px);
            samples.Add($"{px:0.0}");
        }

        await Task.Delay(60);
        var final = (CursorInCanvas(canvas) - anchor()) - startOffset;
        worst = Math.Max(worst, final.Length * zoom * VisualTreeHelper.GetDpi(canvas).DpiScaleX);

        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(150);

        Console.WriteLine($"      끌기 캔버스 ({from.X:0},{from.Y:0})→({to.X:0},{to.Y:0}) · 걸음마다 벌어짐 {string.Join(" ", samples)} px");

        return worst;
    }

    private static Point CursorInCanvas(RegionCanvas canvas)
    {
        GetCursorPos(out var cursor);
        return canvas.PointFromScreen(new Point(cursor.X, cursor.Y));
    }

    private static Rect RectOf(FrameworkElement item) => new(Canvas.GetLeft(item), Canvas.GetTop(item), item.Width, item.Height);

    private static async Task Settle() => await Task.Delay(250);

    private static bool Near(Rect a, Rect b)
    {
        // 화면 2px 까지 봐준다(비율로).
        const double x = 2.0 / ImageWidth;
        const double y = 2.0 / ImageHeight;
        return Math.Abs(a.X - b.X) <= x && Math.Abs(a.Y - b.Y) <= y && Math.Abs(a.Width - b.Width) <= x && Math.Abs(a.Height - b.Height) <= y;
    }

    private static string Describe(Rect r) => $"({r.X:0.000}, {r.Y:0.000}) {r.Width:0.000}x{r.Height:0.000}";

    private static int Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name} — {detail}");
        return ok ? 0 : 1;
    }

    /// <summary>시험 창 - 미리보기와 같은 짜임.</summary>
    private sealed class Fixture
    {
        public required Window Window { get; init; }

        public required RegionCanvas Canvas { get; init; }

        public required NamedRegion Region { get; init; }

        public required RegionCell HalfCell { get; init; }

        public static async Task<Fixture> OpenAsync(double zoom)
        {
            var image = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[ImageWidth * ImageHeight * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 60; pixels[i + 1] = 50; pixels[i + 2] = 40; pixels[i + 3] = 255; }
            image.WritePixels(new Int32Rect(0, 0, ImageWidth, ImageHeight), pixels, ImageWidth * 4, 0);
            image.Freeze();

            // 영역은 그림의 (0.15, 0.2) 에 0.3 x 0.3. 구역은 기본 「전체」 + 가운데 반쪽(새 구역과 같은 자리).
            var half = new RegionCell { Name = "구역1", Rect = new Rect(0.25, 0, 0.5, 1) };
            var region = new NamedRegion { Name = "탄약", Rect = new Rect(0.15, 0.2, 0.3, 0.3) };
            region.Cells.Add(half);

            var regions = new ObservableCollection<NamedRegion> { region };

            var canvas = new RegionCanvas
            {
                Width = ImageWidth,
                Height = ImageHeight,
                Source = image,
                Regions = regions,
                Zoom = zoom,
                EditCommand = new DelegateCommand<RegionEdit>(e => e.Region.Rect = e.Rect),
                CellEditCommand = new DelegateCommand<CellEdit>(e => { e.Cell.Rect = e.Rect; e.Cell.Angle = e.Angle; })
            };
            canvas.IsEditing = true;

            var board = new Grid { LayoutTransform = new ScaleTransform(zoom, zoom) };
            board.Children.Add(new Image { Source = image, Width = ImageWidth, Height = ImageHeight, Stretch = Stretch.Uniform });
            board.Children.Add(canvas);

            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = board
            };

            var window = new Window
            {
                Title = "영역 손잡이 끌기 시험",
                Content = scroll,
                Width = 1100,
                Height = 820,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 40,
                Top = 40,
                ResizeMode = ResizeMode.NoResize
            };

            var rendered = new TaskCompletionSource();
            window.ContentRendered += (_, _) => rendered.TrySetResult();

            window.Show();
            window.Activate();

            await Task.WhenAny(rendered.Task, Task.Delay(5000));
            await Task.Delay(400);

            if (_busy) StartBusyPreview(window, board);

            return new Fixture { Window = window, Canvas = canvas, Region = region, HalfCell = half };
        }
    }

    /// <summary>미리보기 흉내 - 16ms 마다 새 그림을 만들어 넣고(판 다시 그리기) UI 스레드를 잠깐 붙든다. 창이 닫히면 멈춘다.</summary>
    private static void StartBusyPreview(Window window, Grid board)
    {
        var picture = (Image)board.Children[0];
        var frame = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(16) };

        timer.Tick += (_, _) =>
        {
            var bitmap = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[ImageWidth * ImageHeight * 4];
            var shade = (byte)(40 + (frame++ % 40));
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = shade; pixels[i + 1] = 50; pixels[i + 2] = 40; pixels[i + 3] = 255; }
            bitmap.WritePixels(new Int32Rect(0, 0, ImageWidth, ImageHeight), pixels, ImageWidth * 4, 0);
            bitmap.Freeze();
            picture.Source = bitmap;

            System.Threading.Thread.Sleep(12);
        };

        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
