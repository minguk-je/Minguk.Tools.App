using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Vortice.MediaFoundation;

namespace Minguk.Tools.Capture.Recording;

/// <summary>
/// Media Foundation SourceReader 로 영상을 처음부터 풀며 간격마다 한 장씩 PNG 로 떨어뜨린다.
/// </summary>
/// <remarks>
/// - 푸는 길은 영상 캡처(<see cref="VideoFileCaptureSession"/>)와 같다 - RGB32 로 받아 위에서 아래·알파 255 로 고친다.
/// - <b>되감기(seek)가 아니라 처음부터 푼다</b>: 조각 mp4 는 키프레임이 드문드문이라(실측 0·9·90번째) 1초마다 되감아도 키프레임부터 다시 풀어야 해 얻는 것이 없다.
/// - 이름은 <c>영상이름-00012.0s.png</c> - 같은 영상을 다시 뽑으면 같은 이름이라 덮지 않고 건너뛴다(찍어 둔 라벨이 다른 그림에 붙지 않게).
/// - <b>앞에 저장한 장과 거의 같으면 건너뛴다</b>: 메뉴·대기 화면처럼 멈춘 장면 수십 장은 라벨만 늘리고 배울 것이 없다.
///   32×18 밝기 조각의 평균 차이가 <see cref="SimilarThreshold"/> 밑이면 같은 장면으로 본다.
/// - PNG 로 쓰는 것이 푸는 것보다 느려(1080p 한 장 100ms 남짓) 동시에 <see cref="EncodeParallelism"/> 장까지 스레드풀에서 쓴다.
/// </remarks>
public sealed class MediaFoundationVideoFrameExtractor : IVideoFrameExtractor
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>MF_PD_DURATION - 영상 길이(100ns).</summary>
    private static readonly Guid DurationKey = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");

    /// <summary>같은 장면으로 볼 밝기 차이(0~255 의 평균). 사람 눈으로 "그대로" 인 화면이 1 안팎이었다.</summary>
    public const double SimilarThreshold = 2.0;

    private const int EncodeParallelism = 3;
    private const int ThumbWidth = 32;
    private const int ThumbHeight = 18;

    public string Name => "Media Foundation";

    public Task<VideoFrameExtractResult> ExtractAsync(string videoPath, string outputDirectory, TimeSpan interval,
                                                      IProgress<VideoFrameExtractProgress>? progress, CancellationToken token)
        => Task.Run(() => Extract(videoPath, outputDirectory, interval, progress, token), token);

    private static VideoFrameExtractResult Extract(string videoPath, string outputDirectory, TimeSpan interval,
                                                   IProgress<VideoFrameExtractProgress>? progress, CancellationToken token)
    {
        if (!File.Exists(videoPath)) throw new FileNotFoundException($"영상 파일이 없습니다: {videoPath}", videoPath);
        if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(1);

        Directory.CreateDirectory(outputDirectory);

        var stem = Path.GetFileNameWithoutExtension(videoPath);
        var saved = new List<string>();
        var skippedSimilar = 0;
        var skippedExisting = 0;
        var duration = TimeSpan.Zero;
        var watch = Stopwatch.StartNew();

        using var encoders = new SemaphoreSlim(EncodeParallelism);
        var writing = new List<Task>();
        var started = false;

        try
        {
            MediaFactory.MFStartup(true).CheckError();
            started = true;

            using var reader = VideoFileCaptureSession.CreateReader(videoPath);
            var (width, height) = VideoFileCaptureSession.OutputSize(reader);

            if (width <= 0 || height <= 0) throw new InvalidDataException($"영상 크기를 알 수 없습니다: {videoPath}");

            duration = ReadDuration(reader);

            var pixels = new byte[width * height * 4];
            var nextDue = 0L;
            byte[]? lastThumb = null;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out var sampleTime);

                if ((flags & SourceReaderFlag.Error) != 0) throw new InvalidDataException($"영상을 읽다 오류가 났습니다: {videoPath}");
                if ((flags & SourceReaderFlag.EndOfStream) != 0) break;

                if ((flags & SourceReaderFlag.CurrentMediaTypeChanged) != 0)
                {
                    (width, height) = VideoFileCaptureSession.OutputSize(reader);
                    pixels = new byte[width * height * 4];
                    lastThumb = null;
                }

                if (sample is null || sampleTime < nextDue) continue;

                // 다음 박자는 간격씩 민다 - 한참 건너뛴 영상(끊긴 녹화)이면 지금 시각 다음 박자로.
                while (nextDue <= sampleTime) nextDue += interval.Ticks;

                var position = TimeSpan.FromTicks(sampleTime);

                VideoFileCaptureSession.CopyPixels(sample, pixels, width, height);

                var thumb = Thumbnail(pixels, width, height);

                if (lastThumb is not null && Difference(thumb, lastThumb) < SimilarThreshold)
                {
                    skippedSimilar++;
                    progress?.Report(new VideoFrameExtractProgress(position, duration, saved.Count, skippedSimilar));
                    continue;
                }

                lastThumb = thumb;

                var path = Path.Combine(outputDirectory, $"{stem}-{position.TotalSeconds:00000.0}s.png");

                if (File.Exists(path))
                {
                    skippedExisting++;
                    continue;
                }

                // 쓰는 동안 다음 장을 풀어야 하니 픽셀을 떼어 넘긴다. 동시에 몇 장까지만 - 1080p 한 장이 8MB 다.
                var copy = pixels.ToArray();
                var (w, h) = (width, height);

                encoders.Wait(token);
                writing.Add(Task.Run(() =>
                {
                    try { SavePng(copy, w, h, path); }
                    finally { encoders.Release(); }
                }, CancellationToken.None));

                saved.Add(path);
                progress?.Report(new VideoFrameExtractProgress(position, duration, saved.Count, skippedSimilar));
            }
        }
        finally
        {
            // 멈추거나 터져도 쓰던 PNG 는 끝까지 쓴다 - 반쯤 쓴 그림이 데이터셋에 남으면 학습이 그 파일에서 넘어진다.
            Task.WaitAll([.. writing]);

            if (started) MediaFactory.MFShutdown();
        }

        Logger.Info($"영상에서 뽑기: {videoPath} → {outputDirectory} · {interval.TotalSeconds:0.#}초마다 · 저장 {saved.Count} · 비슷해 건너뜀 {skippedSimilar} · 이미 있음 {skippedExisting} · 길이 {duration} · {watch.Elapsed.TotalSeconds:0.0}초");

        return new VideoFrameExtractResult(saved, skippedSimilar, skippedExisting, duration);
    }

    private static TimeSpan ReadDuration(IMFSourceReader reader)
    {
        try
        {
            var value = reader.GetPresentationAttribute(SourceReaderIndex.MediaSource, DurationKey);
            return value.Value is ulong ticks ? TimeSpan.FromTicks((long)ticks) : TimeSpan.Zero;
        }
        catch (Exception)
        {
            return TimeSpan.Zero;   // 길이를 모르면 진행에 퍼센트만 안 보일 뿐이다(쓰는 중이던 조각 mp4 등)
        }
    }

    /// <summary>32×18 칸의 평균 밝기. 칸마다 가운데 몇 점만 본다 - 전부 더하면 1080p 한 장에 200만 번이다.</summary>
    private static byte[] Thumbnail(byte[] bgra, int width, int height)
    {
        var thumb = new byte[ThumbWidth * ThumbHeight];

        for (var ty = 0; ty < ThumbHeight; ty++)
        {
            for (var tx = 0; tx < ThumbWidth; tx++)
            {
                var sum = 0;

                for (var sy = 0; sy < 3; sy++)
                {
                    for (var sx = 0; sx < 3; sx++)
                    {
                        var x = (int)((tx + ((sx + 0.5) / 3)) * width / ThumbWidth);
                        var y = (int)((ty + ((sy + 0.5) / 3)) * height / ThumbHeight);
                        var at = ((Math.Min(y, height - 1) * width) + Math.Min(x, width - 1)) * 4;

                        sum += (bgra[at] + (bgra[at + 1] * 2) + bgra[at + 2]) / 4;
                    }
                }

                thumb[(ty * ThumbWidth) + tx] = (byte)(sum / 9);
            }
        }

        return thumb;
    }

    private static double Difference(byte[] a, byte[] b)
    {
        var total = 0L;
        for (var i = 0; i < a.Length; i++) total += Math.Abs(a[i] - b[i]);
        return (double)total / a.Length;
    }

    private static void SavePng(byte[] bgra, int width, int height, string path)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        source.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        // 임시 이름에 다 쓰고 바꾼다 - 라벨링 화면이 다시 읽다 반쯤 쓴 PNG 를 집지 않게.
        var temporary = path + ".tmp";

        using (var stream = File.Create(temporary))
            encoder.Save(stream);

        File.Move(temporary, path, overwrite: true);
    }
}
