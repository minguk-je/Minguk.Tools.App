using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Minguk.Tools.Markup;

/// <summary>
/// 미리보기 위에 찾은 몹을 겹쳐 그린다.
/// </summary>
/// <remarks>
/// <b>왜 <see cref="LabelCanvas"/> 를 안 쓰는가</b>
///
/// 그쪽은 그림도 제가 그린다. 미리보기는 D3DImage 를 <c>Image</c> 요소가 그리고 있고
/// 그 경로가 60fps 로 도는 빠른 길이라, 그리는 주체를 바꾸면 얻을 것 없이 느려질 수 있다.
/// 여기서는 <b>사각형만</b> 그리고 그림은 아래 <c>Image</c> 에 맡긴다.
///
/// <b>자리를 어떻게 맞추는가</b>
///
/// <c>Image</c> 가 <c>Stretch=Uniform</c> 이라 비율을 지키며 가운데 놓이고 남는 자리는
/// 빈 띠가 된다. 그래서 0~1 좌표를 이 컨트롤 전체가 아니라 <b>그림이 실제로 놓인 자리</b>에
/// 맞춰야 한다. 그 자리를 스스로 계산하려면 원본 크기를 알아야 해서 <see cref="Source"/> 를
/// 받는다 - <b>그리지는 않고 크기만 본다.</b>
///
/// 마우스는 통과시켜야 한다(<c>IsHitTestVisible=false</c>). 미리보기는 클릭을 대상 창으로
/// 넘기는 일을 하고 있어서, 여기서 가로채면 그것이 죽는다.
/// </remarks>
public sealed class DetectionOverlay : FrameworkElement
{
    public DetectionOverlay()
    {
        IsHitTestVisible = false;
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>아래 <c>Image</c> 와 같은 것. 크기를 알기 위해서만 쓴다 - 그리지 않는다.</summary>
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DependencyProperty DetectionsProperty = DependencyProperty.Register(
        nameof(Detections), typeof(ObservableCollection<PredictedBox>), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDetectionsChanged));

    public ObservableCollection<PredictedBox>? Detections
    {
        get => (ObservableCollection<PredictedBox>?)GetValue(DetectionsProperty);
        set => SetValue(DetectionsProperty, value);
    }

    private static void OnDetectionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (DetectionOverlay)d;

        // 옛 컬렉션의 구독을 반드시 푼다. 안 그러면 화면을 닫아도 그 컬렉션이 안 죽는다.
        if (e.OldValue is ObservableCollection<PredictedBox> old)
            old.CollectionChanged -= overlay.OnCollectionChanged;

        if (e.NewValue is ObservableCollection<PredictedBox> added)
            added.CollectionChanged += overlay.OnCollectionChanged;

        overlay.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    /// <summary>글자 영역 색. 몹 색(황금각 팔레트)과 헷갈리지 않게 청록 하나로 고정한다.</summary>
    private static readonly Color OcrColour = Color.FromRgb(0x00, 0xBC, 0xD4);

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// 미리보기 배율(PreviewZoom). 선 굵기·글자 크기·여백을 이것으로 나눠, 확대해도 화면에서 늘 같은 크기로 그린다 - 라벨링 캔버스와 같다(2026-09-15).
    /// </summary>
    /// <remarks>판이 LayoutTransform 으로 커져 이 요소도 같이 커진다. 배율이 바뀌면 다시 그려야 해 속성으로 받는다(크기는 그대로라 다시 그리기가 안 온다).</remarks>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>화면 1px 이 이 요소 좌표로 몇인가.</summary>
    private double Unit => Zoom > 0 ? 1 / Zoom : 1;

    private FormattedText Text(string text, double size, Brush brush)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size * Unit, brush,
               VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        if (ComputeImageRect() is not { IsEmpty: false } area) return;

        if (Detections is { Count: > 0 } detections)
            foreach (var detection in detections) Draw(dc, area, detection);

        // 새 자리를 끄는 중인 사각형.
        DrawOcrRegion(dc, area, RegionDraft, dashed: true, text: null);

        if (ShowRegions && Regions is { } named)
            foreach (var region in named)
            {
                // 편집 중에는 캔버스(RegionCanvas)의 항목이 제 테두리·이름표를 그린다 - 여기서는 읽은 글자만 붙인다(두 겹이 안 되게).
                if (!IsRegionEditing) DrawNamed(dc, area, region, selected: ReferenceEquals(region, SelectedRegion));
                if (region.KeepReading) DrawReadText(dc, area, region);
            }
    }

    public static readonly DependencyProperty SelectedRegionProperty = DependencyProperty.Register(
        nameof(SelectedRegion), typeof(Minguk.Tools.Vision.Regions.NamedRegion), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>고른 자리. 목록에서 고른 것이 어느 것인지 굵게 보인다.</summary>
    public Minguk.Tools.Vision.Regions.NamedRegion? SelectedRegion
    {
        get => (Minguk.Tools.Vision.Regions.NamedRegion?)GetValue(SelectedRegionProperty);
        set => SetValue(SelectedRegionProperty, value);
    }

    public static readonly DependencyProperty IsRegionEditingProperty = DependencyProperty.Register(
        nameof(IsRegionEditing), typeof(bool), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>영역 지정 중인가. 그동안 이름 붙인 자리는 여기서 안 그린다 - 캔버스의 항목이 그린다.</summary>
    public bool IsRegionEditing
    {
        get => (bool)GetValue(IsRegionEditingProperty);
        set => SetValue(IsRegionEditingProperty, value);
    }

    /// <summary>이름 붙인 자리들. 화면에서 만든 것을 그대로 보여 준다.</summary>
    public static readonly DependencyProperty RegionsProperty = DependencyProperty.Register(
        nameof(Regions), typeof(IEnumerable<Minguk.Tools.Vision.Regions.NamedRegion>), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<Minguk.Tools.Vision.Regions.NamedRegion>? Regions
    {
        get => (IEnumerable<Minguk.Tools.Vision.Regions.NamedRegion>?)GetValue(RegionsProperty);
        set => SetValue(RegionsProperty, value);
    }

    /// <summary>
    /// 자리가 바뀔 때마다 오르는 값. 이것이 바뀌면 다시 그린다.
    /// </summary>
    /// <remarks>
    /// 목록(<see cref="Regions"/>)은 같은 객체를 그대로 들고 있고 안의 x·너비만 바뀌는 일이 있다. 컬렉션이
    /// 안 바뀌니 알림이 안 오고, 화면은 옛 자리를 그대로 그린다 - 사람은 칸을 고쳤는데 사각형이 안 움직이는
    /// 것을 본다. 고친 쪽이 이 값을 올려 알린다.
    /// </remarks>
    public static readonly DependencyProperty RegionsRevisionProperty = DependencyProperty.Register(
        nameof(RegionsRevision), typeof(int), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int RegionsRevision
    {
        get => (int)GetValue(RegionsRevisionProperty);
        set => SetValue(RegionsRevisionProperty, value);
    }

    public static readonly DependencyProperty ShowRegionsProperty = DependencyProperty.Register(
        nameof(ShowRegions), typeof(bool), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowRegions
    {
        get => (bool)GetValue(ShowRegionsProperty);
        set => SetValue(ShowRegionsProperty, value);
    }

    public static readonly DependencyProperty RegionDraftProperty = DependencyProperty.Register(
        nameof(RegionDraft), typeof(Rect), typeof(DetectionOverlay),
        new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public Rect RegionDraft
    {
        get => (Rect)GetValue(RegionDraftProperty);
        set => SetValue(RegionDraftProperty, value);
    }

    /// <summary>이름 붙인 자리 하나. 글자 영역과 색을 달리해 헷갈리지 않게 한다.</summary>
    private void DrawNamed(DrawingContext dc, Rect area, Minguk.Tools.Vision.Regions.NamedRegion region, bool selected)
    {
        var box = region.Rect;

        if (box.Width <= 0 || box.Height <= 0) return;

        var rect = new Rect(
            area.X + (box.X * area.Width),
            area.Y + (box.Y * area.Height),
            box.Width * area.Width,
            box.Height * area.Height);

        // 라벨링 캔버스와 같은 모양 - 1.5px(고르면 3px) 테두리, 자리 색 바탕에 흰 11px 이름표. 크기는 배율로 나눠 화면에서 같게.
        var unit = Unit;
        var colour = selected ? SelectedNamedColour : NamedColour;
        var pen = new Pen(new SolidColorBrush(colour), (selected ? 3d : 1.5d) * unit);

        pen.Freeze();
        dc.DrawRectangle(null, pen, rect);

        DrawCells(dc, rect, region, unit);

        var formatted = Text(region.Name, 11d, Brushes.White);

        // 이름표는 사각형 위에. 위가 좁으면 안쪽에 넣는다 - 화면 밖으로 나가면 안 보인다.
        var top = rect.Top - formatted.Height - (2 * unit);
        if (top < area.Top) top = rect.Top + (2 * unit);

        var background = new SolidColorBrush(colour) { Opacity = 0.85 };
        background.Freeze();

        dc.DrawRectangle(background, null, new Rect(rect.X, top, formatted.Width + (6 * unit), formatted.Height + (2 * unit)));
        dc.DrawText(formatted, new Point(rect.X + (3 * unit), top + unit));
    }

    /// <summary>
    /// 자리 안의 칸 - 초록 점선(편집기의 칸 항목과 같은 색). 돌린 칸은 가운데를 중심으로 돌려 그린다.
    /// </summary>
    /// <remarks>자리 전체를 덮는 돌리지 않은 칸 하나뿐이면(새 자리·옛 파일) 자리 테두리와 겹쳐 안 그린다.</remarks>
    private static void DrawCells(DrawingContext dc, Rect rect, Minguk.Tools.Vision.Regions.NamedRegion region, double unit)
    {
        if (region.Cells.Count == 1 && region.Cells[0] is { X: 0, Y: 0, Width: 1, Height: 1 } whole && Math.Abs(whole.Angle) < 0.01) return;

        var pen = new Pen(new SolidColorBrush(CellColour), 1d * unit) { DashStyle = new DashStyle([3, 2], 0) };
        pen.Freeze();

        foreach (var cell in region.Cells)
        {
            var cellBox = new Rect(rect.X + (cell.X * rect.Width), rect.Y + (cell.Y * rect.Height), cell.Width * rect.Width, cell.Height * rect.Height);
            var turned = Math.Abs(cell.Angle) >= 0.01;

            if (turned) dc.PushTransform(new RotateTransform(cell.Angle, cellBox.X + (cellBox.Width / 2), cellBox.Y + (cellBox.Height / 2)));

            dc.DrawRectangle(null, pen, cellBox);

            if (turned) dc.Pop();
        }
    }

    /// <summary>칸의 색. 편집기(RegionChrome.xaml 의 RegionCellBrush)와 같다.</summary>
    private static readonly Color CellColour = Color.FromRgb(0x4C, 0xD9, 0x64);

    /// <summary>이름 붙인 자리의 색. 글자 영역(노랑)·몹(초록)과 달라야 한다.</summary>
    private static readonly Color NamedColour = Color.FromRgb(120, 200, 255);

    /// <summary>목록에서 고른 자리. 여럿 사이에서 무엇을 골랐는지 한눈에 갈리게 마젠타 - 게임 화면에 거의 안 나오는 색이다(사용자, 2026-09-18). 편집기(RegionSelectedBrush)와 같다.</summary>
    private static readonly Color SelectedNamedColour = Color.FromRgb(0xFF, 0x2B, 0xD6);

    /// <summary>계속 읽기를 켠 자리 아래에 읽은 글자(청록 바탕). 아직 못 읽었으면 「읽는 중」.</summary>
    private void DrawReadText(DrawingContext dc, Rect area, Minguk.Tools.Vision.Regions.NamedRegion region)
    {
        var box = region.Rect;
        if (box.Width <= 0 || box.Height <= 0) return;

        var rect = new Rect(area.X + (box.X * area.Width), area.Y + (box.Y * area.Height), box.Width * area.Width, box.Height * area.Height);
        var unit = Unit;
        var formatted = Text(string.IsNullOrEmpty(region.LastText) ? "읽는 중…" : FirstLine(region.LastText, 60), 11d, Brushes.White);

        var top = rect.Bottom + (2 * unit);
        if (top + formatted.Height > area.Bottom) top = rect.Top - formatted.Height - (2 * unit);

        var background = new SolidColorBrush(OcrColour) { Opacity = 0.85 };
        background.Freeze();

        dc.DrawRectangle(background, null, new Rect(rect.X, top, formatted.Width + (6 * unit), formatted.Height + (2 * unit)));
        dc.DrawText(formatted, new Point(rect.X + (3 * unit), top + unit));
    }

    private void DrawOcrRegion(DrawingContext dc, Rect area, Rect region, bool dashed, string? text)
    {
        if (region.IsEmpty || region.Width <= 0 || region.Height <= 0) return;

        var rect = new Rect(
            area.X + (region.X * area.Width),
            area.Y + (region.Y * area.Height),
            region.Width * area.Width,
            region.Height * area.Height);

        var unit = Unit;
        var pen = new Pen(new SolidColorBrush(OcrColour), 2d * unit);
        if (dashed) pen.DashStyle = new DashStyle([4, 3], 0);
        pen.Freeze();

        dc.DrawRectangle(null, pen, rect);

        var caption = string.IsNullOrEmpty(text) ? "글자" : "글자: " + FirstLine(text, 60);
        var formatted = Text(caption, 11d, Brushes.White);

        var top = rect.Bottom + (2 * unit);
        if (top + formatted.Height > area.Bottom) top = rect.Top - formatted.Height - (2 * unit);

        var background = new SolidColorBrush(OcrColour) { Opacity = 0.85 };
        background.Freeze();

        dc.DrawRectangle(background, null, new Rect(rect.X, top, formatted.Width + (6 * unit), formatted.Height + (2 * unit)));
        dc.DrawText(formatted, new Point(rect.X + (3 * unit), top + unit));
    }

    private static string FirstLine(string text, int max)
    {
        var line = text.Split('\n')[0].TrimEnd('\r');
        return line.Length <= max ? line : line[..max] + "…";
    }

    /// <summary>
    /// 그림이 실제로 놓인 자리. <c>Stretch=Uniform</c> 과 같은 셈이다.
    /// </summary>
    private Rect ComputeImageRect()
    {
        if (Source is not { Width: > 0, Height: > 0 } image) return Rect.Empty;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return Rect.Empty;

        var scale = Math.Min(RenderSize.Width / image.Width, RenderSize.Height / image.Height);

        var width = image.Width * scale;
        var height = image.Height * scale;

        return new Rect((RenderSize.Width - width) / 2, (RenderSize.Height - height) / 2, width, height);
    }

    private void Draw(DrawingContext dc, Rect area, PredictedBox detection)
    {
        var box = detection.Box;

        var rect = new Rect(
            area.X + (box.Left * area.Width),
            area.Y + (box.Top * area.Height),
            box.Width * area.Width,
            box.Height * area.Height);

        var colour = LabelCanvas.ColorOf(box.ClassId);

        // 라벨링 화면과 같은 규칙 - 모델이 찾은 것은 2px 점선, 11px 이름표. 크기는 배율로 나눠 확대해도 화면에서 같게.
        var unit = Unit;
        var pen = new Pen(new SolidColorBrush(colour), 2d * unit) { DashStyle = new DashStyle([4, 3], 0) };
        pen.Freeze();

        dc.DrawRectangle(null, pen, rect);

        var text = Text(detection.Caption, 11d, Brushes.White);

        // 위쪽에 붙이되, 화면 위에 걸린 것은 안으로 넣는다. 안 그러면 잘려 안 보인다.
        var top = rect.Top - text.Height - (2 * unit);
        if (top < area.Top) top = rect.Top + (2 * unit);

        var background = new SolidColorBrush(colour) { Opacity = 0.7 };
        background.Freeze();

        dc.DrawRectangle(background, null, new Rect(rect.X, top, text.Width + (6 * unit), text.Height + (2 * unit)));
        dc.DrawText(text, new Point(rect.X + (3 * unit), top + unit));
    }

    /// <summary>
    /// 평범한 피어를 준다.
    /// </summary>
    /// <remarks>
    /// FrameworkElement 는 기본이 null 이라 UI 자동화 트리에 아예 안 나온다.
    /// 밖에서 화면을 확인할 때 이 자리가 통째로 비어 보인다.
    /// </remarks>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
}
