using System;
using System.Collections.Generic;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Vision.Inference;

/// <summary>
/// 학습해 둔 모델로 그림에서 몹을 찾는 것.
/// </summary>
/// <remarks>
/// <b>왜 가르나</b> - 지금 쓰는 ML.NET AutoFormerV2 가 한 장에 660ms 다(GTX 1060 실측). 같은 카드에서
/// ONNX Runtime 으로 돌린 검출기는 6~37ms 였다. 갈아탈 것이지만 <b>한 번에 갈아엎지 않는다</b> - 학습해 둔
/// 모델을 못 쓰게 만들면 그 사이에 아무것도 못 한다. 그래서 찾는 일을 인터페이스로 두고 구현을 둘로 둔다.
/// 어느 것을 쓸지는 모델 옆 쪽지(<see cref="DetectorManifest.Engine"/>)가 정하고 <see cref="DetectorFactory"/> 가 고른다.
///
/// <b>입력이 왜 파일 경로인가</b> - 지금 경로가 그렇다(프레임 → PNG → 읽기). ONNX 쪽은 GPU 텍스처를 바로
/// 텐서로 만들어 넣는 것이 제 모습이라, 그때 <c>ITensorDetector</c> 를 따로 두고 쓸 수 있는 쪽만 구현한다
/// (입력 어댑터에서 <c>IScanCodeInput</c> 을 가른 것과 같은 이유 - 못 하는 것을 빈 메서드로 두지 않는다).
/// </remarks>
public interface IDetector : IDisposable
{
    /// <summary>읽어 온 모델 파일.</summary>
    string ModelPath { get; }

    /// <summary>모델 옆 쪽지. 학습할 때의 입력 크기가 들어 있어 좌표를 되돌릴 때 쓴다.</summary>
    DetectorManifest Manifest { get; }

    /// <summary>
    /// 그림 하나에서 찾는다. 사각형은 0~1 비율이라 화면 크기를 안 탄다.
    /// </summary>
    /// <param name="imagePath">볼 그림. 크기는 아무래도 좋다 - 구현이 제 입력 크기로 맞춘다.</param>
    /// <param name="minimumScore">이보다 자신 없는 것은 버린다.</param>
    IReadOnlyList<Detection> Detect(string imagePath, LabelClasses classes, float minimumScore = 0.5f);
}
