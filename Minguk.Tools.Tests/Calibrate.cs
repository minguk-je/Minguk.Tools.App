using System;
using System.Threading;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Adapters;

namespace Minguk.Tools.Tests;

/// <summary>
/// 정규화 값(0~65535)을 SendInput 에 그대로 넣고 커서가 어느 픽셀에 놓이는지 실측한다.
///
/// 왜 어댑터를 거치지 않는가
///   재려는 것이 우리 코드가 아니라 <b>OS 의 되돌리는 규칙</b>이다. 어댑터를 통과시키면
///   그 안의 환산이 섞여 무엇을 재는지 알 수 없게 된다.
///
/// 언제 쓰는가
///   Windows 가 정규화 좌표를 픽셀로 되돌리는 규칙은 문서에 없다.
///   <see cref="VirtualScreen.Normalize"/> 를 건드릴 일이 생기면 추측 대신 이 출력으로 판단한다.
/// </summary>
internal static class Calibrate
{
    public static int Run()
    {
        var screen = VirtualScreen.GetBounds();
        Console.WriteLine($"가상 화면: X {screen.Left}~{screen.Right - 1} (폭 {screen.Width}), "
                          + $"Y {screen.Top}~{screen.Bottom - 1} (높이 {screen.Height})");

        using var adapter = new SendInputAdapter();
        var origin = adapter.GetCursorPosition() ?? (0, 0);

        // y 는 화면 한가운데쯤 고정하고 x 축만 훑는다.
        const int fixedNy = 32768;

        // 먼저 커서가 실제로 움직이는지부터 본다. 안 움직이면 아래 탐색은 의미가 없다.
        Console.WriteLine();
        Console.WriteLine("[예비 확인] 정규화 값을 넣었을 때 커서가 가는 자리");

        var moved = false;
        foreach (var nx in (int[])[0, 16384, 32768, 49152, 65535])
        {
            var sent = adapter.MoveMouseToNormalized(nx, fixedNy);
            Thread.Sleep(30);
            var pos = adapter.GetCursorPosition() ?? (0, 0);

            Console.WriteLine($"  n=({nx,5},{fixedNy}) -> 커서 ({pos.Item1,6},{pos.Item2,5})  SendInput 반환 {sent}");
            if (pos.Item1 != origin.Item1) moved = true;
        }

        if (!moved)
        {
            Console.WriteLine();
            Console.WriteLine("== 커서가 전혀 움직이지 않았다. 아래 탐색은 의미가 없다 ==");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("[x축 경계 탐색] 목표 오프셋 k 를 처음으로 만들어 내는 최소 정규화 값");
        Console.WriteLine("    k |  최소 n | 지금 공식 | 판정");

        var mismatches = 0;
        var unreachable = 0;

        foreach (var k in (int[])[0, 1, 100, 1000, 1920, 3200, 4479, screen.Width - 1])
        {
            var firstN = FindFirstN(adapter, screen, fixedNy, k);
            var ours = VirtualScreen.Normalize(k, screen.Width);

            if (firstN < 0)
            {
                // 이음매에 맞닿은 열처럼 실제로 지정 불가능한 오프셋이 있다. 다만 전부 이러면
                // 측정이 잘못된 것이므로 아래에서 따로 센다.
                unreachable++;
                Console.WriteLine($"  {k,5} |    없음 | {ours,9} | 이 오프셋은 정규화 좌표로 지정할 수 없다");
                continue;
            }

            // 우리 값이 경계 이상이어야 그 픽셀에 닿는다. 모자라면 한 칸 앞으로 떨어진다.
            var ok = ours >= firstN;
            if (!ok) mismatches++;

            Console.WriteLine($"  {k,5} | {firstN,7} | {ours,9} | {(ok ? "닿음" : "모자람 - 한 칸 앞으로 떨어진다")}");
        }

        Console.WriteLine();
        Console.WriteLine(mismatches == 0 && unreachable == 0
            ? "== 지금 공식으로 모든 지점에 닿는다 =="
            : $"== 모자란 지점 {mismatches}개, 지정 불가 {unreachable}개 ==");

        adapter.MoveMouseTo(origin.Item1, origin.Item2);

        return mismatches == 0 && unreachable == 0 ? 0 : 1;
    }

    /// <summary>오프셋 k 가 처음 나타나는 최소 정규화 값을 이분 탐색으로 찾는다.</summary>
    private static int FindFirstN(SendInputAdapter adapter, VirtualScreen.Bounds screen, int ny, int k)
    {
        int lo = 0, hi = 65535, answer = -1;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;

            adapter.MoveMouseToNormalized(mid, ny);
            Thread.Sleep(12);
            var got = (adapter.GetCursorPosition()?.X ?? 0) - screen.Left;

            if (got >= k)
            {
                if (got == k) answer = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return answer;
    }


}
