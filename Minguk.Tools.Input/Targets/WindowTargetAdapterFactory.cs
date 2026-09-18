namespace Minguk.Tools.Input.Targets;

/// <summary>
/// 창을 찾는 방법을 고른다.
/// </summary>
/// <remarks>
/// 지금은 하나뿐이지만 팩터리를 둔다. 둘째가 필요해질 때(UI Automation 으로 자식 컨트롤까지
/// 짚는 것 같은) 부르는 쪽을 안 고치려는 것이다.
/// </remarks>
public static class WindowTargetAdapterFactory
{
    public static IWindowTargetAdapter Create() => new Win32WindowTargetAdapter();
}
