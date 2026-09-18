using System;
using System.Collections.Generic;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>검출한 글자 줄 사각형(맵 또는 조각 픽셀 좌표)과 점수.</summary>
public readonly record struct DbBox(double Left, double Top, double Right, double Bottom, float Score)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;
}

/// <summary>
/// 글자 검출 모델(DBNet)의 확률맵에서 글자 줄 사각형을 뽑는다.
/// </summary>
/// <remarks>
/// PaddleOCR <c>DBPostProcess</c> 를 따르되 <b>사각형은 축에 맞춘다</b>(최소 회전 사각형이 아니다) - 게임 UI 글자는 눕지 않고,
/// 이탤릭은 기울임이라 축 사각형이 덮는다. 회전하면 인식에 넣기 전에 원근 변환이 따라온다.
///
/// DBNet 은 글자를 <b>안으로 줄여서</b> 칠한다. 그래서 찾은 덩어리를 넓이 × 비율 / 둘레 만큼 넓혀야 글자 가장자리가 들어온다.
/// 값(문턱 0.3 · 상자 0.6 · 넓히기 1.5)은 모델 <c>inference.yml</c> 의 것이다.
/// </remarks>
public static class DbPostProcess
{
    public const float Threshold = 0.3f;
    public const float BoxThreshold = 0.6f;
    public const double UnclipRatio = 1.5;
    public const int MinSize = 3;
    public const int MaxCandidates = 1000;

    public static IReadOnlyList<DbBox> Extract(ReadOnlySpan<float> map, int width, int height)
    {
        if (map.Length < width * height)
            throw new ArgumentException($"맵 길이({map.Length})가 {width}x{height} 보다 짧다", nameof(map));

        var visited = new bool[width * height];
        var stack = new Stack<int>();
        var boxes = new List<DbBox>();
        var candidates = 0;

        for (var start = 0; start < width * height; start++)
        {
            if (visited[start] || map[start] <= Threshold) continue;
            if (++candidates > MaxCandidates) break;

            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            double sum = 0;
            var count = 0;

            visited[start] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var x = index % width;
                var y = index / width;

                sum += map[index];
                count++;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);

                if (x > 0) Visit(map, visited, stack, index - 1);
                if (x < width - 1) Visit(map, visited, stack, index + 1);
                if (y > 0) Visit(map, visited, stack, index - width);
                if (y < height - 1) Visit(map, visited, stack, index + width);
            }

            var boxWidth = right - left + 1;
            var boxHeight = bottom - top + 1;

            if (Math.Min(boxWidth, boxHeight) < MinSize) continue;

            var score = (float)(sum / count);

            if (score < BoxThreshold) continue;

            // PaddleOCR unclip 과 같은 거리: 넓이 × 비율 / 둘레.
            var distance = boxWidth * boxHeight * UnclipRatio / (2.0 * (boxWidth + boxHeight));
            var box = new DbBox(
                Math.Max(0, left - distance),
                Math.Max(0, top - distance),
                Math.Min(width, right + 1 + distance),
                Math.Min(height, bottom + 1 + distance),
                score);

            if (Math.Min(box.Width, box.Height) < MinSize + 2) continue;

            boxes.Add(box);
        }

        return boxes;
    }

    // 지역 함수는 Span 을 붙잡지 못해 인자로 넘긴다.
    private static void Visit(ReadOnlySpan<float> map, bool[] visited, Stack<int> stack, int index)
    {
        if (visited[index] || map[index] <= Threshold) return;

        visited[index] = true;
        stack.Push(index);
    }
}
