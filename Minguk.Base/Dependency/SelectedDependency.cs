using System.Windows;
using DevExpress.Xpf.Core;

namespace Minguk.Base.Dependency;

public static class IsSelectedDependency
{
    public static readonly DependencyProperty IsSelectedProperty = 
        DependencyProperty.RegisterAttached("IsSelected", typeof(bool), typeof(SimpleButton),
            new PropertyMetadata(false));

    public static void SetIsSelected(this UIElement element, Boolean value)
    {
        element.SetValue(IsSelectedProperty, value);
    }

    public static bool GetIsSelected(this UIElement element)
    {
        return (bool)element.GetValue(IsSelectedProperty);
    }
}
