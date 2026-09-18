using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Inference.Onnx;

/// <summary>
/// DETR 계열(RT-DETR · D-FINE) 출력 해석. 출력은 <c>logits [1, 질의수, 클래스수]</c> 와 <c>pred_boxes [1, 질의수, 4]</c>.
/// </summary>
/// <remarks>
/// <b>겹침 제거(NMS)가 없다</b> - 이 계열은 질의 하나가 물체 하나를 맡도록 학습돼, 같은 것을 여러 번 내놓지 않는다.
/// 그래서 점수 문턱만 넘기면 끝이다. 대신 <b>질의 수만큼(보통 300개)</b> 늘 돌려주므로 문턱이 낮으면 쓰레기가 쏟아진다.
///
/// <b>점수는 시그모이드다</b> - 소프트맥스가 아니다(배경 클래스가 따로 없다). 클래스마다 따로 0~1 로 보고
/// 그중 가장 높은 것을 그 질의의 답으로 삼는다.
///
/// <b>사각형은 0~1 의 중심xywh</b> - 입력 칸(레터박스를 포함한 정사각) 기준이라, 원본으로 되돌리려면
/// <see cref="LetterboxMap"/> 를 거쳐야 한다.
/// </remarks>
public sealed class DetrDecoder : IDetectionDecoder
{
    public string Name => "DETR";

    public bool CanDecode(IReadOnlyList<string> outputNames)
        => outputNames.Contains("logits") && outputNames.Contains("pred_boxes");

    public IReadOnlyList<Detection> Decode(IReadOnlyDictionary<string, (float[] Values, long[] Shape)> outputs,
                                           LetterboxMap map,
                                           LabelClasses classes,
                                           float minimumScore)
    {
        var (logits, logitShape) = outputs["logits"];
        var (boxes, boxShape) = outputs["pred_boxes"];

        if (logitShape.Length != 3 || boxShape.Length != 3)
            throw new InvalidOperationException($"DETR 출력 모양이 다르다: logits [{string.Join(",", logitShape)}], pred_boxes [{string.Join(",", boxShape)}]");

        var queries = (int)logitShape[1];
        var classCount = (int)logitShape[2];
        var found = new List<Detection>();

        for (var q = 0; q < queries; q++)
        {
            var best = -1;
            var bestScore = 0f;

            for (var c = 0; c < classCount; c++)
            {
                var score = Sigmoid(logits[(q * classCount) + c]);

                if (score <= bestScore) continue;

                bestScore = score;
                best = c;
            }

            if (best < 0 || bestScore < minimumScore) continue;

            var at = q * 4;
            var (x, y) = map.ToSource(boxes[at], boxes[at + 1]);

            // 너비·높이는 여백을 빼는 것이 아니라 그린 자리의 비율로 나눈다.
            var width = boxes[at + 2] * map.InputWidth / DrawWidth(map);
            var height = boxes[at + 3] * map.InputHeight / DrawHeight(map);

            // 화면 밖으로 나간 것은 잘라 둔다. 스크립트가 그 좌표로 마우스를 옮기기 때문이다.
            var left = Math.Clamp(x - (width / 2), 0, 1);
            var top = Math.Clamp(y - (height / 2), 0, 1);
            var right = Math.Clamp(x + (width / 2), 0, 1);
            var bottom = Math.Clamp(y + (height / 2), 0, 1);

            if (right - left <= 0 || bottom - top <= 0) continue;

            found.Add(new Detection(classes.NameOf(best), LabelBox.FromCorners(best, left, top, right, bottom), bestScore));
        }

        // 점수가 높은 것부터. 화면은 앞의 몇 개만 적고, 스크립트의 "가장 가까운 검출" 도 이 순서를 본다.
        return [.. found.OrderByDescending(d => d.Score)];
    }

    private static double DrawWidth(LetterboxMap map) => map.IsStretched ? map.InputWidth : map.InputWidth - (map.PadX * 2);

    private static double DrawHeight(LetterboxMap map) => map.IsStretched ? map.InputHeight : map.InputHeight - (map.PadY * 2);

    private static float Sigmoid(float value) => 1f / (1f + MathF.Exp(-value));
}
