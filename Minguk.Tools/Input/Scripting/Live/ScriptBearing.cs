namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>미니맵에서 읽은 방향 하나 - 어느 쪽으로 얼마나.</summary>
/// <param name="Bearing">방위(도) - 북 0, 시계 방향.</param>
/// <param name="Distance">미니맵 픽셀 거리. 실제 거리는 미니맵 배율에 달렸다 - 가까운지 먼지를 견주는 데 쓴다.</param>
/// <param name="Turn">지금 몸 방향에서 그쪽으로 돌 각(−180~180). 양수면 오른쪽.</param>
public sealed record ScriptBearing(double Bearing, double Distance, double Turn)
{
    public double 방위 => Bearing;

    public double 거리 => Distance;

    public double 돌각 => Turn;

    public override string ToString() => $"방위 {Bearing:0}도 · 거리 {Distance:0}px · 돌 각 {Turn:+0;-0;0}도";
}
