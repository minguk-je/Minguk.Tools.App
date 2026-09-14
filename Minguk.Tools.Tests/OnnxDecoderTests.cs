using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Vision.Inference.Onnx;
using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Tests;

/// <summary>
/// ONNX 출력 해석. 레터박스 되돌리기와 DETR 출력 풀기 - 모델 파일 없이 숫자만 본다.
/// </summary>
/// <remarks>
/// 여기가 틀리면 사각형이 통째로 밀리는데, 화면에서는 "모델이 좀 못 찾네" 로 보인다. 숫자로 못 박아 둔다.
/// </remarks>
internal static partial class Program
{
    private static void TestOnnxDecoder()
    {
        // ── 레터박스: 1920x1080 을 640x640 에 넣으면 위아래로 140px 씩 남는다 ──
        {
            var map = LetterboxMap.For(1920, 1080, 640, 640);

            var center = map.ToSource(0.5, 0.5);
            var topLeft = map.ToSource(0, 140 / 640.0);
            var bottomRight = map.ToSource(1, (640 - 140) / 640.0);

            Check("레터박스: 가운데는 가운데, 여백 끝은 0 과 1",
                  Near(map.Scale, 640 / 1920.0) && Near(map.PadY, 140) && Near(map.PadX, 0)
                  && Near(center.X, 0.5) && Near(center.Y, 0.5)
                  && Near(topLeft.X, 0) && Near(topLeft.Y, 0)
                  && Near(bottomRight.X, 1) && Near(bottomRight.Y, 1),
                  $"배율 {map.Scale:0.000}, 여백 ({map.PadX:0}, {map.PadY:0}), 가운데 ({center.X:0.000}, {center.Y:0.000})");
        }

        // ── DETR: 질의 둘 중 점수가 문턱을 넘는 하나만, 좌표는 원본 0~1 로 ──
        {
            var decoder = new DetrDecoder();
            var classes = new LabelClasses(["일반 봇", "다른 몹"]);
            var map = LetterboxMap.For(1920, 1080, 640, 640);

            // logits 는 시그모이드 전 값이다. 2.0 ≈ 88%, -2.0 ≈ 12%.
            var logits = new[] { 2.0f, -3.0f, -2.0f, -3.0f };

            // 첫 질의: 화면 한가운데, 원본 기준 10% x 20% 크기. 둘째는 아무 데나.
            var boxes = new[] { 0.5f, 0.5f, 0.1f * (640 / 1920f) * (1920 / 640f) * 0.3333f, 0.2f * 0.5625f, 0.2f, 0.2f, 0.05f, 0.05f };

            var found = decoder.Decode(
                new Dictionary<string, (float[], long[])>
                {
                    ["logits"] = (logits, [1, 2, 2]),
                    ["pred_boxes"] = (boxes, [1, 2, 4])
                },
                map, classes, minimumScore: 0.5f);

            var one = found.Count == 1 ? found[0] : default;

            Check("DETR: 문턱을 넘는 질의만, 이름·점수·가운데가 맞는다",
                  found.Count == 1 && one.Label == "일반 봇" && one.Score > 0.85f && one.Score < 0.92f
                  && Near(one.Box.CenterX, 0.5, 0.01) && Near(one.Box.CenterY, 0.5, 0.01)
                  && Near(one.Box.Height, 0.2, 0.01),
                  found.Count == 0 ? "하나도 안 나왔다" : $"{found.Count}개, {one.Describe}, 가운데({one.Box.CenterX:0.000}, {one.Box.CenterY:0.000}) 높이 {one.Box.Height:0.000}");
        }

        // ── D-FINE: 후처리를 그래프에 넣어 내보낸 것. 사각형은 입력 칸 픽셀 xyxy 다 ──
        {
            var decoder = new DFineDecoder();
            var classes = new LabelClasses(["일반 봇", "다른 몹"]);

            // 늘려 넣었다고 일러 준다(D-FINE 공식 설정이 그렇다). 그러면 640 으로 나눈 값이 곧 원본 0~1 이다.
            var map = LetterboxMap.For(1920, 1080, 640, 640, letterbox: false);

            // 줄 셋: 0.9(가운데 언저리) · 0.8(왼쪽 위) · 0.1(문턱 아래라 여기서 끊긴다).
            var labels = new[] { 0f, 1f, 0f };
            var boxes = new[] { 288f, 288f, 352f, 352f, 64f, 32f, 128f, 96f, 10f, 10f, 20f, 20f };
            var scores = new[] { 0.9f, 0.8f, 0.1f };

            var found = decoder.Decode(
                new Dictionary<string, (float[], long[])>
                {
                    ["labels"] = (labels, [1, 3]),
                    ["boxes"] = (boxes, [1, 3, 4]),
                    ["scores"] = (scores, [1, 3])
                },
                map, classes, minimumScore: 0.5f);

            var first = found.Count > 0 ? found[0] : default;
            var second = found.Count > 1 ? found[1] : default;

            Check("D-FINE: 픽셀 xyxy 를 원본 0~1 로 · 문턱 아래는 끊는다",
                  found.Count == 2
                  && first.Label == "일반 봇" && Near(first.Box.CenterX, 0.5, 0.001) && Near(first.Box.CenterY, 0.5, 0.001)
                  && Near(first.Box.Width, 0.1, 0.001) && Near(first.Box.Height, 0.1, 0.001)
                  && second.Label == "다른 몹" && Near(second.Box.CenterX, 0.15, 0.001) && Near(second.Box.CenterY, 0.1, 0.001),
                  found.Count == 0 ? "하나도 안 나왔다" : $"{found.Count}개, {first.Describe} 가운데({first.Box.CenterX:0.000}, {first.Box.CenterY:0.000}) 크기({first.Box.Width:0.000})");
        }

        // ── YOLO(Ultralytics): output0 [1, 4+몹수, 후보수]. 중심 xywh 픽셀 + 몹별 점수. 겹침은 우리가 누른다 ──
        {
            var decoder = new YoloDecoder();
            var classes = new LabelClasses(["일반 봇", "다른 몹"]);
            var map = LetterboxMap.For(1920, 1080, 640, 640, letterbox: false);

            // 후보 여덟(채널 6 = 4 + 몹 2). 채널이 먼저다: 채널마다 후보 여덟이 이어진다. 뒤 넷은 점수 0 인 빈 후보 -
            // 해석기가 "후보 축이 채널 축보다 길다" 로 모양을 가리므로 후보가 채널보다 많아야 한다(실제 모델은 8,400개다).
            //   0: 가운데 64x64, 일반 봇 0.9
            //   1: 0 과 거의 같은 자리(4px 옆), 일반 봇 0.7  → 겹쳐서 눌린다
            //   2: 0 과 같은 자리, 다른 몹 0.6              → 다른 몹이라 산다
            //   3: 왼쪽 위, 일반 봇 0.2                    → 문턱 아래
            const int n = 8;
            float[] cx = [320, 324, 320, 64, 0, 0, 0, 0], cy = [320, 320, 320, 32, 0, 0, 0, 0];
            float[] w = [64, 64, 64, 64, 0, 0, 0, 0], h = [64, 64, 64, 64, 0, 0, 0, 0];
            float[] bot = [0.9f, 0.7f, 0.05f, 0.2f, 0, 0, 0, 0], other = [0.1f, 0.1f, 0.6f, 0.1f, 0, 0, 0, 0];
            var output = cx.Concat(cy).Concat(w).Concat(h).Concat(bot).Concat(other).ToArray();

            var found = decoder.Decode(
                new Dictionary<string, (float[], long[])> { ["output0"] = (output, [1, 6, n]) },
                map, classes, minimumScore: 0.5f);

            var first = found.Count > 0 ? found[0] : default;
            var second = found.Count > 1 ? found[1] : default;

            Check("YOLO: 중심 xywh 를 원본 0~1 로 · 같은 몹의 겹침은 누르고 다른 몹은 산다 · 문턱 아래는 버린다",
                  found.Count == 2
                  && first.Label == "일반 봇" && Near(first.Score, 0.9, 0.001)
                  && Near(first.Box.CenterX, 0.5, 0.001) && Near(first.Box.CenterY, 0.5, 0.001) && Near(first.Box.Width, 0.1, 0.001)
                  && second.Label == "다른 몹" && Near(second.Score, 0.6, 0.001),
                  found.Count == 0 ? "하나도 안 나왔다" : $"{found.Count}개, {string.Join(" / ", found.Select(f => f.Describe))}");

            // 뒤집힌 모양 [1, 후보수, 4+몹수] 도 같은 답이어야 한다.
            var transposed = new float[output.Length];
            for (var i = 0; i < n; i++)
                for (var c = 0; c < 6; c++)
                    transposed[(i * 6) + c] = output[(c * n) + i];

            var foundT = decoder.Decode(
                new Dictionary<string, (float[], long[])> { ["output0"] = (transposed, [1, n, 6]) },
                map, classes, minimumScore: 0.5f);

            Check("YOLO: 뒤집힌 출력도 같은 답",
                  foundT.Count == 2 && foundT[0].Label == first.Label && Near(foundT[0].Box.CenterX, first.Box.CenterX),
                  $"{foundT.Count}개");
        }

        // ── 아는 출력인지 팩터리가 가린다 ──
        {
            Check("해석기는 제 출력만 맡는다",
                  new DetrDecoder().CanDecode(["logits", "pred_boxes"]) && !new DetrDecoder().CanDecode(["output"])
                  && new DFineDecoder().CanDecode(["labels", "boxes", "scores"]) && !new DFineDecoder().CanDecode(["logits", "pred_boxes"])
                  && new YoloDecoder().CanDecode(["output0"]) && !new YoloDecoder().CanDecode(["labels", "boxes", "scores"]),
                  "");
        }
    }

    private static bool Near(double value, double expected, double tolerance = 0.001) => Math.Abs(value - expected) <= tolerance;
}
