using DevExpress.Mvvm.UI;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Reflection;
using DevExpress.Mvvm;

namespace Minguk.Tools.Locator;

/// <summary>
/// 뷰 이름(문자열) → View 인스턴스.
///
/// 기본 ViewLocator 는 Activator.CreateInstance 로 뷰를 만들기 때문에 생성자 주입을 쓸 수 없다.
/// 여기서는 DI 컨테이너에서 꺼내므로, App.xaml.cs 에 AddTransient 로 등록해 둔 뷰만 열린다.
/// 등록을 빠뜨리면 ResolveView 가 null 을 돌려주고 문서 탭이 비어 보인다.
/// </summary>
public class MainViewLocator : LocatorBase, IViewLocator
{
    private ServiceProvider ServiceProvider { get; }

    protected override IEnumerable<Assembly> Assemblies => new[] { typeof(App).Assembly };

    public MainViewLocator(ServiceProvider serviceProvider) => ServiceProvider = serviceProvider;

    public string GetViewTypeName(Type type) => type.Name;

    public object? ResolveView(string name)
    {
        var type = ResolveViewType(name);
        return type is null ? null : ServiceProvider.GetService(type);
    }

    public Type ResolveViewType(string? name) => base.ResolveType(name, out _);
}
