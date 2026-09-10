using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 실시간으로 쓸 만한 속도가 나오는지 잰다.
/// </summary>
/// <remarks>
/// 캡처 화면에 붙이려면 <b>프레임마다</b> 추론이 돌아야 한다. 몇 ms 가 걸리는지 모르면
/// 붙일지 말지를 정할 수 없다. 캡처가 30fps 면 한 프레임에 33ms 다.
///
/// 그림 크기를 나눠 재는 이유: 검출망은 제 입력 크기로 줄여서 보므로 망 자체는 크기를 안 타지만,
/// <b>파일을 읽고 디코딩하는 값</b>은 크기를 그대로 탄다. 1920x1080 PNG 한 장을 읽는 것만으로
/// 몇 ms 가 나가는지가 실시간에서는 곧바로 문제가 된다.
///
/// 이 모드는 libtorch(4GB)와 학습한 모델이 있어야 돈다. 그래서 평소 검증에는 안 낀다 -
/// <c>--detect-bench</c> 로 따로 부른다.
/// </remarks>
internal static class DetectBench
{
    public static int Run()
    {
        var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);

        Console.WriteLine($"데이터셋: {dataset.Root}");

        if (LibTorchRuntime.Installed is not { } flavor)
        {
            Console.WriteLine("libtorch 가 없다. 앱에서 학습을 한 번 눌러 받아야 한다.");
            return 1;
        }

        Console.WriteLine($"libtorch: {flavor} ({LibTorchRuntime.LibraryDirectory(flavor)})");

        var loading = Stopwatch.StartNew();

        LibTorchRuntime.Load(flavor);

        loading.Stop();

        Console.WriteLine($"libtorch 올리기: {loading.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"CUDA: {TorchSharp.torch.cuda.is_available()}");
        Console.WriteLine();

        var modelPath = DetectorTrainer.ModelPathFor(dataset);

        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"학습한 모델이 없다: {modelPath}");
            Console.WriteLine("먼저 앱에서 학습하거나 --detect-bench --train 으로 부른다.");

            return 1;
        }

        var opening = Stopwatch.StartNew();

        using var model = DetectorModel.Load(modelPath);

        opening.Stop();

        Console.WriteLine($"모델 읽기: {opening.ElapsedMilliseconds:N0} ms " +
                          $"({new FileInfo(modelPath).Length / 1_048_576:N0} MB)");
        Console.WriteLine();

        var classes = dataset.LoadClasses();
        var temp = Path.Combine(Path.GetTempPath(), "minguk-bench-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(temp);

        try
        {
            // 얼마나 줄여야 쓸 만해지는지 곡선을 본다. 검출망이 원본 크기를 그대로 보므로
            // 값이 픽셀 수에 거의 비례한다 - 어디서 타협할지는 이 표를 보고 정한다.
            Measure(model, classes, temp, "1/12", 160, 90);
            Measure(model, classes, temp, "1/6", 320, 180);
            Measure(model, classes, temp, "1/4", 480, 270);
            Measure(model, classes, temp, "1/3", 640, 360);
            Measure(model, classes, temp, "1/2", 960, 540);
            Measure(model, classes, temp, "1080p 그대로", 1920, 1080);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (Exception) { }
        }

        Console.WriteLine();
        Console.WriteLine("== 잰 것 ==");
        Console.WriteLine("파일을 읽는 값이 같이 들어 있다. 실시간에서는 캡처가 이미 픽셀을 들고 있으므로");
        Console.WriteLine("그만큼은 뺄 수 있지만, 지금 API 는 경로만 받는다.");

        return 0;
    }

    private static void Measure(DetectorModel model, LabelClasses classes, string temp,
                                string name, int width, int height)
    {
        var path = Path.Combine(temp, $"{width}x{height}.png");

        File.WriteAllBytes(path, MakePng(width, height));

        // 첫 번째는 버린다. CUDA 커널을 올리는 값과 JIT 이 섞여 있어 평소 값이 아니다.
        var first = Stopwatch.StartNew();
        var found = model.Detect(path, classes, 0.3f);
        first.Stop();

        const int rounds = 20;
        var times = new List<double>(rounds);

        for (var i = 0; i < rounds; i++)
        {
            var watch = Stopwatch.StartNew();

            _ = model.Detect(path, classes, 0.3f);

            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        // 파일을 읽고 디코딩하는 값만 따로 잰다. 추론 시간이 크기를 타는 것이 망 때문인지
        // 그림을 읽는 값 때문인지 갈리지 않으면 무엇을 고쳐야 할지 알 수 없다.
        var decode = new List<double>(5);

        for (var i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();

            using (var stream = File.OpenRead(path))
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();

                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            watch.Stop();
            decode.Add(watch.Elapsed.TotalMilliseconds);
        }

        decode.Sort();

        times.Sort();

        var median = times[times.Count / 2];
        var min = times[0];
        var max = times[^1];

        Console.WriteLine($"{name} ({width}x{height})");
        Console.WriteLine($"  첫 번째 {first.ElapsedMilliseconds:N0} ms (커널 올리기 포함) / 찾은 것 {found.Count}개");
        var decodeMedian = decode[decode.Count / 2];

        Console.WriteLine($"  가운뎃값 {median:N1} ms  최소 {min:N1}  최대 {max:N1}  →  {1000 / median:N1} fps");
        Console.WriteLine($"  그중 그림 읽기 {decodeMedian:N1} ms ({decodeMedian / median:P0}), 나머지 {median - decodeMedian:N1} ms 가 추론");
    }

    /// <summary>바탕에 네모 두 개를 그린 PNG. 크기만 다르게 만든다.</summary>
    private static byte[] MakePng(int width, int height)
    {
        var pixels = new byte[width * height * 3];

        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = 28;
            pixels[i + 1] = 36;
            pixels[i + 2] = 56;
        }

        Fill(pixels, width, height, width / 8, height / 4, width / 5, height / 3, [210, 70, 60]);
        Fill(pixels, width, height, width / 2, height / 2, width / 6, height / 4, [70, 200, 110]);

        var raw = new byte[((width * 3) + 1) * height];

        for (var y = 0; y < height; y++)
        {
            Array.Copy(pixels, y * width * 3, raw, (y * ((width * 3) + 1)) + 1, width * 3);
        }

        using var output = new MemoryStream();

        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];

        BigEndian(header, 0, width);
        BigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 2;

        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, Zlib(raw));
        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();
    }

    private static void Fill(byte[] pixels, int width, int height, int x, int y, int w, int h, byte[] colour)
    {
        for (var py = y; py < Math.Min(y + h, height); py++)
        {
            for (var px = x; px < Math.Min(x + w, width); px++)
            {
                var i = ((py * width) + px) * 3;

                pixels[i] = colour[0];
                pixels[i + 1] = colour[1];
                pixels[i + 2] = colour[2];
            }
        }
    }

    private static void BigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();

        output.WriteByte(0x78);
        output.WriteByte(0x9C);

        using (var deflate = new System.IO.Compression.DeflateStream(
                   output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(data);
        }

        uint a = 1, b = 0;

        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        var adler = new byte[4];

        BigEndian(adler, 0, (int)((b << 16) | a));
        output.Write(adler);

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> tag, byte[] data)
    {
        var length = new byte[4];

        BigEndian(length, 0, data.Length);
        stream.Write(length);
        stream.Write(tag);
        stream.Write(data);

        var body = new byte[tag.Length + data.Length];

        tag.CopyTo(body);
        data.CopyTo(body, tag.Length);

        var crc = new byte[4];

        BigEndian(crc, 0, (int)Crc32(body));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in data)
        {
            crc ^= value;

            for (var i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(crc & 1));
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
