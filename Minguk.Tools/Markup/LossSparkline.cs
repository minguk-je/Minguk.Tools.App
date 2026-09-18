using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Minguk.Tools.Markup;

/// <summary>
/// loss 가 내려가는지 한눈에 보이는 작은 꺾은선.
/// </summary>
/// <remarks>
/// <b>왜 차트 컨트롤을 안 쓰는가</b> - 축·범례·툴팁이 필요 없다. 보고 싶은 것은
/// "내려가고 있나, 제자리인가" 하나다. 실제로 학습률 1.0 으로 100바퀴를 돌리고도 27개 중
/// 0개를 찾았을 때, loss 가 1.4 에서 제자리인 것을 진작 봤더라면 15분을 아꼈다.
///
/// <b>로그 눈금</b> - 첫 스텝 loss 는 수백이고 곧 1 근처로 떨어진다. 선형이면 첫 점 하나가
/// 나머지를 바닥에 깔아 버려 아무것도 안 보인다. log10 으로 그린다.
/// </remarks>
public sealed class LossSparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(ObservableCollection<double>), typeof(LossSparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public ObservableCollection<double>? Values
    {
        get => (ObservableCollection<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LossSparkline)d;

        if (e.OldValue is ObservableCollection<double> old) old.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is ObservableCollection<double> added) added.CollectionChanged += chart.OnCollectionChanged;

        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    /// <summary>선 색. 검출 0번 색과 같게 두어 화면 안에서 튀지 않게 한다.</summary>
    private static readonly Color LineColor = LabelCanvas.ColorOf(0);

    protected override void OnRender(DrawingContext dc)
    {
        // 히트 테스트와 배경. 투명이어도 칠해야 자리를 차지한다.
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80)), null, new Rect(RenderSize));

        var values = Values?.Where(v => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();

        if (values is null || values.Length < 2)
        {
            DrawText(dc, values is { Length: 1 } ? $"loss {values[0]:0.###}" : "loss", new Point(4, 2), 0x88);
            return;
        }

        var logs = values.Select(Math.Log10).ToArray();
        var min = logs.Min();
        var max = logs.Max();
        var span = Math.Max(max - min, 0.05);

        var width = Math.Max(RenderSize.Width - 2, 1);
        var height = Math.Max(RenderSize.Height - 2, 1);

        var pen = new Pen(new SolidColorBrush(LineColor), 1.5d) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            for (var i = 0; i < logs.Length; i++)
            {
                var x = 1 + (width * i / (logs.Length - 1));
                var y = 1 + (height * (1 - ((logs[i] - min) / span)));

                if (i == 0) context.BeginFigure(new Point(x, y), false, false);
                else context.LineTo(new Point(x, y), true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        // 마지막 값과 가장 낮았던 값. 숫자가 있어야 "얼마나" 가 보인다.
        DrawText(dc, $"loss {values[^1]:0.###}", new Point(4, 2), 0xCC);
        DrawText(dc, $"최저 {values.Min():0.###}", new Point(4, RenderSize.Height - 15), 0x88);
    }

    private void DrawText(DrawingContext dc, string text, Point at, byte alpha)
    {
        var formatted = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10d,
            new SolidColorBrush(Color.FromArgb(alpha, 0x40, 0x40, 0x40)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(formatted, at);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
}
