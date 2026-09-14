using System.Collections.Generic;

namespace Minguk.Tools.Modules;

/// <summary>
/// 이 앱에 붙어 있는 기능 모듈 목록.
/// </summary>
/// <remarks>
/// 앱이 뜰 때 <see cref="Use"/> 로 한 번 채우고, 그 뒤로는 읽기만 한다. 검증 하네스도 같은 것을 부른다 -
/// 안 부르면 모듈 화면이 없는 셈이라 <c>--views</c> 가 모듈을 못 본다.
///
/// 리플렉션으로 어셈블리를 훑지 않는다. 어느 모듈이 붙어 있는지는 코드에 적혀 있어야 빌드가 알려 준다 -
/// 폴더에 DLL 을 떨어뜨려 늘리는 방식은 우리가 만든 모듈만 쓰는 지금 얻을 것이 없고, 못 찾을 때 이유가 안 보인다.
/// </remarks>
public static class ToolModules
{
    private static IReadOnlyList<IToolModule> _all = [];

    public static IReadOnlyList<IToolModule> All => _all;

    public static void Use(params IToolModule[] modules) => _all = modules ?? [];
}
