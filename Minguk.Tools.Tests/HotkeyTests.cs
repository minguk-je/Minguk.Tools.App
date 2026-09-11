using System;
using System.Collections.Generic;
using System.Windows.Input;

using Minguk.Tools.Input.Hotkeys;

namespace Minguk.Tools.Tests;

/// <summary>
/// 공용 단축키. 여러 화면이 같은 키를 쥐어도 시스템 등록은 한 번이고, 마지막에 쥐거나 활성화한 화면이 받는지.
/// 가짜 어댑터로 눌림을 흉내 낸다 - 실제 RegisterHotKey 는 부르지 않는다.
/// </summary>
internal static partial class Program
{
    private static void TestSharedHotkeys()
    {
        var adapter = new FakeHotkeyAdapter();
        using var hotkeys = new SharedHotkeys(() => adapter);
        var received = new List<string>();

        var studio = hotkeys.Claim(Key.F5, ModifierKeys.None, "스크립트", () => received.Add("스크립트"), out var problem1);
        var play = hotkeys.Claim(Key.F5, ModifierKeys.None, "플레이", () => received.Add("플레이"), out var problem2);

        Check("공용 단축키: 둘이 쥐어도 시스템 등록은 한 번", studio is not null && play is not null && adapter.Registered.Count == 1,
              $"등록 {adapter.Registered.Count}번, {problem1 ?? problem2 ?? "문제 없음"}");

        adapter.Press(Key.F5);
        Check("공용 단축키: 마지막에 쥔 화면이 받는다", received.Count == 1 && received[0] == "플레이", string.Join(", ", received));

        studio!.Activate();
        adapter.Press(Key.F5);
        Check("공용 단축키: 활성화한 화면이 받는다", received.Count == 2 && received[1] == "스크립트", string.Join(", ", received));

        studio.Dispose();
        adapter.Press(Key.F5);
        Check("공용 단축키: 하나가 놓으면 남은 쪽이 받고 등록은 그대로", received.Count == 3 && received[2] == "플레이" && adapter.Registered.Count == 1, string.Join(", ", received));

        play!.Dispose();
        adapter.Press(Key.F5);
        Check("공용 단축키: 다 놓으면 시스템 등록을 푼다", received.Count == 3 && adapter.Registered.Count == 0, $"등록 {adapter.Registered.Count}개");

        // 권한 견주기: 이 하네스 자신은 열어 볼 수 있으니 답이 나와야 하고, 없는 창은 모른다(null).
        var own = Minguk.Tools.Input.ProcessElevation.IsProcessElevated((uint)Environment.ProcessId);
        Check("권한: 자기 프로세스의 승격 여부를 읽고, 없는 창은 null", own == Minguk.Tools.Input.ProcessElevation.IsCurrentElevated
              && Minguk.Tools.Input.ProcessElevation.IsWindowElevated(IntPtr.Zero) is null, $"자기={own}, 앱={Minguk.Tools.Input.ProcessElevation.IsCurrentElevated}");

        // Raw Input 장치 이름 ↔ Interception 하드웨어 ID 맞추기(실측 문자열).
        const string rawLogi = @"\\?\HID#VID_046D&PID_C547&MI_00#8&1def9795&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}";
        const string idLogi = @"HID\VID_046D&PID_C547&REV_0402&MI_00 HID\VID_046D&PID_C547&MI_00 HID_DEVICE_SYSTEM_MOUSE";
        const string idXenta = @"HID\VID_1D57&PID_FA60&REV_1108&MI_01 HID\VID_1D57&PID_FA60&MI_01 HID_DEVICE_SYSTEM_MOUSE";
        const string idLogiKeyboard = @"HID\VID_046D&PID_C547&REV_0402&MI_01&Col01 HID\VID_046D&PID_C547&MI_01&Col01 HID_DEVICE_SYSTEM_KEYBOARD";
        Check("장치 맞추기: VID·PID·MI 가 같은 자리만 고른다",
              Minguk.Tools.Input.Interop.RawInputDeviceTracker.SameDevice(rawLogi, idLogi)
              && !Minguk.Tools.Input.Interop.RawInputDeviceTracker.SameDevice(rawLogi, idXenta)
              && !Minguk.Tools.Input.Interop.RawInputDeviceTracker.SameDevice(rawLogi, idLogiKeyboard)
              && !Minguk.Tools.Input.Interop.RawInputDeviceTracker.SameDevice(rawLogi, null), "");

        adapter.Refuse = true;
        var refused = hotkeys.Claim(Key.F12, ModifierKeys.None, "F12", () => { }, out var problem3);
        Check("공용 단축키: 시스템이 거부하면 null 과 이유", refused is null && problem3 is not null && problem3.Contains("F12"), problem3 ?? "이유 없음");
    }

    /// <summary>시스템 등록을 흉내 내는 어댑터. Press 로 눌림을 만든다.</summary>
    private sealed class FakeHotkeyAdapter : IGlobalHotkeyAdapter
    {
        public Dictionary<Key, Action> Registered { get; } = [];

        public bool Refuse { get; set; }

        public string Name => "가짜";

        public bool TryRegister(Key key, ModifierKeys modifiers, Action onPressed)
        {
            if (Refuse) return false;

            Registered[key] = onPressed;
            return true;
        }

        public bool Unregister(Key key, ModifierKeys modifiers) => Registered.Remove(key);

        public void UnregisterAll() => Registered.Clear();

        public void Press(Key key)
        {
            if (Registered.TryGetValue(key, out var action)) action();
        }

        public void Dispose() => Registered.Clear();
    }
}
