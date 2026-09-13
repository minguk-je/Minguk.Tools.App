using System;
using System.Windows;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 이름 붙인 자리를 미리보기 캔버스에 놓고 끌 때의 계산. 화면과 떼어 둔다.
/// </summary>
/// <remarks>
/// 자리는 <b>0~1 비율</b>로 저장되고, 캔버스 위의 항목은 <b>그림이 놓인 자리(픽셀)</b> 로 놓인다. 그림은
/// <c>Stretch=Uniform</c> 이라 컨트롤 가운데에 비율을 지켜 놓이므로, 두 좌표계 사이 환산이 여기 한 곳에만 있다
/// (<see cref="Capture.Input.PreviewInputMapper"/> 와 같은 셈). 끌기는 Thumb 이 주는 변위(dx, dy)를 받아 새 사각형을
/// 돌려주는 순수 함수라 <c>--vision</c> 이 마우스 없이 숫자로 본다.
/// </remarks>
public static class RegionGeometry
{
    /// <summary>그림이 실제로 놓인 자리. 컨트롤보다 그림 비율이 넓으면 위아래, 좁으면 좌우에 띠가 남는다.</summary>
    public static Rect ImageArea(Size control, Size source)
    {
        if (control.Width <= 0 || control.Height <= 0 || source.Width <= 0 || source.Height <= 0) return Rect.Empty;

        var scale = Math.Min(control.Width / source.Width, control.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;

        return new Rect((control.Width - width) / 2, (control.Height - height) / 2, width, height);
    }

    /// <summary>0~1 자리 → 캔버스 픽셀.</summary>
    public static Rect ToCanvas(Rect ratio, Rect area) => new(
        area.X + (ratio.X * area.Width),
        area.Y + (ratio.Y * area.Height),
        ratio.Width * area.Width,
        ratio.Height * area.Height);

    /// <summary>캔버스 픽셀 → 0~1 자리. 그림 밖으로 나간 것은 가장자리로 접는다.</summary>
    public static Rect ToRatio(Rect canvas, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0) return Rect.Empty;

        var x = Math.Clamp((canvas.X - area.X) / area.Width, 0, 1);
        var y = Math.Clamp((canvas.Y - area.Y) / area.Height, 0, 1);
        var width = Math.Clamp(canvas.Width / area.Width, 0, 1 - x);
        var height = Math.Clamp(canvas.Height / area.Height, 0, 1 - y);

        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// 통째로 민다. 크기는 그대로고, 그림 밖으로는 안 나간다.
    /// </summary>
    /// <remarks>가장자리에서 더 밀면 <b>멈춘다</b>. 잘라 내면 크기가 줄어 옮기려던 것이 찌그러진다.</remarks>
    public static Rect Move(Rect item, double dx, double dy, Rect area)
    {
        var left = Math.Clamp(item.X + dx, area.Left, Math.Max(area.Left, area.Right - item.Width));
        var top = Math.Clamp(item.Y + dy, area.Top, Math.Max(area.Top, area.Bottom - item.Height));

        return new Rect(left, top, item.Width, item.Height);
    }

    /// <summary>
    /// 잡은 변(손잡이의 정렬)을 변위만큼 끈다. 반대편은 그대로다.
    /// </summary>
    /// <remarks>
    /// 참조한 ResizeThumb 과 같은 셈이되 회전은 없다. 반대편 변을 지나 끌지는 못한다 - 최소 크기에서 <b>멈춘다</b>.
    /// 뒤집히게 두면 손잡이가 반대편으로 옮겨 가 사람이 잡고 있던 변이 바뀐다. 그림 밖으로도 안 나간다.
    /// <c>Stretch</c>·<c>Center</c> 인 축은 안 건드린다 - 변 가운데 손잡이는 한 축만 끈다.
    /// </remarks>
    public static Rect Resize(Rect item, HorizontalAlignment horizontal, VerticalAlignment vertical, double dx, double dy, Size minimum, Rect area)
    {
        var left = item.Left;
        var top = item.Top;
        var right = item.Right;
        var bottom = item.Bottom;

        switch (horizontal)
        {
            case HorizontalAlignment.Left:
                left = Math.Clamp(left + dx, area.Left, right - minimum.Width);
                break;
            case HorizontalAlignment.Right:
                right = Math.Clamp(right + dx, left + minimum.Width, area.Right);
                break;
        }

        switch (vertical)
        {
            case VerticalAlignment.Top:
                top = Math.Clamp(top + dy, area.Top, bottom - minimum.Height);
                break;
            case VerticalAlignment.Bottom:
                bottom = Math.Clamp(bottom + dy, top + minimum.Height, area.Bottom);
                break;
        }

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
