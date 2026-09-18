using System;
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

/// <summary>칸을 끌어 옮기거나 크기·각도를 바꾼 결과. <see cref="Rect"/> 는 자리 기준 0~1(돌리기 전 상자), <see cref="Angle"/> 은 도.</summary>
public sealed record CellEdit(NamedRegion Region, RegionCell Cell, Rect Rect, double Angle, bool Completed);

/// <summary>
/// 미리보기 그림 위에 이름 붙인 자리(<see cref="RegionItem"/>)와 그 안의 칸(<see cref="RegionCellItem"/>)을 놓는 캔버스. 영역 지정 중에만 마우스를 받는다.
/// </summary>
/// <remarks>
/// <b>왜 겹그림(DetectionOverlay)이 그리지 않는가</b> - 겹그림은 클릭을 게임으로 흘려야 해서 히트 테스트를 끈 채 그리기만 한다.
/// 손잡이를 거기에 그리고 마우스 계산을 VM 이 하던 것을, 참조 프로젝트(wpf_test_app.ResizeAdorner)처럼 Thumb 과 어도너로
/// 바꿨다 - 커서·잡기·끌기가 WPF 것이라 손으로 맞출 것이 없다. 게임으로 클릭이 안 새는 것은 <see cref="IsEditing"/> 이
/// 지킨다: 꺼져 있으면 이 캔버스가 히트 테스트에서 빠져 밑의 그림(전달)이 받는다.
///
/// 자리는 <b>그림이 놓인 자리</b>(픽셀)에, 칸은 <b>자리 항목 안</b>(자리 기준 0~1)에 놓인다. 끌면 비율로 되돌려 <see cref="EditCommand"/>·
/// <see cref="CellEditCommand"/> 로 올린다. 저장은 VM 몫이다. 칸 항목은 자리 항목과 형제이고 z 순서가 위다 - 자리를 끄는 동안 칸도 같이 옮겨 놓는다.
///
/// <b>고르기</b> - 칸을 누르면 칸(<see cref="SelectedCell"/>)과 그 자리를, 칸 밖의 자리 안쪽을 누르면 자리만 고른다. 어도너는 한 번에 하나 -
/// 칸을 고른 동안 자리 어도너는 뗀다(손잡이가 겹치지 않게).
/// 빈 자리를 누르면 항목이 없어 밑으로 흘러가고, VM 이 새 자리 그리기를 시작한다 - 그 길은 그대로다.
/// </remarks>
public sealed class RegionCanvas : Canvas
{
    private readonly Dictionary<NamedRegion, RegionItem> _items = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RegionCell, RegionCellItem> _cells = new(ReferenceEqualityComparer.Instance);
    private RegionItemBase? _dragging;
    private bool _dragMoved;

    /// <summary>
    /// "뚫은" 자리 - 이 자리는 이미 고른 채로 한 번 더 눌려서, 그 안의 칸이 이제 클릭을 받는다(<see cref="PlaceCells"/>).
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-18) "처음 클릭하면 영역이 선택되고 또 클릭하면 구역이 선택되어야". 칸은 자리 위 형제(z 순서가 위)라 그냥 두면 새 자리의
    /// 「전체」 칸(자리와 같은 크기로 시작)이 늘 클릭을 먼저 먹어 자리를 고르거나 손잡이로 옮길 수가 없었다. 자리가 고른 것이 되기 전까지는 칸이
    /// 히트 테스트에서 빠져, 첫 클릭이 곧바로 자리로 흘러가 고르고 - 같은 눌러끌기 안에서 옮기기·크기 손잡이도 바로 커서를 따라간다.
    /// </remarks>
    private NamedRegion? _drilled;

    /// <summary>끌기를 시작할 때 마우스(캔버스 좌표)와 항목 사각형. 끄는 동안은 늘 여기서부터의 전체 이동량으로 놓는다.</summary>
    private Point _dragStartMouse;
    private Rect _dragStartRect;

    public RegionCanvas()
    {
        // IsEditing 기본값(false)과 맞춰 접어 둔다 - 안 그러면 한 번도 안 켠 채로는 항목이 보여, 영역 보기가 꺼져 있는데 자리가 그려지다가
        // 켰다 끄면 사라졌다(사용자, 2026-09-17).
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
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
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) =>
        {
            // 새로 고른(또는 바뀐) 자리는 아직 안 뚫었다 - 다음 클릭은 자리부터다. 뚫는 것은 Select() 가 "이미 고른 자리를 또 눌렀다" 에서 한다.
            var canvas = (RegionCanvas)d;

            canvas._drilled = null;
            canvas.Rebuild();
        }));

    /// <summary>고른 자리. 항목을 누르면 여기로 올라가고, 목록에서 고르면 여기로 내려온다.</summary>
    public NamedRegion? SelectedRegion
    {
        get => (NamedRegion?)GetValue(SelectedRegionProperty);
        set => SetValue(SelectedRegionProperty, value);
    }

    public static readonly DependencyProperty SelectedCellProperty = DependencyProperty.Register(
        nameof(SelectedCell), typeof(RegionCell), typeof(RegionCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((RegionCanvas)d).Rebuild()));

    /// <summary>고른 칸. 없으면 자리 전체를 고른 것이다.</summary>
    public RegionCell? SelectedCell
    {
        get => (RegionCell?)GetValue(SelectedCellProperty);
        set => SetValue(SelectedCellProperty, value);
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

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(RegionCanvas), new PropertyMetadata(1d, (d, _) => ((RegionCanvas)d).ApplyZoom()));

    /// <summary>
    /// 미리보기 배율(PreviewZoom). 항목마다 역수를 넣어 테두리·손잡이·이름표가 확대해도 같은 크기로 보이게 한다(라벨링 캔버스와 같게, 2026-09-15).
    /// </summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    private double InverseZoom => Zoom > 0 ? 1 / Zoom : 1;

    private void ApplyZoom()
    {
        foreach (var item in AllItems) item.InverseZoom = InverseZoom;
    }

    public static readonly DependencyProperty EditCommandProperty = DependencyProperty.Register(
        nameof(EditCommand), typeof(ICommand), typeof(RegionCanvas), new PropertyMetadata(null));

    /// <summary>자리를 끌 때마다 <see cref="RegionEdit"/> 를 받는다. 놓을 때 것은 <c>Completed</c>.</summary>
    public ICommand? EditCommand
    {
        get => (ICommand?)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public static readonly DependencyProperty CellEditCommandProperty = DependencyProperty.Register(
        nameof(CellEditCommand), typeof(ICommand), typeof(RegionCanvas), new PropertyMetadata(null));

    /// <summary>칸을 끌 때마다 <see cref="CellEdit"/> 를 받는다. 놓을 때 것은 <c>Completed</c>.</summary>
    public ICommand? CellEditCommand
    {
        get => (ICommand?)GetValue(CellEditCommandProperty);
        set => SetValue(CellEditCommandProperty, value);
    }

    /// <summary>놓인 자리 항목들. 하네스가 본다.</summary>
    public IReadOnlyCollection<RegionItem> Items => _items.Values;

    /// <summary>놓인 칸 항목들. 하네스가 본다.</summary>
    public IReadOnlyCollection<RegionCellItem> CellItems => _cells.Values;

    private IEnumerable<RegionItemBase> AllItems => _items.Values.Cast<RegionItemBase>().Concat(_cells.Values);

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

    /// <summary>자리마다 항목 하나, 칸마다 항목 하나. 끌고 있는 것은 건드리지 않는다 - 되쏜 값으로 손 밑에서 흔들리면 안 된다.</summary>
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

        var wantedCells = wanted.SelectMany(r => r.Cells).ToList();

        foreach (var cell in _cells.Keys.Where(known => !wantedCells.Any(w => ReferenceEquals(w, known))).ToList())
        {
            Children.Remove(_cells[cell]);
            _cells.Remove(cell);
        }

        foreach (var region in wanted)
        {
            if (!_items.TryGetValue(region, out var item))
            {
                item = new RegionItem { Region = region, InverseZoom = InverseZoom };
                SetZIndex(item, 0);
                _items.Add(region, item);
                Children.Add(item);
            }

            // 그림이 없으면(캡처 전) 놓을 자리가 없다. 항목만 숨기면 어도너는 다른 층이라 크기 0 인 항목에 붙어 점으로 남았다(사용자, 2026-09-15).
            var placeable = !area.IsEmpty && region.Width > 0 && region.Height > 0;
            var cellPicked = SelectedCell is { } picked && region.Cells.Any(c => ReferenceEquals(c, picked));

            // 어도너(테두리·손잡이)는 편집 중이고 놓인 자리에만 - 아닐 때 보이면 잡히는 줄 안다. 칸을 고른 동안에는 칸 어도너만.
            item.IsSelected = IsEditing && placeable && ReferenceEquals(region, SelectedRegion) && !cellPicked;

            if (!placeable)
            {
                item.Visibility = Visibility.Collapsed;
                foreach (var cell in region.Cells) HideCell(region, cell);
                continue;
            }

            item.Visibility = Visibility.Visible;

            if (!ReferenceEquals(item, _dragging)) Place(item, RegionGeometry.ToCanvas(region.Rect, area), area, source);

            PlaceCells(region, RectOf(item));
        }
    }

    /// <summary>
    /// 자리의 칸들을 자리 항목 사각형 안에 놓는다. 끄고 있는 칸은 건드리지 않는다.
    /// </summary>
    /// <remarks>
    /// <b>칸은 자리를 "뚫어야"(<see cref="_drilled"/>) 마우스를 받는다</b>(사용자, 2026-09-18 "처음 클릭하면 영역이 선택되고 또 클릭하면 구역이 선택되어야").
    /// 칸이 자리 위 형제라(z 순서가 위) 안 그러면 자리 어디를 눌러도 늘 칸부터 먼저 잡혀 자리를 고르거나 옮길 수가 없었다(새 자리는 「전체」 칸이 자리와 같은 크기로 시작해 늘 이 꼴이다).
    /// 안 뚫렸으면 <see cref="UIElement.IsHitTestVisible"/> 을 꺼서 클릭이 밑의 자리로 그냥 흘러가게 한다 - 그 클릭이 자리를 고르고, 그 자리에서 곧바로
    /// 옮기기·크기 손잡이가 커서를 따라간다(같은 눌러끌기 안에서 된다 - 히트 테스트는 처음 누른 순간에 정해지고 그때는 자리를 골랐으니 손잡이가 그 아래에 있다).
    /// 이미 고른 자리를 또 누르면 그제서야 뚫려 칸이 받는다(<see cref="Select"/>). 트리에서 곧바로 칸을 골랐을 때도(<see cref="SelectedCell"/>) 뚫린 것으로 친다 -
    /// 이미 고른 칸을 몸으로 못 끌면 이상하다.
    /// </remarks>
    private void PlaceCells(NamedRegion region, Rect regionCanvas)
    {
        var area = ImageArea;
        var source = SourceSize;
        // SelectedCell 이 "이 자리" 의 칸인지까지 본다(그냥 not null 이 아니라) - SelectedRegion 만 바깥에서 바로 바뀌고 SelectedCell 은
        // 아직 이전 자리의 칸을 들고 있는 한 틈(예: 목록에서 다른 자리를 고르는 그 순간)에도 엉뚱한 자리의 칸이 뚫린 것처럼 보이면 안 된다.
        var regionPicked = ReferenceEquals(region, SelectedRegion) && (ReferenceEquals(region, _drilled) || region.Cells.Any(c => ReferenceEquals(c, SelectedCell)));

        foreach (var cell in region.Cells)
        {
            if (!_cells.TryGetValue(cell, out var item))
            {
                item = new RegionCellItem { Region = region, Cell = cell, InverseZoom = InverseZoom, CellAngle = cell.Angle };
                SetZIndex(item, 1);
                _cells.Add(cell, item);
                Children.Add(item);
            }

            item.Visibility = Visibility.Visible;
            item.IsSelected = IsEditing && ReferenceEquals(cell, SelectedCell);
            item.IsHitTestVisible = regionPicked;

            if (ReferenceEquals(item, _dragging)) continue;

            item.CellAngle = cell.Angle;
            Place(item, RegionGeometry.CellToCanvas(cell.Rect, regionCanvas), area, source);
        }
    }

    private void HideCell(NamedRegion region, RegionCell cell)
    {
        if (!_cells.TryGetValue(cell, out var item))
        {
            item = new RegionCellItem { Region = region, Cell = cell, InverseZoom = InverseZoom };
            SetZIndex(item, 1);
            _cells.Add(cell, item);
            Children.Add(item);
        }

        item.IsSelected = false;
        item.Visibility = Visibility.Collapsed;
    }

    private static void Place(RegionItemBase item, Rect rect, Rect area, Size source)
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

    private static Rect RectOf(RegionItemBase item) => new(GetLeft(item), GetTop(item), item.Width, item.Height);

    /// <summary>읽을 만한 최소 크기(0~1 의 0.004, <c>NamedRegion.IsUsable</c> 과 같다)를 캔버스 픽셀로.</summary>
    private static Size MinimumOf(Rect area) => new(0.004 * area.Width, 0.004 * area.Height);

    /// <summary>칸의 최소 크기(캔버스 픽셀). 끌다 점이 되면 손잡이를 다시 못 잡는다.</summary>
    private Size CellMinimum => new(4 * InverseZoom, 4 * InverseZoom);

    /// <summary>칸이 든 자리 항목의 사각형.</summary>
    private Rect RegionRectOf(RegionCellItem cell)
        => cell.Region is { } region && _items.TryGetValue(region, out var owner) ? RectOf(owner) : Rect.Empty;

    internal void Select(RegionItemBase item)
    {
        switch (item)
        {
            case RegionCellItem { Region: { } region, Cell: { } cell }:
                if (!ReferenceEquals(region, SelectedRegion)) SelectedRegion = region;
                if (!ReferenceEquals(cell, SelectedCell)) SelectedCell = cell;
                break;

            case RegionItem { Region: { } region }:
                if (SelectedCell is not null) SelectedCell = null;

                if (ReferenceEquals(region, SelectedRegion))
                {
                    // 이미 고른 자리를 또 눌렀다 - 이제부터 그 안의 칸이 클릭을 받는다(두 번째 클릭에서 구역으로).
                    if (!ReferenceEquals(region, _drilled))
                    {
                        _drilled = region;
                        Rebuild();
                    }
                }
                else
                {
                    SelectedRegion = region; // 프로퍼티 콜백이 _drilled 를 지우고 다시 놓는다.
                }

                break;
        }
    }

    internal void BeginDrag(RegionItemBase item)
    {
        Select(item);
        _dragging = item;
        _dragMoved = false;
        _dragStartMouse = Mouse.GetPosition(this);
        _dragStartRect = RectOf(item);
    }

    // ── 손잡이 끌기 ──────────────────────────────────────────────────────
    //    끄는 동안은 Thumb 변위(DragDelta)를 더하지 않고, 시작할 때 잰 마우스·사각형에서 지금 마우스까지의 전체 이동량(캔버스 좌표)으로 놓는다 -
    //    변위를 쌓으면 한 번 빗나간 것이 끝까지 남는다. 캔버스 좌표는 미리보기 확대(LayoutTransform) 안이라 배율을 따로 안 곱하고, 돌린 칸도 화면 방향 그대로다.
    //    사용자, 2026-09-16 "adorner 포인터랑 마우스 포인터가 안 맞는다" 에 "변위가 배치보다 먼저 겹친다" 고 보고 바꿨으나, 실제 마우스 검사
    //    (--region-drag, --busy 포함)로는 바꾸기 전 코드도 배율 1·2 에서 벌어짐 0px 였다 - 그 원인은 재현되지 않았다. 어긋남의 원인은 아직 모른다.

    /// <summary>끄는 중인 항목을 지금 마우스 자리로 옮긴다(옮기기 손잡이).</summary>
    internal void DragMove(RegionItemBase item)
    {
        if (!ReferenceEquals(item, _dragging)) return;

        MoveFrom(item, _dragStartRect, Mouse.GetPosition(this) - _dragStartMouse);
    }

    /// <summary>끄는 중인 항목의 잡은 변을 지금 마우스 자리까지 끈다(크기 손잡이).</summary>
    internal void DragResize(RegionItemBase item, HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        if (!ReferenceEquals(item, _dragging)) return;

        ResizeFrom(item, _dragStartRect, horizontal, vertical, Mouse.GetPosition(this) - _dragStartMouse);
    }

    /// <summary>손잡이 변위(항목의 돌린 좌표계)를 캔버스 좌표로. 자리는 안 돌아 그대로다.</summary>
    private static Vector ToCanvasDelta(RegionItemBase item, double dx, double dy)
    {
        if (Math.Abs(item.Angle) < 0.01) return new Vector(dx, dy);

        var radians = item.Angle * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        return new Vector((dx * cos) - (dy * sin), (dx * sin) + (dy * cos));
    }

    /// <summary>안쪽을 끈 만큼 옮긴다(항목 좌표 변위). 자리는 그림 밖으로, 칸은 자리 밖으로 안 나간다. 하네스가 마우스 없이 부른다.</summary>
    public void MoveBy(RegionItemBase item, double dx, double dy) => MoveFrom(item, RectOf(item), ToCanvasDelta(item, dx, dy));

    /// <summary>시작 사각형에서 캔버스 좌표 이동량만큼 옮긴다.</summary>
    private void MoveFrom(RegionItemBase item, Rect start, Vector delta)
    {
        switch (item)
        {
            case RegionItem region when ImageArea is { IsEmpty: false } area:
                ApplyRegion(region, RegionGeometry.Move(start, delta.X, delta.Y, area), area);
                break;

            case RegionCellItem cell when RegionRectOf(cell) is { IsEmpty: false } owner:
                ApplyCell(cell, RegionGeometry.Move(start, delta.X, delta.Y, owner), cell.CellAngle, owner);
                break;
        }
    }

    /// <summary>잡은 변을 끈 만큼 늘리거나 줄인다(항목 좌표 변위). 반대편은 그대로다(돌린 칸은 화면에서 제자리). 하네스가 마우스 없이 부른다.</summary>
    public void ResizeBy(RegionItemBase item, HorizontalAlignment horizontal, VerticalAlignment vertical, double dx, double dy)
        => ResizeFrom(item, RectOf(item), horizontal, vertical, ToCanvasDelta(item, dx, dy));

    /// <summary>시작 사각형에서 캔버스 좌표 이동량만큼 잡은 변을 끈다.</summary>
    private void ResizeFrom(RegionItemBase item, Rect start, HorizontalAlignment horizontal, VerticalAlignment vertical, Vector delta)
    {
        switch (item)
        {
            case RegionItem region when ImageArea is { IsEmpty: false } area:
                ApplyRegion(region, RegionGeometry.Resize(start, horizontal, vertical, delta.X, delta.Y, MinimumOf(area), area), area);
                break;

            case RegionCellItem cell when RegionRectOf(cell) is { IsEmpty: false } owner:
            {
                // 안 돌린 칸은 캔버스 이동량이 곧 칸의 가로·세로 변위다. 돌린 칸은 ResizeRotated 가 칸의 축으로 돌린다.
                var rect = Math.Abs(cell.CellAngle) < 0.01
                    ? RegionGeometry.Resize(start, horizontal, vertical, delta.X, delta.Y, CellMinimum, owner)
                    : RegionGeometry.ResizeRotated(start, cell.CellAngle, horizontal, vertical, delta.X, delta.Y, CellMinimum);

                ApplyCell(cell, rect, cell.CellAngle, owner);
                break;
            }
        }
    }

    /// <summary>칸을 그 각도로 돌린다(회전 손잡이).</summary>
    public void RotateTo(RegionItemBase item, double angle)
    {
        if (item is RegionCellItem cell && RegionRectOf(cell) is { IsEmpty: false } owner)
            ApplyCell(cell, RectOf(cell), Math.Round(angle, 1), owner);
    }

    private void ApplyRegion(RegionItem item, Rect rect, Rect area)
    {
        if (rect == RectOf(item)) return;

        _dragMoved = true;
        Place(item, rect, area, SourceSize);

        // 칸은 자리를 따라간다 - 끄는 동안에도 같이 놓는다.
        if (item.Region is { } region) PlaceCells(region, rect);

        ReportRegion(item, rect, area, completed: false);
    }

    private void ApplyCell(RegionCellItem item, Rect rect, double angle, Rect owner)
    {
        // 돌린 칸도 가운데는 자리 안에 둔다 - 가운데가 밖으로 나가면 다시 잡기 어렵다.
        var center = new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        var inside = new Point(Math.Clamp(center.X, owner.Left, owner.Right), Math.Clamp(center.Y, owner.Top, owner.Bottom));
        rect.Offset(inside.X - center.X, inside.Y - center.Y);

        if (rect == RectOf(item) && Math.Abs(angle - item.CellAngle) < 0.01) return;

        _dragMoved = true;
        item.CellAngle = angle;
        Place(item, rect, ImageArea, SourceSize);
        ReportCell(item, rect, angle, owner, completed: false);
    }

    internal void EndDrag(RegionItemBase item)
    {
        if (!ReferenceEquals(_dragging, item)) return;

        _dragging = null;

        // 눌렀다 뗀 것은 고르기일 뿐이다. 안 움직였는데 "옮겼다" 고 저장하면 안 된다.
        if (_dragMoved)
        {
            switch (item)
            {
                case RegionItem region:
                    ReportRegion(region, RectOf(region), ImageArea, completed: true);
                    break;

                case RegionCellItem cell when RegionRectOf(cell) is { IsEmpty: false } owner:
                    ReportCell(cell, RectOf(cell), cell.CellAngle, owner, completed: true);
                    break;
            }
        }

        _dragMoved = false;
    }

    private void ReportRegion(RegionItem item, Rect rect, Rect area, bool completed)
    {
        if (item.Region is not { } region || area.IsEmpty) return;

        var edit = new RegionEdit(region, RegionGeometry.ToRatio(rect, area), completed);

        if (EditCommand?.CanExecute(edit) == true) EditCommand.Execute(edit);
    }

    private void ReportCell(RegionCellItem item, Rect rect, double angle, Rect owner, bool completed)
    {
        if (item is not { Region: { } region, Cell: { } cell } || owner.IsEmpty) return;

        // 돌린 칸은 상자가 자리 밖으로 조금 나갈 수 있다 - 접지 않고 비율 그대로 올린다(가운데는 ApplyCell 이 자리 안에 둔다).
        var ratio = new Rect((rect.X - owner.X) / owner.Width, (rect.Y - owner.Y) / owner.Height, rect.Width / owner.Width, rect.Height / owner.Height);
        var edit = new CellEdit(region, cell, Math.Abs(angle) < 0.01 ? RegionGeometry.CellToRatio(rect, owner) : ratio, angle, completed);

        if (CellEditCommand?.CanExecute(edit) == true) CellEditCommand.Execute(edit);
    }
}
