using System;
using System.Windows.Input;

using Minguk.Tools.Input.Hotkeys;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 비상 정지 (F9). 스크립트가 도는 동안만 전역으로 쥐고, 끝나면 놓는다.
/// </summary>
/// <remarks>
/// 스크립트가 게임에 입력을 보내는 동안 사용자는 게임을 보고 있다. 이 앱의 중지 버튼은 앱이 앞에 있어야
/// 눌리므로, 어느 창에 있어도 눌리는 키가 있어야 한다. 도는 동안만 쥐는 이유는 늘 쥐고 있으면 다른 화면·
/// 다른 프로그램의 F9 를 빼앗기 때문이다.
/// 누르면 중지와 함께 누르고 있던 키를 전부 뗀다 - 누른 채로 멈추면 게임이 계속 달린다.
/// </remarks>
public sealed class EmergencyStop : IDisposable
{
    public const string Label = "F9";
    private const Key Hotkey = Key.F9;

    private IGlobalHotkeyAdapter? _hotkeys;

    /// <summary>쥔다. 못 쥐면 이유를 준다(다른 프로그램이 쥐고 있음). 그래도 스크립트는 돈다 - 화면의 중지 버튼이 있다.</summary>
    public bool Arm(Action stop, out string? problem)
    {
        problem = null;

        Disarm();

        try
        {
            _hotkeys = GlobalHotkeyAdapterFactory.Create();

            if (_hotkeys.TryRegister(Hotkey, ModifierKeys.None, stop)) return true;

            problem = $"비상 정지 {Label} 를 등록하지 못했습니다 - 다른 프로그램이 쥐고 있습니다. 화면의 중지 버튼을 쓰세요.";
            Disarm();
            return false;
        }
        catch (Exception ex)
        {
            problem = $"비상 정지 {Label} 를 걸지 못했습니다: {ex.Message}";
            return false;
        }
    }

    public void Disarm()
    {
        _hotkeys?.Dispose();
        _hotkeys = null;
    }

    public void Dispose() => Disarm();
}
