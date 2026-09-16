using System.Windows;
using System.Windows.Input;

using DevExpress.Mvvm.UI.Interactivity;

using Minguk.Tools.ViewModels.Settings;

namespace Minguk.Tools.Markup.Settings;

/// <summary>
/// 설정 탭 도구 상자 목록에서 칸을 끌어 판(<see cref="SettingsFormCanvas"/>)으로 가져간다. 싣는 것은 칸 종류 하나(<see cref="SettingsFormCanvas.DragFormat"/>).
/// </summary>
/// <remarks>
/// 누른 줄의 데이터 문맥(<see cref="SettingsToolboxItem"/>)을 기억했다가, 시스템 끌기 거리보다 움직이면 끌기를 시작한다 - 그냥 누르기·두 번 누르기(놓기)는 그대로 둔다.
/// </remarks>
public sealed class SettingsToolboxDragBehavior : Behavior<FrameworkElement>
{
    private Point _start;
    private SettingsToolboxItem? _pressed;

    protected override void OnAttached()
    {
        base.OnAttached();

        AssociatedObject.PreviewMouseLeftButtonDown += OnMouseDown;
        AssociatedObject.PreviewMouseMove += OnMouseMove;
        AssociatedObject.PreviewMouseLeftButtonUp += OnMouseUp;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseLeftButtonDown -= OnMouseDown;
        AssociatedObject.PreviewMouseMove -= OnMouseMove;
        AssociatedObject.PreviewMouseLeftButtonUp -= OnMouseUp;

        base.OnDetaching();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(AssociatedObject);
        _pressed = (e.OriginalSource as FrameworkElement)?.DataContext as SettingsToolboxItem;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => _pressed = null;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;

        var delta = e.GetPosition(AssociatedObject) - _start;

        if (System.Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var tool = _pressed;
        _pressed = null;

        DragDrop.DoDragDrop(AssociatedObject, new DataObject(SettingsFormCanvas.DragFormat, tool.Kind), DragDropEffects.Copy);
    }
}
