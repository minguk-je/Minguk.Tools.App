using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 밝기로 스스로 문턱을 잡아 글자만 남긴다(오츠 방법).
/// </summary>
/// <remarks>
/// <b>왜</b> - "밝은 글자만"·"어두운 글자만" 은 문턱이 고정이라 배경이 바뀌면 무너진다. 오버워치 탄약 자리는 같은 자리인데도
/// 배경이 어두운 벽·밝은 바닥·하늘로 바뀌어, 고정 문턱으로는 열 장 중 몇 장만 읽혔다(실측 2026-09-16).
/// 여기서는 조각의 밝기 분포를 보고 두 무리로 가르는 문턱을 그때그때 고른다 - 배경이 밝든 어둡든 글자만 남는다.
///
/// 어느 쪽이 글자인지는 <b>적은 쪽</b>으로 본다. 글자는 자리의 일부고 배경이 대부분이다.
/// </remarks>
public sealed class AutoInkOcrPreprocessor : IOcrPreprocessor
{
    public string Id => "auto";

    public string Name => "밝기로 자동";

    public string Summary => "조각의 밝기를 보고 문턱을 스스로 잡아 글자만 남긴다. 배경이 바뀌는 자리에.";

    public BitmapSource Prepare(BitmapSource crop, OcrPreprocessOptions options)
        => OcrPreprocess.Finish(Binarize(crop), options, Colors.White);

    /// <summary>오츠 문턱으로 글자는 검정, 배경은 흰색.</summary>
    public static BitmapSource Binarize(BitmapSource crop)
    {
        var source = crop.Format == PixelFormats.Bgra32 ? crop : new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];

        source.CopyPixels(pixels, stride, 0);

        var histogram = new int[256];
        var count = pixels.Length / 4;

        for (var i = 0; i < pixels.Length; i += 4)
            histogram[Luma(pixels[i + 2], pixels[i + 1], pixels[i])]++;

        var threshold = OtsuThreshold(histogram, count);

        // 밝은 쪽과 어두운 쪽 중 적은 쪽이 글자다.
        var bright = 0;

        for (var value = threshold + 1; value < histogram.Length; value++) bright += histogram[value];

        var inkIsBright = bright <= count - bright;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var luma = Luma(pixels[i + 2], pixels[i + 1], pixels[i]);
            var isInk = inkIsBright ? luma > threshold : luma <= threshold;
            var value = isInk ? (byte)0 : (byte)255;

            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }

        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        result.Freeze();

        return result;
    }

    private static byte Luma(byte r, byte g, byte b) => (byte)(((r * 299) + (g * 587) + (b * 114)) / 1000);

    /// <summary>두 무리의 흩어짐이 가장 작아지는 문턱(오츠).</summary>
    private static int OtsuThreshold(int[] histogram, int count)
    {
        if (count == 0) return 128;

        double sum = 0;

        for (var value = 0; value < histogram.Length; value++) sum += value * (double)histogram[value];

        double sumLow = 0, weightLow = 0, best = -1;
        var threshold = 128;

        for (var value = 0; value < histogram.Length; value++)
        {
            weightLow += histogram[value];

            if (weightLow == 0) continue;

            var weightHigh = count - weightLow;

            if (weightHigh == 0) break;

            sumLow += value * (double)histogram[value];

            var meanLow = sumLow / weightLow;
            var meanHigh = (sum - sumLow) / weightHigh;
            var between = weightLow * weightHigh * (meanLow - meanHigh) * (meanLow - meanHigh);

            if (between <= best) continue;

            best = between;
            threshold = value;
        }

        return threshold;
    }
}
