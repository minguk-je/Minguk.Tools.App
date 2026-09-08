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

        screenPoint = new Point(
            targetBounds.X + targetBounds.Width * ratioX,
            targetBounds.Y + targetBounds.Height * ratioY);

        return true;
    }
}
