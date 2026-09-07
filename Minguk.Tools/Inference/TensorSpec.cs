using System;
using System.Numerics;

namespace Minguk.Tools.Inference;

/// <summary>텐서의 축 순서.</summary>
public enum TensorLayout
{
    /// <summary>[1, 3, H, W]. PyTorch 계열(YOLO · ResNet 등)이 쓰는 기본형.</summary>
    Nchw,

    /// <summary>[1, H, W, 3]. TensorFlow 계열에서 나온 모델이 쓴다.</summary>
    Nhwc
}

/// <summary>
/// 모델이 받고 싶어 하는 입력 텐서의 명세.
///
/// 이 값들이 <see cref="FramePreprocessor"/> 의 컴퓨트 셰이더로 그대로 넘어간다.
/// 모델을 바꾸면 여기만 바꾸면 되고, 캡처 쪽은 건드릴 일이 없다.
/// </summary>
public sealed class TensorSpec
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public TensorLayout Layout { get; init; } = TensorLayout.Nchw;

    /// <summary>
    /// 원본 비율을 유지하고 남는 자리를 <see cref="PadColor"/> 로 채운다(레터박스).
    /// 끄면 그냥 늘려서 맞춘다. 검출 모델은 대개 켜야 좌표가 안 틀어진다.
    /// </summary>
    public bool Letterbox { get; init; } = true;

    /// <summary>
    /// 채널별 평균. 셰이더가 샘플한 값은 이미 0~1 이므로 여기도 0~1 단위로 준다.
    /// (255 로 나누는 일은 UNorm 포맷이라 하드웨어가 공짜로 해 준다.)
    /// </summary>
    public Vector3 Mean { get; init; } = Vector3.Zero;

    public Vector3 Std { get; init; } = Vector3.One;

    /// <summary>레터박스 여백 색. YOLO 계열의 관습은 회색(114/255).</summary>
    public Vector3 PadColor { get; init; } = new(114f / 255f, 114f / 255f, 114f / 255f);

    public int ElementCount => Width * Height * 3;

    public int ByteCount => ElementCount * sizeof(float);

    public long[] Shape => Layout == TensorLayout.Nchw
        ? new long[] { 1, 3, Height, Width }
        : new long[] { 1, Height, Width, 3 };

    /// <summary>YOLO 계열 기본값. 0~1 정규화, 레터박스, NCHW.</summary>
    public static TensorSpec Yolo(int size = 640) => new()
    {
        Width = size,
        Height = size,
        Layout = TensorLayout.Nchw,
        Letterbox = true
    };

    /// <summary>ImageNet 분류 모델 기본값. 늘려 맞추고 ImageNet 통계로 정규화.</summary>
    public static TensorSpec ImageNet(int size = 224) => new()
    {
        Width = size,
        Height = size,
        Layout = TensorLayout.Nchw,
        Letterbox = false,
        Mean = new Vector3(0.485f, 0.456f, 0.406f),
        Std = new Vector3(0.229f, 0.224f, 0.225f)
    };

    public override string ToString() =>
        $"{Layout} {Width}×{Height}×3, {(Letterbox ? "레터박스" : "늘림")}, {ByteCount / 1024.0:n0} KB";
}
