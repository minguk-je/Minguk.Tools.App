using System;

namespace Minguk.Tools.Vision.HealthBars;

/// <summary>체력바를 읽은 결과 - 찬 몫(0~1)과 바로 본 줄의 자리(조각 안 픽셀).</summary>
public readonly record struct HealthBarReading(double Fraction, int Row, int Start, int End);

/// <summary>
/// 체력바 읽기 - 모든 게임이 같은 규칙이고, 색·범위만 <see cref="HealthBarSpec"/> 으로 받는다.
/// </summary>
/// <remarks>
/// 조각(검출 위 띠)에서 찬 칸·빈 칸 색에 가까운 픽셀이 가로로 이어진 토막(칸 사이 틈은 <see cref="HealthBarSpec.Gap"/> 까지 이어 본다) 가운데
/// 찬 칸이 가장 많은 것을 바로 보고, 그 줄에서 찬 칸의 몫을 잰다. 찬 칸이 없으면 모른다(null). 색은 기준색과의 거리로 가른다 - 게임마다 다른 것은 기준색뿐이다.
/// </remarks>
public static class HealthBarReader
{
    /// <summary>
    /// Bgra32 조각에서 읽는다. 바를 못 찾으면 null.
    /// </summary>
    /// <param name="minRun">바로 볼 가장 짧은 길이(px) - 부르는 쪽이 몸 너비 × <see cref="HealthBarSpec.MinWidth"/> 로 준다.</param>
    public static HealthBarReading? Read(byte[] bgra, int width, int height, HealthBarSpec spec, int minRun)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;

        var tolerance = spec.Tolerance * spec.Tolerance;
        var hasEmpty = spec.Empty.Length == 3;

        // 1 = 찬 칸, 2 = 빈 칸, 0 = 그 밖. 둘 다 가까우면 더 가까운 쪽.
        var kinds = new byte[width * height];

        for (var i = 0; i < kinds.Length; i++)
        {
            int b = bgra[i * 4], g = bgra[(i * 4) + 1], r = bgra[(i * 4) + 2];

            var toFilled = Distance(r, g, b, spec.Filled);
            var toEmpty = hasEmpty ? Distance(r, g, b, spec.Empty) : double.MaxValue;

            if (toFilled <= tolerance && toFilled <= toEmpty) kinds[i] = 1;
            else if (toEmpty <= tolerance) kinds[i] = 2;
        }

        // 줄 고르기 - 바로 볼 만큼 긴 토막 가운데 찬 칸이 가장 많은 것(같으면 더 긴 것). 가장 긴 줄만 고르면 바 위 테두리나 벽의 어두운 줄이
        // 빈 칸 색에 가까워 먼저 뽑혔다(사격장 진단 조각 224장이 전부 0 으로 읽힘, 2026-09-19).
        int bestRow = -1, bestStart = 0, bestEnd = 0, bestFilled = -1, bestLength = 0;
        var need = Math.Max(4, minRun);

        for (var y = 0; y < height; y++)
        {
            int start = -1, last = -1;

            void Consider(int s, int e)
            {
                var length = e - s + 1;
                if (length < need) return;

                var filled = 0;
                for (var x = s; x <= e; x++) if (kinds[(y * width) + x] == 1) filled++;

                if (filled > bestFilled || (filled == bestFilled && length > bestLength))
                {
                    bestFilled = filled;
                    bestLength = length;
                    bestRow = y;
                    bestStart = s;
                    bestEnd = e;
                }
            }

            for (var x = 0; x < width; x++)
            {
                if (kinds[(y * width) + x] == 0) continue;

                if (start < 0 || x - last > spec.Gap)
                {
                    if (start >= 0) Consider(start, last);
                    start = x;
                }

                last = x;
            }

            if (start >= 0) Consider(start, last);
        }

        // 찬 칸이 하나도 없으면 "모름" - 빈 바와 바가 없는 화면(멀거나 안 맞은 봇은 바를 안 띄운다)을 가를 수 없고, 체력 0 이면 봇은 이미 사라진다.
        // 0 으로 주면 쏜 뒤 "그대로" 로 보여 빗나감으로 세졌다.
        if (bestRow < 0 || bestFilled <= 0) return null;

        // 찬 몫은 고른 줄 하나에서 - 위아래 줄까지 세면 빈 칸 색과 비슷한 테두리 줄이 섞여 찬 몫이 낮게 나왔다(0.7 → 0.45).
        int filledCount = 0, emptyCount = 0;

        for (var x = bestStart; x <= bestEnd; x++)
        {
            var k = kinds[(bestRow * width) + x];
            if (k == 1) filledCount++;
            else if (k == 2) emptyCount++;
        }

        return new HealthBarReading(filledCount / (double)(filledCount + emptyCount), bestRow, bestStart, bestEnd);
    }

    private static double Distance(int r, int g, int b, int[] rgb)
    {
        double dr = r - rgb[0], dg = g - rgb[1], db = b - rgb[2];
        return (dr * dr) + (dg * dg) + (db * db);
    }
}
