using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Markup;

/// <summary>
/// 캔버스가 점선으로 그릴 예측 하나.
/// </summary>
/// <param name="Box">자리. 사람이 찍은 것과 같은 0~1 이고 <c>ClassId</c> 도 같은 번호다.</param>
/// <param name="Caption">사각형 아래 적을 글. 이름과 신뢰도.</param>
/// <remarks>
/// <c>Vision.Inference.Detection</c> 을 그대로 안 쓴다. 캔버스는 <c>Markup</c> 에 있는
/// 그리기 도구고, 그것이 추론 쪽을 알게 되면 모델을 안 쓰는 화면에서도 그 무게를 지게 된다.
/// 그릴 때 필요한 것만 추려 받는다.
/// </remarks>
public readonly record struct PredictedBox(LabelBox Box, string Caption);
