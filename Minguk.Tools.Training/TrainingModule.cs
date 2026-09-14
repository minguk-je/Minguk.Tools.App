using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;

using Minguk.Image;

using Minguk.Tools.Models;
using Minguk.Tools.Modules;
using Minguk.Tools.Training.Views;

namespace Minguk.Tools.Training;

/// <summary>
/// 학습 환경 모듈. 셸에 붙는 첫 기능 프로젝트다.
/// </summary>
/// <remarks>
/// 기능 프로젝트를 하나 더 만들 때 따라 할 본보기다.
/// <list type="number">
/// <item>프로젝트를 만들고 <c>Minguk.Base</c> · <c>Minguk.Tools.Core</c> 만 참조한다(셸은 참조하지 않는다 - 순환).</item>
/// <item><see cref="IToolModule"/> 구현을 하나 둔다.</item>
/// <item>셸의 <c>App.xaml.cs</c> 에서 <c>ToolModules.Use(...)</c> 에 한 줄 더한다.</item>
/// </list>
/// 메뉴 번호(<c>menu_cd</c>)는 셸 것과 겹치지 않게 2000번대를 쓴다 - 정렬에 쓰인다.
/// </remarks>
public sealed class TrainingModule : IToolModule
{
    public string Name => "학습";

    public void RegisterServices(IServiceCollection services)
    {
        // 이것을 빠뜨리면 메뉴는 보이는데 탭이 비어서 열린다 - MainViewLocator 가 DI 에서 뷰를 꺼내기 때문이다.
        services.AddTransient<TrainingEnvironmentView>();
    }

    public IEnumerable<MenuItemModel> CreateMenuItems()
    {
        yield return MenuItemModel.Create(
            menu_cd: "2100",
            menu_nm: "환경",
            dll_nm: "Minguk.Tools.Training.dll",
            class_nm: "Minguk.Tools.Training.Views.TrainingEnvironmentView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/gear.png"));
    }
}
