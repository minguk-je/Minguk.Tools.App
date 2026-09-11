namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 스크립트가 보는 몹 하나. 자리는 <b>화면 픽셀</b>이라 <c>이동(몹.중심x, 몹.중심y)</c> 에 그대로 넣는다.
/// </summary>
/// <remarks>
/// 검출은 0~1 비율로 들고 있지만 스크립트에는 픽셀로 준다. 스크립트가 비율↔픽셀을 직접 계산하게 하면
/// 대상 창이 옮겨지거나 크기가 바뀔 때마다 틀린다. 바꾸는 일은 API 가 부르는 순간의 창 자리로 한다.
/// 영문·한글 이름을 같이 둔다 - 스크립트를 한글로 쓰는 사람과 영문으로 쓰는 사람이 있다.
/// </remarks>
public sealed record ScriptMob(string Name, double Score, int CenterX, int CenterY, int Width, int Height, string Caption)
{
    public string 이름 => Name;

    public double 점수 => Score;

    public int 중심x => CenterX;

    public int 중심y => CenterY;

    public int 너비 => Width;

    public int 높이 => Height;

    /// <summary>이름표 읽기가 켜져 있으면 머리 위 글자. 아니면 빈 글.</summary>
    public string 이름표 => Caption;

    public override string ToString() => $"{Name} {Score:P0} ({CenterX}, {CenterY})" + (Caption.Length > 0 ? $" 「{Caption}」" : string.Empty);
}
