using System;
using System.Windows;

namespace Minguk.Tools.Capture.Input;

/// <summary>
/// 미리보기에서 누른 자리를 화면 좌표로 옮긴다. 계산만 하고 아무것도 건드리지 않는다.
///
/// 두 번 환산한다.
///   ① 미리보기 컨트롤 좌표 → 캡처 원본 픽셀
///      Image 가 Stretch=Uniform 이라 비율을 지키며 축소된다. 남는 자리는 위아래(또는 좌우)에
///      검은 띠로 남으므로, 그 띠를 빼고 실제로 그림이 그려진 영역만 놓고 환산해야 한다.
///   ② 캡처 원본 픽셀 → 화면 좌표
///      대상이 화면에서 차지하는 사각형에 비례해 얹는다.
/// </summary>
public static class PreviewInputMapper
{
    /// <summary>
    /// 미리보기 컨트롤 안의 한 점을 화면 좌표로 바꾼다.
    /// 그림 바깥(검은 띠)을 눌렀으면 false — 그건 대상 위가 아니다.
    /// </summary>
    /// <param name="pointInControl">Image 컨트롤 기준 좌표.</param>
    /// <param name="controlSize">Image 컨트롤의 실제 크기.</param>
    /// <param name="sourceSize">캡처 원본의 픽셀 크기.</param>
    /// <param name="targetBounds">대상이 화면에서 차지하는 사각형.</param>
    public static bool TryMapToScreen(
        Point pointInControl,
        Size controlSize,
        Size sourceSize,
        Rect targetBounds,
        out Point screenPoint)
    {
        screenPoint = default;

        if (controlSize.Width <= 0 || controlSize.Height <= 0 ||
            sourceSize.Width <= 0 || sourceSize.Height <= 0 ||
            targetBounds.Width <= 0 || targetBounds.Height <= 0)
        {
            return false;
        }

        // ① Uniform 으로 축소했을 때 그림이 실제로 차지하는 영역
        var scale = Math.Min(controlSize.Width / sourceSize.Width, controlSize.Height / sourceSize.Height);

        var drawnWidth = sourceSize.Width * scale;
        var drawnHeight = sourceSize.Height * scale;

        // 남는 자리는 양쪽으로 반씩 나뉜다(가운데 정렬).
        var offsetX = (controlSize.Width - drawnWidth) / 2;
        var offsetY = (controlSize.Height - drawnHeight) / 2;

        var xInDrawn = pointInControl.X - offsetX;
        var yInDrawn = pointInControl.Y - offsetY;

        // 검은 띠를 눌렀다.
        if (xInDrawn < 0 || yInDrawn < 0 || xInDrawn > drawnWidth || yInDrawn > drawnHeight)
            return false;

        // ② 그림 안에서의 비율을 그대로 대상 사각형에 얹는다.
        var ratioX = xInDrawn / drawnWidth;
        var ratioY = yInDrawn / drawnHeight;

        screenPoint = MapRatioToScreen(new Point(ratioX, ratioY), targetBounds);

        return true;
    }

    /// <summary>
    /// Image 컨트롤 기준 좌표를 그림 안의 비율(0~1)로 바꾼다.
    /// </summary>
    /// <remarks>
    /// 클릭 전달은 검은 띠를 누른 것을 거르지만, 영역을 끌 때는 그림 밖으로 조금 나가도
    /// 가장자리로 접어 주는 편이 자연스럽다. <paramref name="clamp"/> 가 그 차이다.
    /// </remarks>
    public static bool TryMapToRatio(Point pointInControl, Size controlSize, Size sourceSize, bool clamp, out Point ratio)
    {
        ratio = default;

        if (controlSize.Width <= 0 || controlSize.Height <= 0 || sourceSize.Width <= 0 || sourceSize.Height <= 0)
            return false;

        var scale = Math.Min(controlSize.Width / sourceSize.Width, controlSize.Height / sourceSize.Height);

        var drawnWidth = sourceSize.Width * scale;
        var drawnHeight = sourceSize.Height * scale;
        var offsetX = (controlSize.Width - drawnWidth) / 2;
        var offsetY = (controlSize.Height - drawnHeight) / 2;

        var x = (pointInControl.X - offsetX) / drawnWidth;
        var y = (pointInControl.Y - offsetY) / drawnHeight;

        if (!clamp && (x < 0 || y < 0 || x > 1 || y > 1)) return false;

        ratio = new Point(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));

        return true;
    }

    /// <summary>
    /// 그림 안의 비율(0~1)을 화면 좌표로 바꾼다.
    /// </summary>
    /// <remarks>
    /// 미리보기를 거치지 않고 <b>그림 안의 자리</b>를 이미 아는 쪽이 쓴다 - 모델이 찾아낸
    /// 검출이 그렇다. 검출은 0~1 로 나오므로 컨트롤 크기도, 레터박스 여백도 알 필요가 없다.
    ///
    /// 미리보기 클릭도 결국 이 계산으로 끝나므로 한 곳에 둔다. 두 벌로 두면 한쪽만 고쳐져
    /// 손으로 누른 자리와 모델이 누른 자리가 어긋난다.
    /// </remarks>
    public static Point MapRatioToScreen(Point ratio, Rect targetBounds) => new(
        targetBounds.X + (targetBounds.Width * ratio.X),
        targetBounds.Y + (targetBounds.Height * ratio.Y));
}
