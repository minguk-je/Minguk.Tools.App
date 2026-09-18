using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace Minguk.Tools.Markup.Regions;

/// <summary>
/// 고른 자리에 붙는 테두리와 손잡이 여덟 개. 참조한 <c>ResizeRotateAdorner</c> 를 옮겼다(회전은 뺐다 - 읽을 자리는 안 돌린다).
/// </summary>
/// <remarks>
/// 어도너 층은 ScrollViewer 안(<c>ScrollContentPresenter</c>)에 있어 미리보기의 <c>LayoutTransform</c> 밖이지만, <see cref="Adorner"/>
/// 가 붙은 요소의 변환을 스스로 따라가므로 확대해도 자리에 맞는다. 모양은 라벨링 캔버스와 같다(2026-09-15) - 같은 색 3px 테두리,
/// 모서리·변 가운데 6px 네모 손잡이. 크기에 배율 역수를 곱해(<see cref="RegionZoomConverter"/>) 확대해도 화면에서 같은 크기다.
/// </remarks>
public sealed class RegionResizeAdorner : Adorner
{
    private readonly VisualCollection _visuals;
    private readonly RegionResizeChrome _chrome;

    public RegionResizeAdorner(RegionItem item) : base(item)
    {
        SnapsToDevicePixels = true;

        // 반올림을 끈다 - 안의 여백·크기가 확대 역수를 곱한 소수(8배면 손잡이 여백 -0.375)라, DevExpress 창에서 물려받은 레이아웃 반올림이
        // 캔버스 1px(화면 8~10px) 단위로 뭉개 그리는 손잡이는 안쪽·잡는 띠는 바깥쪽으로 갈렸다(사용자, 2026-09-17, --script-screen 이 잡는다).
        UseLayoutRounding = false;
        _chrome = new RegionResizeChrome { DataContext = item };
        _visuals = new VisualCollection(this) { _chrome };
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size ArrangeOverride(Size finalSize)
    {
        _chrome.Arrange(new Rect(finalSize));
        return finalSize;
    }
}

/// <summary>
/// 크기를 바꾸는 동안만 붙는 치수 표시 - 아래에 너비, 오른쪽에 높이(원본 픽셀). 참조한 <c>SizeAdorner</c>.
/// </summary>
/// <remarks>
/// 픽셀로 보이는 이유 - 글자 읽기는 원본 픽셀 크기가 좌우한다(높이 160 아래면 키워 넣는다). 비율(%)은 그것을 못 말해 준다.
/// </remarks>
public sealed class RegionSizeAdorner : Adorner
{
    private readonly VisualCollection _visuals;
    private readonly RegionSizeChrome _chrome;

    public RegionSizeAdorner(RegionItemBase item) : base(item)
    {
        SnapsToDevicePixels = true;

        // 반올림을 끈다 - 안의 여백·크기가 확대 역수를 곱한 소수(8배면 손잡이 여백 -0.375)라, DevExpress 창에서 물려받은 레이아웃 반올림이
        // 캔버스 1px(화면 8~10px) 단위로 뭉개 그리는 손잡이는 안쪽·잡는 띠는 바깥쪽으로 갈렸다(사용자, 2026-09-17, --script-screen 이 잡는다).
        UseLayoutRounding = false;
        _chrome = new RegionSizeChrome { DataContext = item };
        _visuals = new VisualCollection(this) { _chrome };
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size ArrangeOverride(Size finalSize)
    {
        _chrome.Arrange(new Rect(new Point(0, 0), finalSize));
        return finalSize;
    }
}

/// <summary>
/// 고른 <b>칸</b>에 붙는 테두리·손잡이 여덟 개·회전 손잡이. 자리 어도너(<see cref="RegionResizeAdorner"/>)와 별개다(사용자 2026-09-16).
/// </summary>
/// <remarks>
/// 색이 초록이라 주황(자리)과 한눈에 갈린다. 칸 항목이 돌면 어도너도 같이 돈다 - 손잡이 변위는 돌린 좌표계로 온다(<see cref="RegionCellItem"/>).
/// </remarks>
public sealed class RegionCellAdorner : Adorner
{
    private readonly VisualCollection _visuals;
    private readonly RegionCellChrome _chrome;

    public RegionCellAdorner(RegionCellItem item) : base(item)
    {
        SnapsToDevicePixels = true;

        // 반올림을 끈다 - 안의 여백·크기가 확대 역수를 곱한 소수(8배면 손잡이 여백 -0.375)라, DevExpress 창에서 물려받은 레이아웃 반올림이
        // 캔버스 1px(화면 8~10px) 단위로 뭉개 그리는 손잡이는 안쪽·잡는 띠는 바깥쪽으로 갈렸다(사용자, 2026-09-17, --script-screen 이 잡는다).
        UseLayoutRounding = false;
        _chrome = new RegionCellChrome { DataContext = item };
        _visuals = new VisualCollection(this) { _chrome };
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size ArrangeOverride(Size finalSize)
    {
        _chrome.Arrange(new Rect(finalSize));
        return finalSize;
    }
}

/// <summary>칸 테두리·손잡이 모양. 템플릿은 <c>RegionChrome.xaml</c> 에 있다.</summary>
public sealed class RegionCellChrome : Control
{
    public RegionCellChrome() => Style = RegionChromeResources.StyleFor(typeof(RegionCellChrome));
}

/// <summary>테두리·손잡이 모양. 템플릿은 <c>RegionChrome.xaml</c> 에 있다.</summary>
public sealed class RegionResizeChrome : Control
{
    public RegionResizeChrome() => Style = RegionChromeResources.StyleFor(typeof(RegionResizeChrome));
}

/// <summary>치수 표시 모양. 템플릿은 <c>RegionChrome.xaml</c> 에 있다.</summary>
public sealed class RegionSizeChrome : Control
{
    public RegionSizeChrome() => Style = RegionChromeResources.StyleFor(typeof(RegionSizeChrome));
}

/// <summary>
/// 배율 역수(<see cref="RegionItem.InverseZoom"/>)에 크기를 곱한다 - 선 굵기·손잡이 크기·여백이 확대해도 화면에서 같게.
/// </summary>
/// <remarks>
/// 매개변수는 화면 픽셀 값. 받는 쪽이 <see cref="Thickness"/>(여백)면 "0,-17,0,0" 이나 "-3" 을, 아니면 수 하나를 받는다.
/// </remarks>
public sealed class RegionZoomConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var inverse = value is double d && d > 0 ? d : 1d;
        var parts = (parameter?.ToString() ?? "1").Split(',').Select(p => double.Parse(p, CultureInfo.InvariantCulture) * inverse).ToArray();

        if (targetType == typeof(Thickness))
            return parts.Length == 4 ? new Thickness(parts[0], parts[1], parts[2], parts[3]) : new Thickness(parts[0]);

        return parts[0];
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
}

/// <summary>
/// 배율 역수를 <b>Transform 통째</b>로 만든다 - 이름표·치수 글자를 확대해도 화면에서 같은 크기로 두려고. 매개변수를 주면 그 각도로 돌린 뒤 키운다.
/// </summary>
/// <remarks>
/// <b>왜 변환기인가</b>(2026-09-18) - 템플릿 안에 <c>&lt;ScaleTransform ScaleX="{Binding InverseZoom}" /&gt;</c> 로 적으면 그 바인딩은 <b>조용히 죽는다</b>.
/// ScaleTransform 은 Freezable 이라 DataContext 를 물려받을 길이 없다(로그: "Cannot find governing FrameworkElement ... target element is 'ScaleTransform'").
/// 값이 기본 1 로 남아 이름표·치수 글자가 배율만큼 크게 그려졌다(사용자, "전체 어도너가 커져서"). Transform 통째를 만들어
/// FrameworkElement 의 <c>LayoutTransform</c> 에 묶으면 평범한 속성 바인딩이라 산다.
/// </remarks>
public sealed class RegionScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var inverse = value is double d && d > 0 ? d : 1d;
        var scale = new ScaleTransform(inverse, inverse);

        if (parameter is null || !double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var angle) || angle == 0)
        {
            scale.Freeze();

            return scale;
        }

        var group = new TransformGroup();

        group.Children.Add(new RotateTransform(angle));
        group.Children.Add(scale);
        group.Freeze();

        return group;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
}

/// <summary>치수 글자는 정수로. 참조한 <c>DoubleFormatConverter</c>.</summary>
public sealed class RegionRoundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? Math.Round(d).ToString(CultureInfo.InvariantCulture) : string.Empty;

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
}

/// <summary>
/// 어도너·항목의 스타일을 든 사전.
/// </summary>
/// <remarks>
/// 이 프로젝트에는 <c>Themes/Generic.xaml</c> 이 없다. 앱 리소스에 병합하면 하네스(<c>--script-screen</c>)는 따로 또 병합해야
/// 하고, 빠뜨리면 항목이 템플릿 없이 보이지 않는다. 컨트롤이 제 스타일을 여기서 직접 가져가면 어디에 놓여도 같다.
/// </remarks>
internal static class RegionChromeResources
{
    private static ResourceDictionary? _dictionary;

    private static ResourceDictionary Dictionary => _dictionary ??= new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/Minguk.Tools;component/Markup/Regions/RegionChrome.xaml")
    };

    public static Style StyleFor(Type type) => (Style)Dictionary[type];
}
