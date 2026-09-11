using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 이름표 조각을 OCR 이 읽을 수 있는 모양으로 바꾼다. 빨간 글자만 검정으로 남기고 나머지는 흰색, 그리고 3배.
/// </summary>
/// <remarks>
/// 게임 이름표는 13px 남짓한 빨간 글자가 회색 벽 위에 얹혀 있다. 그대로 넣으면 Windows OCR 은
/// 두 언어 모두 빈 글을 냈고, 6배로 키워도 마찬가지였다. 색으로 배경을 지우니 3배에서 8곳 중 6곳이
/// 「일반봇」으로 읽혔다(`--nameplate-check` 실측). 6배는 체력 바의 빨간 칸까지 글자로 착각해
/// 쓰레기 줄이 붙었다. 그래서 3배다.
///
/// 적팀 빨강에 맞춘 값이다. 아군 파랑·다른 게임의 노랑 이름표는 <see cref="IsInk"/> 만 바꾸면 된다.
/// 화면과 떼어 순수하게 둔다. `--vision` 이 숫자로 본다.
/// </remarks>
public static class NameplateInk
{
    /// <summary>글자를 남긴 뒤 키우는 배율.</summary>
    public const double Scale = 3;

    /// <summary>빨강으로 치는 문턱. R 이 이보다 크고, G·B 보다 <see cref="MinimumLead"/> 이상 앞서야 한다.</summary>
    public const int MinimumRed = 150;

    /// <summary>R 이 G·B 중 큰 쪽보다 이만큼은 앞서야 글자로 본다. 회색·흰색을 거른다.</summary>
    public const int MinimumLead = 60;

    /// <summary>이 픽셀이 이름표 글자 색인가.</summary>
    public static bool IsInk(byte r, byte g, byte b) => r > MinimumRed && r - Math.Max(g, b) > MinimumLead;

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
