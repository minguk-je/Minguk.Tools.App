using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using Minguk.Tools.Capture;
using Minguk.Tools.Capture.Input;

namespace Minguk.Tools.Tests;

/// <summary>
/// 모니터 캡처가 초당 몇 장 오는지 - 화면 내용 탓인지 캡처 경로 탓인지 가른다(2026-09-15, "모니터로 60 인데 30 수준").
/// </summary>
/// <remarks>
/// WGC 는 화면이 바뀔 때만 프레임을 준다. 그래서 같은 모니터를 세 번 잰다:
/// ① 그대로(지금 화면 내용이 정하는 fps) ② 그 모니터에 매 프레임 색이 바뀌는 창을 띄우고 상한 없이 ③ 같은 창에 상한 60(앱의 솎기 규칙).
/// ②가 60 근처면 경로는 정상이고 30 은 화면 내용 탓, ②도 30 이면 경로·GPU·DWM 쪽이다. 프레임 간격 분포도 적는다.
/// 화면에 창을 몇 초 띄운다(입력은 안 가져간다). <c>--capture-fps[=모니터 번호]</c>, 번호는 1 부터(없으면 모두).
/// </remarks>
internal static class CaptureFpsProbe
{
    private const int SecondsPerRun = 4;

    public static int Run(string? which)
    {
        var monitors = CaptureTarget.EnumerateMonitors();

        if (int.TryParse(which, out var number) && number >= 1 && number <= monitors.Count)
            monitors = [monitors[number - 1]];

        foreach (var monitor in monitors)
        {
            Console.WriteLine($"== {monitor.Display} ==");

            Measure(monitor, animate: false, targetFps: 0, "① 그대로, 상한 없음");
            Measure(monitor, animate: true, targetFps: 0, "② 움직이는 창, 상한 없음");
            Measure(monitor, animate: true, targetFps: 60, "③ 움직이는 창, 상한 60");
        }

        return 0;
    }

    private static void Measure(CaptureTarget monitor, bool animate, int targetFps, string label)
    {
        using var animation = animate ? AnimatedWindow.Show(monitor) : null;

        // 창이 뜨고 그리기 시작할 틈.
        Thread.Sleep(animate ? 700 : 100);

        var arrivals = new List<long>(512);
        using var session = new WgcCaptureSession(monitor, cpuReadback: false, autoFallbackToMonitor: false);

        session.FrameArrived += (_, _) => { lock (arrivals) arrivals.Add(Stopwatch.GetTimestamp()); };
        session.TargetFps = targetFps;
        session.Start();

        Thread.Sleep(TimeSpan.FromSeconds(SecondsPerRun));
        session.Stop();

        long[] times;
        lock (arrivals) times = [.. arrivals];

        // 시작 직후 0.5초는 뺀다(첫 장이 몰려 온다).
        var skip = Stopwatch.GetTimestamp() - (long)((SecondsPerRun + 0.2) * Stopwatch.Frequency);
        var start = times.FirstOrDefault() + Stopwatch.Frequency / 2;
        var steady = times.Where(t => t >= start).ToArray();
        var span = steady.Length > 1 ? (steady[^1] - steady[0]) / (double)Stopwatch.Frequency : 0;
        var fps = span > 0 ? (steady.Length - 1) / span : 0;

        var gaps = steady.Zip(steady.Skip(1), (a, b) => (b - a) * 1000.0 / Stopwatch.Frequency).ToArray();
        var buckets = new (string Name, Func<double, bool> Match)[]
        {
            ("<12ms", g => g < 12), ("12-15", g => g is >= 12 and < 15), ("15-18", g => g is >= 15 and < 18),
            ("18-25", g => g is >= 18 and < 25), ("25-40", g => g is >= 25 and < 40), ("40+", g => g >= 40)
        };

        var histogram = string.Join(" · ", buckets.Select(b => $"{b.Name} {gaps.Count(b.Match)}"));
        var rendered = animation?.RenderedPerSecond is { } r ? $" · 창이 그린 {r:0}/초" : string.Empty;

        Console.WriteLine($"  {label}: {fps:0.0} fps ({steady.Length}장/{span:0.0}초){rendered}");
        Console.WriteLine($"    간격 {histogram}" + (gaps.Length > 0 ? $" · 가운뎃값 {gaps.OrderBy(g => g).ElementAt(gaps.Length / 2):0.0}ms" : string.Empty));
        _ = skip;
    }

    /// <summary>그 모니터 왼쪽 위에 매 렌더링마다 색이 바뀌는 창. 자기 스레드에서 돈다.</summary>
    private sealed class AnimatedWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private Dispatcher? _dispatcher;
        private long _renderCount;
        private long _renderStart;

        private AnimatedWindow(CaptureTarget monitor)
        {
            _thread = new Thread(() => Loop(monitor)) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait(5000);
        }

        public double? RenderedPerSecond
        {
            get
            {
                var elapsed = (Stopwatch.GetTimestamp() - Interlocked.Read(ref _renderStart)) / (double)Stopwatch.Frequency;
                return elapsed > 0 ? Interlocked.Read(ref _renderCount) / elapsed : null;
            }
        }

        public static AnimatedWindow Show(CaptureTarget monitor) => new(monitor);

        private void Loop(CaptureTarget monitor)
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowActivated = false,
                Topmost = true,
                Width = 400,
                Height = 300,
                Title = "캡처 fps 시험"
            };

            var hue = 0;
            window.SourceInitialized += (_, _) =>
            {
                if (CaptureTargetBounds.TryGet(monitor, out var bounds))
                    MoveWindow(new WindowInteropHelper(window).Handle, (int)bounds.Left + 40, (int)bounds.Top + 40, 400, 300, true);
            };

            CompositionTarget.Rendering += (_, _) =>
            {
                hue = (hue + 37) % 255;
                window.Background = new SolidColorBrush(Color.FromRgb((byte)hue, (byte)(255 - hue), 128));
                if (Interlocked.Increment(ref _renderCount) == 1) Interlocked.Exchange(ref _renderStart, Stopwatch.GetTimestamp());
            };

            _dispatcher = Dispatcher.CurrentDispatcher;
            window.Show();
            _ready.Set();
            Dispatcher.Run();
        }

        public void Dispose()
        {
            _dispatcher?.InvokeShutdown();
            _thread.Join(3000);
        }

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
    }
}
