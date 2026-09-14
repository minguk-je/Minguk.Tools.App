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
using Minguk.Tools.Views;

namespace Minguk.Tools.Tests;

/// <summary>
/// 라벨링 화면을 화면 밖 창에 띄워 아래 학습 패널의 높이가 내용에 맞는지 잰다 - <c>--labeling-screen [--out=경로.png]</c>.
/// </summary>
/// <remarks>
/// 학습 패널은 도킹 패널이라 Auto 높이가 없어 내용의 ActualHeight 를 픽셀 높이로 묶는다. 그 묶음이 틀리면 두 가지로 샌다 -
/// 내용보다 작아 아래 줄이 잘리거나(예전에 Grid 행에서 셋째 줄이 사라진 것), 내용이 뷰포트로 늘어나 배치마다 자라거나
/// (실측: "아래가 점점 넓어졌다"). 둘 다 눈으로 봐야 알았다. 여기서는 패널 높이·내용 높이·가장 아래 요소의 밑선을 찍고,
/// 잠깐 더 돌린 뒤 패널이 자랐는지 본다. 커서는 안 가져간다.
/// </remarks>
public static class LabelingScreenProbe
{
    public static int Run(string[] args)
    {
        var output = Program.ArgValue(args, "--out=") ?? Path.Combine(Path.GetTempPath(), "minguk-labeling-screen.png");
        var failures = 0;
        var bindingErrors = new List<string>();

        CompatibilitySettings.UseLightweightThemes = true;
        LightweightThemeManager.AllowStandardControlsTheming = false;
        DevExpress.Xpf.Grid.DataControlBase.AllowInfiniteGridSize = true;

        _ = new System.Threading.Timer(_ =>
        {
            Console.WriteLine("[FAIL] 라벨링 화면 - 60초 안에 끝나지 않았다(배치가 끝나지 않거나 대화 상자가 떴다).");
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
                var view = new LabelingView();
                window = new Window
                {
                    Width = 1400, Height = 900, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                    Content = view
                };
                window.Show();

                await Pump(800);

                // 화면의 초기화(InitializeControls → RestoreSettings → OnLoaded)는 부모 주입까지 와야 돈다. 앱에서는 MainViewModel 이
                // 넣어 주지만 여기는 없으니 아무 객체나 넣는다 - 그래야 그리드 손잡이가 잡히고 데이터셋을 읽어 패널 너비를 잰다.
                if (view.DataContext is DevExpress.Mvvm.ISupportParentViewModel child) child.ParentViewModel = new object();

                await Pump(2000);

                var vm = (Minguk.Tools.ViewModels.LabelingViewModel)view.DataContext;

                // 콤보는 읽기만 본다 - 여기서 고르면 진짜 데이터셋의 몹 찾기 모델이 바뀐다.
                Console.WriteLine($"[INFO] 쓰는 모델 콤보: [{string.Join(", ", vm.ModelChoices.Select(c => c.Name))}] · 고른 것 {vm.SelectedModelChoice?.Name ?? "없음"} · 줄 \"{vm.ModelSummary}\"");
                if (vm.ModelChoices.Count == 0 || vm.SelectedModelChoice is null) { Console.WriteLine("[FAIL] 쓰는 모델 콤보가 비었거나 지금 모델을 못 골랐다"); failures++; }
                else Console.WriteLine("[PASS] 쓰는 모델 콤보가 채워지고 지금 모델을 골랐다");
                var imagesPanel = Descendants<FrameworkElement>(window).FirstOrDefault(e => e.Name == "ImagesDockPanel");
                var imagesGrid = imagesPanel is null ? null : Descendants<DevExpress.Xpf.Grid.GridControl>(imagesPanel).FirstOrDefault();

                if (imagesPanel is null || imagesGrid is null)
                {
                    Console.WriteLine("[FAIL] ImagesDockPanel 또는 그 안의 그리드를 못 찾았다");
                    failures++;
                }
                else
                {
                    var columns = imagesGrid.Columns.Where(c => c.Visible).Sum(c => c.ActualWidth);
                    var units = string.Join(" ", imagesGrid.Columns.Where(c => c.Visible).Select(c => $"{c.FieldName}={c.ActualWidth:0}({c.Width.UnitType})"));

                    Console.WriteLine($"[INFO] 그림 목록 {vm.Items.Count}장 · 열 합 {columns:0}px [{units}] · 잰 패널 너비 {vm.ImagesPanelWidth:0}px · 실제 패널 {imagesPanel.ActualWidth:0}px");

                    if (vm.ImagesPanelWidth <= 0) { Console.WriteLine("[FAIL] 그림 패널 너비를 안 쟀다(ImagesPanelWidth = 0) - 초기화가 안 돌았거나 LayoutUpdated 가 안 왔다"); failures++; }
                    else if (Math.Abs(imagesPanel.ActualWidth - vm.ImagesPanelWidth) > 4) { Console.WriteLine($"[FAIL] 그림 패널이 잰 너비를 안 따른다 - {imagesPanel.ActualWidth:0} ≠ {vm.ImagesPanelWidth:0}"); failures++; }
                    else if (imagesGrid.Columns.Where(c => c.Visible).Any(c => c.Width.UnitType != DevExpress.Xpf.Grid.GridColumnUnitType.Auto)) { Console.WriteLine("[FAIL] 내용 너비(Auto)가 아닌 열이 있다"); failures++; }
                    else Console.WriteLine($"[PASS] 그림 패널이 열 합만큼 넓다 - 열 {columns:0}px, 패널 {imagesPanel.ActualWidth:0}px");
                }

                var panel = Descendants<FrameworkElement>(window).FirstOrDefault(e => e.Name == "TrainingDockPanel");
                var content = Descendants<FrameworkElement>(window).FirstOrDefault(e => e.Name == "TrainingContent");

                if (panel is null || content is null)
                {
                    Console.WriteLine("[FAIL] TrainingDockPanel 또는 TrainingContent 를 못 찾았다");
                    failures++;
                }
                else
                {
                    var before = panel.ActualHeight;
                    await Pump(1000);
                    var after = panel.ActualHeight;

                    // 내용 안에서 실제로 보이는 가장 아래 요소의 밑선(패널 기준).
                    var bottom = 0d;
                    foreach (var element in Descendants<FrameworkElement>(content))
                    {
                        if (!element.IsVisible || element.ActualHeight <= 0) continue;
                        var point = element.TransformToAncestor(panel).Transform(new Point(0, element.ActualHeight));
                        bottom = Math.Max(bottom, point.Y);
                    }

                    Console.WriteLine($"[INFO] 학습 패널 {before:0}px → {after:0}px · 내용 {content.ActualHeight:0}px(원하는 {content.DesiredSize.Height:0}px) · 가장 아래 요소 밑선 {bottom:0}px · 창 {window.ActualHeight:0}px");

                    var grew = after - before > 2;
                    var clipped = bottom > after + 0.5;
                    var slack = after - bottom;

                    if (grew) { Console.WriteLine($"[FAIL] 학습 패널이 가만히 있어도 자란다({before:0} → {after:0}) - 내용 높이가 패널 높이를 따라가는 되먹임"); failures++; }
                    else if (clipped) { Console.WriteLine($"[FAIL] 학습 패널이 내용보다 낮다 - 아래 요소 밑선 {bottom:0} > 패널 {after:0}"); failures++; }
                    else if (slack > 40) { Console.WriteLine($"[FAIL] 학습 패널 아래가 {slack:0}px 비어 있다"); failures++; }
                    else Console.WriteLine($"[PASS] 학습 패널 높이가 내용에 맞는다 - 남는 여백 {slack:0}px");
                }

                var mine = bindingErrors.Where(e => e.Contains("Labeling") || e.Contains("LabelClassRow") || e.Contains("LabelingRow")).Distinct().ToList();
                if (mine.Count > 0)
                {
                    Console.WriteLine($"[FAIL] 바인딩 오류 {mine.Count}건");
                    foreach (var error in mine.Take(10)) Console.WriteLine("       " + error);
                    failures++;
                }
                else Console.WriteLine("[PASS] 라벨링 화면 바인딩 오류 없음");

                Render(window, output);
                Console.WriteLine($"[INFO] 화면을 찍었다: {output}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 라벨링 화면 - {ex.GetType().Name}: {ex.Message}");
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
