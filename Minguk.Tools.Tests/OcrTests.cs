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

    private static BitmapSource DrawText(string text, double size, int width, int height)
    {
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            if (text.Length > 0)
            {
                var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                                  new Typeface("Segoe UI"), size, Brushes.Black, 1.0);
                dc.DrawText(formatted, new Point(6, (height - formatted.Height) / 2));
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }
}
