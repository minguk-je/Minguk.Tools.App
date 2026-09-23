using System;
using System.Collections.Generic;

namespace Minguk.Tools.Vision.Matching;

/// <summary>본보기를 찾은 자리 - 그림 안의 0~1 비율과 닮음(0~1).</summary>
public readonly record struct TemplateHit(double X, double Y, double Width, double Height, double Score)
{
    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);
}

/// <summary>
/// 화면에서 본보기 그림을 찾는다 - 정규화 상호상관(NCC).
/// </summary>
/// <remarks>
/// <b>왜 이것인가</b>(사용자, 2026-09-18 "사격장 글이 아니고 큰 이미지인데") - 게임 메뉴 버튼이 글자가 아니라 그림(썸네일 카드)이면 OCR 로는 못 찾는다.
/// 라벨링·학습은 "화면 어디에 뜰지 모르는 것"(검출)에 쓰는 것이고, 메뉴 카드처럼 <b>그림이 그대로 같은 것</b>은 본보기 한 장과 견주면 된다.
/// OpenCV 를 싣지 않는 이유 - 필요한 것이 이 함수 하나고, OpenCvSharp 는 네이티브 100MB+ 를 끌고 온다.
///
/// <b>정규화 상호상관</b>(NCC) - 조각과 본보기에서 각자 평균을 빼고 표준편차로 나눈 뒤 곱해 더한다. 그래서 <b>밝기·대비가 달라져도</b> 같은 그림이면 1 에 가깝다
/// (게임 메뉴는 마우스를 올리면 밝아지고, 캡처마다 감마가 조금씩 다르다). 평평한 조각(표준편차 0)은 0 으로 본다.
///
/// <b>계단 셋으로 찾는다</b> - 1920x1080 에서 346x216 본보기를 모든 자리에 견주면 3억 번이 넘는다. ① 긴 변을 <see cref="CoarseSide"/> 로 줄여 전부 훑고
/// ② 후보 <see cref="CoarseKeep"/> 개를 절반 크기에서 다시 보고 ③ 남은 하나만 원본에서 다듬는다. 곧바로 ①에서 원본으로 가면 후보 다섯을 원본 크기로 훑느라
/// 1.8초가 걸렸다(실측) - 계단을 하나 두니 0.3초다. 줄인 단계에서 후보를 여럿 남기는 것은 줄이면서 1등이 뒤바뀌는 일이 있어서다.
///
/// <b>회색조로 본다</b> - 색까지 보면 느리기만 하고, 게임 UI 는 모양으로 갈린다. 회색조는 사람 눈 가중(0.114·0.587·0.299, BGR 순).
/// </remarks>
public static class TemplateMatch
{
    /// <summary>대강 찾을 때 화면의 긴 변을 이만큼으로 줄인다. 240 이면 1920 의 1/8 - 300px 카드가 37px 이 된다.</summary>
    public const int CoarseSide = 240;

    /// <summary>줄인 단계에서 남기는 후보 수. 서로 겹치지 않는 것만 센다.</summary>
    public const int CoarseKeep = 10;

    /// <summary>첫 계단에서 본보기의 짧은 변이 이만큼은 남게 한다(px). 이보다 작으면 모양이 뭉개져 후보를 잘못 고른다.</summary>
    public const int CoarseNeedleSide = 16;

    /// <summary>본보기가 화면보다 크거나 이보다 작으면(px, 줄인 뒤) 찾지 않는다.</summary>
    public const int MinimumCoarseSide = 4;

    /// <summary>
    /// 화면에서 본보기를 찾는다. 가장 닮은 자리 하나(비율 0~1). 본보기가 화면보다 크면 null.
    /// </summary>
    /// <param name="haystack">찾을 화면. 회색조 값(0~255), 너비×높이.</param>
    /// <param name="needle">본보기.</param>
    public static TemplateHit? Find(GrayImage haystack, GrayImage needle)
    {
        var hits = FindAll(haystack, needle, 1);

        return hits.Count > 0 ? hits[0] : null;
    }

    /// <summary>닮은 자리들을 닮음 순서로. 서로 겹치는 것은 하나만 남긴다.</summary>
    public static IReadOnlyList<TemplateHit> FindAll(GrayImage haystack, GrayImage needle, int most)
    {
        if (needle.Width > haystack.Width || needle.Height > haystack.Height) return [];
        if (most < 1) return [];

        // ① 줄여서 대강 - 긴 변을 CoarseSide 로.
        // 단, 본보기가 첫 계단에서 CoarseNeedleSide 아래로 뭉개지지 않을 만큼만 줄인다 - 48x60 아이콘을 1/8 로 줄이면 6x8 이 되어
        // 모양이 사라지고 엉뚱한 자리가 후보로 남았다(사용자, 2026-09-19 훈련장 아이콘 닮음 0.49, 검사에서는 딴 자리 0.89).
        var shrink = Math.Max(1.0, Math.Min(
            (double)Math.Max(haystack.Width, haystack.Height) / CoarseSide,
            (double)Math.Min(needle.Width, needle.Height) / CoarseNeedleSide));
        var smallNeedleWidth = (int)Math.Round(needle.Width / shrink);
        var smallNeedleHeight = (int)Math.Round(needle.Height / shrink);

        // 본보기가 줄이면 사라질 만큼 작으면 원본에서 바로 훑는다 - 어차피 싸다.
        if (smallNeedleWidth < MinimumCoarseSide || smallNeedleHeight < MinimumCoarseSide)
            return Best(Scan(haystack, needle, 0, 0, haystack.Width - needle.Width, haystack.Height - needle.Height), needle, haystack, most);

        var smallHaystack = haystack.Resize((int)Math.Round(haystack.Width / shrink), (int)Math.Round(haystack.Height / shrink));
        var smallNeedle = needle.Resize(smallNeedleWidth, smallNeedleHeight);

        var coarse = Scan(smallHaystack, smallNeedle, 0, 0, smallHaystack.Width - smallNeedle.Width, smallHaystack.Height - smallNeedle.Height);
        var found = Top(coarse, smallNeedle.Width, smallNeedle.Height, Math.Max(CoarseKeep, most));
        var scale = shrink;

        // ②③ 절반씩 키워 가며 후보 언저리만 다시 본다 - 커질수록 후보를 줄인다.
        while (scale > 1)
        {
            var next = Math.Max(1, scale / 2);
            var keep = next <= 1 ? most : Math.Max(most, 2);

            found = Refine(haystack, needle, found, scale, next, keep);
            scale = next;
        }

        return Best([.. found], needle, haystack, most);
    }

    /// <summary>
    /// <paramref name="from"/> 배로 줄인 그림에서 찾은 후보들을 <paramref name="to"/> 배에서 다시 본다. 자리는 그 배율의 픽셀이다.
    /// </summary>
    private static List<(int X, int Y, double Score)> Refine(
        GrayImage haystack, GrayImage needle, List<(int X, int Y, double Score)> candidates, double from, double to, int keep)
    {
        var big = to <= 1 ? haystack : haystack.Resize((int)Math.Round(haystack.Width / to), (int)Math.Round(haystack.Height / to));
        var small = to <= 1 ? needle : needle.Resize(Math.Max(1, (int)Math.Round(needle.Width / to)), Math.Max(1, (int)Math.Round(needle.Height / to)));

        // 한 계단 줄인 그림의 한 픽셀이 이 계단에서는 from/to 픽셀이다. 그만큼에 반올림 여유를 더해 본다.
        var step = from / to;
        var slack = (int)Math.Ceiling(step) + 2;
        var refined = new List<(int X, int Y, double Score)>();

        foreach (var (x, y, _) in candidates)
        {
            var fromX = Math.Clamp((int)Math.Round(x * step) - slack, 0, big.Width - small.Width);
            var fromY = Math.Clamp((int)Math.Round(y * step) - slack, 0, big.Height - small.Height);
            var toX = Math.Clamp((int)Math.Round(x * step) + slack, fromX, big.Width - small.Width);
            var toY = Math.Clamp((int)Math.Round(y * step) + slack, fromY, big.Height - small.Height);

            refined.AddRange(Scan(big, small, fromX, fromY, toX, toY));
        }

        return Top(refined, small.Width, small.Height, keep);
    }

    /// <summary>그 범위의 모든 자리에서 닮음을 잰다(끝 포함).</summary>
    /// <summary>
    /// 정규화 상호상관을 그 범위에서 훑는다.
    /// </summary>
    /// <remarks>
    /// <b>빠르게 하는 셋</b>(사용자, 2026-09-19 - 47x32 본보기가 한 번에 2.8초라 영웅 선택 화면에서 "한참을 멍때렸다"):
    /// 창마다의 합·제곱합은 적분 영상으로 한 번에 구하고, 본보기는 평균을 빼 두어 분자가 곱의 합 하나가 되며(Σ(a−ā)(b−b̄) = Σa(b−b̄)),
    /// 그 곱의 합은 벡터로, 줄은 병렬로 나눈다. 식은 예전과 같다 - 닮음 값이 그대로다.
    /// </remarks>
    private static List<(int X, int Y, double Score)> Scan(GrayImage haystack, GrayImage needle, int fromX, int fromY, int toX, int toY)
    {
        if (toX < fromX || toY < fromY) return [];

        if (needle.Mask is not null) return ScanMasked(haystack, needle, fromX, fromY, toX, toY);

        var count = needle.Width * needle.Height;
        double needleSum = 0, needleSquares = 0;

        for (var i = 0; i < count; i++)
        {
            needleSum += needle.Pixels[i];
            needleSquares += (double)needle.Pixels[i] * needle.Pixels[i];
        }

        var needleMean = needleSum / count;
        var needleVariance = (needleSquares / count) - (needleMean * needleMean);

        // 본보기가 한 가지 색이면 견줄 것이 없다 - 어디에나 맞는다고 할 수는 없다.
        if (needleVariance <= 1e-6) return [];

        var needleDeviation = Math.Sqrt(needleVariance);

        // 본보기에서 평균을 뺀 것 - 분자가 곱의 합 하나가 된다.
        var centered = new float[count];
        for (var i = 0; i < count; i++) centered[i] = (float)(needle.Pixels[i] - needleMean);

        // 훑을 범위만큼의 화면을 float 로, 그리고 합·제곱합 적분 영상.
        var width = haystack.Width;
        var spanW = (toX - fromX) + needle.Width;
        var spanH = (toY - fromY) + needle.Height;
        var pixels = new float[spanW * spanH];
        var sums = new double[(spanW + 1) * (spanH + 1)];
        var squares = new double[(spanW + 1) * (spanH + 1)];

        for (var y = 0; y < spanH; y++)
        {
            double rowSum = 0, rowSquares = 0;
            var source = ((fromY + y) * width) + fromX;

            for (var x = 0; x < spanW; x++)
            {
                double value = haystack.Pixels[source + x];
                pixels[(y * spanW) + x] = (float)value;

                rowSum += value;
                rowSquares += value * value;

                sums[((y + 1) * (spanW + 1)) + x + 1] = sums[(y * (spanW + 1)) + x + 1] + rowSum;
                squares[((y + 1) * (spanW + 1)) + x + 1] = squares[(y * (spanW + 1)) + x + 1] + rowSquares;
            }
        }

        double Box(double[] table, int x, int y)
        {
            var w = spanW + 1;
            return table[((y + needle.Height) * w) + x + needle.Width] - table[(y * w) + x + needle.Width]
                   - table[((y + needle.Height) * w) + x] + table[(y * w) + x];
        }

        var rows = (toY - fromY) + 1;
        var perRow = new List<(int, int, double)>[rows];

        System.Threading.Tasks.Parallel.For(0, rows, row =>
        {
            var found = new List<(int, int, double)>((toX - fromX) + 1);
            var pixelSpan = new ReadOnlySpan<float>(pixels);
            var needleSpan = new ReadOnlySpan<float>(centered);

            for (var col = 0; col <= toX - fromX; col++)
            {
                var sum = Box(sums, col, row);
                var mean = sum / count;
                var variance = (Box(squares, col, row) / count) - (mean * mean);

                if (variance <= 1e-6) continue;

                double cross = 0;

                for (var ny = 0; ny < needle.Height; ny++)
                    cross += Dot(pixelSpan.Slice(((row + ny) * spanW) + col, needle.Width), needleSpan.Slice(ny * needle.Width, needle.Width));

                found.Add((fromX + col, fromY + row, cross / count / (Math.Sqrt(variance) * needleDeviation)));
            }

            perRow[row] = found;
        });

        var results = new List<(int, int, double)>();
        foreach (var row in perRow) results.AddRange(row);

        return results;
    }

    /// <summary>마스크 안 무게의 합이 이보다 작으면(픽셀 수) 견줄 것이 없다고 본다 - 몇 픽셀짜리 마스크는 어디에나 맞는다.</summary>
    public const double MinimumMaskedArea = 12;

    /// <summary>
    /// 마스크 본보기(<see cref="GrayImage.Mask"/>)의 정규화 상호상관 - 본보기·조각 모두 마스크 안 픽셀만으로 평균·분산을 낸다.
    /// </summary>
    /// <remarks>
    /// <b>식</b>(무게 w, 무게 합 W) - 본보기 평균 n̄ = Σwn/W, 조각 평균 p̄ = Σwp/W. 분자 Σw(n−n̄)(p−p̄) 는 Σw(n−n̄)=0 이라 Σw(n−n̄)p 하나가 된다.
    /// 조각의 합·제곱합이 창마다 무게가 달라 적분 영상을 못 쓰므로, 곱의 합이 셋이다(분자·Σwp·Σwp²) - 마스크 없는 것보다 두세 배 느리다.
    ///
    /// <b>조각 값에서 128 을 뺀다</b> - 상관은 조각 값을 한 값만큼 옮겨도 그대로인데(Σw(n−n̄)=0), float 로 제곱합을 쌓을 때 값이 작아야 분산(제곱합 − 평균²)이
    /// 덜 뭉개진다.
    /// </remarks>
    private static List<(int X, int Y, double Score)> ScanMasked(GrayImage haystack, GrayImage needle, int fromX, int fromY, int toX, int toY)
    {
        var count = needle.Width * needle.Height;
        var mask = needle.Mask!;
        var weights = new float[count];
        double total = 0, needleSum = 0;

        for (var i = 0; i < count; i++)
        {
            weights[i] = mask[i] / 255f;
            total += weights[i];
            needleSum += weights[i] * needle.Pixels[i];
        }

        if (total < MinimumMaskedArea) return [];

        var needleMean = needleSum / total;
        double needleVariance = 0;

        for (var i = 0; i < count; i++)
        {
            var d = needle.Pixels[i] - needleMean;
            needleVariance += weights[i] * d * d;
        }

        needleVariance /= total;

        // 마스크 안이 한 가지 색이면 견줄 것이 없다.
        if (needleVariance <= 1e-6) return [];

        var needleDeviation = Math.Sqrt(needleVariance);

        // 무게를 곱해 둔 평균 뺀 본보기 - 마스크 밖은 0 이라 분자에 안 들어간다.
        var centered = new float[count];
        for (var i = 0; i < count; i++) centered[i] = (float)(weights[i] * (needle.Pixels[i] - needleMean));

        var width = haystack.Width;
        var spanW = (toX - fromX) + needle.Width;
        var spanH = (toY - fromY) + needle.Height;
        var pixels = new float[spanW * spanH];
        var squares = new float[spanW * spanH];

        for (var y = 0; y < spanH; y++)
        {
            var source = ((fromY + y) * width) + fromX;

            for (var x = 0; x < spanW; x++)
            {
                var value = haystack.Pixels[source + x] - 128f;

                pixels[(y * spanW) + x] = value;
                squares[(y * spanW) + x] = value * value;
            }
        }

        var rows = (toY - fromY) + 1;
        var perRow = new List<(int, int, double)>[rows];

        System.Threading.Tasks.Parallel.For(0, rows, row =>
        {
            var found = new List<(int, int, double)>((toX - fromX) + 1);
            var pixelSpan = new ReadOnlySpan<float>(pixels);
            var squareSpan = new ReadOnlySpan<float>(squares);
            var needleSpan = new ReadOnlySpan<float>(centered);
            var weightSpan = new ReadOnlySpan<float>(weights);

            for (var col = 0; col <= toX - fromX; col++)
            {
                double cross = 0, sum = 0, sumSquares = 0;

                for (var ny = 0; ny < needle.Height; ny++)
                {
                    var at = ((row + ny) * spanW) + col;
                    var weightRow = weightSpan.Slice(ny * needle.Width, needle.Width);

                    cross += Dot(pixelSpan.Slice(at, needle.Width), needleSpan.Slice(ny * needle.Width, needle.Width));
                    sum += Dot(pixelSpan.Slice(at, needle.Width), weightRow);
                    sumSquares += Dot(squareSpan.Slice(at, needle.Width), weightRow);
                }

                var mean = sum / total;
                var variance = (sumSquares / total) - (mean * mean);

                if (variance <= 1e-6) continue;

                found.Add((fromX + col, fromY + row, cross / total / (Math.Sqrt(variance) * needleDeviation)));
            }

            perRow[row] = found;
        });

        var results = new List<(int, int, double)>();
        foreach (var row in perRow) results.AddRange(row);

        return results;
    }

    /// <summary>두 줄의 곱의 합 - 벡터로 한 번에 여러 칸씩.</summary>
    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var total = 0f;
        var i = 0;

        if (System.Numerics.Vector.IsHardwareAccelerated && a.Length >= System.Numerics.Vector<float>.Count)
        {
            var accumulator = System.Numerics.Vector<float>.Zero;
            var step = System.Numerics.Vector<float>.Count;

            for (; i <= a.Length - step; i += step)
                accumulator += new System.Numerics.Vector<float>(a.Slice(i, step)) * new System.Numerics.Vector<float>(b.Slice(i, step));

            total = System.Numerics.Vector.Dot(accumulator, System.Numerics.Vector<float>.One);
        }

        for (; i < a.Length; i++) total += a[i] * b[i];

        return total;
    }

    private static List<(int X, int Y, double Score)> Top(List<(int X, int Y, double Score)> all, int width, int height, int most)
    {
        all.Sort((a, b) => b.Score.CompareTo(a.Score));

        var picked = new List<(int X, int Y, double Score)>();

        foreach (var one in all)
        {
            if (picked.Count >= most) break;

            var overlaps = false;

            foreach (var already in picked)
            {
                if (Math.Abs(already.X - one.X) < width && Math.Abs(already.Y - one.Y) < height)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps) picked.Add(one);
        }

        return picked;
    }

    private static IReadOnlyList<TemplateHit> Best(List<(int X, int Y, double Score)> found, GrayImage needle, GrayImage haystack, int most)
        => [.. Top(found, needle.Width, needle.Height, most)
            .ConvertAll(one => new TemplateHit(
                (double)one.X / haystack.Width,
                (double)one.Y / haystack.Height,
                (double)needle.Width / haystack.Width,
                (double)needle.Height / haystack.Height,
                one.Score))];
}
