using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 자리 안의 칸 하나(<see cref="RegionCell"/>). 자리 항목과 같은 캔버스에 형제로 놓이고, 자리 위에 그려진다.
/// </summary>
/// <remarks>
/// <b>별개의 어도너</b>(사용자 2026-09-16 「지금 어도너와 별개로」) - 고르면 <see cref="RegionCellAdorner"/>(초록 테두리·손잡이·회전 손잡이)를 얹는다.
///
/// <b>돌린다</b> - 가운데를 중심으로 <see cref="CellAngle"/> 만큼 <see cref="UIElement.RenderTransform"/> 으로 돈다. 어도너는 붙은 요소의 변환을
/// 스스로 따라가므로 테두리·손잡이도 같이 돈다. 그래서 손잡이가 주는 변위는 <b>돌린 좌표계</b>의 값이다 - 캔버스가 화면 좌표로 바꿔 쓴다.
/// </remarks>
public sealed class RegionCellItem : RegionItemBase
{
    private readonly RotateTransform _rotation = new();

    public RegionCellItem()
    {
        Style = RegionChromeResources.StyleFor(typeof(RegionCellItem));
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotation;
    }

    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(
        nameof(Region), typeof(NamedRegion), typeof(RegionCellItem), new PropertyMetadata(null));

    /// <summary>이 칸이 든 자리.</summary>
    public NamedRegion? Region
    {
        get => (NamedRegion?)GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    public static readonly DependencyProperty CellProperty = DependencyProperty.Register(
        nameof(Cell), typeof(RegionCell), typeof(RegionCellItem), new PropertyMetadata(null));

    /// <summary>이 항목이 나타내는 칸. 이름표가 이것의 이름을 보인다.</summary>
    public RegionCell? Cell
    {
        get => (RegionCell?)GetValue(CellProperty);
        set => SetValue(CellProperty, value);
    }

    public static readonly DependencyProperty CellAngleProperty = DependencyProperty.Register(
        nameof(CellAngle), typeof(double), typeof(RegionCellItem), new PropertyMetadata(0d, (d, e) => ((RegionCellItem)d)._rotation.Angle = (double)e.NewValue));

    /// <summary>화면에 그릴 각도(도, 시계 방향). 끄는 동안은 칸의 저장값보다 앞선다.</summary>
    public double CellAngle
    {
        get => (double)GetValue(CellAngleProperty);
        set => SetValue(CellAngleProperty, value);
    }

    public override double Angle => CellAngle;

    protected override Adorner CreateAdorner() => new RegionCellAdorner(this);
}
