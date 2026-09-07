using System.Windows;

namespace Minguk.Tools.Markup;

/// <summary>
/// 시각 트리 밖에 있는 대상에 DataContext 를 실어 나르는 중계용 객체.
///
/// KeyBinding, Setter.Value, 컨버터 같은 것들은 시각 트리에 속하지 않아 DataContext 상속이 닿지 않는다.
/// 그래서 {Binding Command} 같은 바인딩이 "Cannot find governing FrameworkElement" 로 실패한다.
/// Freezable 은 리소스에 넣으면 상속 컨텍스트를 타므로 DataContext 가 흘러 들어온다.
///
/// 사용법:
///   &lt;Window.Resources&gt;
///       &lt;markup:BindingProxy x:Key="ViewModelProxy" Data="{Binding}" /&gt;
///   &lt;/Window.Resources&gt;
///   &lt;KeyBinding Command="{Binding Data.SomeCommand, Source={StaticResource ViewModelProxy}}" ... /&gt;
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
