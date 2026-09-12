using System.Collections.Generic;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Inference.Onnx;

/// <summary>모델이 돌려준 숫자 덩어리를 사각형 목록으로 푼다.</summary>
/// <remarks>
/// <b>왜 가르나</b> - 같은 ONNX 라도 내놓는 모양이 가족마다 다르다. DETR 계열은 질의 300개마다
/// [점수들, 중심xywh(0~1)] 를 주고 겹침 제거(NMS)가 필요 없다. YOLO 계열은 후보 수천 개를 픽셀 좌표로 주고
/// 우리가 눌러야 한다. 엔진(세션 돌리기)과 해석을 한 클래스에 두면 모델을 바꿀 때마다 그 클래스를 뜯게 된다.
/// </remarks>
public interface IDetectionDecoder
{
    /// <summary>사람이 읽을 이름. 어느 해석기로 돌았는지 로그·상태에 적는다.</summary>
    string Name { get; }

    /// <summary>이 출력 이름들을 풀 수 있는가. 팩터리가 이것으로 고른다.</summary>
    bool CanDecode(IReadOnlyList<string> outputNames);

    /// <summary>
    /// 푼다. 좌표는 <b>원본 그림 기준 0~1</b> 로 돌려준다 - 화면 크기를 안 타게.
    /// </summary>
    /// <param name="outputs">출력 이름 → (값, 모양).</param>
    /// <param name="map">넣을 때 쓴 배율·여백. 좌표를 되돌리는 데 쓴다.</param>
    /// <param name="minimumScore">이보다 자신 없는 것은 버린다.</param>
    IReadOnlyList<Detection> Decode(IReadOnlyDictionary<string, (float[] Values, long[] Shape)> outputs,
                                    LetterboxMap map,
                                    LabelClasses classes,
                                    float minimumScore);
}
