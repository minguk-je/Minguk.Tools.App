using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using DevExpress.Xpf.Core;
using DevExpress.Xpf.Docking;

using Minguk.Tools.Helper;
using Minguk.Tools.ViewModels;
using Minguk.Tools.Views;

namespace Minguk.Tools.Tests;

/// <summary>
/// 스크립트 화면(VS 2026 모양)을 실제 창에 띄워 그려 본다 - <c>--script-screen [--out=경로.png]</c>.
/// </summary>
/// <remarks>
/// <c>--views</c> 는 화면을 만들기만 한다. 도킹·메뉴·트리·탭은 창에 붙어 그려져야 드러나는 것이 많다(바인딩 경로 틀림, 템플릿 속 오류).
/// 여기서는 앱과 같은 경량 테마·리소스를 걸고, 임시 프로젝트를 열어 탭 둘·트리를 채운 뒤
///   - 바인딩 오류(PresentationTraceSources)를 모아 찍고
///   - 화면을 PNG 로 남긴다(사람이 눈으로 본다).
/// 화면 밖(-20000)에 띄운다 - 커서·키보드를 안 가져간다. 부모 주입이 없어 캡처·전역 단축키 초기화는 안 돈다.
/// 설정은 하네스 실행 폴더의 파일에 쓴다(사용자 설정과 무관).
/// </remarks>
internal static class ScriptScreenProbe
{
    public static int Run(string[] args)
    {
        var output = Program.ArgValue(args, "--out=") ?? Path.Combine(Path.GetTempPath(), "minguk-script-screen.png");
        var folder = Path.Combine(Path.GetTempPath(), "minguk-script-screen-" + Guid.NewGuid().ToString("N"));
        var bindingErrors = new List<string>();
        var failures = 0;

        CompatibilitySettings.UseLightweightThemes = true;
        LightweightThemeManager.AllowStandardControlsTheming = false;

        // 앱(App.xaml.cs)과 같게. 이게 없으면 크기가 안 정해진 칸 안의 그리드가 배치를 끝없이 다시 잰다(실측: CPU 600초).
        DevExpress.Xpf.Grid.DataControlBase.AllowInfiniteGridSize = true;

        // UI 스레드에 쌓이는 작업을 종류별로 센다 - 멈추면 무엇이 끝없이 도는지 찍으려고.
        var posted = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();

        // 무엇이 막혀도 하네스가 영영 안 끝나면 안 된다.
        _ = new System.Threading.Timer(_ =>
        {
            Console.WriteLine("[FAIL] 스크립트 화면 - 60초 안에 끝나지 않았다(배치가 끝나지 않거나 대화 상자가 떴다). 많이 쌓인 작업:");
            foreach (var (name, count) in posted.OrderByDescending(p => p.Value).Take(15)) Console.WriteLine($"       {count,8}  {name}");
            Environment.Exit(2);
        }, null, 60_000, System.Threading.Timeout.Infinite);
        UserPreferencesHelper.EnsureDefaults();
        UserPreferencesHelper.ApplyTheme();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.Dispatcher.Hooks.OperationPosted += (_, e) =>
        {
            var method = typeof(DispatcherOperation).GetField("_method", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(e.Operation) as Delegate;
            var key = $"{e.Operation.Priority} {method?.Method.DeclaringType?.Name}.{method?.Method.Name}";
            posted.AddOrUpdate(key, 1, (_, n) => n + 1);
        };

        foreach (var name in new[] { "ControlTemplate", "DataTemplate", "GridColumnStyle", "Style" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Minguk.Base;component/Resource/{name}.xaml") });

        app.Resources["BaseFontSize"] = 12d;
        app.Resources["EditorFontSize"] = 13d;
        app.Resources["BaseFontFamily"] = new FontFamily("D2Coding, Malgun Gothic");

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new CollectingListener(bindingErrors));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            Window? window = null;

            try
            {
                var view = new ScriptStudioView();
                window = new Window
                {
                    Width = 1600, Height = 1000, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                    Content = view
                };
                Console.WriteLine("[INFO] 창을 띄운다");
                window.Show();

                await Pump(800);
                Console.WriteLine("[INFO] 떴다 - 프로젝트를 연다");

                var vm = (ScriptStudioViewModel)view.DataContext;
                var project = vm.Script.Project.CreateProject(Path.Combine(folder, "사격장.mtsproj"), "var 검출 = 목표();\n조준(검출);\n없는함수();\n");
                vm.Script.Project.AddNewFile(ScriptProjectWorkspace.RootId, "조준도우미");

                await Pump(2500);

                Console.WriteLine($"[INFO] 탭 {vm.Script.Project.Documents.Count} · 트리 줄 {vm.Script.Project.Nodes.Count} · 오류 {vm.Script.Errors.Count} ({string.Join(" / ", vm.Script.Errors.Take(3))})");

                // 탭 편집기에 문서 글이 실제로 들어갔는지 - 화면 밖에서는 글 층이 안 그려질 수 있어 PNG 로는 못 가린다.
                var editors = Descendants<Minguk.Tools.Markup.ScriptEditor>(window).ToList();
                foreach (var editor in editors)
                    Console.WriteLine($"[INFO] 편집기 {System.IO.Path.GetFileName(editor.FilePath ?? "(한 파일짜리)")} · 글 {editor.Text.Length}자 · 보임 {editor.IsVisible} · 크기 {editor.ActualWidth:0}x{editor.ActualHeight:0} · 색 {editor.SyntaxHighlighting?.Name ?? "없음"} · 오류 {editor.Errors?.Count ?? 0}");

                var active = vm.Script.Project.ActiveDocument;
                var shown = editors.FirstOrDefault(e => e.IsVisible && string.Equals(e.FilePath, active?.FilePath, StringComparison.OrdinalIgnoreCase));
                if (shown is null || shown.Text != active?.Text) { Console.WriteLine("[FAIL] 앞에 있는 탭의 편집기에 문서 글이 안 들어갔다"); failures++; }
                else Console.WriteLine($"[PASS] 앞 탭 편집기에 문서 글이 들어갔다 - {System.IO.Path.GetFileName(active!.FilePath)}");

                if (!vm.Script.IsProject) { Console.WriteLine("[FAIL] 프로젝트가 열린 것으로 안 보인다"); failures++; }
                if (vm.Script.Project.Documents.Count != 2) { Console.WriteLine("[FAIL] 탭이 둘이 아니다"); failures++; }

                failures += CheckMultiLineTabs(window);
                failures += await CheckExplorerExpansion(window, vm);
                failures += await CheckPreviewZoom(window, vm);
                failures += await CheckRegionCanvas(window, vm);
                failures += await CheckSplitAndBars(window, vm);
                failures += VerticalAlignmentCheck.Report((FrameworkElement)window.Content, "스크립트 화면");

                Render(window, output);
                Console.WriteLine($"[INFO] 화면을 찍었다: {output}");

                failures += await CheckHelpPanel(window, vm, Path.ChangeExtension(output, null) + "-help.png");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 스크립트 화면 - {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                failures++;
            }
            finally
            {
                var distinct = bindingErrors.Distinct().ToList();

                if (distinct.Count == 0) Console.WriteLine("[PASS] 바인딩 오류 없음");
                else
                {
                    Console.WriteLine($"[FAIL] 바인딩 오류 {distinct.Count}가지");
                    foreach (var error in distinct.Take(30)) Console.WriteLine("       " + error);
                    failures++;
                }

                window?.Close();
                try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
                app.Shutdown();
            }
        });

        app.Run();

        Console.WriteLine(failures == 0 ? "== 스크립트 화면 통과 ==" : $"== 스크립트 화면 실패 {failures}건 ==");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 미리보기 확대 - 칸으로 배율을 올리면 스크롤 범위가 그만큼 커지는지, 영역을 손보는 중에 휠이 배율을 바꾸는지.
    /// </summary>
    /// <remarks>
    /// 휠은 실제 입력처럼 <b>Preview(터널) → 안 먹혔으면 Bubble</b> 순으로 올린다. 그림 위의 ScrollViewer 가 버블 MouseWheel 을
    /// 늘 먹어 버려(Handled) 바깥 Border 의 MouseWheel 핸들러가 영영 안 불리던 것을 잡으려는 검사다 - 순서를 흉내 내지 않으면 못 잡는다.
    /// </remarks>
    private static async System.Threading.Tasks.Task<int> CheckPreviewZoom(Window window, ScriptStudioViewModel vm)
    {
        var failures = 0;

        vm.ShowPreview = true;
        await Pump(300);

        var panel = Descendants<Minguk.Tools.Views.Parts.CapturePreviewPanel>(window).FirstOrDefault();
        var scroller = panel is null ? null : Descendants<System.Windows.Controls.ScrollViewer>(panel).FirstOrDefault();
        var image = panel is null ? null : Descendants<System.Windows.Controls.Image>(panel).FirstOrDefault();

        if (panel is null || scroller is null || image is null)
        {
            Console.WriteLine("[FAIL] 미리보기 판·스크롤·그림을 못 찾았다");
            return 1;
        }

        vm.PreviewZoom = 2;
        await Pump(300);

        var viewport = scroller.ViewportWidth;
        var extent = scroller.ExtentWidth;

        if (viewport > 0 && extent >= viewport * 1.8)
            Console.WriteLine($"[PASS] 확대 2 - 스크롤 범위 {extent:0} / 뷰포트 {viewport:0}");
        else { Console.WriteLine($"[FAIL] 확대 2 인데 스크롤 범위가 안 커졌다 - 범위 {extent:0} / 뷰포트 {viewport:0}"); failures++; }

        vm.PreviewZoom = 1;

        // 자리 편집기가 도는 동안(영역 보기 켬 + 입력 전달 끔) 휠은 확대다.
        vm.IsInputForwardingEnabled = false;
        vm.ShowRegions = true;
        await Pump(200);

        // InputManager 가 하는 순서 그대로: 터널이 먼저, 안 먹혔으면 버블.
        var tunnel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent, Source = image };
        image.RaiseEvent(tunnel);

        if (!tunnel.Handled)
        {
            var bubble = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent, Source = image };
            image.RaiseEvent(bubble);
        }

        await Pump(200);

        if (Math.Abs(vm.PreviewZoom - 1.25) < 0.001)
            Console.WriteLine("[PASS] 자리 편집 중 휠 한 칸 - 배율 1 → 1.25");
        else { Console.WriteLine($"[FAIL] 자리 편집 중 휠을 굴렸는데 배율이 {vm.PreviewZoom} 이다(1.25 여야 한다) - 그림 위 ScrollViewer 가 휠을 먹는다"); failures++; }

        vm.ShowRegions = false;
        vm.PreviewZoom = 1;

        // 입력 전달이 꺼져 있으면 영역 편집 없이도 휠이 확대다(2026-09-15). 켜져 있으면 휠은 게임 몫이라 배율이 안 바뀐다.
        void Wheel()
        {
            var tunnelWheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent, Source = image };
            image.RaiseEvent(tunnelWheel);
        }

        vm.IsInputForwardingEnabled = false;
        await Pump(100);
        Wheel();
        await Pump(200);
        var offZoom = vm.PreviewZoom;

        vm.PreviewZoom = 1;
        vm.IsInputForwardingEnabled = true;
        await Pump(100);
        Wheel();
        await Pump(200);
        var onZoom = vm.PreviewZoom;

        vm.IsInputForwardingEnabled = false;
        vm.PreviewZoom = 1;

        if (Math.Abs(offZoom - 1.25) < 0.001 && Math.Abs(onZoom - 1) < 0.001)
            Console.WriteLine("[PASS] 입력 전달이 꺼져 있으면 휠이 확대(1 → 1.25), 켜져 있으면 확대하지 않는다(게임 몫)");
        else { Console.WriteLine($"[FAIL] 휠 확대 조건이 틀렸다 - 전달 끔 {offZoom} (1.25 여야) · 전달 켬 {onZoom} (1 이어야)"); failures++; }

        return failures;
    }

    /// <summary>
    /// 영역 편집 캔버스 - 영역 지정을 켜면 자리마다 항목이 놓이고 고른 것에 어도너가 붙는지, 끌기 계산이 VM 까지 오는지,
    /// 끄면 마우스에서 빠지는지(클릭이 게임으로 가야 한다).
    /// </summary>
    /// <remarks>
    /// 실제 Thumb 끌기는 커서를 가져가야 해서 여기서 안 한다. 손잡이가 부르는 것과 같은 <c>MoveBy</c>·<c>ResizeBy</c> 를 부른다.
    /// 목록은 파일(regions.json)을 안 거치고 VM 의 컬렉션에 바로 넣는다 - 사용자 데이터셋 폴더를 건드리면 안 된다.
    /// </remarks>
    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var d = VisualTreeHelper.GetParent(child); d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T found) return found;

        return null;
    }

    private static async System.Threading.Tasks.Task<int> CheckRegionCanvas(Window window, ScriptStudioViewModel vm)
    {
        var failures = 0;

        // 캔버스가 자리를 놓으려면 그림 크기를 알아야 한다 - 잡은 화면 대신 1920x1080 짜리 빈 그림.
        vm.PreviewImage = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Bgr32, null, new byte[1920 * 1080 * 4], 1920 * 4);

        var region = new Minguk.Tools.Vision.Regions.NamedRegion { Name = "탄약", Rect = new Rect(0.5, 0.2, 0.1, 0.1) };
        vm.Regions.Add(region);
        vm.SelectedRegion = region;
        vm.IsInputForwardingEnabled = false;
        vm.ShowRegions = true;
        await Pump(400);

        var canvas = Descendants<Minguk.Tools.Markup.Regions.RegionCanvas>(window).FirstOrDefault();

        if (canvas is null) { Console.WriteLine("[FAIL] 영역 캔버스를 못 찾았다"); return 1; }

        // 영역 보기 체크와 그려진 것이 늘 같다 - 한 번도 안 켠 캔버스가 자리를 그리다가 켰다 끄면 사라졌다(사용자, 2026-09-17).
        {
            var fresh = new Minguk.Tools.Markup.Regions.RegionCanvas { Regions = vm.Regions, Source = vm.PreviewImage };
            var overlay = Descendants<Minguk.Tools.Markup.DetectionOverlay>(window).FirstOrDefault();

            vm.ShowRegions = false;
            await Pump(100);
            var offMatches = canvas.Visibility != Visibility.Visible && overlay is { ShowRegions: false };
            vm.ShowRegions = true;
            await Pump(100);
            var onMatches = canvas.Visibility == Visibility.Visible && overlay is { ShowRegions: true };

            var ok = fresh.Visibility != Visibility.Visible && offMatches && onMatches;
            Console.WriteLine($"[{(ok ? "OK" : "FAIL")}] 영역 보기 체크와 미리보기가 같다(새 캔버스 {fresh.Visibility} · 끔 {offMatches} · 켬 {onMatches})");
            if (!ok) failures++;
        }

        var item = canvas.Items.FirstOrDefault();

        if (item is null || !canvas.IsHitTestVisible || canvas.Visibility != Visibility.Visible)
        {
            Console.WriteLine($"[FAIL] 영역 지정을 켰는데 항목 {canvas.Items.Count}개 · 히트 {canvas.IsHitTestVisible} · {canvas.Visibility}");
            return 1;
        }

        Console.WriteLine($"[PASS] 영역 지정 - 항목 {canvas.Items.Count}개, 그림 자리 {canvas.ImageArea}");
        Console.WriteLine($"[INFO] 캔버스 {canvas.RenderSize} · 항목 자리 {System.Windows.Controls.Canvas.GetLeft(item)},{System.Windows.Controls.Canvas.GetTop(item)} {item.Width}x{item.Height} · {item.Visibility} · 실제 {item.ActualWidth}x{item.ActualHeight}");

        if (item.IsSelected && item.HasAdorner) Console.WriteLine("[PASS] 고른 자리에 테두리·손잡이 어도너가 붙었다");
        else { Console.WriteLine($"[FAIL] 고른 자리인데 어도너가 없다 - 고름 {item.IsSelected} · 어도너 {item.HasAdorner}"); failures++; }

        // 캡처 전(그림 없음)에는 놓을 자리가 없다 - 어도너가 크기 0 항목에 붙어 점으로 남으면 안 된다(사용자, 2026-09-15).
        {
            var image = vm.PreviewImage;
            vm.PreviewImage = null;
            await Pump(200);

            var noDot = !item.HasAdorner && item.Visibility != Visibility.Visible;

            vm.PreviewImage = image;
            await Pump(200);

            var back = item.HasAdorner && item.Visibility == Visibility.Visible;

            if (noDot && back) Console.WriteLine("[PASS] 그림이 없으면 고른 자리의 어도너도 안 보이고(점 없음), 그림이 오면 다시 붙는다");
            else { Console.WriteLine($"[FAIL] 그림 없을 때 어도너가 남는다 - 없음에서 어도너 {item.HasAdorner} · 항목 {item.Visibility} / 다시 붙음 {back}"); failures++; }
        }

        // 라벨링 캔버스처럼 확대해도 테두리가 화면에서 같은 굵기 - 배율 2 면 항목 좌표의 굵기는 절반(판이 2배로 키운다).
        {
            vm.PreviewZoom = 2;
            await Pump(200);

            var border = Descendants<System.Windows.Shapes.Rectangle>(item).FirstOrDefault(r => r.Stroke is not null && !r.IsHitTestVisible);
            var onScreen = (border?.StrokeThickness ?? 0) * vm.PreviewZoom;

            vm.PreviewZoom = 1;
            await Pump(200);

            var atOne = border?.StrokeThickness ?? 0;

            if (Math.Abs(item.InverseZoom - 1) < 0.001 && Math.Abs(onScreen - 1.5) < 0.01 && Math.Abs(atOne - 1.5) < 0.01)
                Console.WriteLine($"[PASS] 확대해도 영역 테두리가 화면에서 같은 굵기다 - 배율 2 에서 화면 {onScreen:0.##}px · 배율 1 에서 {atOne:0.##}px");
            else { Console.WriteLine($"[FAIL] 확대하면 영역 테두리 굵기가 달라진다 - 배율 2 화면 {onScreen:0.##}px · 배율 1 {atOne:0.##}px (둘 다 1.5 여야) · 역수 {item.InverseZoom}"); failures++; }
        }

        // 빈 자리를 눌렀다 떼면 고른 것이 풀린다(사용자, 2026-09-15). 미리보기 바탕(Border)에 실제 이벤트를 흘린다 - 좌표는 진짜 커서라 창 밖이고, 그림 가장자리로 접힌 점짜리 끌기다.
        {
            var surface = Descendants<System.Windows.Controls.Border>(window).FirstOrDefault(b => b.Name == "Surface");

            if (surface is null) { Console.WriteLine("[FAIL] 미리보기 바탕(Surface)을 못 찾았다"); failures++; }
            else
            {
                // 실제 클릭처럼 그 자리에 맞는 요소(그림 등)에서 올린다 - 바탕에 바로 넣으면 중간(ScrollViewer 등)이 먹는 것을 못 잡는다.
                var empty = canvas.TranslatePoint(new Point(canvas.ImageArea.Left + 20, canvas.ImageArea.Top + 20), surface);
                var hit = surface.InputHitTest(empty) as UIElement ?? surface;
                Console.WriteLine($"[INFO] 빈 자리 누르기 - 맞은 요소 {hit.GetType().Name}");

                hit.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent });
                hit.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.MouseUpEvent });
                await Pump(200);

                var cleared = vm.SelectedRegion is null && !item.IsSelected && !item.HasAdorner;

                if (cleared) Console.WriteLine("[PASS] 빈 자리를 누르면 고른 자리가 풀리고 어도너가 진다");
                else { Console.WriteLine($"[FAIL] 빈 자리를 눌러도 고른 것이 안 풀린다 - VM {vm.SelectedRegion?.Name ?? "없음"} · 고름 {item.IsSelected} · 어도너 {item.HasAdorner}"); failures++; }

                vm.SelectedRegion = region;
                await Pump(200);
            }
        }

        var before = System.Windows.Controls.Canvas.GetLeft(item);

        // 손잡이가 부르는 것과 같은 길. 40px 오른쪽으로 옮기고, 오른쪽 변을 20px 늘린다.
        canvas.MoveBy(item, 40, 0);
        canvas.ResizeBy(item, HorizontalAlignment.Right, VerticalAlignment.Stretch, 20, 0);
        await Pump(100);

        var area = canvas.ImageArea;
        var expectedX = 0.5 + (40 / area.Width);
        var expectedWidth = 0.1 + (20 / area.Width);

        if (Math.Abs(region.Rect.X - expectedX) < 0.001 && Math.Abs(region.Rect.Width - expectedWidth) < 0.001 && System.Windows.Controls.Canvas.GetLeft(item) > before)
            Console.WriteLine($"[PASS] 옮기고 늘린 것이 VM 의 자리로 왔다 - {region.Describe}");
        else { Console.WriteLine($"[FAIL] 끌기가 VM 에 안 왔다 - {region.Describe} (기대 x {expectedX:0.0000}, 너비 {expectedWidth:0.0000})"); failures++; }

        if (Math.Abs(item.SourceWidthPx - (expectedWidth * 1920)) < 1)
            Console.WriteLine($"[PASS] 치수 표시가 원본 픽셀이다 - {item.SourceWidthPx:0} x {item.SourceHeightPx:0}");
        else { Console.WriteLine($"[FAIL] 치수가 원본 픽셀이 아니다 - {item.SourceWidthPx:0}"); failures++; }

        // 칸(사용자 2026-09-16 - 자리는 LayoutGroup, 칸은 그 안 항목) - 칸은 자리와 별개 항목·별개 어도너. 칸을 고르면 칸 어도너만 붙는다.
        // 손잡이가 부르는 것과 같은 캔버스 길로 끈다 - 놓기(저장)는 안 부른다(사용자 데이터셋을 건드리지 않게).
        {
            var current = new Minguk.Tools.Vision.Regions.RegionCell { Name = "현재", Rect = new Rect(0, 0, 0.4, 1) };
            var maximum = new Minguk.Tools.Vision.Regions.RegionCell { Name = "최대", Rect = new Rect(0.6, 0, 0.4, 1), Angle = 20 };

            // 눈으로 볼 수 있게 자리를 크게 - 칸 손잡이·회전 동그라미가 제자리에 그려지는지 PNG 로 남긴다.
            region.Rect = new Rect(0.3, 0.25, 0.4, 0.4);
            region.Cells.Clear();
            region.Cells.Add(current);
            region.Cells.Add(maximum);
            vm.RegionsRevision++;
            await Pump(300);

            var cells = canvas.CellItems.Where(c => ReferenceEquals(c.Region, region)).ToList();
            var currentItem = cells.FirstOrDefault(c => ReferenceEquals(c.Cell, current));
            var maximumItem = cells.FirstOrDefault(c => ReferenceEquals(c.Cell, maximum));

            var placed = cells.Count == 2 && currentItem is not null && maximumItem is not null
                         && Math.Abs(System.Windows.Controls.Canvas.GetLeft(currentItem) - System.Windows.Controls.Canvas.GetLeft(item)) < 0.5
                         && Math.Abs(maximumItem.Width - (item.Width * 0.4)) < 0.5
                         && maximumItem.RenderTransform is RotateTransform { Angle: 20 };

            if (placed) Console.WriteLine($"[PASS] 칸 둘이 자리 안에 따로 놓이고 돌린 칸은 돌아 있다 - 칸 항목 {cells.Count}개");
            else { Console.WriteLine($"[FAIL] 칸이 자리 안에 안 놓였다 - 칸 항목 {cells.Count}개 · 최대 각도 {(maximumItem?.RenderTransform as RotateTransform)?.Angle}"); failures++; }

            if (currentItem is not null && maximumItem is not null)
            {
                vm.SelectedCell = maximum;
                await Pump(200);

                var cellsShot = Path.Combine(Path.GetTempPath(), "minguk-script-screen-cells.png");
                Render(window, cellsShot);
                Console.WriteLine($"[INFO] 칸을 고른 미리보기를 찍었다: {cellsShot}");

                var onlyCell = maximumItem.HasAdorner && !currentItem.HasAdorner && !item.HasAdorner
                               && ReferenceEquals(vm.SelectedRegion, region) && ReferenceEquals(vm.SelectedRegionNode, maximum);

                if (onlyCell) Console.WriteLine("[PASS] 칸을 고르면 칸 어도너만 붙고 자리 어도너는 떨어진다(자리는 고른 채)");
                else { Console.WriteLine($"[FAIL] 칸 고르기 어도너가 틀렸다 - 칸 {maximumItem.HasAdorner} · 다른 칸 {currentItem.HasAdorner} · 자리 {item.HasAdorner} · VM 자리 {vm.SelectedRegion?.Name}"); failures++; }

                var cellAdorner = System.Windows.Documents.AdornerLayer.GetAdornerLayer(maximumItem)?.GetAdorners(maximumItem)?.FirstOrDefault();
                var rotateThumb = cellAdorner is null ? null : Descendants<Minguk.Tools.Markup.Regions.RegionRotateThumb>(cellAdorner).FirstOrDefault();

                if (cellAdorner is Minguk.Tools.Markup.Regions.RegionCellAdorner && rotateThumb is not null)
                    Console.WriteLine("[PASS] 칸 어도너는 자리 어도너와 다른 것이고 회전 손잡이가 있다");
                else { Console.WriteLine($"[FAIL] 칸 어도너가 아니거나 회전 손잡이가 없다 - {cellAdorner?.GetType().Name ?? "없음"} · 회전 {rotateThumb is not null}"); failures++; }

                // 칸 옮기기 - 자리 너비 기준 비율로 VM 에 온다. 자리 밖으로는 못 나간다.
                canvas.MoveBy(currentItem, item.Width * 0.1, 0);
                await Pump(100);
                var moved = Math.Abs(current.X - 0.1) < 0.002;

                canvas.MoveBy(currentItem, -9999, 0);
                await Pump(100);
                var pinned = Math.Abs(current.X) < 0.002;

                canvas.RotateTo(maximumItem, 45);
                await Pump(100);
                var rotated = Math.Abs(maximum.Angle - 45) < 0.01 && maximumItem.RenderTransform is RotateTransform { Angle: 45 };

                if (moved && pinned && rotated)
                    Console.WriteLine($"[PASS] 칸 끌기·돌리기가 VM 의 칸으로 온다 - 옮김 {moved} · 자리 밖 막음 {pinned} · 45° {rotated}");
                else { Console.WriteLine($"[FAIL] 칸 끌기가 틀렸다 - 옮김 {moved}({current.X:0.###}) · 자리 밖 막음 {pinned} · 돌림 {rotated}({maximum.Angle})"); failures++; }

                // 크게 확대(8·10배)해도 보이는 손잡이 자리에서 그 손잡이가 잡힌다(사용자, 2026-09-17 "테두리는 안쪽인데 마우스는 바깥쪽").
                vm.SelectedCell = current;
                foreach (var zoom in new[] { 8.0, 10.0 })
                {
                    vm.PreviewZoom = zoom;
                    await Pump(300);
                    // 칸 오른쪽 아래 모서리를 미리보기 뷰포트 가운데로 스크롤한다(편집기 스크롤은 Focusable=False 라 BringIntoView 가 안 먹는다).
                    var scroller = FindAncestor<System.Windows.Controls.ScrollViewer>(canvas);
                    if (scroller is not null)
                    {
                        var at = currentItem.TranslatePoint(new Point(currentItem.ActualWidth, currentItem.ActualHeight), scroller);
                        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + at.X - (scroller.ViewportWidth / 2));
                        scroller.ScrollToVerticalOffset(scroller.VerticalOffset + at.Y - (scroller.ViewportHeight / 2));
                        await Pump(300);
                    }

                    var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(currentItem);
                    var adorner = layer?.GetAdorners(currentItem)?.FirstOrDefault();
                    var misses = new List<string>();

                    string HitAt(Point inWindow)
                    {
                        for (var d = window.InputHitTest(inWindow) as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
                        {
                            if (d is Minguk.Tools.Markup.Regions.RegionResizeThumb r) return $"{r.HorizontalAlignment}/{r.VerticalAlignment}";
                            if (d is Minguk.Tools.Markup.Regions.RegionMoveThumb) return "옮기기";
                            if (d is Minguk.Tools.Markup.Regions.RegionRotateThumb) return "회전";
                        }
                        return "없음(" + (window.InputHitTest(inWindow)?.GetType().Name ?? "-") + ")";
                    }

                    // 뷰포트 안에 보이는 네모 손잡이만(회전 손잡이 줄기는 폭이 2 미만이라 뺀다). 8배면 칸이 뷰포트보다 커 나머지는 화면 밖이다.
                    var view = scroller is null ? Rect.Empty : new Rect(scroller.TranslatePoint(new Point(0, 0), window), new Size(scroller.ViewportWidth, scroller.ViewportHeight));
                    var grips = (adorner is null ? [] : Descendants<System.Windows.Shapes.Rectangle>(adorner))
                        .Where(r => r.Fill is not null && r.ActualWidth * zoom is > 4 and < 20 && r.ActualHeight * zoom is > 4 and < 20)
                        .Where(r => view.Contains(r.TranslatePoint(new Point(r.ActualWidth / 2, r.ActualHeight / 2), window)))
                        .ToList();

                    foreach (var grip in grips)
                    {
                        var got = HitAt(grip.TranslatePoint(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2), window));
                        var want = $"{(grip.HorizontalAlignment == HorizontalAlignment.Center ? HorizontalAlignment.Stretch : grip.HorizontalAlignment)}/{(grip.VerticalAlignment == VerticalAlignment.Center ? VerticalAlignment.Stretch : grip.VerticalAlignment)}";
                        if (got != want) misses.Add($"손잡이 {want} → {got}");
                    }

                    // 오른쪽 변(모서리에서 화면 40px 위 - 뷰포트 안)에서 화면 px 로 안팎.
                    var toScreen = currentItem.TranslatePoint(new Point(1, 0), window) - currentItem.TranslatePoint(new Point(0, 0), window);
                    var edge = currentItem.TranslatePoint(new Point(currentItem.ActualWidth, currentItem.ActualHeight), window) - new Vector(0, 40);
                    var band = string.Join(" ", new[] { -10, -6, -3, 0, 3, 6, 10 }.Select(dx => $"{dx:+0;-0;0}:{HitAt(new Point(edge.X + dx, edge.Y))}"));
                    var inside = HitAt(new Point(edge.X - 3, edge.Y)) == "Right/Stretch" && HitAt(new Point(edge.X + 3, edge.Y)) == "Right/Stretch" && HitAt(new Point(edge.X - 10, edge.Y)) == "옮기기";

                    if (grips.Count >= 1 && misses.Count == 0 && inside)
                        Console.WriteLine($"[PASS] 배율 {zoom} 칸 - 뷰포트에 보이는 손잡이 {grips.Count}개 자리에서 그 손잡이가 잡히고, 변 안팎 3px 은 크기·10px 안은 옮기기 ({band})");
                    else { Console.WriteLine($"[FAIL] 배율 {zoom} 칸 - 손잡이 {grips.Count}개 · 어긋남 [{string.Join(", ", misses)}] · 변 {band} · 항목 1px = 화면 {toScreen.X:0.##}px · 역수 {currentItem.InverseZoom}"); failures++; }
                }

                vm.PreviewZoom = 1;
                vm.SelectedCell = null;
                vm.SelectedRegion = region;
                await Pump(200);
            }
        }

        // 켜는 조건은 하나 - 영역 보기 켬 + 입력 전달 끔(2026-09-15). 전달을 켜거나 영역 보기를 끄면 편집기가 빠진다(클릭은 게임 몫).
        vm.IsInputForwardingEnabled = true;
        await Pump(100);

        var forwardingOff = !canvas.IsHitTestVisible && canvas.Visibility == Visibility.Collapsed && !item.HasAdorner && !vm.IsRegionEditingActive;

        vm.IsInputForwardingEnabled = false;
        vm.SelectedRegion = region;
        await Pump(200);

        var backOn = canvas.IsHitTestVisible && item.HasAdorner && vm.IsRegionEditingActive;

        vm.ShowRegions = false;
        await Pump(100);

        var hiddenOff = !canvas.IsHitTestVisible && !item.HasAdorner && !vm.IsRegionEditingActive;

        if (forwardingOff && backOn && hiddenOff)
            Console.WriteLine("[PASS] 자리 편집기는 영역 보기 켬 + 입력 전달 끔일 때만 돈다(전달 켜면·영역 보기 끄면 빠지고 어도너도 진다)");
        else { Console.WriteLine($"[FAIL] 자리 편집기 켜는 조건이 틀렸다 - 전달 켬에서 빠짐 {forwardingOff} · 다시 켜짐 {backOn} · 영역 보기 끔에서 빠짐 {hiddenOff}"); failures++; }

        // 영역 패널 - 솔루션 탐색기와 같은 탭 그룹, 이름·계속 읽기 칸에서 바로 고친다(손질·언어 열은 PP-OCRv5 로 바꾸며 없앴다 - 2026-09-16)(사용자 데이터를 안 건드리게 새 자리·이름 바꾸기는 여기서 안 누른다).
        {
            // 영역은 아래 탭 줄(오류 목록·출력과 같은 그룹) 맨 왼쪽이다(사용자, 2026-09-16 - 칸이 늘어 옆 패널에는 좁고, 탭이 있다는 것을 알아채게).
            // 안 고른 탭은 시각 트리에 없다 - 도킹의 항목 목록으로 본다.
            var regionsPanel = Descendants<DevExpress.Xpf.Docking.LayoutPanel>(window).FirstOrDefault(p => p.Name == "RegionsPanel");
            var bottom = regionsPanel?.Parent as DevExpress.Xpf.Docking.TabbedGroup;
            var sameGroup = bottom is not null
                            && bottom.Items.Any(i => i.Name == "ErrorListPanel")
                            && bottom.Items.IndexOf(regionsPanel!) == 0;

            var grid = (regionsPanel?.Content as DependencyObject) is { } content
                ? Descendants<DevExpress.Xpf.Grid.GridControl>(content).FirstOrDefault()
                : null;
            // 트리 - 자리 줄 밑에 칸 줄(자리.칸).
            var tree = grid?.View as DevExpress.Xpf.Grid.TreeListView;
            var regionNode = tree?.Nodes.FirstOrDefault(n => ReferenceEquals(n.Content, region));
            var treeOk = regionNode is not null && regionNode.Nodes.Count == region.Cells.Count && region.Cells.Count > 1;

            if (treeOk) Console.WriteLine($"[PASS] 영역 패널은 트리 - 「{region.Name}」 밑에 칸 {regionNode!.Nodes.Count}줄");
            else { Console.WriteLine($"[FAIL] 영역 패널 트리가 틀렸다 - 트리 {tree is not null} · 자리 줄 {regionNode is not null} · 칸 줄 {regionNode?.Nodes.Count}"); failures++; }

            // 열 너비는 내용에 맞추되 50px 씩 여유를 더한다(사용자, 2026-09-16 "꽉 채운 너비는 별로" · 2026-09-18 "너무 빽빽해") -
            // BestFitColumnsWithPadding 은 고정 픽셀이라(Auto 가 아니다) 값을 아예 못 재면(레이아웃 전) 0 폭인 열이 남는다.
            // 하네스는 OnLoaded 를 안 거쳐 그 안에서 부르는 것을 직접 부른다.
            if (grid is not null)
            {
                typeof(ScriptStudioViewModel).GetMethod("FitRegionsGridColumns", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, null);
                await Pump(200);

                var visible = grid.Columns.Where(c => c.Visible).ToList();
                var widths = visible.Select(c => $"{c.FieldName}:{c.Width.UnitType}={c.ActualWidth:0}").ToList();
                if (visible.All(c => c.Width.UnitType == DevExpress.Xpf.Grid.GridColumnUnitType.Pixel && c.ActualWidth > 50))
                    Console.WriteLine($"[PASS] 영역 그리드 열 너비는 내용에 맞추고 50px 여유를 더한다 - {string.Join(", ", widths)}");
                else { Console.WriteLine($"[FAIL] 영역 그리드 열 너비가 이상하다 - {string.Join(", ", widths)}"); failures++; }

                // OCR 엔진 콤보 - CellTemplate 안에서는 View.DataContext 로 다시 잡아야 목록이 뜬다(사용자, 2026-09-18 "그리드에 OCR 리스트 안나와").
                var ocrCombo = Descendants<DevExpress.Xpf.Editors.ComboBoxEdit>(grid).FirstOrDefault(c => c.ValueMember == "EngineName");
                var ocrCount = (ocrCombo?.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Count() ?? 0;

                if (ocrCombo is not null && ocrCount == vm.RegionOcrEngineOptions.Count)
                    Console.WriteLine($"[PASS] 영역 그리드 OCR 엔진 콤보에 목록이 뜬다 - {ocrCount}개");
                else { Console.WriteLine($"[FAIL] 영역 그리드 OCR 엔진 콤보 목록이 안 뜬다 - 찾음 {ocrCombo is not null} · 개수 {ocrCount}"); failures++; }
            }

            // 트리도 열 머리글이 기본으로 보인다(사용자, 2026-09-16 - BaseTreeListView 가 늘 꺼 영역 그리드에 머리글이 없었다).
            // 열이 하나뿐인 솔루션 탐색기는 XAML 에서 끈다.
            if (grid?.View is DevExpress.Xpf.Grid.TreeListView regionView)
            {
                var explorer = Descendants<DevExpress.Xpf.Grid.TreeListView>(window).FirstOrDefault(v => v.KeyFieldName == "Id");
                var regionHeaders = regionView.ShowColumnHeaders;
                var explorerHeaders = explorer?.ShowColumnHeaders;

                if (regionHeaders && explorerHeaders == false) Console.WriteLine("[PASS] 영역 그리드는 열 머리글이 보이고, 솔루션 탐색기는 안 보인다");
                else { Console.WriteLine($"[FAIL] 열 머리글 - 영역 {regionHeaders} · 솔루션 탐색기 {explorerHeaders?.ToString() ?? "(못 찾음)"}"); failures++; }
            }

            // 미리보기에서 고르면 트리 줄도 따라간다(사용자, 2026-09-16). 캔버스가 누를 때 바꾸는 것과 같은 속성으로 고른다.
            if (grid is not null && region.Cells.Count > 1)
            {
                var cellCanvas = Descendants<Minguk.Tools.Markup.Regions.RegionCanvas>(window).FirstOrDefault();
                var lastCell = region.Cells[^1];

                if (cellCanvas is not null) cellCanvas.SelectedCell = lastCell;
                await Pump(200);
                var cellRow = ReferenceEquals(grid.CurrentItem, lastCell);

                if (cellCanvas is not null) { cellCanvas.SelectedCell = null; cellCanvas.SelectedRegion = region; }
                await Pump(200);
                var regionRow = ReferenceEquals(grid.CurrentItem, region);

                if (cellCanvas is not null && cellRow && regionRow) Console.WriteLine("[PASS] 미리보기에서 칸·자리를 고르면 트리의 그 줄이 선택된다");
                else { Console.WriteLine($"[FAIL] 미리보기 선택이 트리 줄로 안 간다 - 칸 {cellRow} · 자리 {regionRow} · 지금 줄 {grid.CurrentItem}"); failures++; }
            }

            var editable = grid is not null
                           && grid.Columns["Name"] is { AllowEditing: not DevExpress.Utils.DefaultBoolean.False }
                           && grid.Columns["KeepReading"] is not null
                           && grid.Columns["LastText"] is { AllowEditing: DevExpress.Utils.DefaultBoolean.False }
                           && grid.Columns["Preprocessor"] is null
                           && grid.Columns["Language"] is null
                           && grid.Columns["X"] is { Visible: false };

            if (sameGroup && editable)
                Console.WriteLine("[PASS] 영역 패널이 아래 탭 줄 맨 왼쪽이고, 그리드는 이름·계속 읽기·읽은 글자(손질·언어 없음, 자리 열은 숨김)");
            else { Console.WriteLine($"[FAIL] 영역 패널 자리·그리드가 틀렸다 - 아래 탭 맨 왼쪽 {sameGroup} · 그리드 {grid is not null} · 칸 {editable}"); failures++; }
        }

        vm.Regions.Remove(region);
        vm.PreviewImage = null;

        return failures;
    }

    /// <summary>
    /// 문서 탭 여러 줄(사용자, 2026-09-19 "여러 행 탭은 안되는데?") - 저장해 둔 배치를 되살리면 그때의 한 줄 스크롤로 덮여 XAML 값이 안 먹었다.
    /// 옛 배치(한 줄)를 저장했다 되살린 뒤에도 여러 줄인지 본다.
    /// </summary>
    private static int CheckMultiLineTabs(Window window)
    {
        var dock = Descendants<DevExpress.Xpf.Docking.DockLayoutManager>(window).FirstOrDefault(d => d.GetItem("ProjectDocumentGroup") is not null);

        if (dock?.GetItem("ProjectDocumentGroup") is not DevExpress.Xpf.Docking.DocumentGroup group)
        {
            Console.WriteLine("[FAIL] 문서 탭 여러 줄 - 문서 탭 그룹을 못 찾았다");
            return 1;
        }

        var property = DevExpress.Xpf.Docking.DocumentGroup.TabHeaderLayoutTypeProperty;
        var loaded = group.TabHeaderLayoutType.ToString();

        // 옛 배치처럼 - 한 줄로 저장해 두고 여러 줄로 바꾼 뒤 되살린다.
        group.SetValue(property, Enum.Parse(property.PropertyType, "Scroll"));
        using var stream = new System.IO.MemoryStream();
        dock.SaveLayoutToStream(stream);
        group.SetValue(property, Enum.Parse(property.PropertyType, "MultiLine"));

        stream.Position = 0;
        dock.RestoreLayoutFromStream(stream);
        var overwritten = (dock.GetItem("ProjectDocumentGroup") as DevExpress.Xpf.Docking.DocumentGroup)?.TabHeaderLayoutType;

        ScriptStudioViewModel.ApplyDocumentTabLayout(dock);
        var after = (dock.GetItem("ProjectDocumentGroup") as DevExpress.Xpf.Docking.DocumentGroup)?.TabHeaderLayoutType;

        var ok = loaded == "MultiLine" && after?.ToString() == "MultiLine";

        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] 문서 탭 여러 줄 - 뜰 때 {loaded} · 옛 배치 되살리면 {overwritten} · 다시 건 뒤 {after}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// 솔루션 탐색기 펼침 기억(사용자, 2026-09-19 "펼쳐놨던거 기억해서 복구해줘") - 기본은 펼침, 사람이 접으면 적어 두고, 트리를 다시 만들어도 접힌 채로 나온다.
    /// </summary>
    private static async System.Threading.Tasks.Task<int> CheckExplorerExpansion(Window window, ScriptStudioViewModel vm)
    {
        var view = Descendants<DevExpress.Xpf.Grid.TreeListView>(window).FirstOrDefault(v => v.KeyFieldName == "Id");
        var workspace = vm.Script.Project;
        var root = workspace.Nodes.FirstOrDefault(n => n.Id == ScriptProjectWorkspace.RootId);

        if (view is null || root is null)
        {
            Console.WriteLine($"[FAIL] 탐색기 펼침 - 트리 {(view is null ? "못 찾음" : "있음")} · 프로젝트 줄 {(root is null ? "없음" : "있음")}");
            return 1;
        }

        bool? Expanded() => view.GetNodeByContent(workspace.Nodes.FirstOrDefault(n => n.Id == ScriptProjectWorkspace.RootId))?.IsExpanded;

        var firstExpanded = Expanded();

        // 아무것도 안 접었으면 트리를 다시 만들어도 펼친 채여야 한다 - 파일 하나 지웠더니 트리가 다 접혔다(사용자, 2026-09-19).
        workspace.RebuildNodes();
        await Pump(400);
        var rebuiltExpanded = Expanded();

        // 파일이 지워지는 두 길 - (1) 디스크에서 사라져 폴더 감시가 트리를 다시 만든다, (2) 목록에서 제외. 확인 대화 상자와 휴지통을 안 거치려고
        // Delete() 대신 이 둘로 본다(하네스가 대화 상자에서 멈췄다). 임시 프로젝트 안의 파일만 건드린다.
        var project = workspace.Project!;
        var doomed = workspace.Nodes.FirstOrDefault(n => n.Kind == ScriptNodeKind.Source && n.Id != project.Entry && !n.IsExternal);
        bool? afterDelete = null;

        if (doomed is not null)
        {
            var path = project.FullPath(doomed.Id);
            workspace.SelectedNode = doomed;
            workspace.Documents.ToList().ForEach(d => { if (string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)) workspace.CloseDocument(d); });

            System.IO.File.Delete(path);
            await Pump(1500);   // 폴더 감시(300ms 묶기) 뒤 다시 만든다
            afterDelete = Expanded();
            Console.WriteLine($"[INFO] 삭제 검사 - {doomed.Name} 디스크에서 지움 · 줄 {workspace.Nodes.Count} · 뿌리 줄 {(workspace.Nodes.Any(n => n.Id == ScriptProjectWorkspace.RootId) ? "있음" : "없음")} · 펼침 {afterDelete}");
        }

        Console.WriteLine($"[{(firstExpanded == true && rebuiltExpanded == true && afterDelete != false ? "PASS" : "FAIL")}] 탐색기: 다시 만들거나 파일을 지워도 펼친 채 - 처음 {firstExpanded} · 다시 만든 뒤 {rebuiltExpanded} · 지운 뒤 {(afterDelete?.ToString() ?? "(지울 파일 없음)")}");
        var expandedOk = firstExpanded == true && rebuiltExpanded == true && afterDelete != false;

        // 지우기·다시 만들기로 줄이 새로 만들어졌으니 다시 잡는다.
        root = workspace.Nodes.First(n => n.Id == ScriptProjectWorkspace.RootId);

        // 사람이 접은 것처럼 - 트리의 접기를 부른다(NodeCollapsed 가 온다).
        view.CollapseNode(view.GetNodeByContent(root)!.RowHandle);
        await Pump(200);

        var remembered = workspace.IsCollapsed(root);

        // 파일을 더하거나 프로젝트를 바꿀 때처럼 트리를 통째로 다시 만든다.
        workspace.RebuildNodes();
        await Pump(400);

        var stillCollapsed = Expanded() == false;

        // 되돌려 둔다 - 뒤의 검사·화면 사진이 접힌 트리를 보지 않게.
        if (view.GetNodeByContent(workspace.Nodes.First(n => n.Id == ScriptProjectWorkspace.RootId)) is { } again) view.ExpandNode(again.RowHandle);
        await Pump(200);

        var ok = expandedOk && firstExpanded == true && remembered && stillCollapsed && !workspace.IsCollapsed(root);

        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] 탐색기 펼침 기억 - 처음 펼침 {firstExpanded} · 접으면 기억 {remembered} · 다시 만들어도 접힘 {stillCollapsed} · 다시 펴면 지움 {!workspace.IsCollapsed(root)}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// 도움말 패널(사용자, 2026-09-15) - 솔루션 탐색기 탭 그룹에 있고, 함수 표(ScriptApiCatalog)가 빠짐없이 들어가고, 고르면 설명·예시가 뜬다.
    /// </summary>
    private static async System.Threading.Tasks.Task<int> CheckHelpPanel(Window window, ScriptStudioViewModel vm, string output)
    {
        var failures = 0;
        var dock = Descendants<DevExpress.Xpf.Docking.DockLayoutManager>(window).FirstOrDefault();
        var help = dock?.GetItem("HelpPanel") as DevExpress.Xpf.Docking.LayoutPanel;
        var solution = dock?.GetItem("SolutionExplorerPanel") as DevExpress.Xpf.Docking.LayoutPanel;

        if (help is null) { Console.WriteLine("[FAIL] 도움말 패널이 없다"); return 1; }

        var sameGroup = solution?.Parent is DevExpress.Xpf.Docking.TabbedGroup group && ReferenceEquals(help.Parent, group);

        var rows = vm.HelpRows;
        var missing = Minguk.Tools.Input.Scripting.ScriptApiCatalog.Entries.Where(e => !rows.Any(r => r.English == e.Signature)).Select(e => e.Name).ToList();
        var others = rows.Count(r => r.Group.StartsWith("9.", StringComparison.Ordinal));

        if (sameGroup && missing.Count == 0 && others == 0)
            Console.WriteLine($"[PASS] 도움말 패널 - 솔루션 탐색기 탭 그룹 · {rows.Count}줄 · 함수 {Minguk.Tools.Input.Scripting.ScriptApiCatalog.Entries.Count}개 빠짐없이 분류됨");
        else { Console.WriteLine($"[FAIL] 도움말 패널 - 같은 탭 그룹 {sameGroup} · 빠진 함수 {string.Join(", ", missing)} · 분류 없는(기타) {others}줄"); failures++; }

        dock!.DockController.Activate(help);
        vm.SelectedHelpRow = rows.First(r => r.Name.StartsWith("목표(", StringComparison.Ordinal));
        await Pump(400);

        var grid = help.Content is DependencyObject content ? Descendants<DevExpress.Xpf.Grid.GridControl>(content).FirstOrDefault() : null;
        var shown = help.Content is DependencyObject body
            && Descendants<DevExpress.Xpf.Editors.TextEdit>(body).Any(t => t.IsVisible && (t.EditValue as string)?.Contains("목표()", StringComparison.Ordinal) == true);

        if (grid is not null && grid.VisibleRowCount > 0 && shown)
            Console.WriteLine($"[PASS] 도움말에서 고르면 아래에 설명·예시가 뜬다 - 보이는 줄 {grid.VisibleRowCount}");
        else { Console.WriteLine($"[FAIL] 도움말 그리드·설명이 안 보인다 - 그리드 {grid is not null} · 줄 {grid?.VisibleRowCount} · 예시 보임 {shown}"); failures++; }

        failures += VerticalAlignmentCheck.Report((FrameworkElement)window.Content, "스크립트 화면(도움말)");

        Render(window, output);
        Console.WriteLine($"[INFO] 도움말 화면을 찍었다: {output}");

        return failures;
    }

    /// <summary>
    /// 미리보기|문서 나누기(위아래 기본 · 좌우 · 자리 바꾸기)와 도구 모음 배치 저장·복원이 도는지.
    /// </summary>
    private static async System.Threading.Tasks.Task<int> CheckSplitAndBars(Window window, ScriptStudioViewModel vm)
    {
        var failures = 0;
        var dock = Descendants<DevExpress.Xpf.Docking.DockLayoutManager>(window).FirstOrDefault();
        var group = dock?.GetItem("DesignSplitGroup") as DevExpress.Xpf.Docking.LayoutGroup;
        var preview = dock?.GetItem("PreviewPanel");

        if (group is null || preview is null) { Console.WriteLine("[FAIL] 나누기 그룹이나 미리보기 판을 못 찾았다"); return 1; }

        // 그룹에는 미리보기 + 문서 그룹 둘(프로젝트·한 파일짜리)만 있어야 한다 - 솔루션 탐색기가 끼면 바꾸기에 딸려 간다.
        if (group.Orientation == System.Windows.Controls.Orientation.Horizontal && group.Items.IndexOf(preview) == 0 && group.Items.Count == 3)
            Console.WriteLine("[PASS] 기본은 미리보기 왼쪽 · 스크립트 오른쪽, 그룹 안은 셋");
        else { Console.WriteLine($"[FAIL] 기본 나누기가 다르다 - {group.Orientation}, 미리보기 자리 {group.Items.IndexOf(preview)}/{group.Items.Count}"); failures++; }

        vm.SplitVerticalCommand.Execute(null);
        vm.SwapPanesCommand.Execute(null);
        await Pump(300);

        if (group.Orientation == System.Windows.Controls.Orientation.Vertical && group.Items.IndexOf(preview) == group.Items.Count - 1 && preview.IsClosed == false)
            Console.WriteLine($"[PASS] 위아래로 바꾸고 자리를 맞바꿨다 - 미리보기가 {group.Items.Count}개 중 마지막");
        else { Console.WriteLine($"[FAIL] 위아래·바꾸기 - {group.Orientation}, 미리보기 자리 {group.Items.IndexOf(preview)}/{group.Items.Count}, 닫힘 {preview.IsClosed}"); failures++; }

        vm.SwapPanesCommand.Execute(null);
        vm.SplitHorizontalCommand.Execute(null);
        await Pump(200);

        if (group.Orientation == System.Windows.Controls.Orientation.Horizontal && group.Items.IndexOf(preview) == 0)
            Console.WriteLine("[PASS] 되돌리면 처음 자리");
        else { Console.WriteLine($"[FAIL] 되돌리기 - {group.Orientation}, 미리보기 자리 {group.Items.IndexOf(preview)}"); failures++; }

        // 도구 모음 배치 - 저장한 것을 되살릴 수 있고, 도구 모음 이름이 들어 있어야 자리를 되찾는다.
        var manager = Descendants<DevExpress.Xpf.Bars.BarManager>(window).FirstOrDefault();

        if (manager is null) { Console.WriteLine("[FAIL] BarManager 가 없다"); return failures + 1; }

        using var stream = new MemoryStream();
        manager.SaveLayoutToStream(stream);
        var xml = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        stream.Position = 0;
        manager.RestoreLayoutFromStream(stream);
        await Pump(200);

        var named = new[] { "StandardBar", "DebugBar", "CaptureBar", "RecognitionBar", "InputBar" }.Where(xml.Contains).ToList();

        Console.WriteLine($"[INFO] 관리자 Bars {manager.Bars.Count} · 배치 XML {xml.Length}자");

        if (named.Count == 5) Console.WriteLine($"[PASS] 도구 모음 배치 저장·복원 - {stream.Length:N0}바이트, 이름 5개");
        else { Console.WriteLine($"[FAIL] 도구 모음 배치에 이름이 다 안 들어 있다 - {string.Join(",", named)} ({stream.Length}바이트)"); failures++; }

        // 일시정지는 둘 - 디버그에는 실행 일시정지(IsPaused), 캡처에는 캡처 일시정지(IsCapturePaused)(사용자, 2026-09-17).
        {
            string? PauseBinding(string bar, string content) => manager.Bars.FirstOrDefault(b => b.Name == bar)?.Items.OfType<DevExpress.Xpf.Bars.BarCheckItem>()
                .FirstOrDefault(i => Equals(i.Content, content)) is { } item
                ? System.Windows.Data.BindingOperations.GetBinding(item, DevExpress.Xpf.Bars.BarCheckItem.IsCheckedProperty)?.Path.Path
                : null;

            var run = PauseBinding("DebugBar", "실행 일시정지");
            var capture = PauseBinding("CaptureBar", "캡처 일시정지");

            // 캡처가 안 돌면 캡처 일시정지는 안 걸린다, 실행 일시정지는 캡처를 건드리지 않는다.
            vm.IsCapturePaused = true;
            var idleRefused = !vm.IsCapturePaused;
            vm.IsPaused = true;
            await Pump(50);
            var runOnly = !vm.IsPaused && !vm.IsCapturePaused;

            var ok = run == "IsPaused" && capture == "IsCapturePaused" && idleRefused && runOnly;
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] 일시정지 둘 - 디버그 「실행 일시정지」({run}) · 캡처 「캡처 일시정지」({capture}) · 캡처 꺼져 있으면 안 걸림 {idleRefused} · 실행 없으면 안 걸림 {runOnly}");
            if (!ok) failures++;
        }

        return failures;
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

    private static async System.Threading.Tasks.Task Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < milliseconds)
            await Dispatcher.Yield(DispatcherPriority.Background);
    }

    private static void Render(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            // 투명 바탕이면 PNG 에서 검게 보인다 - 흰 바탕을 깐다.
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
        }

        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class CollectingListener(List<string> errors) : TraceListener
    {
        private string _pending = string.Empty;

        public override void Write(string? message) => _pending += message;

        public override void WriteLine(string? message)
        {
            var line = _pending + message;
            _pending = string.Empty;

            // DevExpress 내부 틀의 흔한 경고는 우리 것이 아니다 - 우리 VM·문서 경로만 남긴다.
            if (line.Contains("BindingExpression") && (line.Contains("ViewModel") || line.Contains("Script") || line.Contains("Document") || line.Contains("Settings")))
                errors.Add(line.Length > 400 ? line[..400] : line);
        }
    }
}
