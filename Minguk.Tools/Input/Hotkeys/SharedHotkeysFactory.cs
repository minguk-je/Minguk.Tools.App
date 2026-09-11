using System;

namespace Minguk.Tools.Input.Hotkeys;

/// <summary>
/// 앱에 하나인 공용 단축키. 화면들이 같은 것을 봐야 "한 번 등록해 나눠 쓴다" 가 성립한다.
/// </summary>
public static class SharedHotkeysFactory
{
    private static readonly Lazy<ISharedHotkeys> Shared = new(() => new SharedHotkeys(GlobalHotkeyAdapterFactory.Create));

    public static ISharedHotkeys Default => Shared.Value;
}
