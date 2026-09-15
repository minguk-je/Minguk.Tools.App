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
                var project = vm.Script.Project.CreateProject(Path.Combine(folder, "사격장.mtsproj"), "var 몹 = 목표();\n조준(몹);\n없는함수();\n");
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

                failures += await CheckPreviewZoom(window, vm);
                failures += await CheckRegionCanvas(window, vm);
                failures += await CheckSplitAndBars(window, vm);
                failures += VerticalAlignmentCheck.Report((FrameworkElement)window.Content, "스크립트 화면");

                Render(window, output);
                Console.WriteLine($"[INFO] 화면을 찍었다: {output}");
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
        vm.IsRegionPicking = true;
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
            Console.WriteLine("[PASS] 영역 지정 중 휠 한 칸 - 배율 1 → 1.25");
        else { Console.WriteLine($"[FAIL] 영역 지정 중 휠을 굴렸는데 배율이 {vm.PreviewZoom} 이다(1.25 여야 한다) - 그림 위 ScrollViewer 가 휠을 먹는다"); failures++; }

        vm.IsRegionPicking = false;
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
    private static async System.Threading.Tasks.Task<int> CheckRegionCanvas(Window window, ScriptStudioViewModel vm)
    {
        var failures = 0;

        // 캔버스가 자리를 놓으려면 그림 크기를 알아야 한다 - 잡은 화면 대신 1920x1080 짜리 빈 그림.
        vm.PreviewImage = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Bgr32, null, new byte[1920 * 1080 * 4], 1920 * 4);

        var region = new Minguk.Tools.Vision.Regions.NamedRegion { Name = "탄약", Rect = new Rect(0.5, 0.2, 0.1, 0.1) };
        vm.Regions.Add(region);
        vm.SelectedRegion = region;
        vm.IsRegionPicking = true;
        await Pump(400);

        var canvas = Descendants<Minguk.Tools.Markup.Regions.RegionCanvas>(window).FirstOrDefault();

        if (canvas is null) { Console.WriteLine("[FAIL] 영역 캔버스를 못 찾았다"); return 1; }

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

        vm.IsRegionPicking = false;
        await Pump(100);

        if (!canvas.IsHitTestVisible && canvas.Visibility == Visibility.Collapsed && !item.HasAdorner)
            Console.WriteLine("[PASS] 영역 지정을 끄면 캔버스가 마우스에서 빠지고 어도너도 진다");
        else { Console.WriteLine($"[FAIL] 껐는데 캔버스가 남아 있다 - 히트 {canvas.IsHitTestVisible} · {canvas.Visibility} · 어도너 {item.HasAdorner}"); failures++; }

        vm.Regions.Remove(region);
        vm.PreviewImage = null;

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
