using System;
using System.Windows;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 검출 사각형에서 머리 위 이름표가 있을 자리를 잡는다.
/// </summary>
/// <remarks>
/// 게임은 대개 검출 바로 위에 이름·체력 바를 띄운다. 검출은 몸통만 감싸므로 그 위를 따로 잘라야
/// 글자가 들어온다. 사각형 높이의 45%만큼 위로, 너비는 1.8배(이름이 몸통보다 길다)로 잡고
/// 사각형 위쪽 5% 는 겹치게 둔다 - 라벨이 검출 정수리에 살짝 걸치는 경우가 많다.
/// 값은 오버워치 사격장 봇으로 맞춘 것이고, 다른 게임이면 여기 숫자만 손본다.
///
/// 화면과 떼어 순수하게 둔다. `--vision` 이 숫자로 본다.
/// </remarks>
public static class NameplateRegion
{
    /// <summary>사각형 높이에 대한 이름표 칸의 높이.</summary>
    public const double HeightRatio = 0.45;

    /// <summary>사각형 너비에 대한 이름표 칸의 너비.</summary>
    public const double WidthRatio = 1.8;

    /// <summary>사각형 위쪽으로 이만큼 겹친다(높이 비율).</summary>
    public const double Overlap = 0.05;

    /// <summary>검출 사각형(0~1) 위의 이름표 자리(0~1). 화면 밖으로는 안 나간다. 너무 작으면 비어 있다.</summary>
    public static Rect Above(LabelBox box)
    {
        var width = box.Width * WidthRatio;
        var height = box.Height * HeightRatio;

        var left = Math.Clamp(box.CenterX - (width / 2), 0, 1);
        var right = Math.Clamp(box.CenterX + (width / 2), 0, 1);
        var bottom = Math.Clamp(box.Top + (box.Height * Overlap), 0, 1);
        var top = Math.Clamp(bottom - height, 0, 1);

        if (right - left < 0.005 || bottom - top < 0.005) return Rect.Empty;

        return new Rect(left, top, right - left, bottom - top);
    }
}
