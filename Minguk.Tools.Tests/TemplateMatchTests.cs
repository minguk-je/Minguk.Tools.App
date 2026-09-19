using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Matching;

namespace Minguk.Tools.Tests;

/// <summary>
/// 본보기 그림 대조 - 화면에서 그림 버튼(메뉴 카드)을 찾는다.
/// </summary>
/// <remarks>
/// 여기서 보는 것 - 1080p 화면에 그린 카드를 찾아 자리가 맞는지, 밝기가 달라져도 찾는지, 없는 그림은 안 찾는지,
/// 본보기가 화면보다 크면 빈 답인지, 얼마나 걸리는지(사용자, 2026-09-18 "사격장 글이 아니고 큰 이미지인데").
/// </remarks>
internal static partial class Program
{
    private static void TestTemplateMatch()
    {
        // 카드 넷을 그린 1080p 메뉴 - 셋째(사격장 자리)를 본보기로 삼는다.
        var screen = DrawCards(1920, 1080, 1.0);
        var card = Crop(screen, 0.55, 0.35, 0.18, 0.20);

        var haystack = GrayImage.From(screen);
        var needle = GrayImage.From(card);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var hit = TemplateMatch.Find(haystack, needle);
        watch.Stop();

        var offX = hit is null ? double.NaN : Math.Abs(hit.Value.X - 0.55) * 1920;
        var offY = hit is null ? double.NaN : Math.Abs(hit.Value.Y - 0.35) * 1080;

        // 시간도 본다 - 계단을 하나 빼면 1.8초가 된다(실측). 메뉴 클릭이라 1초 안이면 쓸 만하다.
        Check("본보기: 화면에서 카드를 찾아 자리를 준다(4px 안, 1초 안)",
              hit is not null && hit.Value.Score > 0.95 && offX <= 4 && offY <= 4 && watch.ElapsedMilliseconds <= 1000,
              hit is null ? $"못 찾음 ({watch.ElapsedMilliseconds}ms)" : $"닮음 {hit.Value.Score:0.000} · 어긋남 {offX:0}·{offY:0}px · {watch.ElapsedMilliseconds}ms");

        // 밝기가 달라져도 - 게임 메뉴는 마우스를 올리면 밝아진다.
        var brighter = GrayImage.From(DrawCards(1920, 1080, 1.35));
        var dim = TemplateMatch.Find(brighter, needle);

        Check("본보기: 화면이 35% 밝아져도 찾는다(정규화 상호상관)",
              dim is not null && dim.Value.Score > 0.9 && Math.Abs(dim.Value.X - 0.55) * 1920 <= 4,
              dim is null ? "못 찾음" : $"닮음 {dim.Value.Score:0.000}");

        // 작은 아이콘 - 48x60 은 첫 계단(1/8)에서 6x8 로 뭉개져 엉뚱한 후보만 남았다(사용자, 2026-09-19 훈련장 아이콘 닮음 0.49).
        foreach (var (ix, iy) in new[] { (0.08, 0.40), (0.58, 0.45), (0.81, 0.40) })
        {
            var icon = Crop(screen, ix, iy, 48.0 / 1920, 60.0 / 1080);
            var iconGray = GrayImage.From(icon);
            var iconHit = TemplateMatch.Find(haystack, iconGray);
            var distinct = iconGray.Pixels.Distinct().Count();
            var iconOffX = iconHit is null ? double.NaN : Math.Abs(iconHit.Value.X - ix) * 1920;
            var iconOffY = iconHit is null ? double.NaN : Math.Abs(iconHit.Value.Y - iy) * 1080;

            Check($"본보기: 작은 아이콘(48x60)도 찾는다 ({ix:0.00}, {iy:0.00})",
                  iconHit is not null && iconHit.Value.Score > 0.95 && iconOffX <= 4 && iconOffY <= 4,
                  iconHit is null ? $"못 찾음 (본보기 밝기 {distinct}가지)" : $"닮음 {iconHit.Value.Score:0.000} · 어긋남 {iconOffX:0}·{iconOffY:0}px");
        }

        // 받기를 멈추면 허브가 옛 프레임을 버린다 - 다음에 켰을 때 옛 화면이 영역 이미지로 저장됐다(사용자, 2026-09-19).
        var hub = new Minguk.Tools.Vision.Perception.PerceptionHub { WantsFrames = true };
        hub.PublishFrame(new byte[8 * 6 * 4], 8, 6);
        var hadFrame = hub.TryCropFrame(new Rect(0, 0, 1, 1), out _);
        hub.WantsFrames = false;
        hub.WantsFrames = true;
        var staleGone = !hub.TryCropFrame(new Rect(0, 0, 1, 1), out _);

        Check("허브: 받기를 멈추면 옛 프레임을 버려, 다시 켜도 새 프레임이 올 때까지 안 잘린다", hadFrame && staleGone, $"처음 {hadFrame} · 멈춘 뒤 비었음 {staleGone}");

        // 없는 그림 - 닮음이 낮아야 한다(0.8 문턱이면 안 눌린다).
        var stranger = GrayImage.From(DrawNoise(200, 140));
        var wrong = TemplateMatch.Find(haystack, stranger);

        Check("본보기: 화면에 없는 그림은 닮음이 낮다(0.8 아래)", wrong is null || wrong.Value.Score < 0.8, wrong is null ? "못 찾음" : $"닮음 {wrong.Value.Score:0.000}");

        // 본보기가 화면보다 크면 빈 답.
        Check("본보기: 화면보다 큰 본보기는 빈 답", TemplateMatch.Find(needle, haystack) is null, "");

        // 한 가지 색 본보기는 견줄 것이 없다 - 어디에나 맞는다고 하면 안 된다.
        var flat = new GrayImage(40, 30, Enumerable.Repeat((byte)128, 40 * 30).ToArray());

        Check("본보기: 한 가지 색 본보기는 안 찾는다", TemplateMatch.Find(haystack, flat) is null, "");

        // 회색조·크기 줄이기가 제 값인지.
        var gray = GrayImage.From(DrawSolid(8, 6, Colors.White));

        Check("회색조: 흰색은 255 에 가깝다", gray.Pixels.All(p => p >= 250), $"{gray.Pixels[0]}");

        var half = gray.Resize(4, 3);

        Check("회색조: 줄여도 흰색은 흰색", half.Width == 4 && half.Height == 3 && half.Pixels.All(p => p >= 250), $"{half.Width}x{half.Height}");
    }

    /// <summary>메뉴처럼 보이는 카드 넷. <paramref name="brightness"/> 로 화면 전체를 밝게(1 이면 그대로).</summary>
    private static BitmapSource DrawCards(int width, int height, double brightness)
    {
        var visual = new DrawingVisual();
        var random = new Random(11);

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Tone(Colors.DimGray, brightness), null, new Rect(0, 0, width, height));

            var lefts = new[] { 0.06, 0.30, 0.55, 0.79 };
            var names = new[] { "빠른 대전", "경쟁전", "사격장", "사용자 지정" };

            for (var i = 0; i < lefts.Length; i++)
            {
                var box = new Rect(lefts[i] * width, 0.35 * height, 0.18 * width, 0.20 * height);

                dc.DrawRectangle(Tone(Color.FromRgb((byte)(40 + (i * 30)), (byte)(70 + (i * 20)), 120), brightness), null, box);

                // 카드마다 다른 무늬 - 없으면 어느 카드든 똑같아 찾을 수가 없다.
                for (var n = 0; n < 40; n++)
                {
                    var x = box.X + (random.NextDouble() * box.Width);
                    var y = box.Y + (random.NextDouble() * box.Height);

                    dc.DrawEllipse(Tone(Color.FromRgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)), brightness), null, new Point(x, y), 6 + (i * 2), 6);
                }

                var text = new FormattedText(names[i], System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Malgun Gothic"), 24, Tone(Colors.White, brightness), 96);

                dc.DrawText(text, new Point(box.X + 10, box.Bottom - 40));
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }

    private static SolidColorBrush Tone(Color color, double brightness)
        => new(Color.FromRgb(Cap(color.R * brightness), Cap(color.G * brightness), Cap(color.B * brightness)));

    private static byte Cap(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static BitmapSource DrawSolid(int width, int height, Color color)
    {
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }

    private static BitmapSource DrawNoise(int width, int height)
    {
        var random = new Random(77);
        var pixels = new byte[width * height * 4];

        random.NextBytes(pixels);

        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>그림의 한 부분(0~1 비율)을 잘라 낸다 - 앱의 허브와 같은 반올림.</summary>
    private static BitmapSource Crop(BitmapSource source, double x, double y, double width, double height)
    {
        var left = (int)Math.Floor(x * source.PixelWidth);
        var top = (int)Math.Floor(y * source.PixelHeight);
        var right = (int)Math.Ceiling((x + width) * source.PixelWidth);
        var bottom = (int)Math.Ceiling((y + height) * source.PixelHeight);

        var crop = new CroppedBitmap(source, new Int32Rect(left, top, right - left, bottom - top));
        crop.Freeze();

        return crop;
    }
}
