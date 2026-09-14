using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

using Minguk.Tools.Models;

namespace Minguk.Tools.Modules;

/// <summary>
/// 기능 프로젝트 하나. 화면과 제 설정 페이지를 들고 셸에 붙는다.
/// </summary>
/// <remarks>
/// <b>왜 이것이 있나</b>(사용자 결정 2026-09-14) - 셸(Minguk.Tools)은 뼈대로 두고 기능은 프로젝트를 따로 만들어 붙인다.
/// 기능이 늘 때 셸에서 고치는 곳은 <c>ToolModules.All</c> 한 줄뿐이어야 한다.
///
/// 모듈이 스스로 들고 오는 것 셋이다.
/// <list type="number">
/// <item>DI 등록(<see cref="RegisterServices"/>) - View 를 <c>AddTransient</c> 한다. 안 하면 메뉴는 뜨는데 탭이 빈다.</item>
/// <item>메뉴 항목(<see cref="CreateMenuItems"/>) - <c>CLASS_NM</c> 은 View 의 전체 타입 이름이다.</item>
/// </list>
///
/// 설정은 모듈 화면이 제가 든다. 한동안 설정 화면(ConfigView)에 범주로 끼우는 길을 두었는데,
/// <b>폴더를 바꾼 뒤 그 결과를 볼 화면이 따로 있어</b> 오가야 했다(사용자, 2026-09-14). 지금은 학습 폴더를
/// 학습환경 화면이 직접 들고, 고치면 그 자리에서 표가 다시 그려진다.
///
/// <b>View 를 찾는 자리도 같이 넓어진다</b> - <c>MainViewLocator</c> 는 셸 어셈블리만 뒤지므로,
/// 모듈 어셈블리를 함께 보게 해야 <c>ResolveViewType</c> 이 모듈 화면을 찾는다.
/// </remarks>
public interface IToolModule
{
    /// <summary>사람이 읽는 이름. 로그와 설정 화면 범주에 쓴다.</summary>
    string Name { get; }

    /// <summary>이 모듈의 View 들을 DI 에 등록한다.</summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>메뉴에 붙일 항목. 없으면 빈 목록.</summary>
    IEnumerable<MenuItemModel> CreateMenuItems();
}
