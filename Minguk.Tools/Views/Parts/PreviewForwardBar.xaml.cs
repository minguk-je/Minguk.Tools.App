using System.Windows.Controls;

namespace Minguk.Tools.Views.Parts;

/// <summary>
/// 미리보기 입력 전달 도구 줄(갱신 상한 · 입력 전달 · 요소 검사 · 클릭 후 돌아오기). 입력 경로는 화면의 설정 줄에 있다 -
/// 스크립트도 그 경로로 나가므로 미리보기를 끈 동안에도 보여야 한다.
/// </summary>
/// <remarks>
/// 스크립트·플레이 화면이 같은 줄을 쓴다. 캡처 화면은 순수하게 잡기만 하므로 이 줄이 없다 -
/// 미리보기를 눌러 게임을 조작하는 것은 개발·플레이의 일이다.
/// </remarks>
public partial class PreviewForwardBar : UserControl
{
    public PreviewForwardBar()
    {
        InitializeComponent();
    }
}
