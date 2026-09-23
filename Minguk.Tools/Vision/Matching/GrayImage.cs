using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Matching;

/// <summary>
/// 회색조 그림 한 장(0~255). 본보기 대조(<see cref="TemplateMatch"/>)가 쓴다.
/// </summary>
/// <remarks>
/// 색을 버리는 이유 - 견주는 값이 1/4 로 줄어 그만큼 빠르고, 게임 UI 는 모양으로 갈린다. 가중은 사람 눈 기준(BGR 순 0.114·0.587·0.299).
///
/// <b>마스크</b>(사용자, 2026-09-23 "폴리곤 식의 구역을 자유롭게 지정해서 이부분만 매칭") - 본보기에 <see cref="Mask"/> 가 있으면 대조는 그 안(0 이 아닌 곳)만 본다.
/// 값은 무게(0~255)라 다각형 가장자리처럼 반쯤 걸친 픽셀은 반만 센다. 본보기 PNG 의 알파가 곧 마스크다(<see cref="From(BitmapSource, bool)"/>).
/// </remarks>
public sealed class GrayImage
{
    public GrayImage(int width, int height, byte[] pixels, byte[]? mask = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        if (pixels.Length < width * height)
            throw new ArgumentException($"픽셀이 모자란다: {pixels.Length} < {width}x{height}", nameof(pixels));

        if (mask is not null && mask.Length < width * height)
            throw new ArgumentException($"마스크가 모자란다: {mask.Length} < {width}x{height}", nameof(mask));

        Width = width;
        Height = height;
        Pixels = pixels;
        Mask = mask;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>줄 간격은 <see cref="Width"/> 그대로다.</summary>
    public byte[] Pixels { get; }

    /// <summary>볼 곳의 무게(0~255, 0 은 안 본다). null 이면 전부 본다. 줄 간격은 <see cref="Width"/>.</summary>
    public byte[]? Mask { get; }

    /// <summary>
    /// WPF 그림에서. 다른 스레드에서 만든 것이면 Freeze 돼 있어야 한다.
    /// </summary>
    /// <param name="useAlpha">
    /// 알파를 마스크로 읽을지 - 본보기(Resources 의 PNG)만 켠다. 화면 조각은 끈다(캡처 알파는 믿을 값이 아니다).
    /// 알파가 모두 255 거나 모두 0 이면 마스크 없이 둔다 - 모두 0 은 알파를 안 채운 그림이지 "아무것도 보지 말라" 가 아니다.
    /// </param>
    public static GrayImage From(BitmapSource source, bool useAlpha = false)
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

        return new GrayImage(width, height, gray, useAlpha ? AlphaMask(bytes, gray.Length) : null);
    }

    /// <summary>BGRA 의 알파를 마스크로. 모두 255(가린 곳 없음)거나 모두 0(알파를 안 채움)이면 null.</summary>
    private static byte[]? AlphaMask(byte[] bgra, int count)
    {
        var mask = new byte[count];
        var anyHidden = false;
        var anyShown = false;

        for (var i = 0; i < count; i++)
        {
            var alpha = bgra[(i * 4) + 3];

            mask[i] = alpha;
            anyHidden |= alpha < 255;
            anyShown |= alpha > 0;
        }

        return anyHidden && anyShown ? mask : null;
    }

    /// <summary>마스크 무게의 합(0~1 로 센 픽셀 수). 마스크가 없으면 픽셀 수.</summary>
    public double MaskedArea
    {
        get
        {
            if (Mask is null) return Width * Height;

            double sum = 0;
            for (var i = 0; i < Width * Height; i++) sum += Mask[i];

            return sum / 255;
        }
    }

    /// <summary>줄인(또는 키운) 그림. 겹선형 - <c>BgraImage.Resize</c> 와 같은 규칙이다. 마스크도 같은 규칙으로 줄인다(가장자리는 무게가 반쯤 된다).</summary>
    public GrayImage Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        if (width == Width && height == Height) return this;

        return new GrayImage(width, height, Scale(Pixels, width, height), Mask is null ? null : Scale(Mask, width, height));
    }

    private byte[] Scale(byte[] source, int width, int height)
    {
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

                double top = (source[(y0 * Width) + x0] * (1 - wx)) + (source[(y0 * Width) + x1] * wx);
                double bottom = (source[(y1 * Width) + x0] * (1 - wx)) + (source[(y1 * Width) + x1] * wx);

                pixels[(y * width) + x] = (byte)Math.Clamp(Math.Round((top * (1 - wy)) + (bottom * wy)), 0, 255);
            }
        }

        return pixels;
    }
}
