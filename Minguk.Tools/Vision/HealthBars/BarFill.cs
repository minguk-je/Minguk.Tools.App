using System;

namespace Minguk.Tools.Vision.HealthBars;

/// <summary>
/// 자리에 놓인 막대(대상 체력바·마나·게이지)가 몇 할 찼나(0~1) - 왼쪽부터 <b>색이 있는 칸</b>이 이어진 길이.
/// </summary>
/// <remarks>
/// 사용자(2026-09-25) "체력바로 확인해줘" - 대상 이름만 보고 공격하던 것을 대상 체력바로도 확인한다.
/// 게임마다 막대 색은 달라도 <b>찬 칸은 색이 있고 빈 칸은 회색·검정</b>인 것은 같다(아이온2 대상 체력바 실측: 찬 칸 초록 R−B 60~100, 빈 칸 49,49,49).
/// 그래서 기준색 없이 색의 퍼짐(R·G·B 가장 큰 것 − 가장 작은 것)으로 가른다. 검출 머리 위 체력바는 자리가 매번 달라 기준색으로 찾는다 - <see cref="HealthBarReader"/>.
///
/// 칸 하나는 가운데 줄들(위아래 1/4 뺀) 가운데 절반 넘게 색이 있으면 찬 것으로 본다 - 막대 테두리·위아래 반짝임에 안 흔들린다.
/// 막대 한가운데 문양·오른쪽 끝 장식도 색이 있지만 찬 칸과 떨어져 있어, 빈 칸이 <c>maxGap</c>(너비 비율) 넘게 이어지면 거기서 끊는다.
/// 왼쪽 끝부터 빈 칸이면 0.
/// </remarks>
public static class BarFill
{
    /// <param name="bgra">막대 자리 조각(BGRA32).</param>
    /// <param name="minSpread">색이 있다고 볼 퍼짐. 회색(49,49,49)은 0, 아이온2 초록은 55~100.</param>
    /// <param name="maxGap">빈 칸이 너비의 이만큼 넘게 이어지면 막대가 거기서 끝난 것으로 본다.</param>
    public static double Measure(byte[] bgra, int width, int height, int minSpread = 40, double maxGap = 0.06)
    {
        ArgumentNullException.ThrowIfNull(bgra);

        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return 0;

        var top = height / 4;
        var bottom = Math.Max(top + 1, height - (height / 4));
        var rows = bottom - top;
        var allowedGap = Math.Max(1, (int)Math.Round(width * maxGap));
        var lastFilled = -1;
        var gap = 0;

        for (var x = 0; x < width; x++)
        {
            var colored = 0;

            for (var y = top; y < bottom; y++)
            {
                var i = ((y * width) + x) * 4;
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];

                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) >= minSpread) colored++;
            }

            if (colored * 2 > rows)
            {
                lastFilled = x;
                gap = 0;
            }
            else if (++gap > allowedGap)
            {
                break;
            }
        }

        return (lastFilled + 1) / (double)width;
    }
}
