namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 화면에서 찾은 글자 한 덩어리의 자리 - 가운데(화면 픽셀)와 크기, 읽힌 글.
/// </summary>
/// <remarks>
/// 사용자(2026-09-18) "특정 메뉴를 눌러야 한다" - 게임 메뉴는 대개 글자라 OCR 이 준 낱말 자리로 찾는다(<c>글자찾기("사격장")</c>).
/// 좌표는 <see cref="ScriptMob"/> 과 같은 화면 픽셀이라 <c>이동클릭(자리.x, 자리.y)</c> 에 그대로 넣는다.
/// </remarks>
public sealed record ScriptSpot(int CenterX, int CenterY, int Width, int Height, string Text)
{
    public int x => CenterX;

    public int y => CenterY;

    public int 너비 => Width;

    public int 높이 => Height;

    public string 글 => Text;

    /// <summary>본보기 그림으로 찾았으면 닮음(0~1). 글자로 찾았으면 0.</summary>
    public double Score { get; init; }

    public double 닮음 => Score;

    public override string ToString()
        => Score > 0 ? $"「{Text}」 ({CenterX}, {CenterY}) {Width}x{Height} 닮음 {Score:0.00}" : $"「{Text}」 ({CenterX}, {CenterY}) {Width}x{Height}";
}
