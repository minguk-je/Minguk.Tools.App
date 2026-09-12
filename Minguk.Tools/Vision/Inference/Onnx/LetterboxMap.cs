namespace Minguk.Tools.Vision.Inference.Onnx;

/// <summary>
/// 원본 그림을 모델 입력 칸에 넣을 때의 배율과 여백. 모델이 돌려준 좌표를 원본의 0~1 로 되돌릴 때 쓴다.
/// </summary>
/// <remarks>
/// <b>왜 따로 두나</b> - 검출 모델은 정사각(640x640)을 받는데 화면은 16:9 다. 비율을 지키려면 남는 자리를
/// 회색으로 채우는데(레터박스), 그 여백을 빼지 않으면 사각형이 통째로 밀린다. 눈으로는 "조금 어긋나네" 로
/// 보여 모델 탓을 하게 된다 - 숫자로 짚을 수 있게 계산을 한 군데 모은다.
///
/// <see cref="Minguk.Tools.Inference.FramePreprocessor"/> 의 셰이더가 넣을 때 쓰는 계산과 짝이다.
/// 한쪽을 고치면 다른 쪽도 같이 고쳐야 한다.
/// </remarks>
/// <param name="Scale">원본 1px 이 입력 칸에서 몇 px 이 되는지.</param>
/// <param name="PadX">입력 칸 왼쪽 여백(px).</param>
/// <param name="PadY">입력 칸 위쪽 여백(px).</param>
/// <param name="InputWidth">모델 입력 너비.</param>
/// <param name="InputHeight">모델 입력 높이.</param>
public readonly record struct LetterboxMap(double Scale, double PadX, double PadY, int InputWidth, int InputHeight)
{
    /// <summary>원본 크기와 입력 크기로 배율·여백을 잡는다. 레터박스를 끄면 축마다 따로 늘린다.</summary>
    public static LetterboxMap For(int sourceWidth, int sourceHeight, int inputWidth, int inputHeight, bool letterbox = true)
    {
        if (!letterbox)
            return new LetterboxMap(0, 0, 0, inputWidth, inputHeight);

        var scale = System.Math.Min(inputWidth / (double)sourceWidth, inputHeight / (double)sourceHeight);

        return new LetterboxMap(
            scale,
            (inputWidth - (sourceWidth * scale)) / 2,
            (inputHeight - (sourceHeight * scale)) / 2,
            inputWidth,
            inputHeight);
    }

    /// <summary>레터박스 없이 늘려 넣었는가. 그때는 0~1 이 그대로 0~1 이다.</summary>
    public bool IsStretched => Scale <= 0;

    /// <summary>
    /// 입력 칸 기준 0~1 좌표를 원본 기준 0~1 로 되돌린다.
    /// </summary>
    public (double X, double Y) ToSource(double x01, double y01)
    {
        if (IsStretched) return (x01, y01);

        var drawWidth = InputWidth - (PadX * 2);
        var drawHeight = InputHeight - (PadY * 2);

        return (((x01 * InputWidth) - PadX) / drawWidth,
                ((y01 * InputHeight) - PadY) / drawHeight);
    }

    /// <summary>입력 칸 기준 <b>픽셀</b> 좌표를 원본 기준 0~1 로.</summary>
    public (double X, double Y) PixelToSource(double x, double y)
        => ToSource(x / InputWidth, y / InputHeight);
}
