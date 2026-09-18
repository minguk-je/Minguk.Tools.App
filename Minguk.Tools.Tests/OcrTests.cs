using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Ocr;

namespace Minguk.Tools.Tests;

/// <summary>
/// 글자 읽기. 이름표 자리와 팩터리를 본다 - 엔진 자체는 <c>PaddleOcrTests</c>.
/// </summary>
/// <remarks>
/// 게임 화면이 없어도 돌아야 한다. 그래서 WPF 로 글자를 직접 그려 넣는다(<see cref="DrawText(string, double, int, int)"/>).
/// </remarks>
internal static partial class Program
{
    private static void TestOcr()
    {
        TestNameplateRegion();
        TestPaddleOcr();
        TestWindowsOcrIgnoresCaptureAlpha();

        using var engine = OcrEngineFactory.Create(out var fallback);

        Check("팩터리가 PP-OCRv5 를 준다", engine.Name.StartsWith("PP-OCRv5", StringComparison.Ordinal),
              fallback is null ? engine.Name : $"{engine.Name} - {fallback}");
    }

    /// <summary>
    /// 화면 캡처(WGC)는 불투명한 창인데도 알파를 0 으로 준다 - Windows OCR 에 Premultiplied 로 넘기면 그 픽셀이 통째로
    /// 검게 지워져 아무것도 못 읽었다(사용자, 2026-09-18 "전혀 못읽네"). 알파를 일부러 0 으로 뭉갠 그림도 읽히는지 본다.
    /// </summary>
    private static void TestWindowsOcrIgnoresCaptureAlpha()
    {
        using var engine = WindowsOcrEngine.TryCreate();

        if (engine is null)
        {
            Check("Windows OCR: 언어 팩이 없어 이 PC 에서는 건너뛴다", true, "");
            return;
        }

        var plain = DrawText("1234", 40, 200, 60);
        var plainOutcome = engine.RecognizeAsync(plain).GetAwaiter().GetResult();
        Check("Windows OCR: 알파 255(평소) 대조군", plainOutcome.Text.Contains("1234"), $"[{plainOutcome.Text}] 언어={engine.Language}");

        var captured = ZeroAlpha(plain);
        var outcome = engine.RecognizeAsync(captured).GetAwaiter().GetResult();

        Check("Windows OCR: 캡처처럼 알파가 0이어도 읽는다", outcome.Text.Contains("1234"), $"[{outcome.Text}]");
    }

    /// <summary>알파를 몽땅 0 으로 뭉갠다 - 화면 캡처(WGC) 프레임의 알파가 흔히 이렇다(불투명한 창인데도).</summary>
    private static BitmapSource ZeroAlpha(BitmapSource source)
    {
        var bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        bgra.CopyPixels(pixels, stride, 0);

        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 0;

        var result = BitmapSource.Create(width, height, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();

        return result;
    }

    /// <summary>몹 사각형 위 이름표 자리. 위로 올라가고, 넓어지고, 화면 밖으로는 안 나간다.</summary>
    private static void TestNameplateRegion()
    {
        var box = Minguk.Tools.Vision.Labeling.LabelBox.FromCorners(0, 0.4, 0.4, 0.5, 0.6);   // 0.1 x 0.2
        var above = NameplateRegion.Above(box);

        Check("이름표는 사각형 바로 위", Math.Abs(above.Bottom - 0.41) < 0.001 && Math.Abs(above.Height - 0.09) < 0.001,
              $"{above.Top:0.###}~{above.Bottom:0.###}");
        Check("이름표는 사각형보다 넓다", Math.Abs(above.Width - 0.18) < 0.001 && Math.Abs(above.X - 0.36) < 0.001,
              $"x {above.X:0.###} w {above.Width:0.###}");

        var edge = NameplateRegion.Above(Minguk.Tools.Vision.Labeling.LabelBox.FromCorners(0, 0.0, 0.0, 0.1, 0.2));
        Check("화면 위쪽 끝에서는 잘라도 밖으로 안 나간다", edge.IsEmpty || (edge.Top >= 0 && edge.X >= 0), edge.ToString());
    }

    private static BitmapSource DrawText(string text, double size, int width, int height)
        => DrawText(text, size, width, height, Brushes.White, Brushes.Black);

    private static BitmapSource DrawText(string text, double size, int width, int height, Brush background, Brush ink, string font = "Segoe UI", double left = 6)
    {
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));

            if (text.Length > 0)
            {
                var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                                  new Typeface(font), size, ink, 1.0);
                dc.DrawText(formatted, new Point(left, (height - formatted.Height) / 2));
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }
}
