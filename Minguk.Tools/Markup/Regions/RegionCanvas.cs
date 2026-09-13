using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Markup.Regions;

/// <summary>자리를 끌어 옮기거나 크기를 바꾼 결과. 끄는 동안은 <see cref="Completed"/> 가 false 다.</summary>
public sealed record RegionEdit(NamedRegion Region, Rect Rect, bool Completed);

/// <summary>
/// 미리보기 그림 위에 이름 붙인 자리들을 <see cref="RegionItem"/> 으로 놓는 캔버스. 영역 지정 중에만 마우스를 받는다.
/// </summary>
/// <remarks>
/// <b>왜 겹그림(DetectionOverlay)이 그리지 않는가</b> - 겹그림은 클릭을 게임으로 흘려야 해서 히트 테스트를 끈 채 그리기만 한다.
/// 손잡이를 거기에 그리고 마우스 계산을 VM 이 하던 것을, 참조 프로젝트(wpf_test_app.ResizeAdorner)처럼 Thumb 과 어도너로
/// 바꿨다 - 커서·잡기·끌기가 WPF 것이라 손으로 맞출 것이 없다. 게임으로 클릭이 안 새는 것은 <see cref="IsEditing"/> 이
/// 지킨다: 꺼져 있으면 이 캔버스가 히트 테스트에서 빠져 밑의 그림(전달)이 받는다.
///
/// 항목은 <b>그림이 놓인 자리</b>(픽셀)에 놓이고, 끌면 0~1 로 되돌려 <see cref="EditCommand"/> 로 올린다. 저장은 VM 몫이다.
/// 빈 자리를 누르면 항목이 없어 밑으로 흘러가고, VM 이 새 자리 그리기를 시작한다 - 그 길은 그대로다.
/// </remarks>
public sealed class RegionCanvas : Canvas
{
    private readonly Dictionary<NamedRegion, RegionItem> _items = new(ReferenceEqualityComparer.Instance);
    private RegionItem? _dragging;
    private bool _dragMoved;

    public RegionCanvas()
    {
        IsHitTestVisible = false;
        SizeChanged += (_, _) => Rebuild();
    }

    public static readonly DependencyProperty RegionsProperty = DependencyProperty.Register(
        nameof(Regions), typeof(IEnumerable<NamedRegion>), typeof(RegionCanvas),
        new PropertyMetadata(null, OnRegionsChanged));

    public IEnumerable<NamedRegion>? Regions
    {
        get => (IEnumerable<NamedRegion>?)GetValue(RegionsProperty);
        set => SetValue(RegionsProperty, value);
    }

    /// <summary>자리 안의 값만 바뀌었을 때 올려 다시 놓게 한다(겹그림의 것과 같다).</summary>
    public static readonly DependencyProperty RegionsRevisionProperty = DependencyProperty.Register(
        nameof(RegionsRevision), typeof(int), typeof(RegionCanvas), new PropertyMetadata(0, (d, _) => ((RegionCanvas)d).Rebuild()));

    public int RegionsRevision
    {
        get => (int)GetValue(RegionsRevisionProperty);
        set => SetValue(RegionsRevisionProperty, value);
    }

    public static readonly DependencyProperty SelectedRegionProperty = DependencyProperty.Register(
        nameof(SelectedRegion), typeof(NamedRegion), typeof(RegionCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((RegionCanvas)d).Rebuild()));

    /// <summary>고른 자리. 항목을 누르면 여기로 올라가고, 목록에서 고르면 여기로 내려온다.</summary>
    public NamedRegion? SelectedRegion
    {
        get => (NamedRegion?)GetValue(SelectedRegionProperty);
        set => SetValue(SelectedRegionProperty, value);
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(RegionCanvas), new PropertyMetadata(null, (d, _) => ((RegionCanvas)d).Rebuild()));

    /// <summary>아래 그림. 원본 크기를 알려고만 본다 - 겹그림과 같다.</summary>
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(RegionCanvas), new PropertyMetadata(false, OnIsEditingChanged));

    /// <summary>영역 지정 중인가. 그때만 보이고, 그때만 마우스를 받는다.</summary>
    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public static readonly DependencyProperty EditCommandProperty = DependencyProperty.Register(
        nameof(EditCommand), typeof(ICommand), typeof(RegionCanvas), new PropertyMetadata(null));

    /// <summary>끌 때마다 <see cref="RegionEdit"/> 를 받는다. 놓을 때 것은 <c>Completed</c>.</summary>
    public ICommand? EditCommand
    {
        get => (ICommand?)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    /// <summary>놓인 항목들. 하네스가 본다.</summary>
    public IReadOnlyCollection<RegionItem> Items => _items.Values;

    /// <summary>그림이 놓인 자리(이 캔버스 좌표).</summary>
    public Rect ImageArea => RegionGeometry.ImageArea(RenderSize, SourceSize);

    private Size SourceSize => Source is { Width: > 0, Height: > 0 } image ? new Size(image.Width, image.Height) : Size.Empty;

    private static void OnRegionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (RegionCanvas)d;

        // 옛 컬렉션의 구독을 반드시 푼다. 안 그러면 화면을 닫아도 그 컬렉션이 안 죽는다.
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= canvas.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged added) added.CollectionChanged += canvas.OnCollectionChanged;

        canvas.Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (RegionCanvas)d;
        var editing = (bool)e.NewValue;

        canvas.IsHitTestVisible = editing;
        canvas.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        if (!editing && canvas._dragging is { } item) canvas.EndDrag(item);

        // 어도너는 캔버스와 다른 층에 있어 캔버스를 접어도 남는다. 고름을 풀어 떼게 한다.
        canvas.Rebuild();
    }

    /// <summary>자리마다 항목 하나. 끌고 있는 것은 건드리지 않는다 - 되쏜 값으로 손 밑에서 흔들리면 안 된다.</summary>
    private void Rebuild()
    {
        var wanted = Regions?.ToList() ?? [];
        var area = ImageArea;
        var source = SourceSize;

        foreach (var region in _items.Keys.Where(known => !wanted.Any(w => ReferenceEquals(w, known))).ToList())
        {
            Children.Remove(_items[region]);
            _items.Remove(region);
        }

        foreach (var region in wanted)
        {
            if (!_items.TryGetValue(region, out var item))
            {
                item = new RegionItem { Region = region };
                _items.Add(region, item);
                Children.Add(item);
            }

            // 어도너(테두리·손잡이)는 편집 중에만 - 아닐 때 보이면 잡히는 줄 안다.
            item.IsSelected = IsEditing && ReferenceEquals(region, SelectedRegion);

            if (ReferenceEquals(item, _dragging)) continue;

            if (area.IsEmpty || region.Width <= 0 || region.Height <= 0)
            {
                item.Visibility = Visibility.Collapsed;
                continue;
            }

            item.Visibility = Visibility.Visible;
            Place(item, RegionGeometry.ToCanvas(region.Rect, area), area, source);
        }
    }

    private static void Place(RegionItem item, Rect rect, Rect area, Size source)
    {
        SetLeft(item, rect.X);
        SetTop(item, rect.Y);
        item.Width = rect.Width;
        item.Height = rect.Height;

        if (area.Width > 0 && area.Height > 0)
        {
            item.SourceWidthPx = rect.Width / area.Width * source.Width;
            item.SourceHeightPx = rect.Height / area.Height * source.Height;
        }
    }

    private static Rect RectOf(RegionItem item) => new(GetLeft(item), GetTop(item), item.Width, item.Height);

    /// <summary>읽을 만한 최소 크기(0~1 의 0.004, <c>NamedRegion.IsUsable</c> 과 같다)를 캔버스 픽셀로.</summary>
    private static Size MinimumOf(Rect area) => new(0.004 * area.Width, 0.004 * area.Height);

    internal void Select(RegionItem item)
    {
        if (item.Region is { } region && !ReferenceEquals(region, SelectedRegion)) SelectedRegion = region;
    }

    internal void BeginDrag(RegionItem item)
    {
        Select(item);
        _dragging = item;
        _dragMoved = false;
    }

    /// <summary>안쪽을 끈 만큼 옮긴다. 그림 밖으로는 안 나간다.</summary>
    public void MoveBy(RegionItem item, double dx, double dy)
    {
        var area = ImageArea;
        if (area.IsEmpty) return;

        Apply(item, RegionGeometry.Move(RectOf(item), dx, dy, area), area);
    }

    /// <summary>잡은 변을 끈 만큼 늘리거나 줄인다. 반대편은 그대로다.</summary>
    public void ResizeBy(RegionItem item, HorizontalAlignment horizontal, VerticalAlignment vertical, double dx, double dy)
    {
        var area = ImageArea;
        if (area.IsEmpty) return;

        Apply(item, RegionGeometry.Resize(RectOf(item), horizontal, vertical, dx, dy, MinimumOf(area), area), area);
    }

    private void Apply(RegionItem item, Rect rect, Rect area)
    {
        if (rect == RectOf(item)) return;

        _dragMoved = true;
        Place(item, rect, area, SourceSize);
        Report(item, rect, area, completed: false);
    }

    internal void EndDrag(RegionItem item)
    {
        if (!ReferenceEquals(_dragging, item)) return;

        _dragging = null;

        // 눌렀다 뗀 것은 고르기일 뿐이다. 안 움직였는데 "옮겼다" 고 저장하면 안 된다.
        if (_dragMoved) Report(item, RectOf(item), ImageArea, completed: true);

        _dragMoved = false;
    }

    private void Report(RegionItem item, Rect rect, Rect area, bool completed)
    {
        if (item.Region is not { } region || area.IsEmpty) return;

        var edit = new RegionEdit(region, RegionGeometry.ToRatio(rect, area), completed);

        if (EditCommand?.CanExecute(edit) == true) EditCommand.Execute(edit);
    }
}
