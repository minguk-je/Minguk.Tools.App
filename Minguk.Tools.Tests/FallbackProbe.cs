using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Minguk.Tools.Input;

namespace Minguk.Tools.Tests;

/// <summary>
/// Interception 드라이버가 없는 상황을 만들어 자동 폴백이 실제로 도는지 확인한다.
/// </summary>
/// <remarks>
/// 드라이버가 설치된 PC 에서는 폴백 분기가 영영 안 타므로, interception.dll 로드를
/// 의도적으로 실패시켜 그 상황을 재현한다. 시스템 파일은 건드리지 않는다.
/// 한 번 결정된 네이티브 함수 바인딩은 되돌릴 수 없어(성공적으로 로드된 뒤에는 리졸버가
/// 다시 불리지 않는다) 본 검증과 같은 프로세스에서 돌릴 수 없다. 그래서 인자로 분리했다.
/// </remarks>
internal static class FallbackProbe
{
    public static int Run()
    {
        var failures = 0;

        // 이 어셈블리가 아니라 InterceptionNative 가 든 어셈블리(Minguk.Tools)를 가로채야 한다.
        var target = typeof(InputAdapterFactory).Assembly;

        NativeLibrary.SetDllImportResolver(target, (name, _, _) =>
            name.Equals("interception.dll", StringComparison.OrdinalIgnoreCase)
                ? throw new DllNotFoundException("검증이 의도적으로 실패시킨 로드다.")
                : IntPtr.Zero);

        // 드라이버가 없는 상태에서 Interception 을 고르면 SendInput 으로 내려앉아야 한다.
        var selection = InputAdapterFactory.CreateWithFallback(
            InputBackend.Interception, InputBackend.SendInput, () => IntPtr.Zero);

        using var adapter = selection.Adapter;

        failures += Check("드라이버 없을 때 Interception 선택",
                          selection.FellBack && adapter.IsAvailable && adapter.Name == "SendInput",
                          $"폴백={selection.FellBack}, 밀려난 것={selection.FellBackFrom ?? "(없음)"}, "
                          + $"지금={adapter.Name}, 사용 가능={adapter.IsAvailable}");

        failures += Check("폴백 사유를 사용자에게 전달할 수 있는지",
                          !string.IsNullOrWhiteSpace(selection.Reason),
                          $"사유=\"{selection.Reason ?? "(없음)"}\"");

        // 폴백한 뒤에도 실제로 입력을 보낼 수 있어야 한다.
        var moved = adapter.GetCursorPosition() is { } p && adapter.MoveMouseTo(p.X, p.Y);
        failures += Check("폴백한 경로로 전송 가능", moved, $"경로={adapter.Name}");

        // 처음부터 SendInput 을 고르면 폴백 없이 그대로 쓰여야 한다.
        var direct = InputAdapterFactory.CreateWithFallback(
            InputBackend.SendInput, InputBackend.SendInput, () => IntPtr.Zero);
        using var directAdapter = direct.Adapter;

        failures += Check("SendInput 직접 선택은 폴백 아님",
                          !direct.FellBack && directAdapter.Name == "SendInput",
                          $"폴백={direct.FellBack}, 경로={directAdapter.Name}");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "== 폴백 검증 통과 ==" : $"== 폴백 검증 실패 {failures}건 ==");

        return failures == 0 ? 0 : 1;
    }

    private static int Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name} — {detail}");
        return ok ? 0 : 1;
    }
}
