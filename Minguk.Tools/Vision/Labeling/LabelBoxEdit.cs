using System;

namespace Minguk.Tools.Vision.Labeling;

/// <summary>사각형의 어디를 잡았는지. 모서리·변은 크기 조절, 안쪽은 옮기기다.</summary>
public enum BoxHandle
{
    None,
    Inside,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left
}

/// <summary>
/// 찍어 둔 사각형을 옮기고 크기를 바꾸는 계산. 화면과 떼어 둔다.
/// </summary>
/// <remarks>
/// 캔버스 안에 두면 마우스 없이는 시험할 수 없다. 여기는 0~1 좌표와 숫자만 받으므로
/// "왼쪽 변을 오른쪽 변 너머로 끌면 어떻게 되나" 같은 것을 마우스 없이 확인한다.
/// 결과는 늘 <see cref="LabelBox.FromCorners"/> 를 거쳐 나오므로 파일에 적히는 자릿수와 같다.
/// </remarks>
public static class LabelBoxEdit
{
    /// <summary>
    /// 통째로 민다. 크기는 그대로고, 그림 밖으로는 안 나간다.
    /// </summary>
    /// <remarks>
    /// 가장자리에서 더 밀면 <b>멈춘다</b>. 잘라 내면 크기가 줄어 옮기려던 것이 찌그러진다 -
    /// 그것은 크기 조절이지 옮기기가 아니다.
    /// </remarks>
    public static LabelBox Move(LabelBox box, double dx, double dy)
    {
        var left = Math.Clamp(box.Left + dx, 0d, Math.Max(0d, 1d - box.Width));
        var top = Math.Clamp(box.Top + dy, 0d, Math.Max(0d, 1d - box.Height));

        return LabelBox.FromCorners(box.ClassId, left, top, left + box.Width, top + box.Height);
    }

    /// <summary>
    /// 잡은 모서리나 변을 (x, y) 로 끌어 놓는다. 반대편은 그대로다.
    /// </summary>
    /// <remarks>
    /// 반대편 변을 지나 끌면 뒤집힌다 - <see cref="LabelBox.FromCorners"/> 가 작은 쪽을 왼쪽으로
    /// 놓으므로 너비가 음수인 사각형은 안 생긴다. 점이 될 만큼 작아지면 원래 것을 돌려준다.
    /// 지우는 것은 Delete 가 할 일이지 크기 조절이 슬쩍 할 일이 아니다.
    /// </remarks>
    public static LabelBox Resize(LabelBox box, BoxHandle handle, double x, double y)
    {
        var left = box.Left;
        var top = box.Top;
        var right = box.Right;
        var bottom = box.Bottom;

        switch (handle)
        {
            case BoxHandle.TopLeft: left = x; top = y; break;
            case BoxHandle.Top: top = y; break;
            case BoxHandle.TopRight: right = x; top = y; break;
            case BoxHandle.Right: right = x; break;
            case BoxHandle.BottomRight: right = x; bottom = y; break;
            case BoxHandle.Bottom: bottom = y; break;
            case BoxHandle.BottomLeft: left = x; bottom = y; break;
            case BoxHandle.Left: left = x; break;
            default: return box;
        }

        var resized = LabelBox.FromCorners(box.ClassId, left, top, right, bottom);

        return resized.IsTooSmall ? box : resized;
    }

    /// <summary>
    /// 화면 사각형(left, top, right, bottom)에서 점 (x, y) 가 어디를 짚었는지.
    /// </summary>
    /// <remarks>
    /// 모서리 → 변 → 안쪽 순서다. 모서리는 두 변이 겹치는 자리라 변보다 먼저 봐야 잡힌다.
    /// <paramref name="grip"/> 은 손잡이 반지름(화면 픽셀). 작은 사각형에서는 손잡이가
    /// 안쪽을 다 덮어 옮길 자리가 없어지는데, 그때는 크기 조절이 우선이다 - 작은 것은
    /// 옮기는 것보다 늘리는 일이 잦다.
    /// </remarks>
    public static BoxHandle HitHandle(double left, double top, double right, double bottom, double x, double y, double grip)
    {
        var nearLeft = Math.Abs(x - left) <= grip;
        var nearRight = Math.Abs(x - right) <= grip;
        var nearTop = Math.Abs(y - top) <= grip;
        var nearBottom = Math.Abs(y - bottom) <= grip;

        if (nearTop && nearLeft) return BoxHandle.TopLeft;
        if (nearTop && nearRight) return BoxHandle.TopRight;
        if (nearBottom && nearLeft) return BoxHandle.BottomLeft;
        if (nearBottom && nearRight) return BoxHandle.BottomRight;

        var withinX = x >= left - grip && x <= right + grip;
        var withinY = y >= top - grip && y <= bottom + grip;

        if (nearTop && withinX) return BoxHandle.Top;
        if (nearBottom && withinX) return BoxHandle.Bottom;
        if (nearLeft && withinY) return BoxHandle.Left;
        if (nearRight && withinY) return BoxHandle.Right;

        return withinX && withinY ? BoxHandle.Inside : BoxHandle.None;
    }

    /// <summary>모서리·변인지. 안쪽·바깥은 아니다.</summary>
    public static bool IsResizeHandle(BoxHandle handle) => handle is not (BoxHandle.None or BoxHandle.Inside);
}
