using System.Windows;
using System.Windows.Controls;

namespace Minguk.Tools.Views.Parts;

/// <summary>
/// 잡은 화면을 보여 주는 판. 그림 + 입력 받기 + (화면이 얹는) 겹그림.
/// </summary>
/// <remarks>
/// 캡처·스크립트·플레이 세 화면이 같은 미리보기를 쓴다. XAML 을 세 벌 두면 서비스 이름이나 이벤트 하나가
/// 한쪽에서만 고쳐진다. DataContext 는 얹은 화면 것을 그대로 물려받아 <c>CaptureViewModelBase</c> 의
/// 커맨드와 <c>PreviewImage</c> 에 묶인다.
///
/// 겹그림(<see cref="Overlay"/>)만 화면이 준다 - 몹 점선은 몹 찾기가 있는 화면에만 있다. 이 판이
/// 직접 그리면 캡처 화면이 없는 프로퍼티에 묶여 바인딩 오류가 남는다.
/// </remarks>
public partial class CapturePreviewPanel : UserControl
{
    public static readonly DependencyProperty OverlayProperty = DependencyProperty.Register(
        nameof(Overlay), typeof(UIElement), typeof(CapturePreviewPanel), new PropertyMetadata(null));

    public CapturePreviewPanel()
    {
        InitializeComponent();
    }

    /// <summary>그림 위에 겹쳐 그릴 것. 그림과 같은 자리를 차지한다.</summary>
    public UIElement? Overlay
    {
        get => (UIElement?)GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(UIElement), typeof(CapturePreviewPanel), new PropertyMetadata(null));

    /// <summary>
    /// 그림 위에서 마우스를 받는 것(영역 손잡이). 겹그림과 달리 히트 테스트를 켠 채 얹는다.
    /// </summary>
    /// <remarks>
    /// 겹그림과 한 자리에 두지 않는 이유 - 겹그림은 클릭을 게임으로 흘려야 해서 판이 히트 테스트를 끈다. 편집기는
    /// 편집 중에만 마우스를 먹어야 하므로, 켜고 끄는 것을 제 안에서 한다(<see cref="Markup.Regions.RegionCanvas.IsEditing"/>).
    /// </remarks>
    public UIElement? Editor
    {
        get => (UIElement?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }
}
