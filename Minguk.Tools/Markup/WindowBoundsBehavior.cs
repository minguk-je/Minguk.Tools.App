using System;
using System.Globalization;
using System.Linq;
using System.Windows;

using DevExpress.Mvvm.UI.Interactivity;

namespace Minguk.Tools.Markup;

/// <summary>
/// 붙은 요소가 든 창의 자리·크기를 <see cref="Bounds"/>(<c>왼쪽,위,너비,높이</c>)와 잇는다 - 값이 오면 창에 한 번 걸고, 창이 옮겨지거나 커지면 적는다.
/// </summary>
/// <remarks>
/// <b>왜</b> - WindowService 가 띄우는 창(플레이·스크립트의 [설정] 값 창)은 화면 모델이 창을 모른다. 코드 비하인드에서 하던 것을 옮겼다(사용자, 2026-09-17 "Behavior 로").
/// 저장·복원은 화면 모델이 한다(<c>Bounds</c> 를 양방향으로 묶는다).
///
/// <b>순서</b> - 화면 모델의 복원(RestoreSettings)은 창이 뜬 뒤에 온다. 그래서 값이 오면 그때 한 번 건다. 걸기 전에 창이 가운데로 뜬 자리를 적으면
/// 저장값을 덮으므로, 건 뒤(또는 저장값이 없을 때)부터 적는다. 모니터가 빠져 제목 줄이 화면 밖이면 걸지 않는다. 최대화·최소화 중에는 안 적는다.
/// </remarks>
public sealed class WindowBoundsBehavior : Behavior<FrameworkElement>
{
    public static readonly DependencyProperty BoundsProperty = DependencyProperty.Register(
        nameof(Bounds), typeof(string), typeof(WindowBoundsBehavior),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((WindowBoundsBehavior)d).Apply()));

    /// <summary>창 자리·크기 <c>왼쪽,위,너비,높이</c>(소수 없이, 문화권 무관).</summary>
    public string? Bounds
    {
        get => (string?)GetValue(BoundsProperty);
        set => SetValue(BoundsProperty, value);
    }

    private Window? _window;
    private bool _applied;
    private bool _writing;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.Unloaded += OnUnloaded;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.Unloaded -= OnUnloaded;
        Unhook();
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Unhook();

        _window = Window.GetWindow(AssociatedObject);
        if (_window is null) return;

        Apply();

        _window.LocationChanged += OnWindowMoved;
        _window.SizeChanged += OnWindowMoved;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Unhook();

    private void Unhook()
    {
        if (_window is null) return;

        _window.LocationChanged -= OnWindowMoved;
        _window.SizeChanged -= OnWindowMoved;
        _window = null;
    }

    private void Apply()
    {
        if (_writing || _applied || _window is null || Bounds is not { Length: > 0 } text) return;

        var parts = text.Split(',').Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN).ToArray();

        if (parts.Length != 4 || parts.Any(double.IsNaN) || parts[2] < 200 || parts[3] < 150) return;

        _applied = true;

        var bounds = new Rect(parts[0], parts[1], parts[2], parts[3]);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        // 제목 줄(양끝 40px 뺀 위 30px)이 화면 안에 있어야 끌어 옮길 수 있다.
        if (!screen.IntersectsWith(new Rect(bounds.X + 40, bounds.Y, Math.Max(1, bounds.Width - 80), 30))) return;

        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Left = bounds.X;
        _window.Top = bounds.Y;
        _window.Width = bounds.Width;
        _window.Height = bounds.Height;
    }

    private void OnWindowMoved(object? sender, EventArgs e)
    {
        if (_window is null || _window.WindowState != WindowState.Normal) return;
        if (!_applied && !string.IsNullOrEmpty(Bounds)) return;

        _applied = true;
        _writing = true;

        try
        {
            Bounds = string.Join(",", new[] { _window.Left, _window.Top, _window.ActualWidth, _window.ActualHeight }
                .Select(v => Math.Round(v).ToString(CultureInfo.InvariantCulture)));
        }
        finally
        {
            _writing = false;
        }
    }
}
