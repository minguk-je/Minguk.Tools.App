using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Ocr.Paddle;
using Minguk.Tools.Vision.Perception;

namespace Minguk.Tools.Vision.Regions;

/// <summary>읽을 칸 하나. <see cref="Box"/> 는 화면 기준 0~1 상자(돌리기 전), <see cref="Angle"/> 은 시계 방향 각도(도).</summary>
public readonly record struct RegionTarget(RegionCell Cell, Rect Box, double Angle);

/// <summary>
/// 자리·칸을 읽을 곳으로 바꾸고, 돌린 칸을 똑바로 세우고, 칸마다 읽은 것을 잇는다. 화면·스크립트가 같이 쓴다.
/// </summary>
/// <remarks>
/// <b>회전은 픽셀 공간에서</b> - 0~1 비율은 가로세로 배율이 달라(1920x1080 이면 1:0.56) 거기서 돌리면 칸이 찌그러진다.
/// 상자를 프레임 픽셀로 바꾼 뒤 가운데를 중심으로 돌린다. 미리보기 캔버스도 같은 배율로 늘어나 같은 모양이 된다.
///
/// <b>똑바로 세우기</b> - WPF <c>TransformedBitmap</c> 은 90° 단위로만 돌린다. 그래서 돌린 칸을 감싸는 축 상자(<see cref="Bounds"/>)를 잘라,
/// 세운 상자의 픽셀마다 역회전 자리를 양선형으로 뽑는다(<see cref="Upright"/>).
/// </remarks>
public static class RegionTargets
{
    /// <summary>읽을 칸들. <paramref name="only"/> 가 있으면 그 칸 하나, 없으면 칸 순서대로 전부.</summary>
    public static IReadOnlyList<RegionTarget> Of(NamedRegion region, RegionCell? only)
    {
        IEnumerable<RegionCell> cells = only is not null ? [only] : region.Cells;

        return [.. cells.Select(cell => new RegionTarget(cell, region.CellRect(cell), cell.Angle))];
    }

    /// <summary>돌린 칸을 감싸는 축 상자(화면 기준 0~1, 화면 밖은 접는다). 각도가 0 이면 상자 그대로.</summary>
    public static Rect Bounds(RegionTarget target, int frameWidth, int frameHeight)
    {
        if (IsUpright(target.Angle) || frameWidth <= 0 || frameHeight <= 0) return target.Box;

        var (cx, cy, halfW, halfH) = PixelBox(target, frameWidth, frameHeight);
        var radians = target.Angle * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var extentX = (halfW * cos) + (halfH * sin);
        var extentY = (halfW * sin) + (halfH * cos);

        var left = Math.Clamp((cx - extentX) / frameWidth, 0, 1);
        var top = Math.Clamp((cy - extentY) / frameHeight, 0, 1);
        var right = Math.Clamp((cx + extentX) / frameWidth, 0, 1);
        var bottom = Math.Clamp((cy + extentY) / frameHeight, 0, 1);

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 감싸는 상자를 자른 그림(<paramref name="boundsCrop"/>)에서 칸을 똑바로 세운 그림을 만든다. 각도가 0 이면 그대로 돌려준다.
    /// </summary>
    /// <param name="bounds"><see cref="Bounds"/> 로 구해 자른 자리. 자른 쪽과 같은 반올림(내림·올림)이어야 픽셀이 맞는다.</param>
    public static BitmapSource Upright(BitmapSource boundsCrop, Rect bounds, RegionTarget target, int frameWidth, int frameHeight)
    {
        if (IsUpright(target.Angle)) return boundsCrop;

        var source = BgraImage.From(boundsCrop);
        var (cx, cy, halfW, halfH) = PixelBox(target, frameWidth, frameHeight);
        var width = Math.Max(1, (int)Math.Round(halfW * 2));
        var height = Math.Max(1, (int)Math.Round(halfH * 2));

        // 자른 그림의 왼쪽 위가 프레임의 어디인가 - 자르는 쪽(허브·캡처)은 내림으로 자른다.
        var originX = Math.Floor(bounds.X * frameWidth);
        var originY = Math.Floor(bounds.Y * frameHeight);

        var radians = target.Angle * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var pixels = new byte[width * height * 4];

        for (var v = 0; v < height; v++)
        {
            for (var u = 0; u < width; u++)
            {
                // 세운 상자 안의 자리 → 가운데 기준 → 시계 방향으로 돌려 프레임 자리 → 자른 그림 자리(픽셀 가운데 맞춤)
                var lx = u + 0.5 - (width / 2d);
                var ly = v + 0.5 - (height / 2d);
                var sx = cx + (lx * cos) - (ly * sin) - originX - 0.5;
                var sy = cy + (lx * sin) + (ly * cos) - originY - 0.5;

                Sample(source, sx, sy, pixels, ((v * width) + u) * 4);
            }
        }

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        result.Freeze();

        return result;
    }

    /// <summary>
    /// 허브의 마지막 프레임에서 칸을 잘라 똑바로 세운 그림을 준다. 프레임이 없으면 false.
    /// </summary>
    /// <remarks>돌리지 않은 칸은 상자를 그대로 자른다. 돌린 칸은 프레임 크기로 감싸는 상자를 구해 잘라 세운다.</remarks>
    public static bool TryCrop(IPerceptionHub hub, RegionTarget target, out BitmapSource? crop)
    {
        crop = null;

        if (IsUpright(target.Angle)) return hub.TryCropFrame(target.Box, out crop) && crop is not null;

        if (!hub.TryGetFrameSize(out var width, out var height)) return false;

        var bounds = Bounds(target, width, height);

        if (!hub.TryCropFrame(bounds, out var raw) || raw is null) return false;

        crop = Upright(raw, bounds, target, width, height);

        return true;
    }

    /// <summary>
    /// 칸마다 읽은 글을 잇는다 - 글은 띄어쓰기로(빈 글은 건너뜀), 숫자는 글마다 덩어리를 차례로.
    /// </summary>
    /// <remarks>숫자 덩어리는 숫자가 아닌 글자에서 끊는다 - 「HP 5 / 9」 → 5, 9. 구분선을 1 로 읽은 「30140」 은 하나다(칸으로 나눠야 하는 까닭).</remarks>
    public static (string Text, int[] Numbers) Combine(IReadOnlyList<string> texts)
    {
        var parts = texts.Select(t => (t ?? string.Empty).Replace(Environment.NewLine, " ").Trim()).Where(t => t.Length > 0).ToList();
        var numbers = new List<int>();

        foreach (var part in parts) numbers.AddRange(NumbersIn(part));

        return (string.Join(" ", parts), [.. numbers]);
    }

    /// <summary>
    /// 천 단위 쉼표 - 숫자 뒤 쉼표(또는 마침표) 다음에 숫자가 딱 셋(「9,473」·「2,479,369」). OCR 이 작은 쉼표를 마침표로 읽는다(실측: 「9.473/9.473」).
    /// </summary>
    /// <remarks>소수 한 자리 「93.3%」 는 셋이 아니라 그대로 끊는다. 소수 세 자리(「1.250」)는 천 단위로 잘못 볼 수 있다 - 게임 HUD 에서는 드물다.</remarks>
    private static readonly System.Text.RegularExpressions.Regex ThousandsComma = new(@"(?<=\d)[,.](?=\d{3}(?!\d))", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 글에서 숫자 덩어리들을 왼쪽부터. 너무 길어 int 를 넘는 덩어리는 버린다. 천 단위 쉼표는 숫자 안으로 친다.
    /// </summary>
    /// <remarks>
    /// 아이온2 HUD 는 체력 「9,473 / 9,473」·재화 「2,479,369」 처럼 쉼표를 붙인다(사용자, 2026-09-24 "아이온2 HUD 영역 잡는 것도") -
    /// 쉼표에서 끊으면 [9, 473, 9, 473] 이 된다. 쉼표 뒤가 딱 세 자리일 때만 붙인다 - 「17,24」 같은 것은 그대로 둘로 끊는다.
    /// </remarks>
    public static IReadOnlyList<int> NumbersIn(string text)
    {
        var numbers = new List<int>();
        var digits = new StringBuilder();

        text = ThousandsComma.Replace(text ?? string.Empty, string.Empty);

        foreach (var letter in text + " ")
        {
            if (char.IsAsciiDigit(letter))
            {
                digits.Append(letter);
                continue;
            }

            if (digits.Length > 0 && int.TryParse(digits.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                numbers.Add(value);

            digits.Clear();
        }

        return numbers;
    }

    /// <summary>안 돌린 칸인가(0°·360°).</summary>
    public static bool IsUpright(double angle) => Math.Abs(angle % 360) < 0.01;

    /// <summary>칸 상자의 프레임 픽셀 가운데와 반너비·반높이.</summary>
    private static (double Cx, double Cy, double HalfW, double HalfH) PixelBox(RegionTarget target, int frameWidth, int frameHeight)
    {
        var box = target.Box;

        return ((box.X + (box.Width / 2)) * frameWidth, (box.Y + (box.Height / 2)) * frameHeight, box.Width * frameWidth / 2, box.Height * frameHeight / 2);
    }

    /// <summary>양선형으로 한 점을 뽑는다. 그림 밖은 가장자리 색.</summary>
    private static void Sample(BgraImage image, double x, double y, byte[] target, int offset)
    {
        x = Math.Clamp(x, 0, image.Width - 1);
        y = Math.Clamp(y, 0, image.Height - 1);

        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, image.Width - 1);
        var y1 = Math.Min(y0 + 1, image.Height - 1);
        var wx = x - x0;
        var wy = y - y0;

        for (var k = 0; k < 4; k++)
        {
            var upper = (image.Pixels[(((y0 * image.Width) + x0) * 4) + k] * (1 - wx)) + (image.Pixels[(((y0 * image.Width) + x1) * 4) + k] * wx);
            var lower = (image.Pixels[(((y1 * image.Width) + x0) * 4) + k] * (1 - wx)) + (image.Pixels[(((y1 * image.Width) + x1) * 4) + k] * wx);

            target[offset + k] = (byte)Math.Round((upper * (1 - wy)) + (lower * wy));
        }
    }
}
