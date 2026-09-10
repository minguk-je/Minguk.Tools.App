using System;
using System.IO;
using System.Linq;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 찍어 둔 라벨이 학습기가 받는 모양으로 제대로 바뀌는지.
/// </summary>
/// <remarks>
/// 우리는 0~1 로 담는데 AutoFormerV2 는 <b>픽셀</b>을 받는다. 여기서 어긋나면 사각형이
/// 전부 왼쪽 위 한 점에 몰리는데, 학습은 그대로 돌아가고 몇 시간 뒤에 아무것도 못 배운
/// 모델이 나온다. 그때는 데이터가 나쁜 건지 코드가 틀린 건지 가릴 수가 없다.
///
/// libtorch 없이 볼 수 있는 데까지만 본다. 실제 학습은 2GB 를 받아야 해서 하네스에 못 넣는다.
/// </remarks>
internal static partial class Program
{
    private static void TestTrainingShape(LabelDataset dataset, string imagePath)
    {
        // ── 그림 크기를 머리말만 읽어 알아내는지 ──
        //
        // 진짜 PNG 를 하나 만든다. 앞선 검사들이 쓰는 것은 0 바이트짜리 가짜라 여기 못 쓴다.
        var realPng = Path.Combine(dataset.ImageDirectory, "20260910-150000-000.png");

        File.WriteAllBytes(realPng, MakePng(320, 200));

        Check("PNG 크기를 머리말에서 읽는다",
              ImageSize.TryRead(realPng, out var width, out var height) && width == 320 && height == 200,
              $"{width}x{height}");

        Check("모르는 파일은 크기를 안 준다",
              !ImageSize.TryRead(imagePath + ".bmp", out _, out _),
              "없는 파일도 터지지 않음");

        // ── 0~1 이 픽셀로 바뀌는지 ──
        var classes = new LabelClasses(["슬라임", "버섯"]);

        dataset.SaveClasses(classes);

        // 왼쪽 위 1/4 을 차지하는 사각형. 320x200 이면 (0,0)~(160,100) 이어야 한다.
        var box = LabelBox.FromCorners(1, 0d, 0d, 0.5, 0.5);

        LabelFile.Save(dataset.LabelPathFor(realPng), [box]);

        var sample = TrainingSample.From(new LabelItem(realPng, dataset.LabelPathFor(realPng)), classes);

        Check("0~1 을 픽셀로 바꾼다",
              sample is not null
              && sample.Box.Length == 4
              && Near(sample.Box[0], 0f) && Near(sample.Box[1], 0f)
              && Near(sample.Box[2], 160f) && Near(sample.Box[3], 100f),
              sample is null ? "(못 만듦)" : string.Join(", ", sample.Box));

        Check("몹 번호를 이름으로 되돌린다",
              sample is not null && sample.Labels.SequenceEqual(["버섯"]),
              sample is null ? "(못 만듦)" : string.Join(", ", sample.Labels));

        // ── 안 찍은 그림은 빠지는지 ──
        //
        // 우리 목록의 "안 찍음" 은 아직 안 본 그림이라는 뜻이지 비었다는 뜻이 아니다.
        // 넣으면 "여기엔 아무것도 없다" 를 가르치게 된다.
        var empty = Path.Combine(dataset.ImageDirectory, "20260910-150000-001.png");

        File.WriteAllBytes(empty, MakePng(64, 64));

        Check("라벨 없는 그림은 학습에서 뺀다",
              TrainingSample.From(new LabelItem(empty, dataset.LabelPathFor(empty)), classes) is null,
              "null 로 걸러짐");

        var collected = DetectorTrainer.Collect(dataset, classes);

        Check("모을 때도 찍은 것만 담는다",
              collected.All(c => c.Labels.Length > 0) && collected.Any(c => c.ImagePath == realPng),
              $"{collected.Count}장");

        Check("모델은 데이터셋 폴더에 둔다",
              DetectorTrainer.ModelPathFor(dataset) == Path.Combine(dataset.Root, DetectorTrainer.ModelFileName),
              Path.GetFileName(DetectorTrainer.ModelPathFor(dataset)));

        // ── libtorch 를 안 올리고 학습을 부르면 ──
        //
        // 조용히 터지면 안 된다. 무엇이 없어서인지 말해야 한다.
        if (!LibTorchRuntime.IsLoaded)
        {
            var threw = false;
            var message = string.Empty;

            try
            {
                DetectorTrainer.TrainAsync(dataset, 1).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex)
            {
                threw = true;
                message = ex.Message;
            }
            catch (Exception ex)
            {
                message = $"{ex.GetType().Name}: {ex.Message}";
            }

            Check("libtorch 없이 학습하면 그렇다고 말한다", threw && message.Contains("libtorch"), message);
        }
        else
        {
            Skip("libtorch 없이 학습하면 그렇다고 말한다", "이 프로세스는 이미 libtorch 를 올렸다");
        }

        // ── 받는 자리와 크기가 말이 되는지 ──
        Check("판을 TorchSharp 와 맞춰 둔다", LibTorchRuntime.Version == "2.2.1", LibTorchRuntime.Version);

        Check("CUDA 와 CPU 를 다른 자리에 둔다",
              LibTorchRuntime.LibraryDirectory(LibTorchFlavor.Cuda)
              != LibTorchRuntime.LibraryDirectory(LibTorchFlavor.Cpu),
              LibTorchRuntime.LibraryDirectory(LibTorchFlavor.Cuda));

        Check("받는 자리는 실행 폴더 밖이다",
              !LibTorchRuntime.RootDirectory.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase),
              LibTorchRuntime.RootDirectory);

        Check("이 PC 에 CUDA 드라이버가 있나",
              true,
              LibTorchRuntime.HasCudaDriver
                  ? $"있다 - {LibTorchRuntime.Recommended} ({LibTorchRuntime.DescribeSize(LibTorchFlavor.Cuda)}) 를 받게 된다"
                  : $"없다 - {LibTorchRuntime.Recommended} ({LibTorchRuntime.DescribeSize(LibTorchFlavor.Cpu)}) 로 떨어진다");
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;

    /// <summary>단색 PNG. 크기를 읽는 것만 보므로 내용은 아무래도 좋다.</summary>
    private static byte[] MakePng(int width, int height)
    {
        var raw = new byte[((width * 3) + 1) * height];

        using var output = new MemoryStream();

        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];

        BigEndian(header, 0, width);
        BigEndian(header, 4, height);
        header[8] = 8;    // 채널당 8비트
        header[9] = 2;    // 트루컬러

        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, Zlib(raw));
        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();
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
