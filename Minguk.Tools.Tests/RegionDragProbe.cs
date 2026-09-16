using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

    /// <summary>판 크기. 그림(16:9)보다 세로가 길어 위아래 여백이 생긴다.</summary>
    private const double BoardWidth = 760;
    private const double BoardHeight = 520;

    /// <summary>끄는 동안 커서와 잡은 점이 이만큼(화면 픽셀) 넘게 벌어지면 실패.</summary>
    private const double TolerancePx = 2.5;

    /// <summary>
    /// <c>--busy</c>: 미리보기처럼 그림을 60fps 로 갈아 끼우고 한 장마다 UI 스레드를 12ms 붙든다 - 실제 앱은 캡처 그림을 그리느라 바쁘다.
    /// 한가한 창에서는 배치가 입력을 곧바로 따라와 어긋남이 안 드러날 수 있다.
    /// </summary>
    private static bool _busy;

    /// <summary><c>--hit</c>: 끌지 않고, 테두리 안팎 몇 px 자리에서 어느 요소가 마우스를 받는지만 잰다(커서 안 가져감).</summary>
    private static bool _hit;

    private static async Task<int> RunHitAllAsync()
    {
        var failures = 0;

        foreach (var (zoom, scroll) in new[] { (1.0, new Vector()), (2.0, new Vector()), (8.0, new Vector(1400, 1000)) })
        {
            var fixture = await Fixture.OpenAsync(zoom, scroll);

            try
            {
                foreach (var cell in new[] { false, true })
                {
                    fixture.Canvas.SelectedCell = null;
                    fixture.Canvas.SelectedRegion = fixture.Region;
                    if (cell) fixture.Canvas.SelectedCell = fixture.HalfCell;
                    await Settle();

                    RegionItemBase item = cell ? fixture.Canvas.CellItems.Single(c => ReferenceEquals(c.Cell, fixture.HalfCell)) : fixture.Canvas.Items.Single();
                    var what = $"배율 {zoom} · {(cell ? "구역" : "영역")}";
                    var window = fixture.Window;

                    Console.WriteLine($"[INFO] {what} - InverseZoom {item.InverseZoom:0.###} · 항목 {item.ActualWidth:0.#}x{item.ActualHeight:0.#}");

                    // 보이는 손잡이(Rectangle) 가운데가 어느 요소에 맞나.
                    var layer = AdornerLayer.GetAdornerLayer(item);
                    var adorner = layer?.GetAdorners(item)?.FirstOrDefault();
                    if (adorner is not null)
                    {
                        foreach (var grip in Descendants<System.Windows.Shapes.Rectangle>(adorner).Where(r => r.ActualWidth > 0 && r.ActualWidth < 20 && r.Fill is not null))
                        {
                            var center = grip.PointToScreen(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2));
                            var gripSize = grip.PointToScreen(new Point(grip.ActualWidth, grip.ActualHeight)) - grip.PointToScreen(new Point(0, 0));
                            Console.WriteLine($"   손잡이 {grip.HorizontalAlignment}/{grip.VerticalAlignment} 화면 크기 {gripSize.X:0.#}x{gripSize.Y:0.#} · 가운데 누르면 → {HitName(window, center)}");
                        }

                        foreach (var thumb in Descendants<RegionResizeThumb>(adorner))
                        {
                            var a = thumb.PointToScreen(new Point(0, 0));
                            var z = thumb.PointToScreen(new Point(thumb.ActualWidth, thumb.ActualHeight));
                            Console.WriteLine($"   잡는 띠 {thumb.HorizontalAlignment}/{thumb.VerticalAlignment} 화면 ({a.X:0},{a.Y:0})~({z.X:0},{z.Y:0})");
                        }
                    }

                    // 오른쪽 변 가운데에서 가로로 -10~+10 화면 px.
                    var edge = item.PointToScreen(new Point(item.ActualWidth, item.ActualHeight / 2));
                    Console.WriteLine($"   오른쪽 변 화면 x={edge.X:0.#}");
                    foreach (var dx in new[] { -10, -6, -4, -2, 0, 2, 4, 6, 10 })
                        Console.WriteLine($"     변{dx:+0;-0;0}px → {HitName(window, new Point(edge.X + dx, edge.Y))}");
                }
            }
            finally
            {
                fixture.Window.Close();
            }
        }

        return failures;
    }

    private static string HitName(Window window, Point screen)
    {
        var local = window.PointFromScreen(screen);
        var hit = window.InputHitTest(local) as DependencyObject;
        var d = hit;

        while (d is not null)
        {
            if (d is RegionResizeThumb r) return $"크기 손잡이 {r.HorizontalAlignment}/{r.VerticalAlignment} ({r.Cursor})";
            if (d is RegionMoveThumb) return "옮기기(안쪽)";
            if (d is RegionRotateThumb) return "회전";
            if (d is RegionItemBase) return "항목";
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }

        return hit?.GetType().Name ?? "없음";
    }

    public static int Run(string[] args)
    {
        _busy = args.Contains("--busy");
        _hit = args.Contains("--hit");
        var failures = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            try
            {
                failures = _hit ? await RunHitAllAsync() : await RunAllAsync();
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
            // 확대하고 스크롤한 경우(사용자, 2026-09-16 "확대하고 스크롤하면 어긋나") - 판이 뷰포트 밖으로 밀려 있다.
            // 휠 확대는 1.25배씩이라 배율이 소수다(1.5625·2.44…) - 반픽셀 자리에서 어긋나는지도 본다.
            foreach (var (zoom, scroll) in new[] { (1.0, new Vector()), (2.0, new Vector()), (2.0, new Vector(200, 100)), (3.0, new Vector(300, 200)),
                                                   (1.5625, new Vector(150, 120)), (2.44140625, new Vector(350, 260)) })
                failures += await RunZoomAsync(zoom, scroll);
        }
        finally
        {
            SetCursorPos(original.X, original.Y);
        }

        return failures;
    }

    private static async Task<int> RunZoomAsync(double zoom, Vector scroll)
    {
        var failures = 0;
        var fixture = await Fixture.OpenAsync(zoom, scroll);
        var name = scroll.Length > 0 ? $"배율 {zoom:0.#} · 스크롤 ({scroll.X:0},{scroll.Y:0})" : $"배율 {zoom:0.#}";

        try
        {
            Console.WriteLine($"[INFO] {name} - 판 {fixture.Canvas.ActualWidth:0}x{fixture.Canvas.ActualHeight:0} · 실제 스크롤 ({fixture.Scroll.HorizontalOffset:0},{fixture.Scroll.VerticalOffset:0}) · dpi {VisualTreeHelper.GetDpi(fixture.Canvas).DpiScaleX:0.##}");

            // ── 1) 영역 크기 손잡이(오른쪽 아래) ── 영역을 목록에서 고른 것처럼 SelectedRegion 으로 고른다.
            fixture.Canvas.SelectedCell = null;
            fixture.Canvas.SelectedRegion = fixture.Region;
            await Settle();

            var regionItem = fixture.Canvas.Items.Single();
            var start = RectOf(regionItem);

            // 끌기 전에 - 오른쪽 아래 손잡이(어도너)가 화면에서 영역 모서리에 있는가. 어도너 층은 확대·스크롤 밖이라 따로 따라가야 한다.
            var gap = GripGap(regionItem, HorizontalAlignment.Right, VerticalAlignment.Bottom);
            failures += Check($"{name} · 영역 오른쪽 아래 손잡이가 화면에서 모서리에 있다", gap <= TolerancePx + 1.5, $"모서리와 {gap:0.0}px");
            var target = new Point(start.Right + (60 / zoom), start.Bottom + (40 / zoom));

            var error = await DragAsync(fixture, start.BottomRight, target, () => RectOf(regionItem).BottomRight, zoom);
            var area = fixture.Canvas.ImageArea;
            var expectRegion = new Rect((start.X - area.X) / area.Width, (start.Y - area.Y) / area.Height, (target.X - start.X) / area.Width, (target.Y - start.Y) / area.Height);
            failures += Check($"{name} · 영역 오른쪽 아래 손잡이가 끄는 동안 커서를 따라온다", error <= TolerancePx, $"가장 벌어짐 {error:0.0}px");
            await Settle();
            gap = GripGap(regionItem, HorizontalAlignment.Right, VerticalAlignment.Bottom);
            failures += Check($"{name} · 놓은 뒤에도 손잡이가 모서리에 있다", gap <= TolerancePx + 1.5, $"모서리와 {gap:0.0}px");
            failures += Check($"{name} · 놓은 영역 크기가 커서가 간 만큼이다", Near(fixture.Region.Rect, expectRegion), $"{Describe(fixture.Region.Rect)} / 기대 {Describe(expectRegion)}");

            // ── 2) 구역 옮기기(안쪽 잡기) ── 반쪽 구역을 고르고 안쪽을 끈다.
            fixture.Canvas.SelectedCell = fixture.HalfCell;
            await Settle();

            var cellItem = fixture.Canvas.CellItems.Single(c => ReferenceEquals(c.Cell, fixture.HalfCell));
            var cellStart = RectOf(cellItem);
            var press = new Point(cellStart.X + (cellStart.Width * 0.5), cellStart.Y + (cellStart.Height * 0.5));
            var owner = RectOf(regionItem);

            // 영역 안에서만 움직인다 - 끝까지 가면 막혀 커서와 벌어지는 것이 맞다.
            var move = new Vector(Math.Max(-cellStart.X + owner.X + 4, -80 / zoom), 0);
            error = await DragAsync(fixture, press, press + move, () => RectOf(cellItem).TopLeft, zoom);
            failures += Check($"{name} · 구역 안쪽을 끄는 동안 구역이 커서를 따라온다", error <= TolerancePx, $"가장 벌어짐 {error:0.0}px");

            // ── 3) 구역 크기 손잡이(오른쪽 변 가운데) ──
            await Settle();
            cellStart = RectOf(cellItem);
            var edge = new Point(cellStart.Right, cellStart.Y + (cellStart.Height / 2));
            error = await DragAsync(fixture, edge, edge + new Vector(40 / zoom, 0), () => new Point(RectOf(cellItem).Right, edge.Y), zoom);
            failures += Check($"{name} · 구역 오른쪽 손잡이가 끄는 동안 커서를 따라온다", error <= TolerancePx, $"가장 벌어짐 {error:0.0}px");
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

    /// <summary>
    /// 고른 영역의 손잡이(어도너 속 <see cref="RegionResizeThumb"/>) 가운데와 영역의 그 모서리가 화면에서 얼마나 떨어졌는가(화면 픽셀).
    /// 손잡이 칸은 모서리에서 배율 역수로 1px 안쪽이 가운데라 화면에서 1px 쯤은 늘 있다.
    /// </summary>
    private static double GripGap(RegionItem item, HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        var layer = AdornerLayer.GetAdornerLayer(item);
        var adorner = layer?.GetAdorners(item)?.OfType<RegionResizeAdorner>().FirstOrDefault();
        if (adorner is null) return double.PositiveInfinity;

        var thumb = Descendants<RegionResizeThumb>(adorner).FirstOrDefault(t => t.HorizontalAlignment == horizontal && t.VerticalAlignment == vertical);
        if (thumb is null) return double.PositiveInfinity;

        var gripCenter = thumb.PointToScreen(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2));
        var corner = item.PointToScreen(new Point(horizontal == HorizontalAlignment.Right ? item.ActualWidth : 0, vertical == VerticalAlignment.Bottom ? item.ActualHeight : 0));

        return (gripCenter - corner).Length;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
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
        // 판 2px 까지 봐준다(비율로, 그림 영역 약 760x428).
        const double x = 2.0 / BoardWidth;
        const double y = 2.0 / (BoardWidth * ImageHeight / ImageWidth);
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

        public required ScrollViewer Scroll { get; init; }

        public static async Task<Fixture> OpenAsync(double zoom, Vector scrollTo)
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
                Source = image,
                Regions = regions,
                Zoom = zoom,
                EditCommand = new DelegateCommand<RegionEdit>(e => e.Region.Rect = e.Rect),
                CellEditCommand = new DelegateCommand<CellEdit>(e => { e.Cell.Rect = e.Rect; e.Cell.Angle = e.Angle; })
            };
            canvas.IsEditing = true;

            // 미리보기(CapturePreviewPanel)처럼 편집기는 ContentPresenter 안에 얹는다.
            // 판은 그림 비율과 다르게(위아래 여백) - 미리보기 판은 창 크기라 늘 레터박스가 생긴다.
            var board = new Grid { Width = BoardWidth, Height = BoardHeight, LayoutTransform = new ScaleTransform(zoom, zoom) };
            board.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform });
            board.Children.Add(new ContentPresenter { Content = canvas });

            var scroll = new ScrollViewer
            {
                Focusable = false,
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

            if (scrollTo.Length > 0)
            {
                scroll.ScrollToHorizontalOffset(scrollTo.X);
                scroll.ScrollToVerticalOffset(scrollTo.Y);
                await Task.Delay(300);
            }

            if (_busy) StartBusyPreview(window, board);

            return new Fixture { Window = window, Canvas = canvas, Region = region, HalfCell = half, Scroll = scroll };
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
