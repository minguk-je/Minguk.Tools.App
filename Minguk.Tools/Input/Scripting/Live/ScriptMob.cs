using System;

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

    /// <summary>
    /// 머리 자리(가로는 가운데, 세로는 사각형 위에서 <see cref="HeadFraction"/> 만큼 내려온 곳).
    /// <c>조준(몹.머리x, 몹.머리y)</c> 로 쓴다.
    /// </summary>
    /// <remarks>
    /// <b>왜 가운데가 아닌가</b> - 사각형 가운데는 사람으로 치면 배다. 머리를 노리는 편이 한 발의 값이 크고,
    /// 몹이 좌우로 움직여도 머리는 덜 흔들린다(팔다리가 사각형을 넓혔다 좁혔다 하는 것은 아래쪽이다).
    ///
    /// 위 모서리에 딱 붙이지 않는다 - 검출 사각형은 늘 조금 넉넉해서, 꼭대기를 겨누면 머리 위 허공을 본다.
    /// 위에서 18% 내려온 자리가 사람 몸에서 머리 한가운데쯤이었는데, 사격장 봇(높이 70px)에서 조금 높아 22% 로 내렸다(사용자, 2026-09-18 "타겟 위치 조금만 아래로" - 약 3px).
    /// </remarks>
    public int HeadX => CenterX;

    public int HeadY => CenterY - (Height / 2) + (int)Math.Round(Height * HeadFraction);

    /// <summary>사각형 위에서 머리까지, 높이의 몇 배로 내려갈지.</summary>
    public const double HeadFraction = 0.22;

    public int 머리x => HeadX;

    public int 머리y => HeadY;

    /// <summary>
    /// 머리 위 글자 - <b>부를 때</b> 지금 화면에서 사각형 위를 잘라 읽는다(몹마다 한 번만 읽고 기억). 이름표가 없는 게임·못 읽으면 빈 글.
    /// </summary>
    /// <remarks>
    /// 모든 게임이 이름표를 띄우지 않아 늘 읽지 않는다(사용자, 2026-09-17) - 한때 「이름표 읽기」 체크로 검출마다 읽었다.
    /// 몹을 찾은 화면보다 조금 뒤(최대 0.25초) 화면을 읽는다 - 이름표 칸은 사각형보다 넉넉하다(<see cref="Minguk.Tools.Vision.Ocr.NameplateRegion"/>).
    /// </remarks>
    public string 이름표 => Nameplate;

    public string Nameplate => Reader?.Read(this) ?? Caption;

    /// <summary>검출 사각형(0~1). 이름표 자리를 여기서 잡는다.</summary>
    internal Minguk.Tools.Vision.Labeling.LabelBox Box { get; init; }

    /// <summary>이 몹을 준 API 의 이름표 읽기. 같은 실행의 몹은 한 벌을 나눠 가져 같음 비교에 안 걸린다.</summary>
    internal ScriptNameplateReader? Reader { get; init; }

    public override string ToString() => $"{Name} {Score:P0} ({CenterX}, {CenterY})" + (Caption.Length > 0 ? $" 「{Caption}」" : string.Empty);
}
