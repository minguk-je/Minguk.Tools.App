using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Minguk.Tools.Capture.Recording;

namespace Minguk.Tools.Tests;

/// <summary>
/// 화면캡처 녹화기 - 만든 프레임을 넣으면 재생되는 mp4 가 나오는가. 게임·캡처 없이 Media Foundation 인코더만 본다.
/// </summary>
/// <remarks>
/// 홀수 크기(H.264 는 짝수만)·줄 폭이 넓은 버퍼(GPU 정렬 RowPitch)·빈 녹화(파일을 안 남긴다)·크기가 도중에 바뀜(멈춤)을 본다.
/// 파일 머리의 'ftyp' 로 mp4 인지 가린다. 실제로 트는 것은 사람이 다른 플레이어로.
/// </remarks>
internal static partial class Program
{
    private static void TestVideoRecorder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "minguk-record-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            // 줄 폭은 가로×4 보다 넓다(GPU 정렬을 흉내 낸다).
            const int width = 321, height = 241, rowPitch = width * 4 + 12, frames = 45;

            var path = Path.Combine(folder, "시험.mp4");
            var pixels = Marshal.AllocHGlobal(rowPitch * height);

            using (var recorder = new MediaFoundationVideoRecorder(path, frameRate: 30))
            {
                var start = Stopwatch.GetTimestamp();

                for (var i = 0; i < frames; i++)
                {
                    FillFrame(pixels, width, height, rowPitch, i);

                    // 1/30초 간격의 시각 - 실제로 기다리지 않고 시각만 준다.
                    var timestamp = start + (long)(i * Stopwatch.Frequency / 30.0);

                    // 쓰기 스레드가 따라오게 줄이 차면 잠깐 기다린다(실제 캡처는 버리지만 시험은 다 넣어 본다).
                    var waited = Stopwatch.StartNew();
                    while (!recorder.TryAddFrame(pixels, rowPitch, width, height, timestamp) && recorder.Error is null && waited.ElapsedMilliseconds < 2000)
                        System.Threading.Thread.Sleep(5);
                }

                recorder.Finish();

                var header = File.Exists(path) ? File.ReadAllBytes(path).AsSpan(4, 4).ToArray() : [];
                var isMp4 = header.Length == 4 && Encoding.ASCII.GetString(header) == "ftyp";
                var size = File.Exists(path) ? new FileInfo(path).Length : 0;

                Check("녹화: 만든 프레임(홀수 크기·넓은 줄 폭)이 H.264 mp4 로 써진다",
                      recorder.Error is null && recorder.FramesWritten == frames && isMp4 && size > 1024,
                      recorder.Error is not null ? recorder.Error.Message : $"{recorder.FramesWritten}/{frames}장 · 버림 {recorder.FramesDropped} · {size:N0}바이트 · 머리 '{Encoding.ASCII.GetString(header)}' · {recorder.Duration.TotalSeconds:0.00}초");
            }

            // 디스크에 확정 - 한 파일에 이어 쓰되 조각(moof)이 쓰는 도중 디스크에 붙는다(싱크가 약 9장마다 끊는다, 실측). 앱이 죽은 것처럼
            // Finalize 전에 파일을 떠 두고 그 사본을 Media Foundation 으로 풀어 트는지(프레임을 읽는지) 본다. 끝나지 않은 마지막 조각만 빠진다.
            {
                var flushPath = Path.Combine(folder, "확정.mp4");
                var snapshots = new System.Collections.Generic.List<(int Written, long Size, int Moofs, string Copy)>();

                using (var live = new MediaFoundationVideoRecorder(flushPath, frameRate: 30))
                {
                    var start = Stopwatch.GetTimestamp();

                    for (var i = 0; i < 75; i++)
                    {
                        FillFrame(pixels, width, height, rowPitch, i);
                        var timestamp = start + (long)(i * Stopwatch.Frequency / 30.0);

                        var waited = Stopwatch.StartNew();
                        while (!live.TryAddFrame(pixels, rowPitch, width, height, timestamp) && live.Error is null && waited.ElapsedMilliseconds < 2000)
                            System.Threading.Thread.Sleep(5);

                        // 쓰는 도중에 두 번(40장·75장) 떠 둔다 - 쓰기 스레드가 따라온 뒤에.
                        if (i is 39 or 74)
                        {
                            var caughtUp = Stopwatch.StartNew();
                            while (live.FramesWritten < i + 1 && live.Error is null && caughtUp.ElapsedMilliseconds < 3000) System.Threading.Thread.Sleep(5);
                            System.Threading.Thread.Sleep(300);

                            var copy = Path.Combine(folder, $"죽은척-{i + 1}.mp4");
                            var bytes = ReadShared(flushPath);

                            // 녹화 줄의 크기(FileSizeBytes)는 쓰는 도중에도 실제 크기다 - 폴더 목록(FileInfo)은 닫기 전까지 늦게 고친다.
                            if (i == 74)
                                Check("녹화: 쓰는 도중 크기(FileSizeBytes)가 실제로 쓴 크기와 맞다",
                                      live.FileSizeBytes >= bytes.LongLength && bytes.LongLength > 0,
                                      $"FileSizeBytes {live.FileSizeBytes:N0} · 읽은 {bytes.LongLength:N0} · 폴더 목록 {new FileInfo(flushPath).Length:N0}");
                            File.WriteAllBytes(copy, bytes);
                            snapshots.Add((live.FramesWritten, bytes.LongLength, CountBox(bytes, "moof"), copy));
                        }
                    }

                    // 사본은 Finalize 전 상태 그대로다 - 녹화기를 닫기 전에 열어 본다(닫는 동안 원본이 바뀌어도 상관없다).
                    var readable = snapshots.Select(s => (s.Written, s.Size, s.Moofs, Frames: CountPlayableFrames(s.Copy))).ToArray();
                    var growing = readable.Length == 2 && readable[1].Size > readable[0].Size && readable[1].Moofs > readable[0].Moofs;
                    var playable = readable.Length == 2 && readable[0].Frames >= 30 && readable[1].Frames >= 60;

                    Check("녹화: Finalize 전(앱이 죽은 것처럼)에도 트는 파일이다 - 쓰는 도중 조각(moof)이 늘고 사본에서 프레임을 읽는다(40장→30 이상, 75장→60 이상)",
                          live.Error is null && growing && playable,
                          live.Error?.Message ?? string.Join(" / ", readable.Select(r => $"{r.Written}장 쓴 때: {r.Size:N0}바이트 · moof {r.Moofs} · 읽은 프레임 {r.Frames}")));

                    live.Finish();
                }

                var finalBytes = File.Exists(flushPath) ? File.ReadAllBytes(flushPath) : [];
                var finalFrames = File.Exists(flushPath) ? CountPlayableFrames(flushPath) : 0;

                Check("녹화: 파일은 하나(나누지 않는다)이고, 닫으면 모든 프레임을 읽는다",
                      Directory.GetFiles(folder, "확정*.mp4").Length == 1 && finalFrames == 75,
                      $"파일 {Directory.GetFiles(folder, "확정*.mp4").Length}개 · {finalBytes.LongLength:N0}바이트 · moof {CountBox(finalBytes, "moof")} · 읽은 프레임 {finalFrames}/75");
            }

            // 크기가 도중에 바뀌면 받지 않고 이유를 남긴다.
            var resizedPath = Path.Combine(folder, "크기바뀜.mp4");
            using (var resized = new MediaFoundationVideoRecorder(resizedPath))
            {
                var first = resized.TryAddFrame(pixels, rowPitch, width, height, Stopwatch.GetTimestamp());
                var second = resized.TryAddFrame(pixels, rowPitch, width - 20, height, Stopwatch.GetTimestamp());
                resized.Finish();

                Check("녹화: 도중에 캡처 크기가 바뀌면 더 받지 않고 이유를 남긴다", first && !second && resized.Error is not null, resized.Error?.Message ?? "(이유 없음)");
            }

            // 한 장도 안 넣고 닫으면 빈 파일을 남기지 않는다.
            var emptyPath = Path.Combine(folder, "빈것.mp4");
            using (var empty = new MediaFoundationVideoRecorder(emptyPath)) empty.Finish();
            Check("녹화: 한 장도 없으면 파일을 남기지 않는다", !File.Exists(emptyPath), File.Exists(emptyPath) ? "남았다" : "없음");

            Marshal.FreeHGlobal(pixels);

            // 실제 크기·속도 - 1080p 를 고른 fps 간격으로 2초. 쓰기가 따라오는지(버리는 장수)를 본다. 프레임마다 그림을 바꿔 인코더가 일하게 한다.
            {
                const int fullWidth = 1920, fullHeight = 1080, fullPitch = fullWidth * 4;

                var full = Marshal.AllocHGlobal(fullPitch * fullHeight);

                foreach (var fps in new[] { 30, 60 })
                {
                    var count = fps * 2;
                    var fullPath = Path.Combine(folder, $"1080p-{fps}.mp4");

                    using var recorder = new MediaFoundationVideoRecorder(fullPath, frameRate: fps);
                    var clock = Stopwatch.StartNew();

                    for (var i = 0; i < count; i++)
                    {
                        FillFrame(full, fullWidth, fullHeight, fullPitch, i);
                        recorder.TryAddFrame(full, fullPitch, fullWidth, fullHeight, Stopwatch.GetTimestamp());

                        var wait = (i + 1) * 1000.0 / fps - clock.Elapsed.TotalMilliseconds;
                        if (wait > 0) System.Threading.Thread.Sleep((int)wait);
                    }

                    recorder.Finish();

                    var dropRate = 100.0 * recorder.FramesDropped / count;
                    var megabytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length / 1024.0 / 1024.0 : 0;
                    var moofs = File.Exists(fullPath) ? CountBox(File.ReadAllBytes(fullPath), "moof") : 0;

                    Check($"녹화: 1080p {fps}fps 에서 쓰기가 따라온다(버림 10% 이하)",
                          recorder.Error is null && dropRate <= 10,
                          recorder.Error is not null ? recorder.Error.Message
                              : $"{recorder.FramesWritten}/{count}장 · 버림 {recorder.FramesDropped}({dropRate:0}%) · 솎음 {recorder.FramesSkipped} · {megabytes:0.00} MB · " +
                                $"{MediaFoundationVideoRecorder.DefaultBitrate(fps) / 1_000_000.0:0}Mbps · 닫기까지 {clock.Elapsed.TotalSeconds:0.0}초");

                    // 앱이 죽을 때 잃는 것은 끝나지 않은 조각 하나 - 1080p 에서도 1초 안쪽이어야 한다(2초 녹화에 조각 2개 넘게).
                    Check($"녹화: 1080p {fps}fps 에서도 조각이 1초 안쪽마다 디스크에 붙는다",
                          recorder.FramesWritten > 0 && moofs > 2,
                          $"조각 {moofs}개 · 조각당 {(moofs == 0 ? 0 : recorder.FramesWritten / (double)moofs):0.#}장({(moofs == 0 ? 0 : recorder.FramesWritten / (double)moofs / fps):0.00}초)");
                }

                Marshal.FreeHGlobal(full);
            }

            // 캡처가 고른 fps 보다 빠르면(세션을 나눈 다른 화면이 60 을 원함) 고른 fps 로 솎는다 - 60fps 로 1초 넣으면 30fps 녹화는 30장 안팎.
            {
                const int smallWidth = 320, smallHeight = 240, smallPitch = smallWidth * 4, feed = 60;

                var small = Marshal.AllocHGlobal(smallPitch * smallHeight);
                var thinPath = Path.Combine(folder, "솎기.mp4");

                using var thin = new MediaFoundationVideoRecorder(thinPath, frameRate: 30);
                var clock = Stopwatch.StartNew();

                for (var i = 0; i < feed; i++)
                {
                    FillFrame(small, smallWidth, smallHeight, smallPitch, i);
                    thin.TryAddFrame(small, smallPitch, smallWidth, smallHeight, Stopwatch.GetTimestamp());

                    var wait = (i + 1) * 1000.0 / feed - clock.Elapsed.TotalMilliseconds;
                    if (wait > 0) System.Threading.Thread.Sleep((int)wait);
                }

                thin.Finish();
                Marshal.FreeHGlobal(small);

                Check("녹화: 고른 fps 보다 빨리 오는 프레임은 솎아 고른 fps 로 저장한다(60 → 30)",
                      thin.Error is null && thin.FramesWritten is >= 27 and <= 33 && thin.FramesSkipped is >= 25,
                      $"넣음 {feed} · 씀 {thin.FramesWritten} · 솎음 {thin.FramesSkipped} · 버림 {thin.FramesDropped}");
            }

            Check("녹화: 비트레이트는 fps 에 맞춘다(30 → 8Mbps, 60 → 12Mbps)",
                  MediaFoundationVideoRecorder.DefaultBitrate(30) == 8_000_000 && MediaFoundationVideoRecorder.DefaultBitrate(60) == 12_000_000,
                  $"30 {MediaFoundationVideoRecorder.DefaultBitrate(30):N0} · 60 {MediaFoundationVideoRecorder.DefaultBitrate(60):N0}");

            // 하네스는 Minguk.Image 를 직접 참조하지 않는다 - 앱 출력에 딸려 온 어셈블리를 리플렉션으로 불러 아이콘이 있는지 본다.
            var freeImage = Type.GetType("Minguk.Image.FreeImage, Minguk.Image");
            var instance = freeImage?.GetProperty("Instance")?.GetValue(null);
            var icon = instance is null ? null : freeImage!.GetMethod("CacheImageSource", [typeof(string)])?.Invoke(instance, ["Axialis/Multimedia/16x16/record-control_record.png"]);
            Check("녹화: 도구 줄 아이콘이 있다", icon is not null, freeImage is null ? "Minguk.Image 를 못 불렀다" : icon is null ? "없음" : "있음");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (Exception) { }
        }
    }

    /// <summary>쓰는 중인 파일을 읽는다 - 녹화기가 쓰기로 열어 두어 쓰기 공유를 허락해야 열린다.</summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>mp4 상자 이름(4글자)이 몇 번 나오나. 상자를 따라 걷지 않고 글자로 센다 - 영상 데이터에 우연히 나올 수 있지만 시험 크기에서는 무시할 만하다.</summary>
    private static int CountBox(byte[] bytes, string type)
    {
        var pattern = Encoding.ASCII.GetBytes(type);
        var count = 0;

        for (var index = bytes.AsSpan().IndexOf(pattern); index >= 0;)
        {
            count++;
            var next = bytes.AsSpan(index + 4).IndexOf(pattern);
            index = next < 0 ? -1 : index + 4 + next;
        }

        return count;
    }

    /// <summary>Media Foundation 으로 열어 NV12 로 풀면서 끝까지 읽은 프레임 수. 못 열면 0 - 트지 못하는 파일이다.</summary>
    private static int CountPlayableFrames(string path)
    {
        Vortice.MediaFoundation.MediaFactory.MFStartup(true).CheckError();

        try
        {
            using var reader = Vortice.MediaFoundation.MediaFactory.MFCreateSourceReaderFromURL(path, null!);

            using (var type = Vortice.MediaFoundation.MediaFactory.MFCreateMediaType())
            {
                type.Set(Vortice.MediaFoundation.MediaTypeAttributeKeys.MajorType, Vortice.MediaFoundation.MediaTypeGuids.Video);
                type.Set(Vortice.MediaFoundation.MediaTypeAttributeKeys.Subtype, new Guid("3231564E-0000-0010-8000-00AA00389B71"));   // NV12
                reader.SetCurrentMediaType(Vortice.MediaFoundation.SourceReaderIndex.FirstVideoStream, type);
            }

            var frames = 0;

            while (true)
            {
                using var sample = reader.ReadSample(Vortice.MediaFoundation.SourceReaderIndex.FirstVideoStream, Vortice.MediaFoundation.SourceReaderControlFlag.None,
                                                     out _, out var flags, out _);
                if (sample is not null) frames++;
                if ((flags & (Vortice.MediaFoundation.SourceReaderFlag.EndOfStream | Vortice.MediaFoundation.SourceReaderFlag.Error)) != 0) break;
            }

            return frames;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    (못 읽음: {Path.GetFileName(path)} - {ex.Message})");
            return 0;
        }
        finally
        {
            Vortice.MediaFoundation.MediaFactory.MFShutdown();
        }
    }

    /// <summary>프레임마다 움직이는 색 띠 - 인코더가 전부 같은 그림으로 줄이지 않게.</summary>
    private static void FillFrame(IntPtr pixels, int width, int height, int rowPitch, int index)
    {
        var buffer = new byte[rowPitch * height];

        for (var y = 0; y < height; y++)
        {
            var row = y * rowPitch;

            for (var x = 0; x < width; x++)
            {
                var band = ((x + index * 7) / 20) % 3;
                buffer[row + x * 4] = (byte)(band == 0 ? 255 : 30);
                buffer[row + x * 4 + 1] = (byte)(band == 1 ? 255 : 30);
                buffer[row + x * 4 + 2] = (byte)(band == 2 ? 255 : 30);
                buffer[row + x * 4 + 3] = 255;
            }
        }

        Marshal.Copy(buffer, 0, pixels, buffer.Length);
    }
}
