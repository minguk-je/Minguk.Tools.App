using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Inference.Onnx;

/// <summary>
/// D-FINE 이 <b>후처리까지 넣어</b> 내보낸 모양. 출력은 <c>labels [1,N]</c> · <c>boxes [1,N,4]</c> · <c>scores [1,N]</c>.
/// </summary>
/// <remarks>
/// <b><see cref="DetrDecoder"/> 와 무엇이 다른가</b> - 같은 D-FINE 이라도 내보내기 방식이 둘이다.
/// 날것으로 내보내면 <c>logits</c>·<c>pred_boxes</c> 가 나와 시그모이드와 중심xywh 를 우리가 풀어야 하고,
/// <c>tools/deployment/export_onnx.py</c> 로 내보내면 그 계산이 그래프 안에 들어가 이 세 개가 나온다.
/// 우리가 학습해 쓰는 것은 뒤쪽이다.
///
/// <b>사각형은 픽셀 xyxy 다</b> - 0~1 이 아니다. 후처리가 둘째 입력(<c>orig_target_sizes</c>)을
/// <c>repeat(1,2)</c> 해 [w,h,w,h] 를 만들어 곱한다. 우리는 그 입력에 <b>레터박스한 칸의 크기</b>를 넣으므로
/// 좌표계는 입력 칸 픽셀이고, 원본으로 되돌리는 일은 <see cref="LetterboxMap.PixelToSource"/> 가 한다.
/// (원본 크기를 넣어 한 번에 받지 않는 이유는 여백만큼 어긋나기 때문이다 - 후처리는 여백을 모른다.)
///
/// <b>겹침 제거(NMS)는 없다</b> - DETR 계열이라 질의 하나가 물체 하나를 맡는다. 다만 점수순 상위 300개를
/// <b>늘</b> 돌려주므로 문턱이 낮으면 쓰레기가 쏟아지는 것도 같다.
/// </remarks>
public sealed class DFineDecoder : IDetectionDecoder
{
    public string Name => "D-FINE";

    public bool CanDecode(IReadOnlyList<string> outputNames)
        => outputNames.Contains("labels") && outputNames.Contains("boxes") && outputNames.Contains("scores");

    public IReadOnlyList<Detection> Decode(IReadOnlyDictionary<string, (float[] Values, long[] Shape)> outputs,
                                           LetterboxMap map,
                                           LabelClasses classes,
                                           float minimumScore)
    {
        var (labels, labelShape) = outputs["labels"];
        var (boxes, boxShape) = outputs["boxes"];
        var (scores, _) = outputs["scores"];

        if (boxShape.Length != 3 || boxShape[^1] != 4)
            throw new InvalidOperationException($"D-FINE 출력 모양이 다르다: boxes [{string.Join(",", boxShape)}]");

        var count = (int)boxShape[1];
        var found = new List<Detection>();

        for (var i = 0; i < count; i++)
        {
            var score = scores[i];

            // 점수순으로 오므로 문턱 아래가 나오면 뒤도 다 아래다.
            if (score < minimumScore) break;

            var at = i * 4;
            var (left, top) = map.PixelToSource(boxes[at], boxes[at + 1]);
            var (right, bottom) = map.PixelToSource(boxes[at + 2], boxes[at + 3]);

            // 화면 밖으로 나간 것은 잘라 둔다. 스크립트가 그 좌표로 마우스를 옮기기 때문이다.
            left = Math.Clamp(left, 0, 1);
            top = Math.Clamp(top, 0, 1);
            right = Math.Clamp(right, 0, 1);
            bottom = Math.Clamp(bottom, 0, 1);

            if (right - left <= 0 || bottom - top <= 0) continue;

            // 라벨은 int64 로 나오지만 여기까지 float 로 옮겨져 온다(클래스 번호는 작아서 상하지 않는다).
            var classIndex = i < labelShape[^1] ? (int)labels[i] : 0;

            found.Add(new Detection(classes.NameOf(classIndex), LabelBox.FromCorners(classIndex, left, top, right, bottom), score));
        }

        return found;
    }
}
