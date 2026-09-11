using System;
using System.Runtime.InteropServices;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>지금 앞에 있는 창. 입력을 보내기 전에 대상이 앞에 있는지 볼 때 쓴다.</summary>
internal static class ForegroundWindow
{
    public static IntPtr Handle => GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
