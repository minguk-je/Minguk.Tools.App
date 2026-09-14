using System;
using System.Collections.Generic;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Inference;

/// <summary>
/// 사람이 찍은 사각형과 모델이 찾은 것을 짝지어 "몇 개를 다시 찾았나" 를 센다.
/// </summary>
/// <remarks>
/// 라벨링 화면의 그림별 인식률과 하네스 <c>--detect-check</c> 가 같은 계산을 써야 한다.
/// 두 벌이면 화면은 2/2 인데 하네스는 1/2 인 식으로 어긋나고, 어느 쪽이 맞는지 다투게 된다.
///
/// 라벨 하나에 검출 하나만 짝짓는다. 붙어 있는 봇 둘을 하나의 큰 사각형으로 뭉뚱그려 찾은
/// 것을 둘 다 찾은 것으로 세면 안 된다.
/// </remarks>
public static class DetectionMatch
{
    /// <summary>이만큼 겹치면 같은 것을 찾은 것으로 친다. 검출 쪽에서 흔히 쓰는 기준이다.</summary>
    public const double MatchIou = 0.5;

    /// <summary>다시 찾은 라벨 수와 라벨에 안 붙은 검출(헛것) 수.</summary>
    public readonly record struct Result(int Found, int Labels, int Extra)
    {
        public bool IsComplete => Found == Labels;

        /// <summary>목록에 적는 짧은 글. "2/2", "1/2 · 헛것 3".</summary>
        public string Describe => Extra > 0 ? $"{Found}/{Labels} · 헛것 {Extra}" : $"{Found}/{Labels}";
    }

    public static Result Match(IReadOnlyList<LabelBox> labels, IReadOnlyList<Detection> detections, double matchIou = MatchIou)
    {
        var used = new HashSet<int>();
        var found = 0;

        foreach (var label in labels)
        {
            var bestIndex = -1;
            var bestIou = 0d;

            for (var i = 0; i < detections.Count; i++)
            {
                if (used.Contains(i)) continue;

                var iou = Iou(label, detections[i].Box);
                if (iou > bestIou) { bestIou = iou; bestIndex = i; }
            }

            if (bestIndex >= 0 && bestIou >= matchIou)
            {
                used.Add(bestIndex);
                found++;
            }
        }

        return new Result(found, labels.Count, detections.Count - used.Count);
    }

    /// <summary>두 사각형이 겹치는 넓이 / 합친 넓이. 0~1 좌표라 그림 크기를 안 탄다.</summary>
    public static double Iou(LabelBox a, LabelBox b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

        if (width <= 0 || height <= 0) return 0;

        var overlap = width * height;
        var union = (a.Width * a.Height) + (b.Width * b.Height) - overlap;

        return union <= 0 ? 0 : overlap / union;
    }
}
