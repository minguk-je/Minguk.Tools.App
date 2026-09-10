using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Markup;

/// <summary>
/// 그림 한 장을 보여 주고 그 위에 라벨 사각형을 그리게 한다.
/// </summary>
/// <remarks>
/// <b>왜 컨트롤을 새로 만드는가</b>
///
/// 끌어서 사각형을 그리는 일은 마우스가 지금 어디 있는지를 매 순간 알아야 하는, 화면에
/// 붙은 일이다. ViewModel 로 올리면 픽셀 좌표가 오르내리기만 하고 얻는 것이 없다.
/// 대신 <b>결과</b>인 <see cref="Boxes"/> 만 바깥과 주고받는다 - 저장할지 말지는 화면이 정한다.
///
/// <b>왜 Viewbox 에 담지 않고 직접 그리는가</b>
///
/// Viewbox 로 늘리면 테두리 굵기와 글자까지 같이 늘어난다. 크게 확대한 그림에서는 사각형이
/// 뭉툭해져 어디가 경계인지 안 보인다. 여기서는 <b>그림만</b> 늘리고 테두리·글자는 늘 같은
/// 굵기로 그린다.
///
/// <b>좌표</b>
///
/// 바깥과는 0~1 로만 주고받는다(<see cref="LabelBox"/>). 화면 좌표로 오가는 일은 이 안에서
/// <see cref="ToScreen"/> · <see cref="ToNormalized"/> 두 곳에서만 한다.
/// </remarks>
public sealed class LabelCanvas : FrameworkElement
{
    /// <summary>사각형을 알아볼 만한 가장 작은 크기(화면 픽셀). 이보다 짧게 끌면 안 만든다.</summary>
    private const double MinimumDragPixels = 4d;

    /// <summary>모서리를 잡을 수 있는 두께(화면 픽셀).</summary>
    private const double HandleHitPixels = 8d;

    private Rect _imageRect = Rect.Empty;
    private Point? _dragStart;
    private Point _dragCurrent;

    static LabelCanvas()
    {
        FocusableProperty.OverrideMetadata(typeof(LabelCanvas), new FrameworkPropertyMetadata(true));
    }

    public LabelCanvas()
    {
        // 배경이 없으면 마우스 이벤트가 통과해 버린다. 빈 자리를 눌러 고르기를 풀 수 없다.
        // 투명 배경을 칠하는 것으로 히트 테스트 대상이 된다.
        ClipToBounds = true;
    }

    // ── 바깥과 주고받는 것 ───────────────────────────────────────────────

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(LabelCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public static readonly DependencyProperty BoxesProperty = DependencyProperty.Register(
        nameof(Boxes), typeof(ObservableCollection<LabelBox>), typeof(LabelCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnBoxesChanged));

    /// <summary>
    /// 지금 그림의 사각형들. 이 컨트롤이 <b>직접 고친다</b>.
    /// </summary>
    /// <remarks>
    /// 커맨드로 올렸다 내리지 않는다. 사각형을 하나 그릴 때마다 ViewModel 을 거치면
    /// 좌표를 두 번 옮겨 적게 되고, 그리는 동안의 중간 상태까지 올려야 해서 얻는 것이 없다.
    /// 바뀌었다는 것은 <see cref="CollectionChanged"/> 대신 컬렉션 자체가 알려 준다.
    /// </remarks>
    public ObservableCollection<LabelBox>? Boxes
    {
        get => (ObservableCollection<LabelBox>?)GetValue(BoxesProperty);
        set => SetValue(BoxesProperty, value);
    }

    public static readonly DependencyProperty PredictionsProperty = DependencyProperty.Register(
        nameof(Predictions), typeof(ObservableCollection<PredictedBox>), typeof(LabelCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPredictionsChanged));

    /// <summary>
    /// 모델이 찾아낸 것들. 사람이 찍은 <see cref="Boxes"/> 와 <b>따로</b> 그린다.
    /// </summary>
    /// <remarks>
    /// 섞어 그리면 무엇이 내가 찍은 것이고 무엇이 모델이 찾은 것인지 갈리지 않는다.
    /// 이쪽은 점선이고, 마우스로 고를 수도 지울 수도 없다 - 고칠 것은 사람이 찍은 쪽뿐이다.
    /// </remarks>
    public ObservableCollection<PredictedBox>? Predictions
    {
        get => (ObservableCollection<PredictedBox>?)GetValue(PredictionsProperty);
        set => SetValue(PredictionsProperty, value);
    }

    private static void OnPredictionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (LabelCanvas)d;

        if (e.OldValue is ObservableCollection<PredictedBox> old)
            old.CollectionChanged -= canvas.OnBoxesCollectionChanged;

        if (e.NewValue is ObservableCollection<PredictedBox> added)
            added.CollectionChanged += canvas.OnBoxesCollectionChanged;

        canvas.InvalidateVisual();
    }

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(LabelCanvas),
        new FrameworkPropertyMetadata(-1,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>고른 사각형의 자리. 아무것도 안 골랐으면 -1.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public static readonly DependencyProperty CurrentClassIdProperty = DependencyProperty.Register(
        nameof(CurrentClassId), typeof(int), typeof(LabelCanvas),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>새로 그리는 사각형에 붙일 몹 번호.</summary>
    public int CurrentClassId
    {
        get => (int)GetValue(CurrentClassIdProperty);
        set => SetValue(CurrentClassIdProperty, value);
    }

    public static readonly DependencyProperty ClassNamesProperty = DependencyProperty.Register(
        nameof(ClassNames), typeof(IReadOnlyList<string>), typeof(LabelCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>사각형 위에 적을 이름들. 번호가 곧 자리다.</summary>
    public IReadOnlyList<string>? ClassNames
    {
        get => (IReadOnlyList<string>?)GetValue(ClassNamesProperty);
        set => SetValue(ClassNamesProperty, value);
    }

    private static void OnBoxesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (LabelCanvas)d;

        // 옛 컬렉션의 구독을 반드시 푼다. 그림을 넘길 때마다 쌓이면 지운 그림의 컬렉션이
        // 바뀔 때도 다시 그리게 되고, 컨트롤이 살아 있는 한 그 컬렉션도 안 죽는다.
        if (e.OldValue is ObservableCollection<LabelBox> old)
            old.CollectionChanged -= canvas.OnBoxesCollectionChanged;

        if (e.NewValue is ObservableCollection<LabelBox> added)
            added.CollectionChanged += canvas.OnBoxesCollectionChanged;

        canvas.SelectedIndex = -1;
        canvas.InvalidateVisual();
    }

    private void OnBoxesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    // ── 좌표 옮기기 ──────────────────────────────────────────────────────

    /// <summary>0~1 을 화면 좌표로.</summary>
    private Rect ToScreen(LabelBox box) => new(
        _imageRect.X + (box.Left * _imageRect.Width),
        _imageRect.Y + (box.Top * _imageRect.Height),
        box.Width * _imageRect.Width,
        box.Height * _imageRect.Height);

    /// <summary>화면 좌표를 0~1 로. 그림 밖을 눌러도 0~1 안으로 접어 넣는다.</summary>
    private Point ToNormalized(Point point)
    {
        if (_imageRect.Width <= 0 || _imageRect.Height <= 0) return new Point(0, 0);

        return new Point(
            Math.Clamp((point.X - _imageRect.X) / _imageRect.Width, 0d, 1d),
            Math.Clamp((point.Y - _imageRect.Y) / _imageRect.Height, 0d, 1d));
    }

    /// <summary>
    /// 그림이 놓일 자리. 비율을 지키고 가운데 둔다.
    /// </summary>
    /// <remarks>
    /// 늘려서 꽉 채우면 안 된다 - 사람이 보고 찍은 사각형과 실제 그림의 비율이 어긋난다.
    /// </remarks>
    private Rect ComputeImageRect()
    {
        if (ImageSource is not { Width: > 0, Height: > 0 } image) return Rect.Empty;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return Rect.Empty;

        var scale = Math.Min(RenderSize.Width / image.Width, RenderSize.Height / image.Height);

        var width = image.Width * scale;
        var height = image.Height * scale;

        return new Rect((RenderSize.Width - width) / 2, (RenderSize.Height - height) / 2, width, height);
    }

    // ── 그리기 ───────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        _imageRect = ComputeImageRect();

        // 히트 테스트를 받으려면 무엇이든 칠해야 한다. 투명이어도 된다.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (_imageRect.IsEmpty)
        {
            DrawHint(dc, ImageSource is null ? "왼쪽에서 그림을 고르세요." : "그림을 읽지 못했습니다.");
            return;
        }

        dc.DrawImage(ImageSource, _imageRect);

        if (Boxes is { } boxes)
        {
            for (var i = 0; i < boxes.Count; i++) DrawBox(dc, boxes[i], i == SelectedIndex);
        }

        // 예측을 사람이 찍은 것보다 먼저 그린다. 겹쳤을 때 내가 찍은 것이 위로 와야
        // 무엇을 고치는 중인지 안 가린다.
        if (Predictions is { } predictions)
        {
            foreach (var prediction in predictions) DrawPrediction(dc, prediction);
        }

        DrawRubberBand(dc);
    }

    /// <summary>
    /// 모델이 찾은 것. 점선으로 그리고 얼마나 자신 있는지 같이 적는다.
    /// </summary>
    /// <remarks>
    /// 점선인 것과 이름 뒤에 %가 붙는 것, 둘로 사람이 찍은 것과 갈린다. 색은 같은 몹이면
    /// 같게 둔다 - 색까지 다르면 어느 몹을 찾았는지 알아보기 어렵다.
    /// </remarks>
    private void DrawPrediction(DrawingContext dc, PredictedBox prediction)
    {
        var rect = ToScreen(prediction.Box);
        var color = ColorOf(prediction.Box.ClassId);

        var pen = new Pen(new SolidColorBrush(color), 2d)
        {
            DashStyle = new DashStyle([4, 3], 0)
        };
        pen.Freeze();

        dc.DrawRectangle(null, pen, rect);

        var text = MakeText(prediction.Caption, 11d, Brushes.White);

        // 사람이 찍은 이름이 사각형 위에 붙으니, 이쪽은 아래에 붙여 겹치지 않게 한다.
        var top = rect.Bottom + 2;
        if (top + text.Height > _imageRect.Bottom) top = rect.Bottom - text.Height - 2;

        var background = new SolidColorBrush(color) { Opacity = 0.7 };
        background.Freeze();

        dc.DrawRectangle(background, null, new Rect(rect.X, top, text.Width + 6, text.Height + 2));
        dc.DrawText(text, new Point(rect.X + 3, top + 1));
    }

    private void DrawBox(DrawingContext dc, LabelBox box, bool selected)
    {
        var rect = ToScreen(box);
        var color = ColorOf(box.ClassId);

        // 고른 것은 굵게. 색까지 바꾸면 무슨 몹인지가 안 보인다.
        var pen = new Pen(new SolidColorBrush(color), selected ? 3d : 1.5d);
        pen.Freeze();

        dc.DrawRectangle(null, pen, rect);

        // 고른 것에만 모서리를 그린다. 늘 그리면 사각형이 많을 때 화면이 점으로 뒤덮인다.
        if (selected)
        {
            var handle = new SolidColorBrush(color);
            handle.Freeze();

            foreach (var corner in Corners(rect))
                dc.DrawRectangle(handle, null, new Rect(corner.X - 3, corner.Y - 3, 6, 6));
        }

        DrawName(dc, rect, box.ClassId, color);
    }

    private void DrawName(DrawingContext dc, Rect rect, int classId, Color color)
    {
        var text = MakeText(NameOf(classId), 11d, Brushes.White);

        // 사각형 위에 붙인다. 위쪽 끝에 걸린 사각형이면 안으로 넣는다 - 안 그러면 잘려서 안 보인다.
        var top = rect.Top - text.Height - 2;
        if (top < _imageRect.Top) top = rect.Top + 2;

        var background = new SolidColorBrush(color) { Opacity = 0.85 };
        background.Freeze();

        dc.DrawRectangle(background, null, new Rect(rect.X, top, text.Width + 6, text.Height + 2));
        dc.DrawText(text, new Point(rect.X + 3, top + 1));
    }

    /// <summary>끌고 있는 동안 보여 주는 점선 사각형.</summary>
    private void DrawRubberBand(DrawingContext dc)
    {
        if (_dragStart is not { } start) return;

        var pen = new Pen(new SolidColorBrush(ColorOf(CurrentClassId)), 1.5d)
        {
            DashStyle = DashStyles.Dash
        };
        pen.Freeze();

        dc.DrawRectangle(null, pen, new Rect(start, _dragCurrent));
    }

    private void DrawHint(DrawingContext dc, string message)
    {
        var text = MakeText(message, 12d, new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));

        dc.DrawText(text, new Point(
            (RenderSize.Width - text.Width) / 2,
            (RenderSize.Height - text.Height) / 2));
    }

    private FormattedText MakeText(string value, double size, Brush brush) => new(
        value,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface("Segoe UI"),
        size,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private string NameOf(int classId)
        => ClassNames is { } names && classId >= 0 && classId < names.Count ? names[classId] : $"{classId}번";

    /// <summary>
    /// 몹 번호로 색을 정한다.
    /// </summary>
    /// <remarks>
    /// 황금각(137.5도)씩 돌린다. 번호가 몇 개든 이웃한 번호끼리 색이 가장 멀어져,
    /// 사각형이 겹쳐 있어도 어느 것이 어느 몹인지 눈으로 갈린다.
    /// 목록에 색을 적어 두지 않는 것은 몹이 늘 때마다 색을 새로 고르게 하지 않기 위해서다.
    /// </remarks>
    public static Color ColorOf(int classId)
    {
        var hue = (Math.Abs(classId) * 137.508) % 360;

        return FromHsv(hue, 0.85, 0.95);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static IEnumerable<Point> Corners(Rect rect)
    {
        yield return rect.TopLeft;
        yield return rect.TopRight;
        yield return rect.BottomLeft;
        yield return rect.BottomRight;
    }

    // ── 마우스 ───────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        Focus();   // Delete 키를 받으려면 포커스가 있어야 한다

        if (_imageRect.IsEmpty) return;

        // 이미 있는 사각형을 눌렀으면 고르는 것이지 새로 그리는 것이 아니다.
        var hit = HitTest(e.GetPosition(this));

        if (hit >= 0)
        {
            SelectedIndex = hit;
            InvalidateVisual();
            return;
        }

        SelectedIndex = -1;
        _dragStart = e.GetPosition(this);
        _dragCurrent = _dragStart.Value;

        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragStart is null) return;

        _dragCurrent = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_dragStart is not { } start) return;

        var end = e.GetPosition(this);

        _dragStart = null;
        ReleaseMouseCapture();

        // 그냥 클릭한 것과 끈 것을 가른다. 클릭만으로 점짜리 사각형이 생기면
        // 화면에는 안 보이는데 파일에는 남는다.
        var dragged = Math.Abs(end.X - start.X) >= MinimumDragPixels
                      && Math.Abs(end.Y - start.Y) >= MinimumDragPixels;

        if (!dragged || Boxes is not { } boxes)
        {
            InvalidateVisual();
            return;
        }

        var from = ToNormalized(start);
        var to = ToNormalized(end);
        var box = LabelBox.FromCorners(CurrentClassId, from.X, from.Y, to.X, to.Y);

        if (box.IsTooSmall)
        {
            InvalidateVisual();
            return;
        }

        boxes.Add(box);
        SelectedIndex = boxes.Count - 1;   // 방금 그린 것을 골라 둔다. 잘못 그렸으면 바로 지울 수 있게.

        InvalidateVisual();
    }

    /// <summary>
    /// 그 자리에 있는 사각형. 없으면 -1.
    /// </summary>
    /// <remarks>
    /// 겹쳐 있으면 <b>작은 것</b>을 고른다. 큰 사각형 안에 든 작은 사각형은, 큰 것을 먼저 잡으면
    /// 영영 고를 수 없다. 반대는 큰 것의 빈 자리를 누르면 되므로 막히지 않는다.
    /// </remarks>
    private int HitTest(Point point)
    {
        if (Boxes is not { } boxes || _imageRect.IsEmpty) return -1;

        var best = -1;
        var bestArea = double.MaxValue;

        for (var i = 0; i < boxes.Count; i++)
        {
            var rect = ToScreen(boxes[i]);

            // 아주 작은 사각형은 테두리를 정확히 짚기 어렵다. 조금 넉넉하게 잡는다.
            rect.Inflate(HandleHitPixels / 2, HandleHitPixels / 2);

            if (!rect.Contains(point)) continue;

            var area = rect.Width * rect.Height;
            if (area >= bestArea) continue;

            best = i;
            bestArea = area;
        }

        return best;
    }

    /// <summary>
    /// 평범한 피어를 준다.
    /// </summary>
    /// <remarks>
    /// FrameworkElement 는 기본이 null 이라 UI 자동화 트리에 아예 안 나온다. 그러면 화면을
    /// 밖에서 확인할 때 이 자리가 통째로 비어 보인다. (AvalonEdit 은 반대로 <b>제 피어를
    /// 만드는 바람에</b> 창 전체 트리가 비었다 - Markup/ScriptEditor 에 그 이야기가 있다.)
    /// </remarks>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);

    // ── 키보드 ───────────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (Boxes is not { } boxes) return;

        switch (e.Key)
        {
            case Key.Delete or Key.Back when SelectedIndex >= 0 && SelectedIndex < boxes.Count:
                boxes.RemoveAt(SelectedIndex);
                SelectedIndex = -1;
                e.Handled = true;
                break;

            case Key.Escape when SelectedIndex >= 0:
                SelectedIndex = -1;
                e.Handled = true;
                break;

            // Tab 으로 사각형을 넘겨 가며 본다. 작은 사각형을 마우스로 찾는 것보다 확실하다.
            case Key.Tab when boxes.Count > 0:
                SelectedIndex = (SelectedIndex + 1) % boxes.Count;
                e.Handled = true;
                break;
        }

        InvalidateVisual();
    }
}
