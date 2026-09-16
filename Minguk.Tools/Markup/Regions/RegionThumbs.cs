using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 자리·칸 안쪽을 잡고 통째로 옮기는 손잡이. 항목 템플릿이 투명하게 깔아 둔다.
/// </summary>
/// <remarks>
/// 참조한 <c>MoveThumb</c> 을 옮긴 것이다. 그쪽은 <c>Canvas.Left/Top</c> 을 직접 고쳤는데, 여기서는 그림(칸이면 자리) 밖으로 못 나가게
/// 하고 비율로 되돌려 저장해야 해서 캔버스(<see cref="RegionCanvas"/>)에 맡긴다. 변위는 Thumb 이 제 좌표계로 준다 -
/// 미리보기 확대는 캔버스와 같아 그대로 쓰고, 돌린 칸이면 캔버스가 화면 좌표로 돌린다.
/// </remarks>
public sealed class RegionMoveThumb : Thumb
{
    public RegionMoveThumb()
    {
        DragStarted += (_, _) => Item?.Owner?.BeginDrag(Item);
        DragDelta += (_, e) =>
        {
            Item?.Owner?.MoveBy(Item, e.HorizontalChange, e.VerticalChange);
            e.Handled = true;
        };
        DragCompleted += (_, _) => Item?.Owner?.EndDrag(Item);
    }

    private RegionItemBase? Item => DataContext as RegionItemBase;
}

/// <summary>
/// 모서리·변 가운데의 크기 조절 손잡이. 어느 변을 끄는지는 정렬(<see cref="FrameworkElement.HorizontalAlignment"/> ·
/// <see cref="FrameworkElement.VerticalAlignment"/>)이 말한다 - 참조한 <c>ResizeThumb</c> 과 같다.
/// </summary>
/// <remarks>
/// 끄는 동안 캔버스의 어도너 층에 <see cref="RegionSizeAdorner"/> 를 얹어 지금 크기(원본 픽셀)를 보여 주고, 놓으면 뗀다.
/// </remarks>
public sealed class RegionResizeThumb : Thumb
{
    private Adorner? _sizeAdorner;

    public RegionResizeThumb()
    {
        DragStarted += OnDragStarted;
        DragDelta += OnDragDelta;
        DragCompleted += OnDragCompleted;
    }

    private RegionItemBase? Item => DataContext as RegionItemBase;

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (Item is not { Owner: { } canvas } item) return;

        canvas.BeginDrag(item);

        if (AdornerLayer.GetAdornerLayer(canvas) is { } layer)
        {
            _sizeAdorner = new RegionSizeAdorner(item);
            layer.Add(_sizeAdorner);
        }
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Item is { Owner: { } canvas } item)
            canvas.ResizeBy(item, HorizontalAlignment, VerticalAlignment, e.HorizontalChange, e.VerticalChange);

        e.Handled = true;
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_sizeAdorner is { } adorner && Item?.Owner is { } canvas && AdornerLayer.GetAdornerLayer(canvas) is { } layer)
            layer.Remove(adorner);

        _sizeAdorner = null;

        if (Item is { Owner: { } owner } item) owner.EndDrag(item);
    }
}

/// <summary>
/// 칸 위쪽의 회전 손잡이. 참조한 <c>RotateThumb</c> - 자리 어도너에서는 뺐던 것을 칸 어도너에 되살렸다(사용자 2026-09-16 「대각선 사각」).
/// </summary>
/// <remarks>
/// 각도는 칸 가운데에서 누른 곳과 지금 곳이 이루는 각의 <b>차이</b>로 준다(<see cref="RegionGeometry.Rotate"/>) - 손잡이 한가운데를
/// 안 눌러도 칸이 튀지 않는다. 좌표는 캔버스 기준으로 잰다 - 칸 안 좌표는 칸과 같이 돌아 각도를 못 잰다.
/// </remarks>
public sealed class RegionRotateThumb : Thumb
{
    private Point _center;
    private Point _start;
    private double _startAngle;

    public RegionRotateThumb()
    {
        DragStarted += OnDragStarted;
        DragDelta += OnDragDelta;
        DragCompleted += (_, _) => { if (Item is { Owner: { } canvas } item) canvas.EndDrag(item); };
    }

    private RegionItemBase? Item => DataContext as RegionItemBase;

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (Item is not { Owner: { } canvas } item) return;

        canvas.BeginDrag(item);

        _center = item.TranslatePoint(new Point(item.ActualWidth / 2, item.ActualHeight / 2), canvas);
        _start = Mouse.GetPosition(canvas);
        _startAngle = item.Angle;
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Item is { Owner: { } canvas } item)
            canvas.RotateTo(item, RegionGeometry.Rotate(_startAngle, _center, _start, Mouse.GetPosition(canvas)));

        e.Handled = true;
    }
}
