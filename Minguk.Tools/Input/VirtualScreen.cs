using System;
using System.Runtime.InteropServices;

namespace Minguk.Tools.Input;

/// <summary>
/// 모든 모니터를 감싸는 가상 화면. 절대 좌표 환산을 여기 모아 둔다.
///
/// 왜 한곳에 두는가
///   절대 좌표를 쓰는 경로가 둘(SendInput, Interception)이고 둘 다 같은 환산이 필요하다.
///   각자 계산하면 한쪽만 고쳐지는 일이 생긴다.
/// </summary>
public static class VirtualScreen
{
    /// <param name="Left">주 모니터보다 왼쪽에 있는 모니터가 있으면 음수다.</param>
    public readonly record struct Bounds(int Left, int Top, int Width, int Height)
    {
        public int Right => Left + Width;

        public int Bottom => Top + Height;
    }

    /// <summary>모니터 구성은 실행 중에도 바뀌므로 매번 조회한다.</summary>
    public static Bounds GetBounds() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>화면 픽셀 좌표를 0~65535 정규화 좌표로 바꾼다.</summary>
    /// <returns>가상 화면 크기를 읽지 못하면 false.</returns>
    public static bool TryNormalize(int screenX, int screenY, out int normalizedX, out int normalizedY)
    {
        var bounds = GetBounds();

        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            (normalizedX, normalizedY) = (0, 0);
            return false;
        }

        normalizedX = Normalize(screenX - bounds.Left, bounds.Width);
        normalizedY = Normalize(screenY - bounds.Top, bounds.Height);
        return true;
    }

    /// <summary>
    /// 화면 안 위치(0 기준 오프셋)를 0~65535 로 편다.
    /// </summary>
    /// <remarks>
    /// +0.5 를 더하는 이유
    ///   Windows 는 받은 값을 되돌릴 때 <b>내림</b>한다. 목표 픽셀에 정확히 대응하는 값을 그대로
    ///   보내면 내림 때문에 한 칸 앞 픽셀로 떨어진다. 목표 픽셀이 차지하는 구간의 한가운데를
    ///   보내야 되돌렸을 때 그 픽셀이 나온다.
    ///
    ///   실측으로 확인한 값이다. +0.5 없이 <c>offset * 65535 / (size - 1)</c> 만 쓰면
    ///   화면 여러 곳에서 1px 씩 밀린다.
    ///
    /// 가장자리에서는 1px 로 끝나지 않는다
    ///   모니터를 어긋나게 배치하면 가상 화면 사각형 안에 어느 모니터에도 속하지 않는 빈 공간이
    ///   생긴다. 1px 밀려 그곳으로 떨어지면 Windows 가 커서를 가장 가까운 화면으로 되돌려
    ///   엉뚱한 모니터로 튕겨 나간다.
    /// </remarks>
    public static int Normalize(int offset, int size)
    {
        if (size <= 1) return 0;

        var normalized = (int)Math.Round((offset + 0.5) * 65535.0 / (size - 1));

        return Math.Clamp(normalized, 0, 65535);
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
