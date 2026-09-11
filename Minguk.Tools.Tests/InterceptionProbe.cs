using System;
using System.Threading;

using Minguk.Tools.Input.Adapters;

namespace Minguk.Tools.Tests;

/// <summary>
/// Interception 드라이버가 실제로 입력을 넣는지. 자리마다 붙은 장치를 적고, 마우스를 3px 옮겨 커서가 움직였는지 본다.
/// 실제 입력이 한 번 나가므로(3px) 검증 묶음에는 안 끼운다 - 필요할 때 <c>--interception-probe</c> 로.
/// </summary>
internal static class InterceptionProbe
{
    public static int Run()
    {
        using var adapter = new InterceptionInputAdapter();

        Console.WriteLine($"사용 가능: {adapter.IsAvailable}  {adapter.UnavailableReason}");
        if (!adapter.IsAvailable) return 1;

        Console.WriteLine($"장치: {adapter.DescribeDevices()}");
        Console.WriteLine($"고른 자리: 키보드 {adapter.KeyboardDevice}, 마우스 {adapter.MouseDevice}");

        var before = adapter.GetCursorPosition();
        var sent = adapter.MoveMouseBy(3, 0);
        Thread.Sleep(100);
        var after = adapter.GetCursorPosition();

        Console.WriteLine($"상대 이동 (3, 0): 보냄={sent}, 커서 {before} → {after}");

        var moved = before is { } b && after is { } a && (a.X != b.X || a.Y != b.Y);
        Console.WriteLine(moved ? "== 커서가 움직였다 - 드라이버가 입력을 넣는다 ==" : "== 커서가 안 움직였다 - 빈 자리로 보냈거나 커서를 잡는 창이 앞에 있다 ==");

        return 0;
    }
}
