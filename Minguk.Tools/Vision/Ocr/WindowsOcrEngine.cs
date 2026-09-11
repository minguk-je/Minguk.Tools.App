using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Labeling;

using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// Windows 에 내장된 OCR(<c>Windows.Media.Ocr</c>).
/// </summary>
/// <remarks>
/// <b>왜 이것인가</b> - 따로 받을 것이 없고(언어 팩은 Windows 설정에 있다), 한글을 읽고,
/// 결과에 단어마다 자리가 온다. 게임 UI 글자는 깨끗해서 이 정도로 충분하다. 이 PC 에는
/// 한국어 팩(ko)만 있는데 그것으로 영문·숫자도 읽는다.
///
/// <b>작은 글자</b> - 게임 UI 글자는 12~20px 로 작다. 그대로 넣으면 못 읽는 것이 있어
/// 높이가 <see cref="MinimumHeight"/> 아래면 키워서 넣는다. 자리는 비율로 돌려주므로
/// 키운 것과 무관하다.
///
/// <b>스레드</b> - WinRT 비동기라 어느 스레드에서 불러도 된다. 넘기는 그림은 Freeze 된 것이어야
/// 캡처 스레드에서 만든 것을 여기서 읽을 수 있다.
/// </remarks>
public sealed class WindowsOcrEngine : IOcrEngine
{
    /// <summary>이보다 낮은 그림은 이 높이 근처까지 키워서 넣는다.</summary>
    public const int MinimumHeight = 160;

    private readonly OcrEngine _engine;

    private WindowsOcrEngine(OcrEngine engine)
    {
        _engine = engine;
        Language = engine.RecognizerLanguage.LanguageTag;
    }

    public string Name => "Windows OCR";

    public string Language { get; }

    /// <summary>깔려 있는 OCR 언어들. 비어 있으면 언어 팩이 없는 것이다.</summary>
    public static IReadOnlyList<string> AvailableLanguages
        => OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToArray();

    /// <summary>
    /// 만든다. 원하는 언어가 없으면 사용자 언어, 그것도 없으면 깔린 아무 언어. 하나도 없으면 null.
    /// </summary>
    public static WindowsOcrEngine? TryCreate(string? languageTag = null)
    {
        OcrEngine? engine = null;

        if (!string.IsNullOrEmpty(languageTag))
        {
            var language = new Language(languageTag);
            if (OcrEngine.IsLanguageSupported(language)) engine = OcrEngine.TryCreateFromLanguage(language);
        }

        engine ??= OcrEngine.TryCreateFromUserProfileLanguages();

        if (engine is null && OcrEngine.AvailableRecognizerLanguages.FirstOrDefault() is { } any)
            engine = OcrEngine.TryCreateFromLanguage(any);

        return engine is null ? null : new WindowsOcrEngine(engine);
    }

    public async Task<OcrOutcome> RecognizeAsync(BitmapSource image, CancellationToken token = default)
    {
        var prepared = Prepare(image);

        var width = prepared.PixelWidth;
        var height = prepared.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        prepared.CopyPixels(pixels, stride, 0);

        var watch = Stopwatch.StartNew();

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);

        var result = await _engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);

        watch.Stop();

        var lines = new List<OcrLine>(result.Lines.Count);

        foreach (var line in result.Lines)
        {
            var words = line.Words.Select(word =>
            {
                var r = word.BoundingRect;

                // 넣어 준 그림 안의 비율. 키워 넣었어도 비율은 같다.
                return new OcrWord(word.Text, LabelBox.FromCorners(0,
                    r.X / width, r.Y / height, (r.X + r.Width) / width, (r.Y + r.Height) / height));
            }).ToArray();

            lines.Add(new OcrLine(line.Text, words));
        }

        return new OcrOutcome(string.Join(Environment.NewLine, lines.Select(l => l.Text)), lines, watch.Elapsed);
    }

    /// <summary>Bgra32 로 맞추고, 작으면 키운다. 돌려주는 것은 Freeze 된 것이다.</summary>
    private static BitmapSource Prepare(BitmapSource image)
    {
        BitmapSource source = image.Format == PixelFormats.Bgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        if (source.PixelHeight < MinimumHeight && source.PixelHeight > 0)
        {
            var scale = Math.Min(Math.Ceiling((double)MinimumHeight / source.PixelHeight), 4d);
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        if (source.CanFreeze) source.Freeze();

        return source;
    }

    public void Dispose()
    {
        // OcrEngine 은 놓을 것이 없다. 인터페이스가 IDisposable 인 것은 다른 엔진(네이티브) 때문이다.
    }
}
