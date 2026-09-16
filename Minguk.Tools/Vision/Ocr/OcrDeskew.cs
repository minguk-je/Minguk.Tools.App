using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 기울어진 글자를 세운다(기울이기 = shear).
/// </summary>
/// <remarks>
/// <b>왜</b> - 게임 HUD 글꼴은 이탤릭인 것이 많다(오버워치 탄약·체력). OCR 은 똑바로 선 글자를 기준으로 배워서,
/// 기울어진 얇은 획은 못 읽거나 다른 글자로 읽는다. 읽기 전에 반대로 기울여 세우면 같은 엔진이 그대로 읽는다.
///
/// <see cref="TransformedBitmap"/> 은 크기·90도 회전만 되므로 직접 그려서 만든다. 남는 자리는 배경색으로 채운다 -
/// 투명하게 두면 OCR 이 검은 테두리로 본다.
/// </remarks>
public static class OcrDeskew
{
    /// <summary>글자를 <paramref name="degrees"/> 만큼 반대로 기울여 세운다. 0 이면 그대로 돌려준다.</summary>
    /// <param name="degrees">글자가 오른쪽으로 기운 각도(도). 오버워치 HUD 는 10~12도쯤이다.</param>
    /// <param name="background">남는 자리를 채울 색. 글자만 남긴 그림이면 흰색, 원본이면 가장자리 색이 낫다.</param>
    public static BitmapSource Apply(BitmapSource source, double degrees, Color background)
    {
        if (Math.Abs(degrees) < 0.1) return source;

        var slant = Math.Tan(degrees * Math.PI / 180);
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        // 기울인 만큼 폭이 늘어난다. 잘리면 글자 끝이 사라진다.
        var extra = (int)Math.Ceiling(Math.Abs(slant) * height);
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width + extra, height));

            var skew = new SkewTransform(-degrees, 0, 0, height);   // 아래쪽을 고정하고 위를 왼쪽으로

            dc.PushTransform(new TranslateTransform(slant > 0 ? 0 : extra, 0));
            dc.PushTransform(skew);
            dc.DrawImage(source, new Rect(0, 0, width, height));
            dc.Pop();
            dc.Pop();
        }

        var target = new RenderTargetBitmap(width + extra, height, 96, 96, PixelFormats.Pbgra32);

        target.Render(visual);
        target.Freeze();

        return target;
    }
}
