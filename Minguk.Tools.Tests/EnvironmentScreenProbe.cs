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

using Minguk.Tools.Helper;
using Minguk.Tools.Training.Views;

namespace Minguk.Tools.Tests;

/// <summary>
/// 환경 화면을 화면 밖 창에 띄워 바인딩 오류를 모으고 PNG 로 찍는다 - <c>--environment-screen [--out=경로.png]</c>.
/// </summary>
/// <remarks>
/// 학습 경로·작업공간 두 칸이 붙어 있는지(사이 간격)를 재어 적는다 - 폼 칸 배치는 눈으로 봐야 알아서 찍는다.
/// 폴더를 고르거나 설정을 쓰지 않는다. 커서는 안 가져간다.
/// </remarks>
public static class EnvironmentScreenProbe
{
    public static int Run(string[] args)
    {
        var output = Program.ArgValue(args, "--out=") ?? Path.Combine(Path.GetTempPath(), "minguk-environment-screen.png");
        var failures = 0;
        var bindingErrors = new List<string>();

        CompatibilitySettings.UseLightweightThemes = true;
        LightweightThemeManager.AllowStandardControlsTheming = false;
        DevExpress.Xpf.Grid.DataControlBase.AllowInfiniteGridSize = true;

        _ = new System.Threading.Timer(_ =>
        {
            Console.WriteLine("[FAIL] 환경 화면 - 60초 안에 끝나지 않았다.");
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
                var view = new TrainingEnvironmentView();
                window = new Window
                {
                    Width = 1000, Height = 600, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                    Content = view
                };
                window.Show();

                await Pump(800);

                if (view.DataContext is DevExpress.Mvvm.ISupportParentViewModel child) child.ParentViewModel = new object();

                await Pump(1500);

                var edits = Descendants<DevExpress.Xpf.Editors.ButtonEdit>(window).ToList();

                if (edits.Count < 2)
                {
                    Console.WriteLine($"[FAIL] 폴더 칸 둘을 못 찾았다 ({edits.Count}개)");
                    failures++;
                }
                else
                {
                    var first = edits[0].TransformToAncestor(view).Transform(new Point(0, edits[0].ActualHeight)).Y;
                    var second = edits[1].TransformToAncestor(view).Transform(new Point(0, 0)).Y;
                    var gap = second - first;

                    Console.WriteLine($"[INFO] 학습 경로 칸 밑선 {first:0}px · 작업공간 칸 윗선 {second:0}px · 사이 {gap:0}px");

                    if (gap > 1) { Console.WriteLine($"[FAIL] 두 칸 사이가 {gap:0}px 떠 있다"); failures++; }
                    else Console.WriteLine($"[PASS] 두 칸이 붙어 있다 - 사이 {gap:0}px");
                }

                var mine = bindingErrors.Where(e => e.Contains("Training")).Distinct().ToList();
                if (mine.Count > 0)
                {
                    Console.WriteLine($"[FAIL] 바인딩 오류 {mine.Count}건");
                    foreach (var error in mine.Take(10)) Console.WriteLine("       " + error);
                    failures++;
                }
                else Console.WriteLine("[PASS] 환경 화면 바인딩 오류 없음");

                // 그리드 칸 너비는 내용에 맞춘다(사용자, 2026-09-17) - 보이는 열이 모두 Auto 이고, 긴 글(자리)은 머리글보다 훨씬 넓다.
                foreach (var grid in Descendants<DevExpress.Xpf.Grid.GridControl>(window))
                {
                    var columns = grid.Columns.Where(c => c.Visible && c.Header is not null).ToList();
                    var detail = string.Join(" · ", columns.Select(c => $"{c.Header} {c.Width.UnitType} {c.ActualWidth:0}"));
                    var path = columns.FirstOrDefault(c => c.FieldName == "Path");

                    if (columns.All(c => c.Width.UnitType == DevExpress.Xpf.Grid.GridColumnUnitType.Auto) && path is { ActualWidth: > 120 })
                        Console.WriteLine($"[PASS] 그리드 칸 너비가 내용에 맞는다 - {detail}");
                    else { Console.WriteLine($"[FAIL] 그리드 칸 너비가 내용에 안 맞는다 - {detail}"); failures++; }
                }

                failures += VerticalAlignmentCheck.Report((FrameworkElement)window.Content, "환경 화면");

                // 폴더 칸 안의 폴더 그림 - 버튼 칸의 가로·세로 가운데에 있어야 한다(사용자 2026-09-14).
                foreach (var image in Descendants<System.Windows.Controls.Image>(window).Where(i => i.IsVisible && i.ActualWidth > 0))
                {
                    FrameworkElement? button = null;
                    for (DependencyObject? d = VisualTreeHelper.GetParent(image); d is not null; d = VisualTreeHelper.GetParent(d))
                        if (d is System.Windows.Controls.Primitives.ButtonBase b) { button = b; break; }

                    if (button is null) continue;

                    var origin = image.TransformToAncestor(button).Transform(new Point(0, 0));
                    var dx = origin.X + image.ActualWidth / 2 - button.ActualWidth / 2;
                    var dy = origin.Y + image.ActualHeight / 2 - button.ActualHeight / 2;

                    Console.WriteLine($"[INFO] 폴더 그림: 버튼 {button.ActualWidth:0}x{button.ActualHeight:0} · 가운데에서 가로 {dx:+0.0;-0.0}px 세로 {dy:+0.0;-0.0}px");

                    if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) { Console.WriteLine("[FAIL] 폴더 그림이 버튼 가운데에 있지 않다"); failures++; }
                }

                Render(window, output);
                Console.WriteLine($"[INFO] 화면을 찍었다: {output}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 환경 화면 - {ex.GetType().Name}: {ex.Message}");
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
