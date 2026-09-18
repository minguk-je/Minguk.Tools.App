using System;

namespace Minguk.Tools.Vision.Labeling;

/// <summary>
/// 라벨 사각형 하나 - "이 자리에 이 몹이 있다".
/// </summary>
/// <remarks>
/// <b>왜 픽셀이 아니라 0~1 로 담는가</b>
///
/// 같은 몹을 1920x1080 에서도 잡고 1280x720 에서도 잡는다. 픽셀로 담으면 그림 크기가
/// 달라지는 순간 라벨이 어긋나고, 학습할 때 어차피 다시 나눠야 한다. 0~1 로 담아 두면
/// 그림 크기를 몰라도 뜻이 통한다 - 화면에 그릴 때만 그때 크기를 곱한다.
///
/// 가운데 점 + 너비/높이로 담는 것도 그대로 관습을 따른 것이다(YOLO). 왼쪽 위 + 크기와
/// 담는 정보는 같지만, 형식을 바꿔 두면 남이 만든 학습 코드에 넣을 때마다 옮겨 적어야 한다.
/// </remarks>
public readonly record struct LabelBox
{
    /// <summary>몇 번째 몹인지. <see cref="LabelClasses"/> 의 자리 번호다.</summary>
    public required int ClassId { get; init; }

    /// <summary>가운데 x (0~1).</summary>
    public required double CenterX { get; init; }

    /// <summary>가운데 y (0~1).</summary>
    public required double CenterY { get; init; }

    /// <summary>너비 (0~1).</summary>
    public required double Width { get; init; }

    /// <summary>높이 (0~1).</summary>
    public required double Height { get; init; }

    public double Left => CenterX - (Width / 2);

    public double Top => CenterY - (Height / 2);

    public double Right => CenterX + (Width / 2);

    public double Bottom => CenterY + (Height / 2);

    /// <summary>
    /// 두 점으로 만든다. 어느 쪽을 먼저 찍든 같은 사각형이 나오고, 그림 밖으로는 안 나간다.
    /// </summary>
    /// <remarks>
    /// 사람은 오른쪽 아래에서 왼쪽 위로도 끈다. 그대로 두면 너비가 음수인 사각형이 생겨
    /// 파일에는 남는데 화면에는 안 보이는 라벨이 된다. 여기서 한 번 바로잡는다.
    /// </remarks>
    public static LabelBox FromCorners(int classId, double x1, double y1, double x2, double y2)
    {
        var left = Clamp01(Math.Min(x1, x2));
        var top = Clamp01(Math.Min(y1, y2));
        var right = Clamp01(Math.Max(x1, x2));
        var bottom = Clamp01(Math.Max(y1, y2));

        return new LabelBox
        {
            ClassId = classId,
            CenterX = Round((left + right) / 2),
            CenterY = Round((top + bottom) / 2),
            Width = Round(right - left),
            Height = Round(bottom - top)
        };
    }

    /// <summary>
    /// 파일에 적히는 것과 같은 자릿수로 맞춘다.
    /// </summary>
    /// <remarks>
    /// 이걸 안 하면 <b>저장하고 다시 읽은 사각형이 원래 것과 같지 않다</b>. (0.2+0.6)/2 는
    /// 0.4 가 아니라 0.4000000000000001 이라, 파일에는 0.4 로 적히고 되읽으면 딱 0.4 가 된다.
    /// 눈에 보일 차이는 아니지만 <c>==</c> 가 어긋나므로 "고른 사각형" 을 못 찾거나
    /// "고친 것이 있나" 가 늘 참이 되는 식으로 엉뚱한 데서 샌다.
    ///
    /// 여섯 자리는 1920px 에서 0.002px 다 - 파일에 그보다 자세히 적지 않으므로
    /// 메모리에만 더 들고 있어 봐야 쓸 데가 없다.
    /// </remarks>
    public const int Digits = 6;

    private static double Round(double value) => Math.Round(value, Digits);

    /// <summary>
    /// 사람이 실수로 찍은 점 하나짜리인지.
    /// </summary>
    /// <remarks>
    /// 클릭만 해도 드래그로 잡혀 넓이 0 인 사각형이 생긴다. 학습에 넣으면 좌표 계산이
    /// 0 으로 나뉘는 자리가 나오므로 만들 때 걸러 낸다. 1/1000 은 1920px 기준 2px 이다.
    /// </remarks>
    public bool IsTooSmall => Width < 0.001 || Height < 0.001;

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);
}
