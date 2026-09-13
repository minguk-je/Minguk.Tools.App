using System.Windows;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 게임 HUD 에서 숫자를 읽을 자리(화면 기준 0~1 비율).
/// </summary>
/// <remarks>
/// <b>왜 비율인가</b> - 오버워치 HUD 는 해상도가 바뀌어도 같은 비율 자리에 그려진다. 픽셀로 적으면 1920x1080
/// 에서만 맞는다.
///
/// <b>왜 넉넉한가</b> - Windows OCR 은 글자가 그림 가장자리에 닿으면 빈 글을 낸다. 같은 자리를 딱 맞게 잘랐을 때는
/// 못 읽던 「17 24」 가, 사방으로 조금 넓히자 읽혔다(실측). 배경이 더 들어와도 <see cref="HudInk"/> 가 지운다.
///
/// <b>오버워치 기준이다.</b> 다른 게임이면 이 숫자들을 바꾼다 - 자리는 `--ocr-crop --image=화면.png --region=…
/// --ink --lang=en-US` 로 찾는다. 찍어 둔 화면으로 맞춰 보고 나온 값을 여기 적으면 된다.
///
/// 실측(사격장 화면 14장): 탄약 14/14, 체력 14/14 를 읽었다.
/// </remarks>
public static class HudRegions
{
    /// <summary>탄약 「현재 | 최대」. 오른쪽 아래.</summary>
    public static readonly Rect Ammo = new(0.895, 0.860, 0.095, 0.060);

    /// <summary>체력 「현재 | 최대」. 왼쪽 아래.</summary>
    public static readonly Rect Health = new(0.093, 0.802, 0.115, 0.055);

    /// <summary>
    /// 궁극기 충전(%). 가운데 아래 고리 안쪽.
    /// </summary>
    /// <remarks>
    /// <b>이 자리는 아직 실측으로 못 맞췄다.</b> 가지고 있는 화면 97장이 전부 궁극기가 다 찬 상태라, 고리 안에
    /// 숫자 대신 영웅 아이콘만 있었다. 차는 중인 화면이 생기면 <c>--ocr-crop</c> 로 맞춰 이 값을 고칠 것.
    /// 다 찼을 때는 숫자가 없으므로 읽기가 null 을 준다 - 부르는 쪽이 그것을 "준비됨" 으로 본다.
    /// </remarks>
    public static readonly Rect Ultimate = new(0.468, 0.812, 0.065, 0.075);
}
