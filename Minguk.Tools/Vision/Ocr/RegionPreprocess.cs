using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 자리마다 고른 손질(그레이스케일 + 문턱값 이진화, 확대)을 OCR 에 넣기 전에 자른 그림에 씌운다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-18) "탄약 숫자도 잘 못 읽잖아" · "게임에서 많이 쓰는 기능들로". <see cref="NamedRegion.Threshold"/> 가 0 이고
/// <see cref="NamedRegion.Scale"/> 이 1(둘 다 기본)이면 손 안 대고 그대로 돌려준다 - 대부분 자리는 PP-OCRv5 가 손질 없이 읽는다(2026-09-16).
/// 그레이스케일 가중은 <see cref="Matching.GrayImage"/> 와 같다(BGR 순 29·150·77, 사람 눈 기준).
/// </remarks>
public static class RegionPreprocess
{
    public static BitmapSource Apply(BitmapSource crop, NamedRegion region)
    {
        if (region.Threshold <= 0 && region.Scale <= 1.01) return crop;

        var bgra = crop.Format == PixelFormats.Bgra32 ? crop : new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);

        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        bgra.CopyPixels(pixels, stride, 0);

        if (region.Threshold > 0) Binarize(pixels, region.Threshold, region.Invert);

        if (region.Scale > 1.01)
            (pixels, width, height, stride) = Resize(pixels, width, height, (int)Math.Round(width * region.Scale), (int)Math.Round(height * region.Scale));

        var result = BitmapSource.Create(width, height, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();

        return result;
    }

    /// <summary>그레이스케일로 바꾼 뒤 문턱값을 기준으로 검거나 희게(반전이면 반대로) 가른다. 제자리에서 고친다.</summary>
    private static void Binarize(byte[] pixels, int threshold, bool invert)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var gray = (byte)(((pixels[i] * 29) + (pixels[i + 1] * 150) + (pixels[i + 2] * 77)) >> 8);
            var bright = gray >= threshold;
            var value = (byte)(bright != invert ? 255 : 0);

            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
        }
    }

    /// <summary>겹선형으로 키운다(작은 HUD 글자를 OCR 이 더 잘 읽게) - <see cref="Matching.GrayImage.Resize"/> 와 같은 규칙, 채널 4개(BGRA)판.</summary>
    private static (byte[] Pixels, int Width, int Height, int Stride) Resize(byte[] source, int width, int height, int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);

        var stride = newWidth * 4;
        var pixels = new byte[stride * newHeight];
        var scaleX = (double)width / newWidth;
        var scaleY = (double)height / newHeight;
        var sourceStride = width * 4;

        for (var y = 0; y < newHeight; y++)
        {
            var fy = Math.Clamp(((y + 0.5) * scaleY) - 0.5, 0, height - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(y0 + 1, height - 1);
            var wy = fy - y0;

            for (var x = 0; x < newWidth; x++)
            {
                var fx = Math.Clamp(((x + 0.5) * scaleX) - 0.5, 0, width - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(x0 + 1, width - 1);
                var wx = fx - x0;

                for (var c = 0; c < 4; c++)
                {
                    double top = (source[(y0 * sourceStride) + (x0 * 4) + c] * (1 - wx)) + (source[(y0 * sourceStride) + (x1 * 4) + c] * wx);
                    double bottom = (source[(y1 * sourceStride) + (x0 * 4) + c] * (1 - wx)) + (source[(y1 * sourceStride) + (x1 * 4) + c] * wx);

                    pixels[(y * stride) + (x * 4) + c] = (byte)Math.Clamp(Math.Round((top * (1 - wy)) + (bottom * wy)), 0, 255);
                }
            }
        }

        return (pixels, newWidth, newHeight, stride);
    }
}
