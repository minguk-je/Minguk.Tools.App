using System;
using System.Collections.Generic;

namespace Minguk.Tools.Vision.Minimap;

/// <summary>
/// 미니맵 위에서 바닥(밝은 칸)만 밟아 한 점에서 다른 점까지 가는 길 - 던전 통로처럼 곧게 못 가는 곳.
/// </summary>
/// <remarks>
/// 사용자(2026-09-26) "미니맵 바닥/벽을 구분해 길찾기가 좋을거 같은데" - 점 쪽으로 곧게만 걷던 <c>마커로걷기</c> 가
/// 던전 벽에 막혀 40초 동안 퀘스트 표시까지 거리가 44~48px 그대로였다(로그 08:06).
///
/// 아이온2 던전 미니맵 실측(캡처 29·30·52): 바닥은 푸르스름한 회색(밝기 55~100), 벽·바깥은 어두운 남색(11~17).
/// 캐릭터 둘레의 밝은 원과 시야 부채꼴은 그 아래를 가려 바닥으로 본다(모르는 곳은 가 보고, 막히면 다시 찾는다).
/// 청록 거리 원·금색 테두리 같은 가는 선은 열림(깎고 다시 불리기)으로 지운다 - 안 지우면 벽을 가로지르는 가짜 길이 된다.
/// 벽에서 떨어진 칸을 좋아하게 값을 매겨 벽에 붙어 걷다 걸리지 않게 한다.
/// </remarks>
public static class MinimapPathFinder
{
    /// <summary>칸 한 변(px) - 미니맵 1080p 가 246×164 라 123×82 칸.</summary>
    public const int CellPixels = 2;

    /// <summary>캐릭터·목적지 둘레 이만큼(칸)은 늘 바닥으로 본다 - 화살표·표시 그림이 바닥을 가린다.</summary>
    private const int ForcedRadius = 3;

    /// <summary>목적지 둘레 억지 바닥(칸) - 퀘스트 표시(금색 테두리 다이아몬드, 1080p 약 22px)가 통로 바닥을 가려 길이 끊겼다(캡처 53).</summary>
    private const int GoalForcedRadius = 6;

    /// <summary>벽에서 가까울수록 더하는 값의 세기 - 값 = 세기 ÷ 벽까지 칸².</summary>
    /// <remarks>
    /// 4 ÷ (칸+1) 이던 때는 통로 가운데와 가장자리 값 차이가 작아 길이 벽 쪽으로 붙었고, 미니맵에 안 나오는 장애물(무너진 기둥·잔해)에
    /// 걸려 못 지나갔다(사용자, 2026-09-26 "장애물이 있어서 못지나가고 있었어.. 통로 중앙으로 가게 해야 할거 같아", 캡처 53).
    /// </remarks>
    private const double WallPenalty = 24;

    /// <summary>벽에서 이만큼(칸) 떨어진 칸만 먼저 밟아 본다 - 길이 없으면 한 칸씩 줄인다(좁은 방·문).</summary>
    private static readonly int[] MinClearances = [4, 3, 2, 1];

    /// <summary>
    /// 바닥만 밟는 길(미니맵 조각 안 px, 시작 다음 칸부터 목적지까지). 못 찾으면 null.
    /// </summary>
    /// <param name="bgra">미니맵 조각(BGRA32).</param>
    /// <param name="floorMinBrightness">이만큼 넘게 밝은 칸이 바닥.</param>
    /// <param name="floorMinBlueMinusRed">칸 평균 B − R 이 이만큼 넘어야 바닥(푸른 바닥만) - 기본 −255 는 안 본다.</param>
    public static IReadOnlyList<(double X, double Y)>? Find(byte[] bgra, int width, int height,
                                                            (double X, double Y) start, (double X, double Y) goal, int floorMinBrightness,
                                                            int floorMinBlueMinusRed = -255)
    {
        ArgumentNullException.ThrowIfNull(bgra);

        var floor = FloorGrid(bgra, width, height, floorMinBrightness, out var columns, out var rows, floorMinBlueMinusRed);

        if (columns == 0 || rows == 0) return null;

        var (sx, sy) = ToCell(start, columns, rows);
        var (gx, gy) = ToCell(goal, columns, rows);

        Force(floor, columns, rows, sx, sy, ForcedRadius);
        Force(floor, columns, rows, gx, gy, GoalForcedRadius);

        var clearance = Clearance(floor, columns, rows);

        // 벽에서 넉넉히 떨어진 칸으로만 갈 수 있으면 그 길 - 시작·목적지 둘레(억지 바닥)는 늘 밟는다.
        foreach (var minimum in MinClearances)
        {
            var route = Search(floor, clearance, columns, rows, (sx, sy), (gx, gy), minimum);

            if (route is not null) return route;
        }

        return null;
    }

    /// <summary>바닥 칸 - 칸 평균 밝기가 기준 넘으면(푸른 기준을 주면 B − R 도). 가는 선은 열림으로 지운다.</summary>
    /// <remarks>
    /// 푸른 기준(아이온2 던전, 캡처 53): 캐릭터 둘레의 밝은 원은 누런색이라 밝기로만 보면 원 안이 다 바닥이 되어, 좁은 방 벽을 뚫고
    /// 표시 쪽으로 곧게 가는 길이 나왔다(로그 08:59 - 남서로 걷다 1~2초씩 제자리). 원 밑에서도 실제 바닥은 푸르스름하게 남는다
    /// (방 안 46,70,79 · 원 속 바깥 33,37,33 · 145,130,90).
    /// </remarks>
    public static bool[] FloorGrid(byte[] bgra, int width, int height, int floorMinBrightness, out int columns, out int rows,
                                   int floorMinBlueMinusRed = -255)
    {
        columns = width / CellPixels;
        rows = height / CellPixels;

        var raw = new bool[columns * rows];

        for (var cy = 0; cy < rows; cy++)
        {
            for (var cx = 0; cx < columns; cx++)
            {
                double sum = 0;
                double blueMinusRed = 0;
                var count = 0;

                for (var y = cy * CellPixels; y < (cy + 1) * CellPixels && y < height; y++)
                {
                    for (var x = cx * CellPixels; x < (cx + 1) * CellPixels && x < width; x++)
                    {
                        var i = ((y * width) + x) * 4;

                        if (i + 2 >= bgra.Length) continue;

                        sum += (0.114 * bgra[i]) + (0.587 * bgra[i + 1]) + (0.299 * bgra[i + 2]);
                        blueMinusRed += bgra[i] - bgra[i + 2];
                        count++;
                    }
                }

                raw[(cy * columns) + cx] = count > 0 && sum / count >= floorMinBrightness && blueMinusRed / count >= floorMinBlueMinusRed;
            }
        }

        // 열림 - 한 칸 깎고(8이웃이 다 바닥이어야 남는다) 한 칸 다시 불린다. 2~3px 선은 사라지고 넓은 바닥은 그대로다.
        return Dilate(Erode(raw, columns, rows), columns, rows);
    }

    private static bool[] Erode(bool[] grid, int columns, int rows)
    {
        var result = new bool[grid.Length];

        for (var y = 1; y < rows - 1; y++)
        {
            for (var x = 1; x < columns - 1; x++)
            {
                var keep = true;

                for (var dy = -1; dy <= 1 && keep; dy++)
                    for (var dx = -1; dx <= 1 && keep; dx++)
                        keep = grid[((y + dy) * columns) + x + dx];

                result[(y * columns) + x] = keep;
            }
        }

        return result;
    }

    private static bool[] Dilate(bool[] grid, int columns, int rows)
    {
        var result = new bool[grid.Length];

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                if (!grid[(y * columns) + x]) continue;

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;

                        if (nx >= 0 && ny >= 0 && nx < columns && ny < rows) result[(ny * columns) + nx] = true;
                    }
                }
            }
        }

        return result;
    }

    private static (int X, int Y) ToCell((double X, double Y) point, int columns, int rows)
        => (Math.Clamp((int)(point.X / CellPixels), 0, columns - 1), Math.Clamp((int)(point.Y / CellPixels), 0, rows - 1));

    private static void Force(bool[] floor, int columns, int rows, int cx, int cy, int radius)
    {
        for (var y = Math.Max(0, cy - radius); y <= Math.Min(rows - 1, cy + radius); y++)
            for (var x = Math.Max(0, cx - radius); x <= Math.Min(columns - 1, cx + radius); x++)
                floor[(y * columns) + x] = true;
    }

    /// <summary>칸마다 가장 가까운 벽까지(칸, 4이웃 걸음) - 벽에서 떨어진 길을 좋아하게 한다.</summary>
    private static int[] Clearance(bool[] floor, int columns, int rows)
    {
        var distance = new int[floor.Length];
        var queue = new Queue<int>();

        for (var i = 0; i < floor.Length; i++)
        {
            if (floor[i])
            {
                distance[i] = int.MaxValue;
            }
            else
            {
                distance[i] = 0;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            var x = i % columns;
            var y = i / columns;

            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (nx < 0 || ny < 0 || nx >= columns || ny >= rows) continue;

                var n = (ny * columns) + nx;

                if (distance[n] <= distance[i] + 1) continue;

                distance[n] = distance[i] + 1;
                queue.Enqueue(n);
            }
        }

        return distance;
    }

    /// <summary>다익스트라(8이웃). 한 걸음 값 = 거리 × (1 + 벽가까움 값). 벽까지 <paramref name="minClearance"/> 칸 안 되는 칸은 안 밟는다(시작·목적지 둘레는 밟는다).</summary>
    private static IReadOnlyList<(double X, double Y)>? Search(bool[] floor, int[] clearance, int columns, int rows,
                                                                (int X, int Y) start, (int X, int Y) goal, int minClearance)
    {
        bool Walkable(int index)
        {
            if (!floor[index]) return false;
            if (clearance[index] >= minClearance) return true;

            var x = index % columns;
            var y = index / columns;

            return (Math.Abs(x - start.X) <= ForcedRadius && Math.Abs(y - start.Y) <= ForcedRadius)
                || (Math.Abs(x - goal.X) <= GoalForcedRadius && Math.Abs(y - goal.Y) <= GoalForcedRadius);
        }

        var total = floor.Length;
        var cost = new double[total];
        var previous = new int[total];

        Array.Fill(cost, double.MaxValue);
        Array.Fill(previous, -1);

        var startIndex = (start.Y * columns) + start.X;
        var goalIndex = (goal.Y * columns) + goal.X;
        var open = new PriorityQueue<int, double>();

        cost[startIndex] = 0;
        open.Enqueue(startIndex, 0);

        while (open.TryDequeue(out var current, out var currentCost))
        {
            if (currentCost > cost[current]) continue;
            if (current == goalIndex) break;

            var x = current % columns;
            var y = current / columns;

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    var nx = x + dx;
                    var ny = y + dy;

                    if (nx < 0 || ny < 0 || nx >= columns || ny >= rows) continue;

                    var next = (ny * columns) + nx;

                    if (!Walkable(next)) continue;

                    // 대각선은 모서리를 긁지 않게 옆 두 칸도 바닥이어야
                    if (dx != 0 && dy != 0 && (!Walkable((y * columns) + nx) || !Walkable((ny * columns) + x))) continue;

                    var wall = Math.Max(1, Math.Min(clearance[next], 1000));
                    var step = (dx != 0 && dy != 0 ? Math.Sqrt(2) : 1) * (1 + (WallPenalty / ((double)wall * wall)));
                    var nextCost = cost[current] + step;

                    if (nextCost >= cost[next]) continue;

                    cost[next] = nextCost;
                    previous[next] = current;
                    open.Enqueue(next, nextCost);
                }
            }
        }

        if (previous[goalIndex] < 0 && goalIndex != startIndex) return null;

        var path = new List<(double X, double Y)>();

        for (var i = goalIndex; i != startIndex && i >= 0; i = previous[i])
            path.Add((((i % columns) + 0.5) * CellPixels, ((i / columns) + 0.5) * CellPixels));

        path.Reverse();
        return path;
    }
}
