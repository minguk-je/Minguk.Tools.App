using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 자리마다 고른 이진화(그레이스케일 + 문턱값)를 OCR 에 넣기 전에 자른 그림에 씌운다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-18) "탄약 숫자도 잘 못 읽잖아". <see cref="NamedRegion.Threshold"/> 가 0(기본)이면 손 안 대고 그대로 돌려준다 -
/// 대부분 자리는 PP-OCRv5 가 손질 없이 읽는다(2026-09-16). 가중은 <see cref="Matching.GrayImage"/> 와 같다(BGR 순 29·150·77, 사람 눈 기준).
/// </remarks>
public static class RegionPreprocess
{
    public static BitmapSource Apply(BitmapSource crop, NamedRegion region)
    {
        if (region.Threshold <= 0) return crop;

        var bgra = crop.Format == PixelFormats.Bgra32 ? crop : new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);

        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        bgra.CopyPixels(pixels, stride, 0);

        var threshold = region.Threshold;
        var invert = region.Invert;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var gray = (byte)((pixels[i] * 29) + (pixels[i + 1] * 150) + (pixels[i + 2] * 77) >> 8);
            var bright = gray >= threshold;
            var value = (byte)(bright != invert ? 255 : 0);

            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
        }

        var result = BitmapSource.Create(width, height, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();

        return result;
    }
}
