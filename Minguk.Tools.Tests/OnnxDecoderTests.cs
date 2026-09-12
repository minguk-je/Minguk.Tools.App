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

        // ── 아는 출력인지 팩터리가 가린다 ──
        {
            var decoder = new DetrDecoder();

            Check("DETR 해석기는 제 출력만 맡는다",
                  decoder.CanDecode(["logits", "pred_boxes"]) && !decoder.CanDecode(["output"]),
                  "");
        }
    }

    private static bool Near(double value, double expected, double tolerance = 0.001) => Math.Abs(value - expected) <= tolerance;
}
