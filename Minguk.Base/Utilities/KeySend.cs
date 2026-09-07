using System.Runtime.InteropServices;

namespace Minguk.Base.Utilities;

/*
KeySend.ControlWinKey(Keys.Left);
*/

public class KeySend
{
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
    private const int KEYEVENTF_EXTENDEDKEY = 1;
    private const int KEYEVENTF_KEYUP = 2;

    public static void KeyDown(Keys vKey)
    {
        keybd_event((byte)vKey, 0, KEYEVENTF_EXTENDEDKEY, 0);
    }
    public static void KeyUp(Keys vKey)
    {
        keybd_event((byte)vKey, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
    }

    //see https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.keys?view=windowsdesktop-9.0 for key names
    public static void WinKey(Keys key)
    {
        KeyDown(Keys.LWin);
        KeyDown(key);
        KeyUp(Keys.LWin);
        KeyUp(key);
    }

    public static void ControlWinKey(Keys key)
    {
        KeyDown(Keys.LWin);
        KeyDown(Keys.ControlKey);
        KeyDown(key);
        KeyUp(Keys.LWin);
        KeyUp(Keys.ControlKey);
        KeyUp(key);
    }
        
    public static void ShiftWinKey(Keys key)
    {
        KeyDown(Keys.LWin);
        KeyDown(Keys.ShiftKey);
        KeyDown(key);
        KeyUp(Keys.LWin);
        KeyUp(Keys.ShiftKey);
        KeyUp(key);
    }
        
    public static void AltWinKey(Keys key)
    {
        KeyDown(Keys.LWin);
        KeyDown(Keys.Menu);
        KeyDown(key);
        KeyUp(Keys.LWin);
        KeyUp(Keys.Menu);
        KeyUp(key);
    }
        
    public static void ControlShiftWinKey(Keys key)
    {
        KeyDown(Keys.LWin);
        KeyDown(Keys.ControlKey);
        KeyDown(Keys.ShiftKey);
        KeyDown(key);
        KeyUp(Keys.LWin);
        KeyUp(Keys.ShiftKey);
        KeyUp(Keys.ControlKey);
        KeyUp(key);
    }
}