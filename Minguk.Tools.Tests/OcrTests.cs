using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Ocr;

namespace Minguk.Tools.Tests;

/// <summary>
/// 글자 읽기. 글자를 직접 그린 그림을 넣어 그 글자가 나오는지 본다.
/// </summary>
/// <remarks>
/// 게임 화면이 없어도 돌아야 한다. 그래서 WPF 로 흰 바탕에 검은 글자를 그려 넣는다.
/// 작은 글자(게임 UI 크기)도 키워서 읽히는지 같이 본다 - 안 키우면 놓치는 것이 있었다.
/// </remarks>
internal static partial class Program
{
    private static void TestOcr()
    {
        TestNameplateRegion();
        TestNameplateInk();

        var engine = OcrEngineFactory.TryCreate();

        if (engine is null)
        {
            Check("OCR 엔진", false, $"언어 팩 없음 (깔린 것: {string.Join(", ", WindowsOcrEngine.AvailableLanguages)})");
            return;
        }

        Check("OCR 엔진을 만든다", true, $"{engine.Name} {engine.Language}");

        // ── 큰 글자 ──
        var big = DrawText("HP 1234", 40, 320, 90);
        var read = engine.RecognizeAsync(big).GetAwaiter().GetResult();
        var flat = read.Text.Replace(" ", string.Empty);

        Check("큰 글자를 읽는다", flat.Contains("1234"), $"[{read.Text}] {read.Elapsed.TotalMilliseconds:0}ms");

        var inside = read.Lines.SelectMany(l => l.Words).All(w => w.Box.Left >= 0 && w.Box.Top >= 0 && w.Box.Right <= 1 && w.Box.Bottom <= 1);
        Check("단어 자리는 0~1 안", read.Lines.Count > 0 && inside,
              string.Join(" ", read.Lines.SelectMany(l => l.Words).Select(w => $"{w.Text}@{w.Box.Left:0.00},{w.Box.Top:0.00}")));

        // ── 작은 글자 (게임 UI 크기, 높이 24px) - 키워서 읽어야 한다 ──
        var small = DrawText("LV 57", 14, 90, 24);
        var readSmall = engine.RecognizeAsync(small).GetAwaiter().GetResult();

        Check("작은 글자도 읽는다", readSmall.Text.Replace(" ", string.Empty).Contains("57"),
              $"[{readSmall.Text}] {readSmall.Elapsed.TotalMilliseconds:0}ms");

        // ── 글자 없는 그림 ──
        var blank = DrawText(string.Empty, 20, 120, 40);
        var readBlank = engine.RecognizeAsync(blank).GetAwaiter().GetResult();

        Check("빈 그림은 빈 글", readBlank.Text.Length == 0, $"[{readBlank.Text}]");

        engine.Dispose();
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

    /// <summary>이름표 전처리. 회색 바탕의 빨간 글자만 검정으로 남고, 3배로 커지고, 그 결과를 OCR 이 읽는다.</summary>
    private static void TestNameplateInk()
    {
        Check("빨강은 글자", NameplateInk.IsInk(220, 40, 50) && NameplateInk.IsInk(255, 120, 120), "220,40,50 / 255,120,120");
        Check("회색·흰색·파랑은 배경", !NameplateInk.IsInk(140, 140, 150) && !NameplateInk.IsInk(255, 255, 255) && !NameplateInk.IsInk(60, 80, 220), "");

        // 게임처럼: 회색 바탕 224x72 조각 가운데에 13px 빨간 글자 (실제 잘린 조각 크기)
        var plate = DrawText("일반 봇", 13, 224, 72, Brushes.Gray, Brushes.Red, "Malgun Gothic", 90);
        var masked = NameplateInk.MaskInk(plate);
        var pixels = new byte[masked.PixelWidth * 4 * masked.PixelHeight];
        masked.CopyPixels(pixels, masked.PixelWidth * 4, 0);

        var black = 0;
        for (var i = 0; i < pixels.Length; i += 4) if (pixels[i] == 0) black++;

        var total = masked.PixelWidth * masked.PixelHeight;
        Check("글자 픽셀만 남는다(0.5~30%)", black > total * 0.005 && black < total * 0.30, $"{black}/{total}");

        var prepared = NameplateInk.Prepare(plate);
        Check("3배로 커진다", prepared.PixelWidth == 672 && prepared.PixelHeight == 216, $"{prepared.PixelWidth}x{prepared.PixelHeight}");

        var ko = OcrEngineFactory.TryCreate("ko");
        if (ko is null) { Check("이름표 OCR (ko 팩 없음, 건너뜀)", true, ""); return; }

        var raw = ko.RecognizeAsync(plate).GetAwaiter().GetResult().Text;
        var read = ko.RecognizeAsync(prepared).GetAwaiter().GetResult().Text.Replace(" ", string.Empty);
        Check("전처리하면 이름표를 읽는다", read.Contains("일반"), $"원본=[{raw}] 전처리=[{read}]");
        ko.Dispose();
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
