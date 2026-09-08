namespace Minguk.Tools.Automation;

/// <summary>고를 수 있는 UI 자동화 경로.</summary>
public enum UiAutomationBackend
{
    /// <summary>윈도우 기본 접근성 API. 추가로 무는 것이 없다.</summary>
    Windows
}

/// <summary>
/// 쓸 UI 자동화 백엔드를 고른다.
///
/// 지금은 하나뿐이다. 그래도 인터페이스와 팩터리를 두는 이유는,
/// 나중에 FlaUI 나 다른 경로를 붙일 때 부르는 쪽을 안 고치기 위해서다.
/// </summary>
public static class UiAutomationAdapterFactory
{
    public static IUiAutomationAdapter Create(UiAutomationBackend backend = UiAutomationBackend.Windows) => backend switch
    {
        _ => new WindowsUiAutomationAdapter()
    };
}
