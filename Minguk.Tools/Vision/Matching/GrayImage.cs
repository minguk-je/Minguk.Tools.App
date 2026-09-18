using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Matching;

/// <summary>
/// 회색조 그림 한 장(0~255). 본보기 대조(<see cref="TemplateMatch"/>)가 쓴다.
/// </summary>
/// <remarks>
/// 색을 버리는 이유 - 견주는 값이 1/4 로 줄어 그만큼 빠르고, 게임 UI 는 모양으로 갈린다. 가중은 사람 눈 기준(BGR 순 0.114·0.587·0.299).
/// </remarks>
public sealed class GrayImage
{
    public GrayImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        if (pixels.Length < width * height)
            throw new ArgumentException($"픽셀이 모자란다: {pixels.Length} < {width}x{height}", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>줄 간격은 <see cref="Width"/> 그대로다.</summary>
    public byte[] Pixels { get; }

    /// <summary>WPF 그림에서. 다른 스레드에서 만든 것이면 Freeze 돼 있어야 한다.</summary>
    public static GrayImage From(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var bytes = new byte[stride * height];

        bgra.CopyPixels(bytes, stride, 0);

        var gray = new byte[width * height];

        for (var i = 0; i < gray.Length; i++)
        {
            var at = i * 4;

            gray[i] = (byte)((bytes[at] * 29 + (bytes[at + 1] * 150) + (bytes[at + 2] * 77)) >> 8);
        }

        return new GrayImage(width, height, gray);
    }

    /// <summary>줄인(또는 키운) 그림. 겹선형 - <c>BgraImage.Resize</c> 와 같은 규칙이다.</summary>
    public GrayImage Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        if (width == Width && height == Height) return this;

        var pixels = new byte[width * height];
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

                double top = (Pixels[(y0 * Width) + x0] * (1 - wx)) + (Pixels[(y0 * Width) + x1] * wx);
                double bottom = (Pixels[(y1 * Width) + x0] * (1 - wx)) + (Pixels[(y1 * Width) + x1] * wx);

                pixels[(y * width) + x] = (byte)Math.Clamp(Math.Round((top * (1 - wy)) + (bottom * wy)), 0, 255);
            }
        }

        return new GrayImage(width, height, pixels);
    }
}
