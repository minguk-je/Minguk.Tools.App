using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Labeling;

using NLog;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 학습에 넣을 그림을 모델 크기로 한 번만 줄여 두는 캐시.
/// </summary>
/// <remarks>
/// <b>왜</b> - 학습기는 매 바퀴 그림을 파일에서 읽는다. 1080p PNG 97장을 40바퀴면 3,880번을
/// 읽고 줄이는데, 그것이 전부 CPU 몫이라 GPU 는 13% 만 돌고 학습이 2시간 걸렸다. 모델이
/// 실제로 보는 것은 640x360 이므로 그 크기로 한 번 줄여 두면 매 바퀴 읽는 양이 10분의 1
/// 아래로 떨어진다. 카드를 바꿔도 이 병목은 그대로라, 카드보다 먼저 할 일이었다.
///
/// <b>라벨은 안 건드린다</b> - 0~1 비율이고 Fill 로 줄여 화면 전체가 그대로 담기므로 좌표가
/// 그대로 맞는다. 줄이는 방법은 캡처 모니터의 실시간 경로(WPF)와 같아, 학습과 실시간이
/// 같은 그림을 보게 된다. 원본으로 재는 되찾기와의 차이는 `--scale-check` 로 확인했다(거의 없음).
///
/// <b>언제 다시 만드나</b> - 캐시 파일이 없거나, 원본의 쓴 시각이 캐시보다 뒤이거나, 캐시가
/// 0 바이트면 그 장만 다시 만든다. 크기마다 폴더가 다르다(<c>cache\640x360\</c>).
/// </remarks>
public static class TrainingImageCache
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public const string FolderName = "cache";

    public static string DirectoryFor(LabelDataset dataset, int width, int height)
        => Path.Combine(dataset.Root, FolderName, $"{width}x{height}");

    /// <summary>
    /// 그림들을 캐시에 맞춰 두고, 원본 경로 → 캐시 경로 표를 돌려준다.
    /// </summary>
    /// <remarks>
    /// 한 장이라도 못 만들면 그 장은 원본 경로를 그대로 쓴다. 학습기가 원본을 줄여 쓰므로
    /// 느릴 뿐 틀리지는 않는다 - 캐시 때문에 학습이 서면 안 된다.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Ensure(LabelDataset dataset, IEnumerable<string> imagePaths,
                                                              int width, int height,
                                                              IProgress<string>? progress = null,
                                                              CancellationToken token = default)
    {
        var directory = DirectoryFor(dataset, width, height);
        Directory.CreateDirectory(directory);

        var sources = imagePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        var made = 0;

        var work = sources.Select(source => (Source: source, Target: Path.Combine(directory, Path.GetFileNameWithoutExtension(source) + ".png"))).ToList();

        // 줄이는 일은 서로 독립이라 코어를 다 쓴다. 한 장 50~80ms 라 97장도 몇 초면 끝난다.
        Parallel.ForEach(work, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount },
            item =>
            {
                var cached = item.Target;

                try
                {
                    if (!IsFresh(item.Source, cached))
                    {
                        Resize(item.Source, cached, width, height);
                        Interlocked.Increment(ref made);
                    }

                    lock (map) map[item.Source] = cached;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"그림을 캐시에 못 넣었다. 원본을 그대로 쓴다: {item.Source}");
                    lock (map) map[item.Source] = item.Source;
                }

                var n = Interlocked.Increment(ref done);
                if (n % 10 == 0 || n == work.Count) progress?.Report($"그림을 {width}x{height} 으로 줄이는 중 {n}/{work.Count}");
            });

        Logger.Info($"그림 캐시 {directory}: {work.Count}장 중 {made}장 새로 만듦.");

        return map;
    }

    /// <summary>캐시가 있고, 비어 있지 않고, 원본보다 나중에 쓰였으면 그대로 쓴다.</summary>
    private static bool IsFresh(string source, string cached)
    {
        var target = new FileInfo(cached);
        if (!target.Exists || target.Length == 0) return false;

        return target.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(source);
    }

    /// <summary>캡처 모니터의 실시간 경로와 같은 방법(TransformedBitmap → PNG)으로 줄인다. 비율은 안 지킨다(Fill).</summary>
    private static void Resize(string source, string target, int width, int height)
    {
        BitmapSource bitmap;

        using (var stream = File.OpenRead(source))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            bitmap = image;
        }

        var scaled = new TransformedBitmap(bitmap, new ScaleTransform((double)width / bitmap.PixelWidth, (double)height / bitmap.PixelHeight));
        scaled.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));

        // 다 쓴 뒤에 이름을 바꾼다. 쓰다 만 파일이 남으면 다음에 "있다" 고 믿고 깨진 그림을 학습한다.
        var temp = target + ".tmp";

        using (var output = File.Create(temp))
            encoder.Save(output);

        File.Move(temp, target, overwrite: true);
    }
}
