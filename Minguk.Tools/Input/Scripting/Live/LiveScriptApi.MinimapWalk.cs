using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Vision.Minimap;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 미니맵 점 쪽으로 <b>키보드만으로</b> 걷기 - 마우스로 몸을 돌리지 않고, 화살표 방위도 믿지 않는다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-25) "마우스로 방향을 틀려고 할 필요는 없고... 키보드로 이동만 하면 될거 같은데".
/// 마우스로 돌며 걷는 <c>마커로가기</c> 는 아이온2 에서 두 가지로 막혔다(실측 2026-09-25):
/// 큰 마우스 이동을 게임이 버렸고(잘게 나누니 먹었다), 화살표(▼)가 세모꼴이라 회전 대조가 120° 마다 거의 같은 점수를 줘
/// (280° 0.506 · 40° 0.488 · 162° 0.488) 방위가 세 방향을 오가며 좌우로 흔들렸다.
///
/// <b>그래서 방향을 점의 움직임으로 안다</b>. 마우스를 안 돌리면 카메라가 그대로라 「W 가 지도에서 어느 쪽인가」 가 한 값이다.
/// 걸으면 미니맵의 점이 반대쪽으로 밀리므로, 한 걸음(0.3초) 뒤 점이 움직인 쪽의 반대가 지금 누른 키의 지도 방향이다.
/// 그것에서 W 방향을 거꾸로 셈해 두고, 다음 걸음은 8방향(W·W+D·D·…) 가운데 점 쪽에 가장 가까운 키를 누른다.
/// 미니맵이 북쪽 고정이든 캐릭터를 따라 돌든 같다 - 모두 미니맵 안에서 잰다. 화살표는 한가운데 자리를 잡는 데만 쓴다.
/// </remarks>
public partial class LiveScriptApi
{
    /// <summary>한 걸음(ms) - 이만큼 누른 뒤 점이 얼마나 밀렸나 본다. 짧으면 밀린 거리가 점 위치 흔들림(±0.3px)에 묻힌다.</summary>
    private const int WalkStepMs = 300;

    /// <summary>점까지 이만큼(px) 안이면 다 왔다.</summary>
    private const double WalkArrivePixels = 6;

    /// <summary>한 걸음 사이에 같은 점으로 볼 거리(px). 이보다 멀리 뛰었으면 다른 점이다.</summary>
    private const double WalkMatchPixels = 8;

    /// <summary>이보다 적게 밀렸으면 안 움직인 것으로 본다(px).</summary>
    private const double WalkMinMovePixels = 0.8;

    /// <summary>안 움직인 걸음이 잇달아 이만큼이면 막힌 것으로 보고 그만둔다.</summary>
    private const int WalkStuckSteps = 4;

    /// <summary>8방향 - 키와 W(앞) 기준 각(도, 시계 방향).</summary>
    private static readonly (string Keys, double Offset)[] WalkDirections =
    [
        ("W", 0), ("W+D", 45), ("D", 90), ("S+D", 135), ("S", 180), ("S+A", 225), ("A", 270), ("W+A", 315)
    ];

    /// <summary>W 가 미니맵에서 가리키는 방위(도) - 걸어 보며 잰 것. 마우스를 안 돌리면 그대로라 다음 호출도 여기서 시작한다.</summary>
    private double? _walkForward;

    /// <summary>
    /// 그 이름 점 가운데 가장 가까운 것 쪽으로 <b>키보드(W·A·S·D)만으로</b> 걷는다 - 마우스는 안 쓴다.
    /// </summary>
    /// <returns>걸었으면 true(다 왔거나 시간이 다 됨), 점이 없거나 막혀 못 움직였으면 false.</returns>
    public bool WalkToMarker(string name, int milliseconds)
        => Traced("WalkToMarker", $"{Quote(name)}, {milliseconds}", () => WalkToMarkerCore(name, milliseconds));

    public bool 마커로걷기(string 이름, int 밀리초) => WalkToMarker(이름, 밀리초);

    private bool WalkToMarkerCore(string name, int milliseconds)
    {
        if (milliseconds <= 0) return false;

        var spots = MarkerSpots(name);

        if (spots.Count == 0) return false;

        var target = Nearest(spots);

        if (Length(target) <= WalkArrivePixels) return true;

        var deadline = Environment.TickCount64 + milliseconds;
        var held = new List<ushort>();
        var (keys, offset) = _walkForward is { } known ? PickDirection(BearingOf(target) - known) : WalkDirections[0];
        var still = 0;

        BeforeInput();

        try
        {
            PressWalkKeys(keys, held);

            while (Environment.TickCount64 < deadline)
            {
                Wait((int)Math.Min(WalkStepMs, deadline - Environment.TickCount64));

                spots = MarkerSpots(name);

                // 점이 사라졌다 - 미니맵 밖으로 나갔거나 몹이 죽었다. 스크립트가 다시 보게 한다.
                if (spots.Count == 0) return true;

                var same = spots.MinBy(s => Distance(s, target));

                if (Distance(same, target) <= WalkMatchPixels)
                {
                    // 점이 밀린 쪽의 반대가 내가 간 쪽이다(점은 제자리, 내가 움직였다).
                    var dx = same.X - target.X;
                    var dy = same.Y - target.Y;

                    if (Math.Sqrt((dx * dx) + (dy * dy)) >= WalkMinMovePixels)
                    {
                        var forward = MinimapReader.Normalize(BearingOf((-dx, -dy)) - offset);

                        // 몹도 움직이고 점 자리도 흔들린다 - 새 값은 반만 믿는다.
                        _walkForward = _walkForward is { } before
                            ? MinimapReader.Normalize(before + (MinimapReader.Difference(before, forward) / 2))
                            : forward;
                        still = 0;
                    }
                    else if (++still >= WalkStuckSteps)
                    {
                        return false;
                    }
                }

                target = Nearest(spots);

                if (Length(target) <= WalkArrivePixels) return true;

                if (_walkForward is not { } walkForward) continue;

                var (nextKeys, nextOffset) = PickDirection(BearingOf(target) - walkForward);

                if (nextKeys == keys) continue;

                ReleaseWalkKeys(held);
                PressWalkKeys(nextKeys, held);
                keys = nextKeys;
                offset = nextOffset;
            }

            return true;
        }
        finally
        {
            ReleaseWalkKeys(held);
        }
    }

    /// <summary>그 이름 점들 - 캐릭터 자리 기준(미니맵 px, 오른쪽 +x · 아래 +y).</summary>
    /// <remarks>캐릭터 자리는 화살표 무게중심, 화살표를 못 찾으면 미니맵 한가운데. 방위는 안 읽는다.</remarks>
    private List<(double X, double Y)> MarkerSpots(string name)
    {
        var spec = MinimapSpecOrThrow();
        var rule = spec.MarkerNamed(name)
                   ?? throw Guard($"미니맵 점 종류 「{name}」 이 {MinimapSpec.FileName} 에 없습니다 - 있는 것: {string.Join(", ", spec.Markers.Keys.Prepend(MinimapSpec.TargetMarkerName))}.");
        var (pixels, width, height) = MinimapPixels(spec);
        var arrow = MinimapReader.FindArrow(pixels, width, height, spec);
        var centerX = arrow?.CenterX ?? width / 2.0;
        var centerY = arrow?.CenterY ?? height / 2.0;

        return MinimapReader.Markers(pixels, width, height, spec, rule, centerX, centerY)
            .Select(m =>
            {
                var radians = m.Bearing * Math.PI / 180.0;

                return (m.Distance * Math.Sin(radians), -m.Distance * Math.Cos(radians));
            })
            .ToList();
    }

    /// <summary>W 기준 그 각에 가장 가까운 8방향 키.</summary>
    private static (string Keys, double Offset) PickDirection(double relative)
        => WalkDirections.MinBy(d => Math.Abs(MinimapReader.Difference(d.Offset, MinimapReader.Normalize(relative))));

    private void PressWalkKeys(string keys, List<ushort> held)
    {
        foreach (var part in keys.Split('+'))
        {
            var key = ToVirtualKey(part);

            Hold(key);
            held.Add(key);
        }
    }

    private void ReleaseWalkKeys(List<ushort> held)
    {
        foreach (var key in held) Release(key);

        held.Clear();
    }

    private static (double X, double Y) Nearest(List<(double X, double Y)> spots) => spots.MinBy(Length);

    private static double Length((double X, double Y) p) => Math.Sqrt((p.X * p.X) + (p.Y * p.Y));

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Length((a.X - b.X, a.Y - b.Y));

    /// <summary>미니맵 위 벡터의 방위(도, 위 0 · 시계 방향).</summary>
    private static double BearingOf((double X, double Y) p) => MinimapReader.Normalize(Math.Atan2(p.X, -p.Y) * 180.0 / Math.PI);
}
