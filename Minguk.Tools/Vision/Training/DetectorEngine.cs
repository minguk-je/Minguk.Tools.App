namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 학습해 둔 모델을 무엇으로 돌리는지. 모델 옆 쪽지(<see cref="DetectorManifest"/>)에 적어 두고 읽을 때 고른다.
/// </summary>
/// <remarks>
/// 쪽지에 없으면 <see cref="Torch"/> 다 - 이 칸이 생기기 전에 학습한 모델들이 그렇다.
/// 설정이 아니라 <b>모델과 함께</b> 두는 이유는 입력 크기와 같다: 설정으로 두면 다른 엔진의 모델을 읽는 순간
/// 조용히 어긋난다.
/// </remarks>
public enum DetectorEngine
{
    /// <summary>ML.NET AutoFormerV2 + TorchSharp(libtorch). 지금까지 학습한 것.</summary>
    Torch,

    /// <summary>ONNX Runtime + DirectML. 같은 카드에서 6~37ms (실측) - 설계 2단계에서 붙인다.</summary>
    Onnx
}
