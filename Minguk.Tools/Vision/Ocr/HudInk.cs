using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// HUD 숫자(탄약·체력 같은 흰 글자)를 OCR 이 읽을 수 있는 모양으로 바꾼다. 흰 글자만 검정으로 남기고 나머지는 흰색.
/// </summary>
/// <remarks>
/// <b>왜 그냥은 안 되나</b> - 오버워치의 탄약·체력은 흰 숫자인데 배경이 바뀐다. 어두운 벽 위에서는 잘 읽히지만
/// <b>밝은 주황 바닥 위에 오면 대비가 거의 없어져</b> 같은 자리·같은 크기인데도 OCR 이 빈 글을 낸다
/// (실측: 여섯 장 중 셋이 빈 결과. 「17 | 24」 가 주황 바닥에 얹힌 장면들이었다).
///
/// <see cref="NameplateInk"/> 와 같은 수법인데 고르는 색이 다르다 - 그쪽은 "빨강", 이쪽은 "밝고 색기 없는 것".
/// 흰 글자는 R·G·B 가 모두 높고 서로 비슷하다. 주황 배경은 밝아도 R 이 B 보다 한참 앞서므로 이 조건에 안 걸린다.
///
/// <b>숫자는 en-US 로 읽는다.</b> 한국어 팩은 같은 그림에서 225 를 <c>22512h5</c>, 193 을 <c>1亐3</c> 으로 냈다(실측).
/// 화면과 떼어 순수하게 둔다 - `--ocr-crop --ink` 가 이 길을 그대로 지나간다.
/// </remarks>
public static class HudInk
{
    /// <summary>글자를 남긴 뒤 키우는 배율. HUD 숫자가 20px 안팎이라 이름표(3배)보다 키운다.</summary>
    public const double Scale = 4;

    /// <summary>흰색으로 치는 문턱. R·G·B 가 모두 이보다 밝아야 한다.</summary>
    public const int MinimumBright = 170;

    /// <summary>가장 밝은 채널과 가장 어두운 채널의 차이가 이 안이어야 "색기 없다" 로 본다.</summary>
    public const int MaximumSpread = 60;

    /// <summary>이 픽셀이 HUD 글자 색(흰색)인가.</summary>
    public static bool IsInk(byte r, byte g, byte b)
    {
        var high = Math.Max(r, Math.Max(g, b));
        var low = Math.Min(r, Math.Min(g, b));

        return low > MinimumBright && high - low <= MaximumSpread;
    }

    /// <summary>글자만 검정으로 남긴 흑백 그림을 <see cref="Scale"/> 배로 키워 돌려준다. 고정(Frozen)되어 있다.</summary>
    public static BitmapSource Prepare(BitmapSource crop)
    {
        var scaled = new TransformedBitmap(MaskInk(crop), new ScaleTransform(Scale, Scale));

        scaled.Freeze();

        return scaled;
    }

    /// <summary>글자 색 픽셀은 검정, 나머지는 흰색. 크기는 그대로.</summary>
    public static BitmapSource MaskInk(BitmapSource crop)
    {
        var source = crop.Format == PixelFormats.Bgra32 ? crop : new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];

        source.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var value = IsInk(pixels[i + 2], pixels[i + 1], pixels[i]) ? (byte)0 : (byte)255;

            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }

        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        result.Freeze();

        return result;
    }
}
