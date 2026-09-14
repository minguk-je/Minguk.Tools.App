using DevExpress.Mvvm.UI;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// 셸 어셈블리 + 붙어 있는 모듈 어셈블리.
    /// </summary>
    /// <remarks>
    /// 셸만 뒤지면 모듈 화면의 <c>CLASS_NM</c> 을 못 찾아 메뉴를 눌러도 아무 일이 안 난다.
    /// 모듈이 늘어도 여기는 안 고친다 - <c>ToolModules.All</c> 을 따라간다.
    /// </remarks>
    protected override IEnumerable<Assembly> Assemblies =>
        new[] { typeof(App).Assembly }
            .Concat(Modules.ToolModules.All.Select(module => module.GetType().Assembly))
            .Distinct();

    public MainViewLocator(ServiceProvider serviceProvider) => ServiceProvider = serviceProvider;

    public string GetViewTypeName(Type type) => type.Name;

    public object? ResolveView(string name)
    {
        var type = ResolveViewType(name);
        return type is null ? null : ServiceProvider.GetService(type);
    }

    public Type ResolveViewType(string? name) => base.ResolveType(name, out _);
}
