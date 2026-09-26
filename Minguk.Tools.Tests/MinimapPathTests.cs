using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Minguk.Tools.Vision.Minimap;

namespace Minguk.Tools.Tests;

/// <summary>
/// 미니맵 길찾기(<see cref="MinimapPathFinder"/>) - 실제 던전 미니맵 한 장과 만든 ㄷ자 통로로.
/// </summary>
/// <remarks>
/// 사용자(2026-09-26) "미니맵 바닥/벽을 구분해 길찾기가 좋을거 같은데" - 점 쪽으로 곧게만 걷다 던전 벽에 막혔다.
/// 실제 그림(dungeon-path.png, 캡처 52 의 미니맵 조각)은 정답 길을 모르니 「바닥만 밟고 목적지에 닿는다」 만 본다.
/// 만든 그림은 곧게 가면 벽을 지나는 ㄷ자라 돌아가는 길을 찾아야 한다.
/// </remarks>
internal static partial class Program
{
    private static void TestMinimapPath(string folder)
    {
        // 실제 던전 - 금색 테두리 다이아몬드(퀘스트 표시)까지.
        var path = Path.Combine(folder, "dungeon-path.png");

        if (File.Exists(path))
        {
            var spec = new MinimapSpec { Ignore = [[0.69, 0.84, 0.13, 0.16]] };
            var questRule = new MinimapMarkerSpec { MinRed = 150, MinGreen = 110, MaxBlue = 255, MinRedMinusBlue = 70, MinRedMinusGreen = -255, MinPixels = 30 };
            var (pixels, width, height) = LoadBgra(path);
            var arrow = MinimapReader.FindArrow(pixels, width, height, spec);
            var marks = arrow is null ? [] : MinimapReader.Markers(pixels, width, height, spec, questRule, arrow.CenterX, arrow.CenterY);

            Check("미니맵 길찾기: 던전 그림에서 화살표와 퀘스트 표시(금색 테두리 다이아몬드) 하나", arrow is not null && marks.Count == 1,
                  marks.Count > 0 ? $"방위 {marks[0].Bearing:0}도 · {marks[0].Distance:0}px" : "표시 없음");

            if (arrow is not null && marks.Count == 1)
            {
                var radians = marks[0].Bearing * Math.PI / 180;
                var goal = (arrow.CenterX + (marks[0].Distance * Math.Sin(radians)), arrow.CenterY - (marks[0].Distance * Math.Cos(radians)));
                var found = MinimapPathFinder.Find(pixels, width, height, (arrow.CenterX, arrow.CenterY), goal, 30);
                var end = found?.LastOrDefault();

                Check("미니맵 길찾기: 던전 그림에서 표시까지 길을 찾는다",
                      found is { Count: > 0 } && end is { } e && Math.Abs(e.X - goal.Item1) <= 3 && Math.Abs(e.Y - goal.Item2) <= 3,
                      found is null ? "길 없음" : $"{found.Count}칸");

                var blue = MinimapPathFinder.Find(pixels, width, height, (arrow.CenterX, arrow.CenterY), goal, 30, RoomFloorBlue);
                var blueEnd = blue?.LastOrDefault();

                Check("미니맵 길찾기: 던전 그림 - 푸른 바닥만 봐도 표시까지 길을 찾는다",
                      blue is { Count: > 0 } && blueEnd is { } b && Math.Abs(b.X - goal.Item1) <= 3 && Math.Abs(b.Y - goal.Item2) <= 3,
                      blue is null ? "길 없음" : $"{blue.Count}칸");
            }
        }
        else
        {
            Fail("미니맵 길찾기: 검사 그림", $"{path} 이 없다");
        }

        TestMinimapRoomExit(folder);

        // 만든 ㄷ자 통로 - 가운데가 벽이라 곧게 가면 벽을 지난다.
        const int W = 120, H = 80;
        var bgra = new byte[W * H * 4];

        void Floor(int x0, int y0, int x1, int y1)
        {
            for (var y = y0; y < y1; y++)
                for (var x = x0; x < x1; x++)
                {
                    var i = ((y * W) + x) * 4;
                    bgra[i] = 100; bgra[i + 1] = 95; bgra[i + 2] = 80; bgra[i + 3] = 255;
                }
        }

        Floor(10, 10, 30, 70);    // 왼쪽 세로
        Floor(90, 10, 110, 70);   // 오른쪽 세로
        Floor(10, 10, 110, 30);   // 위 가로 - 둘을 잇는 유일한 길

        var start = (20.0, 60.0);
        var goalU = (100.0, 60.0);
        var route = MinimapPathFinder.Find(bgra, W, H, start, goalU, 30);
        var floor = MinimapPathFinder.FloorGrid(bgra, W, H, 30, out var columns, out _);
        var onFloor = route is not null && route.All(p => floor[((int)(p.Y / MinimapPathFinder.CellPixels) * columns) + (int)(p.X / MinimapPathFinder.CellPixels)]);
        var wentUp = route is not null && route.Min(p => p.Y) < 30;

        Check("미니맵 길찾기: ㄷ자 통로에서 벽을 돌아간다(위 가로 통로를 지나고, 바닥만 밟는다)",
              route is { Count: > 0 } && onFloor && wentUp,
              route is null ? "길 없음" : $"{route.Count}칸 · 가장 위 y {route.Min(p => p.Y):0} · 바닥만 {onFloor}");

        // 막힌 그림 - 이어진 바닥이 없으면 null(스크립트가 곧게 가거나 비켜 가게).
        Array.Clear(bgra);
        Floor(10, 10, 30, 70);
        Floor(90, 10, 110, 70);

        Check("미니맵 길찾기: 이어진 바닥이 없으면 길이 없다(null)",
              MinimapPathFinder.Find(bgra, W, H, start, goalU, 30) is null, "왼쪽·오른쪽 세로만");
    }

    /// <summary>
    /// 좁은 방 안에서 남서쪽 통로의 표시로(캡처 53, dungeon-room.png) - 캐릭터 둘레 누런 원 안이 밝기로는 다 바닥이라
    /// 방 벽을 뚫고 곧게 가는 길이 나와 1~2초씩 제자리였다(로그 08:59). 푸른 바닥만 보면 방 아래로 나가 통로 가운데로 가야 한다.
    /// </summary>
    private static void TestMinimapRoomExit(string folder)
    {
        var path = Path.Combine(folder, "dungeon-room.png");

        if (!File.Exists(path))
        {
            Fail("미니맵 길찾기: 좁은 방 검사 그림", $"{path} 이 없다");
            return;
        }

        var spec = new MinimapSpec { Ignore = [[0.69, 0.84, 0.13, 0.16]] };
        var questRule = new MinimapMarkerSpec { MinRed = 150, MinGreen = 110, MaxBlue = 255, MinRedMinusBlue = 70, MinRedMinusGreen = -255, MinPixels = 30 };
        var (pixels, width, height) = LoadBgra(path);
        var arrow = MinimapReader.FindArrow(pixels, width, height, spec);
        var marks = arrow is null ? [] : MinimapReader.Markers(pixels, width, height, spec, questRule, arrow.CenterX, arrow.CenterY);

        if (arrow is null || marks.Count != 1)
        {
            Fail("미니맵 길찾기: 좁은 방 - 화살표와 표시 하나", $"화살표 {arrow is not null} · 표시 {marks.Count}개");
            return;
        }

        var radians = marks[0].Bearing * Math.PI / 180;
        var start = (arrow.CenterX, arrow.CenterY);
        var goal = (arrow.CenterX + (marks[0].Distance * Math.Sin(radians)), arrow.CenterY - (marks[0].Distance * Math.Cos(radians)));

        string Describe(IReadOnlyList<(double X, double Y)>? route)
            => route is null ? "길 없음" : string.Join(" ", route.Where((_, i) => i % 4 == 0).Select(p => $"({p.X:0},{p.Y:0})"));

        // 밝기만 - 누런 원 안이 다 바닥이라 방 벽을 뚫는다(고치기 전 그대로).
        var byBrightness = MinimapPathFinder.Find(pixels, width, height, start, goal, 30);
        var route = MinimapPathFinder.Find(pixels, width, height, start, goal, 30, RoomFloorBlue);

        // 방은 화살표 둘레 좁은 세로 칸(가로 ±8px)이고 바닥 쪽으로만 통로와 이어진다 - 방 칸을 벗어날 때 화살표보다 이만큼 아래여야.
        double LeaveDepth(IReadOnlyList<(double X, double Y)> r)
            => r.FirstOrDefault(p => Math.Abs(p.X - start.CenterX) > 8) is { } leave && leave != default ? leave.Y - start.CenterY : double.NaN;

        Console.WriteLine($"  화살표 ({start.CenterX:0},{start.CenterY:0}) · 표시 ({goal.Item1:0},{goal.Item2:0}) {marks[0].Bearing:0}도 {marks[0].Distance:0}px");
        Console.WriteLine($"  밝기만: {Describe(byBrightness)}");
        Console.WriteLine($"  푸른 바닥: {Describe(route)}");

        var end = route?.LastOrDefault();

        Check("미니맵 길찾기: 좁은 방 - 푸른 바닥만 밟아 표시까지 간다",
              route is { Count: > 0 } && end is { } e && Math.Abs(e.X - goal.Item1) <= 3 && Math.Abs(e.Y - goal.Item2) <= 3,
              route is null ? "길 없음" : $"{route.Count}칸");
        Check("미니맵 길찾기: 좁은 방 - 방 아래로 나간다(방 칸을 벗어날 때 화살표보다 14px 넘게 아래, 밝기만은 10px 에서 벽을 뚫었다)",
              route is not null && LeaveDepth(route) > 14,
              route is null ? "길 없음" : $"벗어날 때 {LeaveDepth(route):0}px 아래 (밝기만: {(byBrightness is null ? "길 없음" : $"{LeaveDepth(byBrightness):0}px")})");
    }

    /// <summary>아이온2 던전 바닥의 B − R 최소 - 퀘스트 프로젝트 minimap.json 의 floorMinBlueMinusRed 와 같게.</summary>
    private const int RoomFloorBlue = 4;
}
