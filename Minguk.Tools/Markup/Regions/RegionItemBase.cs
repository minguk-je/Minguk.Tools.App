using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 캔버스 위 항목(자리·칸)의 공통 바탕 - 고르면 어도너 층에 제 어도너를 얹고, 풀거나 트리에서 빠지면 뗀다.
/// </summary>
/// <remarks>
/// 자리(<see cref="RegionItem"/>)와 칸(<see cref="RegionCellItem"/>)은 어도너만 다르다. 손잡이(<see cref="RegionMoveThumb"/>·
/// <see cref="RegionResizeThumb"/>)는 이 바탕만 보고 끌기를 캔버스로 넘기므로 둘에 같이 붙는다.
///
/// 누르는 순간 고른다(<see cref="OnPreviewMouseLeftButtonDown"/>). 옮기기 손잡이가 누름을 먹기 전에 와야 해서 터널이다.
/// 칸은 자리와 같은 캔버스의 형제라(자식이 아니다) 칸을 누르면 자리 항목에는 누름이 안 간다.
/// </remarks>
public abstract class RegionItemBase : ContentControl
{
    private Adorner? _adorner;

    protected RegionItemBase()
    {
        // 어도너와 같은 까닭 - 항목 템플릿(테두리·이름표·옮기기 손잡이)도 확대 역수를 곱한 소수라 반올림하면 확대에서 어긋난다.
        UseLayoutRounding = false;

        Loaded += (_, _) => { if (IsSelected) ShowAdorner(); };
        Unloaded += (_, _) => HideAdorner();
    }

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(RegionItemBase), new PropertyMetadata(false, OnIsSelectedChanged));

    /// <summary>고른 것인가. 고른 것에만 어도너가 붙는다.</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty SourceWidthPxProperty = DependencyProperty.Register(
        nameof(SourceWidthPx), typeof(double), typeof(RegionItemBase), new PropertyMetadata(0d));

    /// <summary>원본 화면에서의 너비(픽셀). 크기를 바꾸는 동안 치수 표시가 보인다.</summary>
    public double SourceWidthPx
    {
        get => (double)GetValue(SourceWidthPxProperty);
        set => SetValue(SourceWidthPxProperty, value);
    }

    public static readonly DependencyProperty SourceHeightPxProperty = DependencyProperty.Register(
        nameof(SourceHeightPx), typeof(double), typeof(RegionItemBase), new PropertyMetadata(0d));

    public double SourceHeightPx
    {
        get => (double)GetValue(SourceHeightPxProperty);
        set => SetValue(SourceHeightPxProperty, value);
    }

    public static readonly DependencyProperty InverseZoomProperty = DependencyProperty.Register(
        nameof(InverseZoom), typeof(double), typeof(RegionItemBase), new PropertyMetadata(1d));

    /// <summary>
    /// 미리보기 배율의 역수. 테두리·손잡이·이름표 크기에 곱해 확대해도 화면에서 늘 같은 크기로 보이게 한다 - 라벨링 캔버스와 같다(2026-09-15).
    /// </summary>
    /// <remarks>미리보기는 판을 LayoutTransform 으로 키워 안의 선·글자가 같이 굵어진다. 캔버스(<see cref="RegionCanvas.Zoom"/>)가 넣어 준다.</remarks>
    public double InverseZoom
    {
        get => (double)GetValue(InverseZoomProperty);
        set => SetValue(InverseZoomProperty, value);
    }

    /// <summary>시계 방향 각도(도). 자리는 늘 0 이다. 손잡이 변위(돌린 좌표계)를 화면 좌표로 바꿀 때 캔버스가 본다.</summary>
    public virtual double Angle => 0;

    /// <summary>이 항목을 든 캔버스. 손잡이들이 끌기를 여기로 넘긴다.</summary>
    internal RegionCanvas? Owner => Parent as RegionCanvas;

    /// <summary>지금 어도너가 붙어 있는가. 하네스가 본다.</summary>
    public bool HasAdorner => _adorner is not null;

    /// <summary>고르면 얹을 어도너.</summary>
    protected abstract Adorner CreateAdorner();

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Owner?.Press(this);
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = (RegionItemBase)d;

        if ((bool)e.NewValue) item.ShowAdorner();
        else item.HideAdorner();
    }

    private void ShowAdorner()
    {
        if (_adorner is not null) return;

        // 아직 트리에 안 붙었으면 층이 없다. Loaded 에서 다시 온다.
        if (AdornerLayer.GetAdornerLayer(this) is not { } layer) return;

        _adorner = CreateAdorner();
        layer.Add(_adorner);
    }

    private void HideAdorner()
    {
        if (_adorner is not { } adorner) return;

        _adorner = null;
        AdornerLayer.GetAdornerLayer(this)?.Remove(adorner);
    }
}
