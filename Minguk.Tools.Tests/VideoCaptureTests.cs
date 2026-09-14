using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

using Minguk.Tools.Capture;
using Minguk.Tools.Capture.Input;
using Minguk.Tools.Capture.Recording;

namespace Minguk.Tools.Tests;

/// <summary>
/// 영상을 캡처 대상으로 - 녹화한 mp4 를 틀면 게임 창처럼 프레임이 흐르는가, 입력은 막히는가.
/// </summary>
/// <remarks>
/// 녹화기로 1080p 영상(위 절반 빨강·아래 절반 파랑, 1.5초)을 만들고 <see cref="VideoFileCaptureSession"/> 으로 튼다.
/// 크기(1088 로 늘지 않는지)·방향(뒤집히지 않는지)·알파·녹화 속도대로 흐르는지·되감기·fps 솎기·입력 막기를 본다.
/// <b>입력은 절대 나가지 않는다</b> - 기록만 하는 가짜 어댑터로 본다.
/// </remarks>
internal static partial class Program
{
    private static void TestVideoCapture()
    {
        var folder = Path.Combine(Path.GetTempPath(), "minguk-video-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            const int width = 1920, height = 1080, fps = 30, frames = 45;

            var path = Path.Combine(folder, "20260915-120000-시험.mp4");
            MakeTwoColorVideo(path, width, height, fps, frames);

            // ── 목록 ──
            File.WriteAllText(Path.Combine(folder, "메모.txt"), "mp4 아님");
            var listed = CaptureTarget.EnumerateVideos(folder);

            Check("영상 대상: 폴더의 mp4 만 [영상] 으로 나온다",
                  listed.Count == 1 && listed[0].Kind == CaptureTargetKind.Video && listed[0].Display == "[영상] 20260915-120000-시험.mp4" && listed[0].FilePath == path,
                  string.Join(" / ", listed.Select(t => t.Display)));

            var target = listed.FirstOrDefault() ?? new CaptureTarget { Kind = CaptureTargetKind.Video, Handle = IntPtr.Zero, Title = "시험", FilePath = path };
            var other = new CaptureTarget { Kind = CaptureTargetKind.Video, Handle = IntPtr.Zero, Title = "다른", FilePath = Path.Combine(folder, "다른.mp4") };

            Check("영상 대상: 핸들이 모두 0 이어도 경로로 가른다(세션이 섞이지 않는다)", target.Key != other.Key, $"{target.Key} / {other.Key}");

            // ── 크기·자리 ──
            var sized = VideoFileCaptureSession.TryGetFrameSize(path, out var readWidth, out var readHeight);
            var bounded = CaptureTargetBounds.TryGet(target, out var bounds);

            Check("영상 대상: 크기는 영상 그대로(H.264 의 1088 로 늘지 않는다), 자리는 (0,0)부터 영상 픽셀",
                  sized && readWidth == width && readHeight == height && bounded && bounds == new Rect(0, 0, width, height),
                  $"크기 {readWidth}x{readHeight} · 자리 {bounds}");

            // ── 틀기 ──
            {
                using var session = ScreenCaptureAdapterFactory.Create(target, cpuReadback: true);
                var times = new List<long>();
                string? firstLook = null;
                string? notice = null;

                session.Notice += (_, message) => notice ??= message;
                session.FrameArrived += (_, e) =>
                {
                    lock (times) times.Add(Stopwatch.GetTimestamp());

                    if (firstLook is not null || !e.HasPixels) return;

                    // 위 1/4 줄의 가운데는 빨강(BGRA 0,0,255), 아래 1/4 는 파랑(255,0,0), 알파 255.
                    var top = ReadPixel(e.PixelData, e.RowPitch, e.Width / 2, e.Height / 4);
                    var bottom = ReadPixel(e.PixelData, e.RowPitch, e.Width / 2, e.Height * 3 / 4);
                    var texture = e.Texture.Description;

                    firstLook = $"{e.Width}x{e.Height} · 위 {top} · 아래 {bottom} · 텍스처 {texture.Width}x{texture.Height} {texture.Format}";

                    var ok = e.Width == width && e.Height == height
                             && top.R > 200 && top.B < 60 && top.A == 255
                             && bottom.B > 200 && bottom.R < 60 && bottom.A == 255
                             && texture.Width == width && texture.Height == height;

                    firstLook = (ok ? "OK " : "틀림 ") + firstLook;
                };

                var clock = Stopwatch.StartNew();
                session.TargetFps = 0;
                session.Start();

                // 1.5초짜리를 2.6초 - 한 번은 되감는다.
                Thread.Sleep(2600);
                session.Stop();

                long[] seen;
                lock (times) seen = [.. times];

                Check("영상 대상: 첫 프레임이 바른 크기·방향(위 빨강·아래 파랑)·알파 255 이고 GPU 텍스처도 같은 크기다",
                      firstLook?.StartsWith("OK") == true, firstLook ?? "(프레임 없음)");

                // 녹화 속도대로 - 처음 45장이 1.5초 안팎에 걸쳐 온다(한꺼번에 쏟아지지 않는다).
                var firstLap = seen.Length >= frames ? (seen[frames - 1] - seen[0]) / (double)Stopwatch.Frequency : 0;
                var loops = session is VideoFileCaptureSession video ? video.Loops : -1;

                Check("영상 대상: 녹화한 속도대로 흐르고(45장 ≈ 1.5초), 끝나면 처음부터 다시 튼다",
                      firstLap is > 1.2 and < 2.0 && seen.Length > frames && loops >= 1,
                      $"받은 {seen.Length}장 · 처음 {frames}장에 {firstLap:0.00}초 · 되감기 {loops}번 · 알림 '{notice}' · {clock.Elapsed.TotalSeconds:0.0}초");
            }

            // ── fps 솎기 ──
            {
                using var session = ScreenCaptureAdapterFactory.Create(target, cpuReadback: false);
                var count = 0;
                var withPixels = 0;

                session.FrameArrived += (_, e) =>
                {
                    Interlocked.Increment(ref count);
                    if (e.HasPixels) Interlocked.Increment(ref withPixels);
                };

                session.TargetFps = 10;
                session.Start();
                Thread.Sleep(1500);
                session.Stop();

                Check("영상 대상: 고른 fps(10)보다 촘촘한 프레임은 솎고, 리드백을 안 켜면 CPU 픽셀을 안 준다",
                      count is >= 11 and <= 18 && withPixels == 0,
                      $"1.5초에 {count}장 · 픽셀 준 장 {withPixels}");
            }

            // ── 허브: 같은 영상을 두 화면이 잡으면 세션 하나 ──
            {
                var made = 0;
                var hub = CaptureSessionHubFactory.Create((t, readback) => { made++; return ScreenCaptureAdapterFactory.Create(t, readback); });

                using (var a = hub.Acquire(target, cpuReadback: false))
                using (var b = hub.Acquire(listed.FirstOrDefault() ?? target, cpuReadback: false))
                {
                    a.Start();
                    b.Start();
                    Thread.Sleep(200);

                    Check("영상 대상: 같은 영상을 두 화면이 잡으면 세션 하나를 나눠 쓴다", made == 1 && hub.ConsumerCount(target) == 2, $"세션 {made}개 · 손잡이 {hub.ConsumerCount(target)}");
                }
            }

            // ── 입력은 막는다 ──
            {
                var adapter = new RecordingAdapter();
                var router = new PreviewInputRouter(() => target, adapter);

                var click = router.PrepareClickAtRatio(new Point(0.5, 0.5), out _, out var activated);
                var move = router.TryMoveMouse(new Point(10, 10), new Size(100, 100), new Size(width, height));
                var key = router.SendKey(0x46, isKeyUp: false);

                Check("영상 대상: 미리보기 입력(클릭·이동·키)은 보내지 않고 이유를 돌려준다",
                      click == InputForwardResult.VideoTarget && move == InputForwardResult.VideoTarget && key == InputForwardResult.VideoTarget
                      && !activated && adapter.Calls.Count == 0 && !router.TryFocusTargetWindow(),
                      $"클릭 {click} · 이동 {move} · 키 {key} · 보낸 것 {adapter.Calls.Count}");

                var silent = Minguk.Tools.Input.InputAdapterFactory.CreateSilent();

                Check("영상 대상: 스크립트용 '보내지 않음' 경로는 앞 창을 요구하지 않고 커서 자리를 모른다",
                      !silent.RequiresForegroundTarget && silent.GetCursorPosition() is null && silent.PressKey(0x46) && silent.MoveMouseBy(10, 0),
                      silent.Name);
            }

            // ── 실시간 스크립트: 대상이 영상이면 보내지 않는 경로·배율 안 배움. 영상이 아니면 고른 경로 그대로 ──
            {
                var chosen = new RecordingAdapter();
                var current = target;
                var monitor = new CaptureTarget { Kind = CaptureTargetKind.Monitor, Handle = 1, Title = "모니터" };

                using var live = new Minguk.Tools.ViewModels.LiveScriptSession(
                    () => new Minguk.Tools.Input.InputService(chosen), () => true, () => current,
                    () => null, () => null, () => System.Threading.Tasks.Task.CompletedTask, action => action(), _ => { });

                var player = new Minguk.Tools.ViewModels.ScriptPlayer(() => null) { IsAimScaleAuto = true };
                var build = typeof(Minguk.Tools.ViewModels.LiveScriptSession).GetMethod("BuildHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

                Minguk.Tools.Input.Scripting.Live.LiveScriptHost Host()
                    => (Minguk.Tools.Input.Scripting.Live.LiveScriptHost)build.Invoke(live, [player, new Minguk.Tools.Input.InputService(chosen), null, CancellationToken.None])!;

                var onVideo = Host();
                current = monitor;
                var onMonitor = Host();

                Check("영상 대상: 실시간 스크립트는 입력을 보내지 않는 경로로 돌고 조준 배율을 배우지 않는다(영상이 아니면 고른 경로 그대로)",
                      onVideo.Service.Adapter is Minguk.Tools.Input.Adapters.SilentInputAdapter && onVideo.AimScaleLearned is null && onVideo.IsAimScaleAuto?.Invoke() == false && !onVideo.RequiresForeground
                      && ReferenceEquals(onMonitor.Service.Adapter, chosen) && onMonitor.AimScaleLearned is not null && onMonitor.IsAimScaleAuto?.Invoke() == true,
                      $"영상: {onVideo.Service.Adapter.Name} · 배움 {onVideo.AimScaleLearned is not null} / 모니터: {onMonitor.Service.Adapter.Name} · 배움 {onMonitor.AimScaleLearned is not null}");
            }

            // ── 없는 파일 ──
            {
                using var missing = ScreenCaptureAdapterFactory.Create(other, cpuReadback: false);
                string? message = null;

                try { missing.Start(); }
                catch (FileNotFoundException ex) { message = ex.Message; }

                Check("영상 대상: 파일이 없으면 시작하지 않고 한국어로 말한다", message?.Contains("영상 파일이 없습니다") == true, message ?? "(예외 없음)");
            }
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (Exception) { }
        }
    }

    /// <summary>위 절반 빨강·아래 절반 파랑 영상. 한가운데에 움직이는 흰 띠를 둬 인코더가 장마다 일하게 한다.</summary>
    private static void MakeTwoColorVideo(string path, int width, int height, int fps, int frames)
    {
        var pitch = width * 4;
        var buffer = new byte[pitch * height];
        var pixels = Marshal.AllocHGlobal(buffer.Length);

        try
        {
            using var recorder = new MediaFoundationVideoRecorder(path, fps);
            var start = Stopwatch.GetTimestamp();

            for (var i = 0; i < frames; i++)
            {
                for (var y = 0; y < height; y++)
                {
                    var red = y < height / 2;

                    for (var x = 0; x < width; x++)
                    {
                        var at = y * pitch + x * 4;
                        var band = Math.Abs(y - height / 2) < 20 && (x + i * 16) % 200 < 100;

                        buffer[at] = band ? (byte)255 : red ? (byte)0 : (byte)255;       // B
                        buffer[at + 1] = band ? (byte)255 : (byte)0;                        // G
                        buffer[at + 2] = band ? (byte)255 : red ? (byte)255 : (byte)0;   // R
                        buffer[at + 3] = 255;
                    }
                }

                Marshal.Copy(buffer, 0, pixels, buffer.Length);

                var timestamp = start + (long)(i * Stopwatch.Frequency / (double)fps);
                var waited = Stopwatch.StartNew();

                while (!recorder.TryAddFrame(pixels, pitch, width, height, timestamp) && recorder.Error is null && waited.ElapsedMilliseconds < 3000)
                    Thread.Sleep(5);
            }

            recorder.Finish();
        }
        finally
        {
            Marshal.FreeHGlobal(pixels);
        }
    }

    private readonly record struct Bgra(byte B, byte G, byte R, byte A)
    {
        public override string ToString() => $"({R},{G},{B},{A})";
    }

    private static Bgra ReadPixel(IntPtr data, int rowPitch, int x, int y)
    {
        var at = (y * rowPitch) + (x * 4);
        return new Bgra(Marshal.ReadByte(data, at), Marshal.ReadByte(data, at + 1), Marshal.ReadByte(data, at + 2), Marshal.ReadByte(data, at + 3));
    }
}
