using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Matching;

/// <summary>
/// 다각형(0~1 비율)을 마스크(0~255)로 칠하고, 그림에 알파로 입힌다 - 본보기 PNG 가 마스크를 알파로 든다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-23) "Adorner 안에 폴리곤 식의 구역을 자유롭게 지정해서 이부분만 매칭". 구역(<c>RegionCell.Mask</c>)의 다각형은 구역 기준 0~1 이고,
/// 「영역 이미지 저장」 이 자르는 그림은 그 구역을 똑바로 세운 것이라 0~1 을 그 그림 크기에 곱하면 그대로 맞는다(돌린 구역도).
///
/// <b>알파로 드는 이유</b> - 따로 파일을 두지 않아도 <c>그림누르기("이름.png")</c> 가 그대로다. 그림판·포토샵으로 지운 PNG 도 같은 길로 먹힌다.
///
/// <b>가장자리는 4x4 로 나눠 센다</b> - 반쯤 걸친 픽셀은 반만 본다(<see cref="GrayImage.Mask"/> 가 무게라서). 칠하기는 짝홀 규칙이라 꼬인 다각형도 터지지 않는다.
/// WPF 로 그려 알파를 읽지 않는 것은 UI 스레드·렌더러 없이(하네스·스크립트 스레드) 돌게 하려는 것이다.
/// </remarks>
public static class PolygonMask
{
    private const int Subsamples = 4;

    /// <summary>쓸 만한 다각형인가 - 점이 셋 이상.</summary>
    public static bool IsUsable(IReadOnlyList<Point>? polygon) => polygon is { Count: >= 3 };

    /// <summary>다각형 안쪽 무게(0~255). 좌표는 0~1 비율, 크기는 그림 픽셀.</summary>
    public static byte[] Rasterize(IReadOnlyList<Point> polygon, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        var mask = new byte[width * height];

        if (!IsUsable(polygon)) return mask;

        // 픽셀 좌표로 - 칸마다 곱하지 않게.
        var xs = new double[polygon.Count];
        var ys = new double[polygon.Count];

        for (var i = 0; i < polygon.Count; i++)
        {
            xs[i] = polygon[i].X * width;
            ys[i] = polygon[i].Y * height;
        }

        const int samples = Subsamples * Subsamples;
        var crossings = new List<double>(polygon.Count);

        for (var y = 0; y < height; y++)
        {
            // 줄마다 부분 표본 줄 넷 - 줄마다 변과 만나는 x 를 구해 정렬하고 짝홀로 칠한다(점마다 모든 변을 보는 것보다 훨씬 싸다).
            var counts = new int[width];

            for (var sy = 0; sy < Subsamples; sy++)
            {
                var py = y + ((sy + 0.5) / Subsamples);

                crossings.Clear();

                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                {
                    if ((ys[i] > py) == (ys[j] > py)) continue;

                    crossings.Add(xs[i] + ((py - ys[i]) / (ys[j] - ys[i]) * (xs[j] - xs[i])));
                }

                crossings.Sort();

                for (var k = 0; k + 1 < crossings.Count; k += 2)
                {
                    var left = crossings[k];
                    var right = crossings[k + 1];

                    // 부분 표본 x = x + (sx + 0.5)/4 가 [left, right) 안인 것들을 센다.
                    var first = (int)Math.Ceiling((left * Subsamples) - 0.5);
                    var last = (int)Math.Ceiling((right * Subsamples) - 0.5) - 1;

                    first = Math.Max(first, 0);
                    last = Math.Min(last, (width * Subsamples) - 1);

                    for (var s = first; s <= last; s++) counts[s / Subsamples]++;
                }
            }

            for (var x = 0; x < width; x++)
                mask[(y * width) + x] = (byte)Math.Round(counts[x] * 255.0 / samples);
        }

        return mask;
    }

    /// <summary>
    /// 그림에 다각형 마스크를 알파로 입힌다 - 밖은 투명, 안은 원래 색. 다각형이 쓸 만하지 않으면 그림 그대로.
    /// </summary>
    public static BitmapSource Apply(BitmapSource source, IReadOnlyList<Point>? polygon)
    {
        if (!IsUsable(polygon)) return source;

        BitmapSource bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var bytes = new byte[stride * height];

        bgra.CopyPixels(bytes, stride, 0);

        var mask = Rasterize(polygon!, width, height);

        // 원래 알파(보통 255)와 곱한다 - 이미 지운 곳을 되살리지 않는다.
        for (var i = 0; i < mask.Length; i++)
            bytes[(i * 4) + 3] = (byte)(bytes[(i * 4) + 3] * mask[i] / 255);

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
        result.Freeze();

        return result;
    }

    /// <summary>
    /// 새 마스크의 처음 모양 - 구역 가운데의 팔각형(반지름 0.4). 둥근 아이콘이 가장 흔하고, 꼭짓점이 모서리 손잡이와 안 겹친다.
    /// </summary>
    public static IReadOnlyList<Point> DefaultShape()
    {
        var points = new Point[8];

        for (var i = 0; i < points.Length; i++)
        {
            var angle = (i * Math.PI / 4) + (Math.PI / 8);

            points[i] = new Point(Math.Round(0.5 + (0.4 * Math.Cos(angle)), 4), Math.Round(0.5 + (0.4 * Math.Sin(angle)), 4));
        }

        return points;
    }
}
