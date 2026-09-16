using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>손질 없이 키우기만. 글자와 배경의 대비가 충분한 자리(대부분)에 쓴다.</summary>
public sealed class PlainOcrPreprocessor : IOcrPreprocessor
{
    public string Id => "plain";

    public string Name => "그대로 키우기";

    public string Summary => "색은 안 건드리고 글자만 키워 읽는다. 대비가 충분한 자리에.";

    public BitmapSource Prepare(BitmapSource crop, OcrPreprocessOptions options)
        => OcrPreprocess.Finish(crop, options, Colors.Black);
}

/// <summary>밝은 글자만 남긴다(흰 HUD 숫자). 배경이 밝아 대비가 사라지는 자리에.</summary>
public sealed class BrightInkOcrPreprocessor : IOcrPreprocessor
{
    public string Id => "bright";

    public string Name => "밝은 글자만";

    public string Summary => "흰 글자만 검정으로 남기고 나머지를 지운다. 밝은 배경 위의 HUD 숫자에.";

    public BitmapSource Prepare(BitmapSource crop, OcrPreprocessOptions options)
        => OcrPreprocess.Finish(OcrPreprocess.Mask(crop, HudInk.IsInk), options, Colors.White);
}

/// <summary>어두운 글자만 남긴다. 밝은 창에 검은 글자를 쓰는 게임·UI 에.</summary>
public sealed class DarkInkOcrPreprocessor : IOcrPreprocessor
{
    public string Id => "dark";

    public string Name => "어두운 글자만";

    public string Summary => "어두운 글자만 남기고 나머지를 지운다. 밝은 창에 검은 글자를 쓰는 화면에.";

    /// <summary>이보다 어두우면 글자로 본다(R·G·B 모두).</summary>
    public const int MaximumDark = 90;

    public BitmapSource Prepare(BitmapSource crop, OcrPreprocessOptions options)
        => OcrPreprocess.Finish(OcrPreprocess.Mask(crop, (r, g, b) => Math.Max(r, Math.Max(g, b)) <= MaximumDark), options, Colors.White);
}

/// <summary>전처리들이 같이 쓰는 손질 - 고르기(마스크), 세우기, 키우기.</summary>
public static class OcrPreprocess
{
    /// <summary>이 픽셀이 글자인가.</summary>
    public delegate bool InkTest(byte r, byte g, byte b);

    /// <summary>글자로 고른 픽셀은 검정, 나머지는 흰색. 크기는 그대로.</summary>
    public static BitmapSource Mask(BitmapSource crop, InkTest isInk)
    {
        var source = crop.Format == PixelFormats.Bgra32 ? crop : new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];

        source.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var value = isInk(pixels[i + 2], pixels[i + 1], pixels[i]) ? (byte)0 : (byte)255;

            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }

        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        result.Freeze();

        return result;
    }

    /// <summary>
    /// 읽을 가로 범위 밖을 배경색으로 지운다. 조각을 자르지 않고 지우는 것이 중요하다 -
    /// 좁게 잘라 넣으면 OCR 이 못 읽는다(실측 2026-09-16: 탄약 자리를 반으로 자르면 10장 중 1장, 통째로는 10장 중 10장).
    /// </summary>
    public static BitmapSource KeepRange(BitmapSource image, OcrPreprocessOptions options, Color background)
    {
        if (!options.HasKeepRange) return image;

        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var from = (int)Math.Round(Math.Clamp(options.KeepFrom, 0, 1) * width);
        var to = (int)Math.Round(Math.Clamp(options.KeepTo, 0, 1) * width);

        if (to <= from) return image;

        var visual = new System.Windows.Media.DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new System.Windows.Rect(0, 0, width, height));
            dc.PushClip(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(from, 0, to - from, height)));
            dc.DrawImage(image, new System.Windows.Rect(0, 0, width, height));
            dc.Pop();
        }

        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        target.Render(visual);
        target.Freeze();

        return target;
    }

    /// <summary>범위 밖을 지우고, 세우고(기울기), 키운다(목표 높이). 전처리들이 마지막에 부르는 공통 길.</summary>
    /// <param name="background">지우거나 세울 때 남는 자리를 채울 색.</param>
    public static BitmapSource Finish(BitmapSource image, OcrPreprocessOptions options, Color background)
    {
        image = KeepRange(image, options, background);

        // 세우기가 먼저다 - 키운 뒤에 기울이면 계단이 같이 늘어난다.
        var upright = OcrDeskew.Apply(image, options.ShearDegrees, background);
        var scale = options.ScaleFor(upright.PixelHeight);

        if (scale <= 1.0001)
        {
            if (upright.CanFreeze) upright.Freeze();

            return upright;
        }

        var scaled = new TransformedBitmap(upright, new ScaleTransform(scale, scale));

        scaled.Freeze();

        return scaled;
    }
}
