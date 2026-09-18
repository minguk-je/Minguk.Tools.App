using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Minguk.Tools.Input.Hotkeys;

/// <summary>
/// user32 의 <c>RegisterHotKey</c> 로 받는다.
/// </summary>
/// <remarks>
/// 창을 하나 만드는 이유
///   RegisterHotKey 는 눌림을 <c>WM_HOTKEY</c> 메시지로 보낸다. 받을 창이 필요하다.
///   화면의 창을 빌려 쓰면 그 화면이 닫힐 때 같이 죽어 버리므로, 보이지 않는 창
///   (message-only)을 직접 만들어 쓴다. 이 어댑터가 자기 수명을 온전히 쥔다.
///
/// UI 스레드에서 만들어야 한다
///   메시지 훅은 창을 만든 스레드의 메시지 루프에서 돈다. 백그라운드 스레드에서
///   만들면 루프가 없어 아무것도 오지 않는다.
/// </remarks>
public sealed class GlobalHotkeyAdapter : IGlobalHotkeyAdapter
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Dictionary<int, Action> _actions = [];
    private readonly Dictionary<(Key, ModifierKeys), int> _ids = [];
    private readonly HwndSource _source;

    private int _nextId = 1;

    public GlobalHotkeyAdapter()
    {
        // 0x0 크기에 부모가 HWND_MESSAGE 면 화면에 뜨지 않는다.
        _source = new HwndSource(new HwndSourceParameters("Minguk.Tools 단축키 수신")
        {
            Width = 0,
            Height = 0,
            ParentWindow = HwndMessage,
            WindowStyle = 0
        });

        _source.AddHook(OnMessage);
    }

    public string Name => "RegisterHotKey";

    public bool TryRegister(Key key, ModifierKeys modifiers, Action onPressed)
    {
        var id = _nextId++;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (!RegisterHotKey(_source.Handle, id, ToNative(modifiers), virtualKey))
        {
            // 흔한 실패다. 다른 프로그램이 같은 조합을 먼저 쥐고 있으면 여기로 온다.
            Logger.Info($"단축키 {modifiers}+{key} 를 등록하지 못했다. 오류 {Marshal.GetLastWin32Error()}");
            _nextId--;
            return false;
        }

        _actions[id] = onPressed;
        _ids[(key, modifiers)] = id;
        return true;
    }

    public bool Unregister(Key key, ModifierKeys modifiers)
    {
        if (!_ids.Remove((key, modifiers), out var id)) return false;

        _actions.Remove(id);
        return UnregisterHotKey(_source.Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);

        _ids.Clear();

        _actions.Clear();
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_HOTKEY) return IntPtr.Zero;

        if (!_actions.TryGetValue(wParam.ToInt32(), out var action)) return IntPtr.Zero;

        handled = true;

        // 여기서 터지면 메시지 루프가 통째로 흔들린다. 삼키고 남긴다.
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "단축키 처리 중 예외");
        }

        return IntPtr.Zero;
    }

    /// <summary>WPF 의 조합키 표현을 RegisterHotKey 가 쓰는 값으로 옮긴다.</summary>
    private static uint ToNative(ModifierKeys modifiers)
    {
        uint value = 0;

        if (modifiers.HasFlag(ModifierKeys.Alt)) value |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) value |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) value |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) value |= MOD_WIN;

        // 누르고 있으면 자동 반복되는 것을 막는다. 시작/중지가 연달아 불리면 곤란하다.
        return value | MOD_NOREPEAT;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(OnMessage);
        _source.Dispose();
    }

    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private static readonly IntPtr HwndMessage = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
