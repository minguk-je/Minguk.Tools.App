using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using DevExpress.Xpf.Bars;
using DevExpress.Xpf.Core;

using Minguk.Tools.Helper;
using Minguk.Tools.Views;

namespace Minguk.Tools.Tests;

/// <summary>
/// 플레이 화면을 화면 밖 창에 띄워 도구 줄 배치·바인딩 오류·세로 정렬을 보고 PNG 로 찍는다 - <c>--play-screen [--out=경로.png]</c>.
/// </summary>
/// <remarks>
/// 도구 줄은 두 줄(윗줄 캡처·인식, 아랫줄 실행·실행 설정, 사용자 2026-09-17)이어야 하고, 앱 안 폭(약 1150px)에서 접힌 항목이 없어야 한다.
/// 게임을 잡거나 스크립트를 돌리지 않는다. 커서는 안 가져간다.
/// </remarks>
public static class PlayScreenProbe
{
    public static int Run(string[] args)
    {
        var output = Program.ArgValue(args, "--out=") ?? Path.Combine(Path.GetTempPath(), "minguk-play-screen.png");
        var failures = 0;
        var bindingErrors = new List<string>();

        CompatibilitySettings.UseLightweightThemes = true;
        LightweightThemeManager.AllowStandardControlsTheming = false;
        DevExpress.Xpf.Grid.DataControlBase.AllowInfiniteGridSize = true;

        _ = new System.Threading.Timer(_ =>
        {
            Console.WriteLine("[FAIL] 플레이 화면 - 60초 안에 끝나지 않았다.");
            Environment.Exit(2);
        }, null, 60_000, System.Threading.Timeout.Infinite);

        UserPreferencesHelper.EnsureDefaults();
        UserPreferencesHelper.ApplyTheme();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new CollectingListener(bindingErrors));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            Window? window = null;

            try
            {
                var view = new PlayView();
                window = new Window
                {
                    Width = double.TryParse(Program.ArgValue(args, "--width="), out var w) ? w : 1150, Height = 800, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                    Content = view
                };
                window.Show();

                await Pump(800);

                if (view.DataContext is DevExpress.Mvvm.ISupportParentViewModel child) child.ParentViewModel = new object();

                await Pump(1500);

                // 도구 줄 - 위에서 아래(줄), 왼쪽에서 오른쪽.
                var bars = Descendants<ToolBarControl>(window)
                    .Where(b => b.IsVisible && !string.IsNullOrEmpty(b.Caption))
                    .Select(b => (b.Caption, Origin: b.TransformToAncestor(view).Transform(new Point(0, 0))))
                    .OrderBy(b => Math.Round(b.Origin.Y / 10)).ThenBy(b => b.Origin.X)
                    .ToList();
                var rows = bars.GroupBy(b => Math.Round(b.Origin.Y / 10)).Select(g => string.Join(" · ", g.Select(b => b.Caption))).ToList();

                Console.WriteLine($"[INFO] 도구 줄: {string.Join(" / ", rows)}");

                if (rows.SequenceEqual(["캡처 · 인식", "실행 · 실행 설정"])) Console.WriteLine("[PASS] 도구 줄이 두 줄 - 윗줄 캡처·인식, 아랫줄 실행·실행 설정");
                else { Console.WriteLine("[FAIL] 도구 줄 배치가 틀렸다 (캡처 · 인식 / 실행 · 실행 설정 이어야)"); failures++; }

                // 앱 안 폭(왼쪽 메뉴를 뺀 약 1150px)에서 넘쳐 접힌 항목이 없어야 한다 - 접히면 ▸ 를 눌러야 보인다.
                foreach (var bar in Descendants<ToolBarControl>(window).Where(b => b.IsVisible && !string.IsNullOrEmpty(b.Caption)))
                {
                    var expected = bar.Items.OfType<BarItem>().Count(i => i.IsVisible);
                    // 경량 테마는 항목마다 BarItemLinkInfo 하나. 넘친 항목은 줄 안에 안 그려진다(▸ 팝업으로 간다).
                    var shown = Descendants<FrameworkElement>(bar).Count(c => c.GetType().Name == "BarItemLinkInfo" && c.IsVisible && c.ActualWidth > 0);

                    if (shown >= expected) continue;

                    Console.WriteLine($"[FAIL] {window.Width:0}px 에서 「{bar.Caption}」 도구 줄이 넘쳐 {expected - shown}개가 접혔다");
                    failures++;
                }

                var status = Descendants<StatusBarControl>(window).FirstOrDefault();
                if (status is not null && status.TransformToAncestor(view).Transform(new Point(0, status.ActualHeight)).Y >= view.ActualHeight - 1)
                    Console.WriteLine("[PASS] 상태 표시줄이 맨 아래에 있다");
                else { Console.WriteLine($"[FAIL] 상태 표시줄이 맨 아래에 없다 ({(status is null ? "없음" : "위치 틀림")})"); failures++; }

                // [설정] 창 - 화면의 WindowService 스타일 그대로 띄워 최대화 단추가 없는지 본다(사용자, 2026-09-17).
                {
                    var service = DevExpress.Mvvm.UI.Interactivity.Interaction.GetBehaviors(view).OfType<DevExpress.Mvvm.UI.WindowService>().Single(b => b.Name == "SettingsWindowService");
                    var project = Path.Combine(Path.GetTempPath(), "minguk-play-screen-" + Guid.NewGuid().ToString("N")[..8], "런");
                    Directory.CreateDirectory(project);

                    var settingsVm = Minguk.Tools.ViewModels.SolutionSettingsViewModel.CreateForPlay(project);
                    DevExpress.Mvvm.WindowServiceExtensions.Show(service, settingsVm);
                    await Pump(800);

                    var settingsWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Content is PlaySettingsView || Descendants<PlaySettingsView>(w).Any());
                    var buttons = (settingsWindow as ThemedWindow)?.ControlBoxButtonSet.ToString() ?? "(ThemedWindow 아님)";

                    if (settingsWindow is ThemedWindow themed && !themed.ControlBoxButtonSet.HasFlag(ControlBoxButtons.MaximizeRestore) && themed.ControlBoxButtonSet.HasFlag(ControlBoxButtons.Close))
                        Console.WriteLine($"[PASS] [설정] 창에 최대화 단추가 없다 - {buttons}");
                    else { Console.WriteLine($"[FAIL] [설정] 창 단추가 틀렸다 - {buttons}"); failures++; }

                    settingsWindow?.Close();
                    await Pump(200);
                    try { Directory.Delete(Path.GetDirectoryName(project)!, true); } catch (Exception) { }
                }

                var mine = bindingErrors.Where(e => e.Contains("Play")).Distinct().ToList();
                if (mine.Count > 0)
                {
                    Console.WriteLine($"[FAIL] 바인딩 오류 {mine.Count}건");
                    foreach (var error in mine.Take(10)) Console.WriteLine("       " + error);
                    failures++;
                }
                else Console.WriteLine("[PASS] 플레이 화면 바인딩 오류 없음");

                failures += VerticalAlignmentCheck.Report((FrameworkElement)window.Content, "플레이 화면");

                Render(window, output);
                Console.WriteLine($"[INFO] 화면을 찍었다: {output}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 플레이 화면 - {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                failures++;
            }
            finally
            {
                try { window?.Close(); } catch (Exception) { }
                app.Shutdown();
            }
        });

        app.Run();

        Console.WriteLine(failures == 0 ? "== 플레이 화면 통과 ==" : $"== 플레이 화면 실패 {failures}건 ==");

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
            errors.Add(_pending + message);
            _pending = string.Empty;
        }
    }
}
