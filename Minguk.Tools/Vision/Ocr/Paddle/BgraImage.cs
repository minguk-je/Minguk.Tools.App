using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>
/// Bgra32 픽셀 한 장(줄 간격 = <see cref="Width"/> × 4). 모델에 넣기 전 자르기·늘리기를 WPF 없이 한다.
/// </summary>
/// <remarks>
/// WPF 그림은 스레드에 묶이고 늘리기(TransformedBitmap)는 그릴 때 계산된다. 읽기는 캡처·검출 스레드에서 도므로
/// 부른 자리에서 바이트로 한 번 옮기고 나머지는 여기서만 한다.
/// </remarks>
public sealed class BgraImage
{
    public BgraImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        if (pixels.Length < width * height * 4)
            throw new ArgumentException($"픽셀이 모자란다: {pixels.Length} < {width}x{height}x4", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>WPF 그림에서 픽셀을 옮긴다. 다른 스레드에서 만든 그림이면 Freeze 돼 있어야 한다.</summary>
    public static BgraImage From(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];

        bgra.CopyPixels(pixels, stride, 0);

        return new BgraImage(bgra.PixelWidth, bgra.PixelHeight, pixels);
    }

    /// <summary>한 부분을 잘라 새 그림으로. 밖으로 나간 곳은 버린다. 남는 것이 없으면 null.</summary>
    public BgraImage? Crop(int left, int top, int width, int height)
    {
        var l = Math.Clamp(left, 0, Width);
        var t = Math.Clamp(top, 0, Height);
        var r = Math.Clamp(left + width, l, Width);
        var b = Math.Clamp(top + height, t, Height);

        if (r - l <= 0 || b - t <= 0) return null;

        var w = r - l;
        var h = b - t;
        var pixels = new byte[w * h * 4];

        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(Pixels, (((t + y) * Width) + l) * 4, pixels, y * w * 4, w * 4);

        return new BgraImage(w, h, pixels);
    }

    /// <summary>양선형으로 늘리거나 줄인다. 픽셀 가운데를 맞춘다.</summary>
    public BgraImage Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        var pixels = new byte[width * height * 4];
        var scaleX = (double)Width / width;
        var scaleY = (double)Height / height;

        for (var y = 0; y < height; y++)
        {
            var fy = Math.Clamp(((y + 0.5) * scaleY) - 0.5, 0, Height - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(y0 + 1, Height - 1);
            var wy = fy - y0;

            for (var x = 0; x < width; x++)
            {
                var fx = Math.Clamp(((x + 0.5) * scaleX) - 0.5, 0, Width - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(x0 + 1, Width - 1);
                var wx = fx - x0;

                var a = ((y0 * Width) + x0) * 4;
                var b = ((y0 * Width) + x1) * 4;
                var c = ((y1 * Width) + x0) * 4;
                var d = ((y1 * Width) + x1) * 4;
                var o = ((y * width) + x) * 4;

                for (var k = 0; k < 4; k++)
                {
                    var upper = (Pixels[a + k] * (1 - wx)) + (Pixels[b + k] * wx);
                    var lower = (Pixels[c + k] * (1 - wx)) + (Pixels[d + k] * wx);

                    pixels[o + k] = (byte)Math.Round((upper * (1 - wy)) + (lower * wy));
                }
            }
        }

        return new BgraImage(width, height, pixels);
    }
}
