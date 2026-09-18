namespace Minguk.Tools.Input.Hotkeys;

/// <summary>
/// 쓸 단축키 경로를 고른다.
///
/// 구현이 하나뿐이어도 여기를 거친다. 둘째가 생길 때(예: 저수준 키보드 훅으로
/// RegisterHotKey 가 못 잡는 조합까지 받는 경로) 부르는 쪽을 고치지 않으려는 것이다.
/// </summary>
public static class GlobalHotkeyAdapterFactory
{
    public static IGlobalHotkeyAdapter Create() => new GlobalHotkeyAdapter();
}
