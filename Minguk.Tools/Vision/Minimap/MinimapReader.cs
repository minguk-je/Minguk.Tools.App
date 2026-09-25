using System;
using System.Collections.Generic;
using System.Linq;

namespace Minguk.Tools.Vision.Minimap;

/// <summary>미니맵에서 뽑은 화살표 - 마스크(무게중심 기준 상대 좌표)와 그 조각 안 자리.</summary>
/// <param name="Mask">화살표 픽셀. 무게중심을 원점으로 옮겨 둔 것이라 그대로 돌려 견줄 수 있다.</param>
/// <param name="CenterX">조각 안 무게중심(px).</param>
/// <param name="CenterY">조각 안 무게중심(px).</param>
/// <param name="HeadAngle">모양만으로 잰 머리 방향(도, 북 0·시계 방향). 익히기가 기준각으로 쓴다.</param>
/// <param name="HeadMargin">머리 쪽이 다른 갈래보다 몇 배 무거운가. 1 에 가까우면 어느 쪽이 머리인지 애매하다(실측 1.13~1.25).</param>
public sealed record MinimapArrow(
    IReadOnlyList<(short X, short Y)> Mask,
    double CenterX,
    double CenterY,
    double HeadAngle,
    double HeadMargin);

/// <summary>미니맵 위 목표 마커 하나 - 화살표에서 본 방위와 거리.</summary>
/// <param name="Bearing">북 0·시계 방향(도).</param>
/// <param name="Distance">미니맵 픽셀. 실제 거리는 미니맵 배율에 달렸다 - 가까운지 먼지를 견주는 데 쓴다.</param>
/// <param name="Pixels">뭉치 크기(px).</param>
public sealed record MinimapMarker(double Bearing, double Distance, int Pixels);

/// <summary>
/// 미니맵 조각에서 캐릭터가 향한 방위와 목표 마커를 읽는다 - 색 분리 + 회전 본보기 대조.
/// </summary>
/// <remarks>
/// <b>왜 이것인가</b>(사용자, 2026-09-23 "미니맵으로 방향 보면서 처리가 가능해?") - 게임에 자동 이동이 있지만
/// 「자동 이동할 수 없는 퀘스트」가 있다. 검출·OCR·본보기 대조는 「화면 어디에 무엇이 있나」를 답하지
/// <b>지금 어느 쪽을 보고 있나</b>는 답하지 못한다.
///
/// <b>화살표는 캐릭터 몸 방향이다</b>(실측 2026-09-23: 걸으면 56° 돌고, 마우스만 돌리면 0°). 카메라 방향은
/// 미니맵에 없다 - 밝은 부채꼴이 후보였으나 한 장에서 둘로 갈라졌고 지도의 통로와 정렬돼 있었다.
/// 그래도 된다: 알고 싶은 것은 「W 를 누르면 어디로 가나」이고, W 를 누르면 몸이 카메라 쪽으로 돌아 그 답이 된다.
///
/// <b>각도는 회전 본보기 대조로</b> 낸다. 무게중심에서 가장 먼 픽셀을 머리로 보는 방법은 <b>틀린다</b> -
/// 이 아이콘은 꼬리가 머리보다 길다(필드 한 장에서는 맞는 듯했으나 던전에서 깨졌다). 본보기를 1°씩 360번 돌려
/// 겹침이 가장 큰 각을 고르면 서로 다른 기준 셋으로 재도 303·304·304° 로 <b>±1~2°</b> 안에 든다.
/// 65픽셀 × 360각 ≈ 2만 번이라 매 프레임 돌려도 무시할 수준이다.
/// </remarks>
public static class MinimapReader
{
    /// <summary>각도를 이만큼씩 돌려 본다(도). 1° 면 잰 정밀도(±1~2°)를 더 잘게 나눌 까닭이 없다.</summary>
    private const int RotationStep = 1;

    /// <summary>머리를 가릴 때 한 방향으로 볼 부챗살(±도). 35° 가 갈래 하나를 담고 이웃 갈래는 안 담는다(실측).</summary>
    private const double HeadWindow = 35;

    /// <summary>머리와 견줄 갈래는 이만큼(도) 넘게 떨어진 것만 - 머리 옆구리를 제 경쟁자로 세지 않는다.</summary>
    private const double HeadRivalGap = 60;

    /// <summary>화살표를 이을 때 이만큼(px) 떨어진 것까지 한 뭉치로 본다.</summary>
    /// <remarks>
    /// 꼬리 <b>끝</b>이 안티앨리어싱으로 흐려져 색 문턱을 아슬아슬하게 넘나든다 - 여덟 이웃만으로 이으면 끝 2~3px 이
    /// 떨어져 나가고(실측: 71px 이 68+2+1 로), 그 3px 이 갈래 길이를 11.6 → 8.5 로 줄여 <b>머리를 잘못 가리게 했다</b>.
    /// 마커는 이 틈이 필요 없고 가까운 둘이 붙을 수 있어 여덟 이웃 그대로 본다.
    /// </remarks>
    private const int ArrowJoinPixels = 2;

    /// <summary>
    /// 조각 한가운데에서 화살표를 찾는다. 색으로 고른 뒤 <b>가장 큰 연결 뭉치</b>만 남긴다 - 지도 선·글자 조각이 걸려도 버린다.
    /// </summary>
    /// <param name="bgra">미니맵 자리 조각(BGRA32).</param>
    /// <param name="width">조각 너비(px).</param>
    /// <param name="height">조각 높이(px).</param>
    /// <param name="spec">색 규칙·네모 크기.</param>
    /// <returns>못 찾으면(픽셀이 너무 적으면) null.</returns>
    public static MinimapArrow? FindArrow(byte[] bgra, int width, int height, MinimapSpec spec)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentNullException.ThrowIfNull(spec);

        var half = Math.Max(4, spec.Box / 2);
        var cx = width / 2;
        var cy = height / 2;
        var left = Math.Max(0, cx - half);
        var top = Math.Max(0, cy - half);
        var right = Math.Min(width, cx + half);
        var bottom = Math.Min(height, cy + half);

        var picked = new List<(short X, short Y)>();

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var i = ((y * width) + x) * 4;

                if (i + 2 >= bgra.Length) continue;

                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];

                if (Math.Max(r, Math.Max(g, b)) < spec.MinBrightness) continue;
                if (r - b >= spec.MaxRedMinusBlue) continue;

                picked.Add(((short)x, (short)y));
            }
        }

        // 화살표는 늘 미니맵 한가운데다 - 12px 넘는 뭉치 가운데 한가운데에 가장 가까운 것. 가장 큰 뭉치를 고르던 때는
        // 아이온2 에서 화살표(58px) 바로 옆 흰 깃털 아이콘(82·74px, 한가운데서 19px)을 골라 방위·몹 방향이 다 틀렸다(실측 2026-09-25).
        // 화살표는 실제 게임에서 58~71px 로 잡힌다. 12px 보다 작으면 지도 선·글자 조각이거나 화살표가 가려진 것이다.
        var blob = Blobs(picked, ArrowJoinPixels)
            .Where(b => b.Count >= 12)
            .OrderBy(b => DistanceSquared(b, cx, cy))
            .FirstOrDefault() ?? [];

        if (blob.Count < 12) return null;

        var sumX = 0.0;
        var sumY = 0.0;

        foreach (var (x, y) in blob)
        {
            sumX += x;
            sumY += y;
        }

        var centerX = sumX / blob.Count;
        var centerY = sumY / blob.Count;

        var mask = blob
            .Select(p => ((short)Math.Round(p.X - centerX), (short)Math.Round(p.Y - centerY)))
            .ToArray();

        var (head, margin) = HeadAngle(blob, centerX, centerY);

        return new MinimapArrow(mask, centerX, centerY, head, margin);
    }

    /// <summary>
    /// 본보기와 견줘 지금 방위(도, 북 0·시계 방향)를 낸다. 화살표를 못 찾으면 null.
    /// </summary>
    /// <param name="template">익혀 둔 화살표.</param>
    /// <param name="templateHeading">본보기를 익힐 때의 방위(<see cref="MinimapSpec.HeadingAtTemplate"/>).</param>
    public static double? Heading(byte[] bgra, int width, int height, MinimapSpec spec, MinimapArrow template, double templateHeading)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (FindArrow(bgra, width, height, spec) is not { } now) return null;

        var turned = BestRotation(template.Mask, now.Mask);

        return Normalize(templateHeading + turned);
    }

    /// <summary>
    /// 본보기를 몇 도 돌리면 지금 모양에 가장 잘 겹치나(0~359). 겹침(IoU)이 가장 큰 각.
    /// </summary>
    public static double BestRotation(IReadOnlyList<(short X, short Y)> template, IReadOnlyList<(short X, short Y)> sample)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(sample);

        if (template.Count == 0 || sample.Count == 0) return 0;

        // 지금 모양을 네모 판에 놓고 본보기를 돌려 가며 찍어 본다 - 해시보다 배열 조회가 싸다.
        var reach = 0;

        foreach (var (x, y) in sample) reach = Math.Max(reach, Math.Max(Math.Abs((int)x), Math.Abs((int)y)));
        foreach (var (x, y) in template) reach = Math.Max(reach, Math.Max(Math.Abs((int)x), Math.Abs((int)y)));

        var side = (reach * 2) + 3;
        var origin = reach + 1;
        var grid = new bool[side * side];

        foreach (var (x, y) in sample) grid[((y + origin) * side) + x + origin] = true;

        var best = 0.0;
        var bestScore = -1.0;

        for (var degree = 0; degree < 360; degree += RotationStep)
        {
            var radians = degree * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var hit = 0;

            foreach (var (x, y) in template)
            {
                var rx = (int)Math.Round((x * cos) - (y * sin)) + origin;
                var ry = (int)Math.Round((x * sin) + (y * cos)) + origin;

                if ((uint)rx >= (uint)side || (uint)ry >= (uint)side) continue;
                if (grid[(ry * side) + rx]) hit++;
            }

            var score = (double)hit / (template.Count + sample.Count - hit);

            if (score > bestScore)
            {
                bestScore = score;
                best = degree;
            }
        }

        return best;
    }

    /// <summary>
    /// 미니맵 위 목표 마커들 - 화살표에서 본 방위·거리. 가까운 것부터.
    /// </summary>
    /// <param name="centerX">화살표 무게중심(조각 안 px).</param>
    /// <param name="centerY">화살표 무게중심(조각 안 px).</param>
    public static IReadOnlyList<MinimapMarker> Markers(byte[] bgra, int width, int height, MinimapSpec spec, double centerX, double centerY)
        => Markers(bgra, width, height, spec, centerX, centerY, out _);

    /// <summary>
    /// 미니맵 위 목표 마커들과 <b>그 조각 안 자리</b>(px) - 익히기가 <c>ignore</c> 후보를 알려 줄 때 자리를 쓴다.
    /// </summary>
    public static IReadOnlyList<MinimapMarker> Markers(byte[] bgra, int width, int height, MinimapSpec spec,
                                                       double centerX, double centerY, out IReadOnlyList<(double X, double Y)> places)
        => Markers(bgra, width, height, spec, spec?.Marker ?? new MinimapMarkerSpec(), centerX, centerY, out places);

    /// <summary>
    /// 그 색 규칙의 점들 - 이름 붙인 종류(<see cref="MinimapSpec.Markers"/>, 예: 「몹」 빨간 점)를 읽는다. 가까운 것부터.
    /// </summary>
    public static IReadOnlyList<MinimapMarker> Markers(byte[] bgra, int width, int height, MinimapSpec spec, MinimapMarkerSpec rule,
                                                       double centerX, double centerY)
        => Markers(bgra, width, height, spec, rule, centerX, centerY, out _);

    private static IReadOnlyList<MinimapMarker> Markers(byte[] bgra, int width, int height, MinimapSpec spec, MinimapMarkerSpec rule,
                                                        double centerX, double centerY, out IReadOnlyList<(double X, double Y)> places)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(rule);

        var picked = new List<(short X, short Y)>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;

                if (i + 2 >= bgra.Length) continue;

                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];

                if (!rule.Matches(r, g, b)) continue;

                picked.Add(((short)x, (short)y));
            }
        }

        var found = new List<MinimapMarker>();
        var spots = new List<(double X, double Y)>();

        foreach (var blob in Blobs(picked))
        {
            var boxWidth = blob.Max(p => p.X) - blob.Min(p => p.X) + 1;
            var boxHeight = blob.Max(p => p.Y) - blob.Min(p => p.Y) + 1;

            // 크기·모양 - 흰 점처럼 색만으로 안 갈리는 종류(깃털·화살표·글자도 흰색)는 꽉 찬 동그라미만 남긴다.
            if (!rule.FitsShape(blob.Count, boxWidth, boxHeight)) continue;

            var sumX = 0.0;
            var sumY = 0.0;

            foreach (var (x, y) in blob)
            {
                sumX += x;
                sumY += y;
            }

            var markerX = sumX / blob.Count;
            var markerY = sumY / blob.Count;

            // 미니맵에 겹친 시계·나침반 같은 UI - 색이 목표 마커와 거의 같아 자리로 뺀다.
            if (spec.IsIgnored(markerX / Math.Max(1, width), markerY / Math.Max(1, height))) continue;

            var dx = markerX - centerX;
            var dy = markerY - centerY;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));

            // 화살표 제 몸은 마커가 아니다 - 색으로 이미 갈리지만 겹쳐 보이는 표시(내 위치 강조)가 걸리는 것을 막는다.
            if (distance < 6) continue;

            found.Add(new MinimapMarker(Normalize(Math.Atan2(dx, -dy) * 180.0 / Math.PI), distance, blob.Count));
            spots.Add((markerX, markerY));
        }

        places = spots;

        return found.OrderBy(m => m.Distance).ToArray();
    }

    /// <summary>−180~180 으로 접은 방위 차 - <paramref name="to"/> 로 가려면 어느 쪽으로 얼마나 돌아야 하나.</summary>
    public static double Difference(double from, double to)
    {
        var delta = Normalize(to - from);

        return delta > 180 ? delta - 360 : delta;
    }

    /// <summary>0~360 으로 접는다.</summary>
    public static double Normalize(double degrees)
    {
        var value = degrees % 360;

        return value < 0 ? value + 360 : value;
    }

    /// <summary>
    /// 모양만으로 머리 방향을 가린다 - 방향마다 부챗살(±<see cref="HeadWindow"/>) 안 픽셀의 <b>중심거리 합</b>이 가장 큰 쪽.
    /// </summary>
    /// <remarks>
    /// 머리는 <b>짧지만 넓고</b>(삼각형) 꼬리는 <b>길지만 가늘다</b>. 그래서 「가장 먼 픽셀」로 보면 꼬리가 이기고
    /// 「픽셀 수 × 거리」로 보면 머리가 이긴다 - 실측 2026-09-23 세 장 모두 머리 87~94, 꼬리 67~77 로 13~25% 갈렸다.
    ///
    /// <b>갈래를 봉우리로 뽑아 순위를 매기는 방법은 버렸다</b>. 꼬리 끝 1~2px 이 색 문턱을 넘나드는 것만으로 갈래 길이가
    /// 11.6 → 8.6 으로 바뀌어 순위가 뒤집혔고, 머리를 148°·265° 로 잘못 골랐다(검사에서 잡음). 거리 <b>합</b>은
    /// 갈래 전체를 보므로 픽셀 한둘에 흔들리지 않는다.
    ///
    /// 절대 정확도는 ±5° 쯤이다(정답을 눈으로 잰 것이라 그 이상은 모른다). 이 값은 <b>익힐 때 기준각을 정하는 데만</b>
    /// 쓰고, 그 뒤 방위는 회전 대조(±1~2°)가 낸다.
    /// </remarks>
    private static (double Angle, double Margin) HeadAngle(IReadOnlyList<(short X, short Y)> blob, double centerX, double centerY)
    {
        var points = new (double Angle, double Distance)[blob.Count];

        for (var i = 0; i < blob.Count; i++)
        {
            var dx = blob[i].X - centerX;
            var dy = blob[i].Y - centerY;

            points[i] = (Normalize(Math.Atan2(dx, -dy) * 180.0 / Math.PI), Math.Sqrt((dx * dx) + (dy * dy)));
        }

        var weights = new double[360];

        for (var degree = 0; degree < 360; degree++)
        {
            var sum = 0.0;

            foreach (var (angle, distance) in points)
            {
                if (Math.Abs(Difference(degree, angle)) <= HeadWindow) sum += distance;
            }

            weights[degree] = sum;
        }

        var head = 0;

        for (var degree = 1; degree < 360; degree++)
        {
            if (weights[degree] > weights[head]) head = degree;
        }

        // 머리에서 멀리 떨어진 쪽 가운데 가장 무거운 것과 견준다 - 얼마나 뚜렷한 머리인지.
        var rival = 0.0;

        for (var degree = 0; degree < 360; degree++)
        {
            if (Math.Abs(Difference(head, degree)) < HeadRivalGap) continue;

            rival = Math.Max(rival, weights[degree]);
        }

        return (head, rival > 0 ? weights[head] / rival : 0);
    }

    /// <summary>이어진 픽셀 뭉치들. <paramref name="join"/> 이 1 이면 여덟 이웃, 2 면 한 칸 건너뛴 것까지 잇는다.</summary>
    private static List<List<(short X, short Y)>> Blobs(IReadOnlyList<(short X, short Y)> pixels, int join = 1)
    {
        var remaining = new HashSet<(short X, short Y)>(pixels);
        var blobs = new List<List<(short X, short Y)>>();

        while (remaining.Count > 0)
        {
            var seed = remaining.First();
            var blob = new List<(short X, short Y)>();
            var queue = new Queue<(short X, short Y)>();

            remaining.Remove(seed);
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();

                blob.Add((x, y));

                for (var dy = -join; dy <= join; dy++)
                {
                    for (var dx = -join; dx <= join; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        var next = ((short)(x + dx), (short)(y + dy));

                        if (remaining.Remove(next)) queue.Enqueue(next);
                    }
                }
            }

            blobs.Add(blob);
        }

        return blobs;
    }

    /// <summary>뭉치 무게중심이 그 점에서 얼마나 먼지(제곱).</summary>
    private static double DistanceSquared(List<(short X, short Y)> blob, double x, double y)
    {
        var dx = blob.Average(p => p.X) - x;
        var dy = blob.Average(p => p.Y) - y;

        return (dx * dx) + (dy * dy);
    }
}
