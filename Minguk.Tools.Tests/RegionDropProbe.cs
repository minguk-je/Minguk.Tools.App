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
using Minguk.Tools.Markup.Regions;
using Minguk.Tools.Projects;
using Minguk.Tools.ViewModels;
using Minguk.Tools.Views;

namespace Minguk.Tools.Tests;

/// <summary>
/// <c>--region-drop</c> - 영역을 끌어 놓는 순간 미리보기 패널이 움직이는지. 매 프레임 패널 위치와 둘레 요소 높이를 적는다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-24) "Adorner 클릭해서 뭔가를 하면 미리보기 화면이 내려갔다가 위로 올라가는 증상" - 끌어 옮기거나 크기를 바꾼 뒤 <b>놓을 때</b>,
/// 100% 에서도, 미리보기 패널이 <b>통째로</b>. 놓을 때는 <c>RegionEditCommand</c>(Completed) 가 저장·RegionsRevision·상태 줄을 바꾼다 - 그것을 그대로 태운다.
/// 저장이 regions.json 을 쓰므로 임시 솔루션을 건다(사용자 작업공간을 건드리지 않게).
///
/// <b>여기서는 재현되지 않았다</b> - 원인은 실제 앱에 넣은 진단 기록으로 찾았다: 미리보기 패널 위쪽은 그대로인데 높이가 892 ↔ 893 으로 0.3~1초마다
/// 오르내렸고(확대 2.7배라 그림이 몇 px 씩 흔들렸다), 상태 표시줄 높이가 담긴 글에 따라 1px 달라지는 탓이었다. 상태 줄 높이를 글꼴 크기에서 못 박자
/// 892 로 고정됐다(<c>FontSizeToBarHeightConverter</c>). 이 검사 창의 글꼴 환경에서는 상태 줄이 늘 24 라 그 흔들림이 안 난다 - 놓을 때 둘레가 그대로인지만 본다.
/// </remarks>
internal static class RegionDropProbe
{
    public static int Run(int width = 1600, int height = 1000)
    {
        var root = Path.Combine(Path.GetTempPath(), "minguk-region-drop-" + Guid.NewGuid().ToString("N"));
        var solution = Solution.Create(root, "검사");
        var projectFolder = Path.Combine(solution.Directory, "사냥");

        Directory.CreateDirectory(projectFolder);
        File.WriteAllText(Path.Combine(projectFolder, "사냥.mtsproj"), "{}");
        solution.Add(Path.Combine(projectFolder, "사냥.mtsproj"));
        SolutionWorkspace.Use(solution, remember: false);

        CompatibilitySettings.UseLightweightThemes = true;
        LightweightThemeManager.AllowStandardControlsTheming = false;
        DevExpress.Xpf.Grid.DataControlBase.AllowInfiniteGridSize = true;
        UserPreferencesHelper.EnsureDefaults();
        UserPreferencesHelper.ApplyTheme();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        foreach (var name in new[] { "ControlTemplate", "DataTemplate", "GridColumnStyle", "Style" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Minguk.Base;component/Resource/{name}.xaml") });

        app.Resources["BaseFontSize"] = 12d;
        app.Resources["EditorFontSize"] = 13d;
        app.Resources["BaseFontFamily"] = new FontFamily("D2Coding, Malgun Gothic");

        var exit = 1;

        _ = new System.Threading.Timer(_ => { Console.WriteLine("[FAIL] 60초 안에 안 끝났다"); Environment.Exit(2); }, null, 60_000, System.Threading.Timeout.Infinite);

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            Window? window = null;

            try
            {
                var view = new ScriptStudioView();

                // 앱과 같게 - 스크립트 화면은 솔루션 화면(AutomationMainView)의 dxlc:LayoutControl 안(위 칸 한 줄 + 아래)에 들어간다.
                // LayoutControl 은 스스로 스크롤하는 컨트롤이라, 창에 바로 얹으면 안쪽의 "보이게 하라"(BringIntoView)를 받는 것이 없어 재현이 안 된다.
                var host = new DevExpress.Xpf.LayoutControl.LayoutControl { Orientation = System.Windows.Controls.Orientation.Vertical, Padding = new Thickness(0), Margin = new Thickness(-1) };
                host.Children.Add(new System.Windows.Controls.TextBlock { Text = "작업공간 · 솔루션 · 프로젝트", Margin = new Thickness(4, 4, 4, 2), VerticalAlignment = VerticalAlignment.Top });
                host.Children.Add(view);

                window = new Window { Width = width, Height = height, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = host };
                window.Show();
                await Pump(1500);

                var vm = (ScriptStudioViewModel)view.DataContext;

                vm.PreviewImage = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Bgr32, null, new byte[1920 * 1080 * 4], 1920 * 4);
                vm.ShowPreview = true;
                vm.IsInputForwardingEnabled = false;
                vm.NewRegionCommand.Execute(null);
                await Pump(600);

                var region = vm.SelectedRegion ?? vm.Regions.First();
                var dock = Descendants<DevExpress.Xpf.Docking.DockLayoutManager>(window).First();
                var panel = (FrameworkElement)dock.GetItem("PreviewPanel");
                var preview = Descendants<Minguk.Tools.Views.Parts.CapturePreviewPanel>(window).First();

                // 둘레 요소 - 이 가운데 높이가 바뀌는 것이 패널을 민다.
                var watched = new List<(string Name, FrameworkElement Element)>
                {
                    ("도구 모음(위)", Descendants<DevExpress.Xpf.Bars.BarContainerControl>(window).First(b => b.Name == "ToolbarContainer")),
                    ("메뉴", Descendants<DevExpress.Xpf.Bars.MainMenuControl>(window).First()),
                    ("상태 표시줄", Descendants<DevExpress.Xpf.Bars.StatusBarControl>(window).First()),
                    ("미리보기 도구 줄", Descendants<Minguk.Tools.Views.Parts.PreviewForwardBar>(window).First()),
                    ("미리보기 판", preview)
                };

                double Top(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), window).Y;

                string Snapshot() => $"패널 위 {Top(panel):0.0} · 판 위 {Top(preview):0.0} · " +
                                     string.Join(" · ", watched.Select(w => $"{w.Name} {w.Element.ActualHeight:0.0}"));

                var before = Snapshot();
                Console.WriteLine($"[INFO] 창 {width}x{height} · 놓기 전: {before}");

                // 진짜 손잡이에 마우스 누름·뗌을 태운다 - 명령만 태우면 재현이 안 됐다(Thumb 이 누를 때 제 초점·마우스 잡기를 한다).
                var canvas = Descendants<RegionCanvas>(window).First();
                var item = canvas.Items.First(i => ReferenceEquals(i.Region, region));
                var adorner = System.Windows.Documents.AdornerLayer.GetAdornerLayer(item)?.GetAdorners(item)?.OfType<RegionResizeAdorner>().FirstOrDefault();
                var grip = adorner is null ? null : Descendants<RegionResizeThumb>(adorner).FirstOrDefault(t => t.HorizontalAlignment == HorizontalAlignment.Right && t.VerticalAlignment == VerticalAlignment.Bottom);

                if (grip is null) { Console.WriteLine($"[FAIL] 크기 손잡이를 못 찾았다(어도너 {adorner is not null})"); return; }

                var samples = new List<(long Ms, string Text)>();
                var watch = Stopwatch.StartNew();

                async System.Threading.Tasks.Task Sample(int ms, string phase)
                {
                    var until = watch.ElapsedMilliseconds + ms;

                    while (watch.ElapsedMilliseconds < until)
                    {
                        await Dispatcher.Yield(DispatcherPriority.Render);
                        await System.Threading.Tasks.Task.Delay(8);
                        samples.Add((watch.ElapsedMilliseconds, $"{phase} · {Snapshot()}"));
                    }
                }

                grip.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseDownEvent });
                Console.WriteLine($"[INFO] 누른 뒤 초점: {System.Windows.Input.Keyboard.FocusedElement?.GetType().Name ?? "없음"} · 끄는 중 {grip.IsDragging}");
                await Sample(400, "누름");

                grip.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseUpEvent });
                vm.RegionEditCommand.Execute(new RegionEdit(region, new Rect(region.X + 0.05, region.Y, region.Width, region.Height), true));
                await Sample(1500, "뗌");

                var changes = samples.Where((s, i) => i == 0 ? s.Text[(s.Text.IndexOf('·') + 2)..] != before : s.Text[(s.Text.IndexOf('·') + 2)..] != samples[i - 1].Text[(samples[i - 1].Text.IndexOf('·') + 2)..]).ToList();

                if (changes.Count == 0)
                {
                    Console.WriteLine($"[PASS] 놓은 뒤 1.5초 동안 미리보기 패널·둘레 요소가 그대로다 ({samples.Count}번 잼)");
                    exit = 0;
                }
                else
                {
                    Console.WriteLine($"[FAIL] 놓은 뒤 {changes.Count}번 바뀌었다:");
                    foreach (var (ms, text) in changes.Take(20)) Console.WriteLine($"       {ms,5}ms  {text}");
                }

                Console.WriteLine($"[INFO] 상태 줄: {vm.StatusText}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                window?.Close();
                SolutionWorkspace.Close();
                try { Directory.Delete(root, recursive: true); } catch (IOException) { }
                app.Shutdown();
            }
        });

        app.Run();

        return exit;
    }

    private static async System.Threading.Tasks.Task Pump(int ms)
    {
        var until = Environment.TickCount64 + ms;

        while (Environment.TickCount64 < until)
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            await System.Threading.Tasks.Task.Delay(10);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T found) yield return found;

            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }
}
