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
    public const int CoarseKeep = 5;

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
        var shrink = Math.Max(1.0, (double)Math.Max(haystack.Width, haystack.Height) / CoarseSide);
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
    private static List<(int X, int Y, double Score)> Scan(GrayImage haystack, GrayImage needle, int fromX, int fromY, int toX, int toY)
    {
        var results = new List<(int, int, double)>();

        if (toX < fromX || toY < fromY) return results;

        // 본보기의 평균·표준편차는 한 번만.
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
        if (needleVariance <= 1e-6) return results;

        var needleDeviation = Math.Sqrt(needleVariance);

        for (var y = fromY; y <= toY; y++)
        {
            for (var x = fromX; x <= toX; x++)
            {
                double sum = 0, squares = 0, cross = 0;

                for (var ny = 0; ny < needle.Height; ny++)
                {
                    var row = ((y + ny) * haystack.Width) + x;
                    var needleRow = ny * needle.Width;

                    for (var nx = 0; nx < needle.Width; nx++)
                    {
                        double a = haystack.Pixels[row + nx];
                        double b = needle.Pixels[needleRow + nx];

                        sum += a;
                        squares += a * a;
                        cross += a * b;
                    }
                }

                var mean = sum / count;
                var variance = (squares / count) - (mean * mean);

                if (variance <= 1e-6) continue;

                var score = ((cross / count) - (mean * needleMean)) / (Math.Sqrt(variance) * needleDeviation);

                results.Add((x, y, score));
            }
        }

        return results;
    }

    /// <summary>닮음이 높은 것부터, 서로 겹치지 않게 골라 준다.</summary>
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
