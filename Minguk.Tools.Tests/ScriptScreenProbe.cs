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
