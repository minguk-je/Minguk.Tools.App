using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>
/// 읽은 조각들을 위에서 아래, 왼쪽에서 오른쪽으로 줄로 묶는다.
/// </summary>
/// <remarks>세로로 작은 쪽 높이의 절반 넘게 겹치면 같은 줄이다. 「체력 225」 처럼 띄어 놓은 글은 검출이 따로 찾으므로 한 줄로 되돌린다.</remarks>
public static class OcrLineGrouping
{
    /// <param name="width">조각 너비(px). 낱말 자리를 0~1 로 바꾸는 데 쓴다.</param>
    /// <param name="height">조각 높이(px).</param>
    public static IReadOnlyList<OcrLine> Group(IReadOnlyList<(DbBox Box, string Text)> words, int width, int height)
    {
        var rows = new List<List<(DbBox Box, string Text)>>();

        foreach (var word in words.OrderBy(w => w.Box.Top))
        {
            var row = rows.FirstOrDefault(r => SameRow(r[0].Box, word.Box));

            if (row is null) rows.Add([word]);
            else row.Add(word);
        }

        return [.. rows.Select(row =>
        {
            var ordered = row.OrderBy(w => w.Box.Left).ToArray();
            var ocrWords = ordered
                .Select(w => new OcrWord(w.Text, LabelBox.FromCorners(0,
                    Math.Clamp(w.Box.Left / width, 0, 1), Math.Clamp(w.Box.Top / height, 0, 1),
                    Math.Clamp(w.Box.Right / width, 0, 1), Math.Clamp(w.Box.Bottom / height, 0, 1))))
                .ToArray();

            return new OcrLine(string.Join(" ", ordered.Select(w => w.Text)), ocrWords);
        })];
    }

    private static bool SameRow(DbBox a, DbBox b)
    {
        var overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

        return overlap > 0.5 * Math.Min(a.Height, b.Height);
    }
}
