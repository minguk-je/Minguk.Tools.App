using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Inference.Onnx;

/// <summary>
/// Ultralytics YOLOv8 · YOLO11 이 내보낸 모양. 출력은 <c>output0 [1, 4+검출수, 후보수]</c> - 후보마다 [cx, cy, w, h, 검출별 점수…].
/// </summary>
/// <remarks>
/// <b>DETR 계열과 다른 것 둘.</b>
/// (1) 후보가 8,400개(640 입력)라 <b>겹침 제거(NMS)</b>를 우리가 해야 한다 - 한 검출에 후보 수십 개가 겹쳐 온다.
///     검출마다 따로 누른다(다른 검출끼리는 겹쳐도 둘 다 산다). 겹침 문턱 0.45 는 Ultralytics 추론 기본값과 같다.
/// (2) 사각형은 <b>입력 칸 픽셀의 중심 xywh</b> 다. 점수는 시그모이드가 이미 걸려 0~1 이고, objectness 는 따로 없다(v5 와 다르다).
///
/// 모양이 <c>[1, 후보수, 4+검출수]</c> 로 뒤집혀 나오는 내보내기도 있어(transpose 옵션) 둘 다 받는다 - 어느 축이 후보인지는
/// "더 긴 쪽" 으로 가른다(후보 수천 vs 채널 대여섯).
///
/// <b>라이선스</b> - Ultralytics 는 AGPL-3.0 이고 그 도구로 학습한 가중치까지 그렇게 본다. 이 디코더는 시험용이다.
/// 팔 물건에는 D-FINE·RT-DETR(Apache) 을 쓴다(docs/ONNX-모델-학습.md 0절).
/// </remarks>
public sealed class YoloDecoder : IDetectionDecoder
{
    /// <summary>같은 검출의 후보끼리 이만큼 겹치면 점수 낮은 쪽을 버린다.</summary>
    public const float NmsIou = 0.45f;

    public string Name => "YOLO (Ultralytics)";

    public bool CanDecode(IReadOnlyList<string> outputNames)
        => outputNames.Count == 1 && outputNames[0] == "output0";

    public IReadOnlyList<Detection> Decode(IReadOnlyDictionary<string, (float[] Values, long[] Shape)> outputs,
                                           LetterboxMap map,
                                           LabelClasses classes,
                                           float minimumScore)
    {
        var (values, shape) = outputs["output0"];

        if (shape.Length != 3 || shape[0] != 1)
            throw new InvalidOperationException($"YOLO 출력 모양이 다르다: output0 [{string.Join(",", shape)}]");

        // [1, C, N](기본) 인지 [1, N, C](뒤집힌 것) 인지. 후보 수가 채널 수보다 훨씬 많다.
        var channelsFirst = shape[1] < shape[2];
        var channels = (int)(channelsFirst ? shape[1] : shape[2]);
        var count = (int)(channelsFirst ? shape[2] : shape[1]);

        if (channels < 5)
            throw new InvalidOperationException($"YOLO 출력 채널이 모자란다: {channels} (cx·cy·w·h + 검출 하나 이상이어야 한다)");

        var classCount = channels - 4;

        float At(int candidate, int channel)
            => channelsFirst ? values[(channel * count) + candidate] : values[(candidate * channels) + channel];

        // 1) 문턱을 넘는 후보를 검출별로 모은다.
        var candidates = new List<(int ClassIndex, float Score, double L, double T, double R, double B)>();

        for (var i = 0; i < count; i++)
        {
            var best = 0;
            var bestScore = At(i, 4);

            for (var c = 1; c < classCount; c++)
            {
                var score = At(i, 4 + c);
                if (score > bestScore) { bestScore = score; best = c; }
            }

            if (bestScore < minimumScore) continue;

            var cx = At(i, 0);
            var cy = At(i, 1);
            var w = At(i, 2);
            var h = At(i, 3);

            var (left, top) = map.PixelToSource(cx - (w / 2), cy - (h / 2));
            var (right, bottom) = map.PixelToSource(cx + (w / 2), cy + (h / 2));

            // 화면 밖으로 나간 것은 잘라 둔다. 스크립트가 그 좌표로 마우스를 옮기기 때문이다.
            left = Math.Clamp(left, 0, 1);
            top = Math.Clamp(top, 0, 1);
            right = Math.Clamp(right, 0, 1);
            bottom = Math.Clamp(bottom, 0, 1);

            if (right - left <= 0 || bottom - top <= 0) continue;

            candidates.Add((best, bestScore, left, top, right, bottom));
        }

        // 2) 검출별로 점수순 정렬 뒤 겹치는 것을 누른다(greedy NMS). 후보가 수천 개여도 문턱을 넘는 것은 몇십 개라 O(n²) 로 족하다.
        var found = new List<Detection>();

        foreach (var group in candidates.GroupBy(c => c.ClassIndex))
        {
            var kept = new List<(int ClassIndex, float Score, double L, double T, double R, double B)>();

            foreach (var candidate in group.OrderByDescending(c => c.Score))
            {
                if (kept.Any(k => Iou(k, candidate) > NmsIou)) continue;
                kept.Add(candidate);
            }

            found.AddRange(kept.Select(k =>
                new Detection(classes.NameOf(k.ClassIndex), LabelBox.FromCorners(k.ClassIndex, k.L, k.T, k.R, k.B), k.Score)));
        }

        // 다른 해석기와 같게 점수순으로 돌려준다 - 부르는 쪽이 "가장 자신 있는 것" 을 첫 줄로 기대한다.
        return found.OrderByDescending(d => d.Score).ToArray();
    }

    private static double Iou((int ClassIndex, float Score, double L, double T, double R, double B) a,
                              (int ClassIndex, float Score, double L, double T, double R, double B) b)
    {
        var interWidth = Math.Min(a.R, b.R) - Math.Max(a.L, b.L);
        var interHeight = Math.Min(a.B, b.B) - Math.Max(a.T, b.T);

        if (interWidth <= 0 || interHeight <= 0) return 0;

        var inter = interWidth * interHeight;
        var union = ((a.R - a.L) * (a.B - a.T)) + ((b.R - b.L) * (b.B - b.T)) - inter;

        return union <= 0 ? 0 : inter / union;
    }
}
