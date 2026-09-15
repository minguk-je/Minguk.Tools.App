using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 캔버스 위의 자리 하나. 참조한 <c>DesignerItem</c>(ContentControl + DesignerItemDecorator) 을 옮겼다.
/// </summary>
/// <remarks>
/// 고르면(<see cref="IsSelected"/>) 어도너 층에 <see cref="RegionResizeAdorner"/> 를 얹고, 풀거나 트리에서 빠지면 뗀다 -
/// 참조 프로젝트의 <c>DesignerItemDecorator</c> 가 하던 일이다. 여기서는 항목이 직접 한다. 데코레이터를 따로 두면
/// 그것이 템플릿 안에 있어야 하고, 템플릿이 바뀌면 조용히 빠진다.
///
/// 누르는 순간 고른다(<see cref="OnPreviewMouseLeftButtonDown"/>). 옮기기 손잡이가 누름을 먹기 전에 와야 해서 터널이다.
/// </remarks>
public sealed class RegionItem : ContentControl
{
    private Adorner? _adorner;

    public RegionItem()
    {
        Style = RegionChromeResources.StyleFor(typeof(RegionItem));

        Loaded += (_, _) => { if (IsSelected) ShowAdorner(); };
        Unloaded += (_, _) => HideAdorner();
    }

    public static readonly DependencyProperty RegionProperty = DependencyProperty.Register(
        nameof(Region), typeof(NamedRegion), typeof(RegionItem), new PropertyMetadata(null));

    /// <summary>이 항목이 나타내는 자리. 이름표가 이것의 이름을 보인다.</summary>
    public NamedRegion? Region
    {
        get => (NamedRegion?)GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(RegionItem), new PropertyMetadata(false, OnIsSelectedChanged));

    /// <summary>고른 것인가. 고른 것에만 테두리·손잡이 어도너가 붙는다.</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty SourceWidthPxProperty = DependencyProperty.Register(
        nameof(SourceWidthPx), typeof(double), typeof(RegionItem), new PropertyMetadata(0d));

    /// <summary>원본 화면에서의 너비(픽셀). 크기를 바꾸는 동안 치수 표시가 보인다.</summary>
    public double SourceWidthPx
    {
        get => (double)GetValue(SourceWidthPxProperty);
        set => SetValue(SourceWidthPxProperty, value);
    }

    public static readonly DependencyProperty SourceHeightPxProperty = DependencyProperty.Register(
        nameof(SourceHeightPx), typeof(double), typeof(RegionItem), new PropertyMetadata(0d));

    public double SourceHeightPx
    {
        get => (double)GetValue(SourceHeightPxProperty);
        set => SetValue(SourceHeightPxProperty, value);
    }

    public static readonly DependencyProperty InverseZoomProperty = DependencyProperty.Register(
        nameof(InverseZoom), typeof(double), typeof(RegionItem), new PropertyMetadata(1d));

    /// <summary>
    /// 미리보기 배율의 역수. 테두리·손잡이·이름표 크기에 곱해 확대해도 화면에서 늘 같은 크기로 보이게 한다 - 라벨링 캔버스와 같다(2026-09-15).
    /// </summary>
    /// <remarks>미리보기는 판을 LayoutTransform 으로 키워 안의 선·글자가 같이 굵어진다. 캔버스(<see cref="RegionCanvas.Zoom"/>)가 넣어 준다.</remarks>
    public double InverseZoom
    {
        get => (double)GetValue(InverseZoomProperty);
        set => SetValue(InverseZoomProperty, value);
    }

    /// <summary>이 항목을 든 캔버스. 손잡이들이 끌기를 여기로 넘긴다.</summary>
    internal RegionCanvas? Owner => Parent as RegionCanvas;

    /// <summary>지금 어도너가 붙어 있는가. 하네스가 본다.</summary>
    public bool HasAdorner => _adorner is not null;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Owner?.Select(this);
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = (RegionItem)d;

        if ((bool)e.NewValue) item.ShowAdorner();
        else item.HideAdorner();
    }

    private void ShowAdorner()
    {
        if (_adorner is not null) return;

        // 아직 트리에 안 붙었으면 층이 없다. Loaded 에서 다시 온다.
        if (AdornerLayer.GetAdornerLayer(this) is not { } layer) return;

        _adorner = new RegionResizeAdorner(this);
        layer.Add(_adorner);
    }

    private void HideAdorner()
    {
        if (_adorner is not { } adorner) return;

        _adorner = null;
        AdornerLayer.GetAdornerLayer(this)?.Remove(adorner);
    }
}
