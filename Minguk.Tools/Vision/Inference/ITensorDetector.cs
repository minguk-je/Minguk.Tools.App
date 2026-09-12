using System.Collections.Generic;

using Minguk.Tools.Inference;
using Minguk.Tools.Vision.Inference.Onnx;
using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Inference;

/// <summary>
/// 그림 파일이 아니라 <b>이미 만들어 둔 텐서</b>를 받을 수 있는 검출기.
/// </summary>
/// <remarks>
/// <b>왜 <see cref="IDetector"/> 와 가르나</b> - ML.NET 쪽(AutoFormerV2)은 파이프라인 안에서 제가 그림을 읽는다.
/// 텐서를 받는 입구가 아예 없다. 못 하는 것을 늘 예외만 던지는 메서드로 두면 부르는 쪽이 되는 줄 알고 쓴다 -
/// 입력 어댑터에서 <c>IScanCodeInput</c> 을 가른 것과 같은 이유로 갈라 둔다.
/// 부르는 쪽은 <c>detector is ITensorDetector</c> 로 물어보고, 아니면 파일 경로 길로 간다.
///
/// 이 길의 값어치: 캡처 프레임은 이미 GPU 에 있다. 셰이더로 바로 텐서를 만들면 CPU 리드백도, PNG 왕복도 없다.
/// </remarks>
public interface ITensorDetector
{
    /// <summary>이 모델이 받고 싶어 하는 텐서 모양. 캡처 쪽이 이 명세로 셰이더를 돌린다.</summary>
    TensorSpec InputSpec { get; }

    /// <summary>
    /// 만들어 둔 텐서로 찾는다. 좌표는 원본 기준 0~1 로 돌려준다.
    /// </summary>
    /// <param name="map">텐서를 만들 때 쓴 배율·여백. 좌표를 되돌리는 데 쓴다.</param>
    IReadOnlyList<Detection> Detect(float[] tensor, LetterboxMap map, LabelClasses classes, float minimumScore = 0.5f);
}
