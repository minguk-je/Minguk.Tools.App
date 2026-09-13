using System.Windows;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// HUD 에서 숫자를 읽을 자리 하나.
/// </summary>
/// <param name="Region">화면 기준 0~1 비율.</param>
/// <param name="Ink">
/// 흰 글자만 남기는 전처리(<see cref="HudInk"/>)를 거칠지. <b>자리마다 다르다</b> - 배경이 밝아질 수 있는 곳은
/// 거쳐야 읽히고, 배경이 늘 어두운 곳은 거치면 오히려 안 읽힌다(실측: 궁극기 「35」 가 전처리하면 빈 글,
/// 그냥 키우면 읽혔다. 원본은 부드럽게 번진 흰 글자인데 마스크는 가장자리가 계단이 된다).
/// </param>
/// <param name="Scale">전처리를 안 할 때 키우는 배율. HUD 숫자는 20px 안팎이라 그대로는 잘 안 읽힌다.</param>
public sealed record HudSpot(Rect Region, bool Ink, double Scale = 6);

/// <summary>
/// 게임 HUD 에서 숫자를 읽을 자리들.
/// </summary>
/// <remarks>
/// <b>왜 비율인가</b> - 오버워치 HUD 는 해상도가 바뀌어도 같은 비율 자리에 그려진다. 픽셀로 적으면 1920x1080
/// 에서만 맞는다.
///
/// <b>왜 넉넉한가</b> - Windows OCR 은 글자가 그림 가장자리에 닿으면 빈 글을 낸다. 같은 자리를 딱 맞게 잘랐을 때는
/// 못 읽던 「17 24」 가, 사방으로 조금 넓히자 읽혔다(실측). 배경이 더 들어와도 상관없다.
///
/// <b>영웅이 달라도 자리는 같다</b> - 탄창이 30|40 · 36|40 · 24|24 · 17|24 · 13|24 로 서로 다른 무기였는데
/// 같은 자리에서 읽혔다(실측 14장, 탄약 14/14 · 체력 14/14). 바뀌는 것은 값이지 자리가 아니다.
/// 다만 근접 무기(탄약 없음)나 게이지로 된 특수 자원은 여기서 읽을 것이 없다.
///
/// <b>오버워치 기준이다.</b> 다른 게임이면 이 숫자들을 바꾼다 - 자리는
/// <c>--ocr-crop --image=화면.png --region=… [--ink] --lang=en-US</c> 로 찾는다.
/// </remarks>
public static class HudRegions
{
    /// <summary>탄약 「현재 | 최대」. 오른쪽 아래. 밝은 바닥 위로 오는 일이 있어 전처리가 필요하다.</summary>
    public static readonly HudSpot Ammo = new(new Rect(0.895, 0.860, 0.095, 0.060), Ink: true);

    /// <summary>체력 「현재 | 최대」. 왼쪽 아래. 탄약과 같은 이유로 전처리한다.</summary>
    public static readonly HudSpot Health = new(new Rect(0.093, 0.802, 0.115, 0.055), Ink: true);

    /// <summary>
    /// 궁극기 충전(%). 가운데 아래 고리 안쪽. <b>다 차면 숫자 대신 영웅 아이콘</b>이라 아무것도 안 읽힌다.
    /// </summary>
    /// <remarks>
    /// 고리 안은 늘 어두워서 전처리를 안 한다 - 하면 오히려 못 읽는다. 마스크는 「35」 가 또렷했는데도 빈 글이
    /// 나왔고, 그냥 6배로 키우니 읽혔다(실측). 원본은 부드럽게 번진 흰 글자인데 마스크는 가장자리가 계단이 된다.
    ///
    /// 실측(오버워치, 담아 둔 화면 두 장): 차는 중 35% → 「35」, 다 찬 화면 → 빈 글(= 준비됨).
    /// </remarks>
    public static readonly HudSpot Ultimate = new(new Rect(0.468, 0.812, 0.065, 0.075), Ink: false);
}
