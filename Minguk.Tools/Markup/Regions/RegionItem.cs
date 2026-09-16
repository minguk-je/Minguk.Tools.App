using System.Windows;
using System.Windows.Documents;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 캔버스 위의 자리 하나. 참조한 <c>DesignerItem</c>(ContentControl + DesignerItemDecorator) 을 옮겼다.
/// </summary>
/// <remarks>
/// 고르면 어도너 층에 <see cref="RegionResizeAdorner"/>(주황 테두리·손잡이)를 얹는다 - 참조 프로젝트의 <c>DesignerItemDecorator</c> 가
/// 하던 일이다. 여기서는 바탕(<see cref="RegionItemBase"/>)이 한다. 자리 안의 칸은 따로 <see cref="RegionCellItem"/> 이다.
/// </remarks>
public sealed class RegionItem : RegionItemBase
{
    public RegionItem() => Style = RegionChromeResources.StyleFor(typeof(RegionItem));

    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(
        nameof(Region), typeof(NamedRegion), typeof(RegionItem), new PropertyMetadata(null));

    /// <summary>이 항목이 나타내는 자리. 이름표가 이것의 이름을 보인다.</summary>
    public NamedRegion? Region
    {
        get => (NamedRegion?)GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    protected override Adorner CreateAdorner() => new RegionResizeAdorner(this);
}
