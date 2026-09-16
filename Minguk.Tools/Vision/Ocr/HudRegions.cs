using System.Windows;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>HUD 에서 숫자를 읽을 자리 하나.</summary>
/// <param name="Region">화면 기준 0~1 비율.</param>
public sealed record HudSpot(Rect Region);

/// <summary>
/// 게임 HUD 에서 숫자를 읽을 자리들.
/// </summary>
/// <remarks>
/// <b>왜 비율인가</b> - 오버워치 HUD 는 해상도가 바뀌어도 같은 비율 자리에 그려진다. 픽셀로 적으면 1920x1080
/// 에서만 맞는다.
///
/// <b>왜 넉넉한가</b> - 글자가 조각 가장자리에 닿으면 검출이 끊긴다. 같은 자리를 딱 맞게 잘랐을 때는
/// 못 읽던 「17 24」 가, 사방으로 조금 넓히자 읽혔다(실측). 배경이 더 들어와도 상관없다.
///
/// <b>영웅이 달라도 자리는 같다</b> - 탄창이 30|40 · 36|40 · 24|24 · 17|24 · 13|24 로 서로 다른 무기였는데
/// 같은 자리에서 읽혔다(실측 14장, 탄약 14/14 · 체력 14/14). 바뀌는 것은 값이지 자리가 아니다.
/// 다만 근접 무기(탄약 없음)나 게이지로 된 특수 자원은 여기서 읽을 것이 없다.
///
/// <b>오버워치 기준이다.</b> 다른 게임이면 이 숫자들을 바꾼다 - 자리는
/// <c>--ocr-crop --image=화면.png --region=…</c> 로 찾는다.
/// </remarks>
public static class HudRegions
{
    /// <summary>탄약 「현재 | 최대」. 오른쪽 아래.</summary>
    public static readonly HudSpot Ammo = new(new Rect(0.895, 0.860, 0.095, 0.060));

    /// <summary>체력 「현재 | 최대」. 왼쪽 아래.</summary>
    public static readonly HudSpot Health = new(new Rect(0.093, 0.802, 0.115, 0.055));

    /// <summary>
    /// 궁극기 충전(%). 가운데 아래 고리 안쪽. <b>다 차면 숫자 대신 영웅 아이콘</b>이라 아무것도 안 읽힌다.
    /// </summary>
    /// <remarks>
    /// 실측(오버워치, 담아 둔 화면 두 장): 차는 중 35% → 「35」, 다 찬 화면 → 빈 글(= 준비됨).
    /// </remarks>
    public static readonly HudSpot Ultimate = new(new Rect(0.468, 0.812, 0.065, 0.075));
}
