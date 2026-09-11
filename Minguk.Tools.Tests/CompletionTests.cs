using System;
using System.Diagnostics;
using System.Linq;

using Minguk.Tools.Input.Scripting;

namespace Minguk.Tools.Tests;

/// <summary>
/// C# 코드 완성(Roslyn). 전역(API 이름)·한글 이름·지역 변수의 멤버·키워드가 나오는지, 두 번째부터는 빠른지.
/// </summary>
internal static partial class Program
{
    private static void TestCSharpCompletion()
    {
        var live = ScriptCompletionSourceFactory.Create(ScriptLanguage.CSharp, isLive: true)!;
        var plan = ScriptCompletionSourceFactory.Create(ScriptLanguage.CSharp, isLive: false)!;

        var watch = Stopwatch.StartNew();
        var first = live.GetAsync("Ty", 2).GetAwaiter().GetResult();
        var firstMs = watch.ElapsedMilliseconds;

        Check("C# 완성: 전역 API 가 나온다", first.Any(s => s.Text == "Type"), $"{first.Count}개, 처음 {firstMs}ms");

        var korean = live.GetAsync("몹", 1).GetAwaiter().GetResult();
        Check("C# 완성: 한글 이름과 실시간 전용이 나온다", korean.Any(s => s.Text == "몹들") && korean.Any(s => s.Text == "몹기다리기"),
              string.Join(", ", korean.Where(s => s.Text.StartsWith('몹')).Select(s => s.Text)));

        var planOnly = plan.GetAsync("몹", 1).GetAwaiter().GetResult();
        Check("C# 완성: 계획 모드에는 실시간 전용이 안 나온다", planOnly.All(s => s.Text != "몹들") && planOnly.Any(s => s.Text == "MoveTo" || s.Text == "이동" || s.Text == "Type"),
              $"{planOnly.Count}개");

        const string member = "var 몹 = 가장가까운몹();\n몹.중";
        var members = live.GetAsync(member, member.Length).GetAwaiter().GetResult();
        Check("C# 완성: 지역 변수의 멤버가 나온다", members.Any(s => s.Text == "중심x"), string.Join(", ", members.Take(6).Select(s => s.Text)));

        watch.Restart();
        var again = live.GetAsync("var s = \"a\"; s.Le", 17).GetAwaiter().GetResult();
        watch.Stop();
        Check("C# 완성: string 멤버가 나오고 두 번째부터는 빠르다", again.Any(s => s.Text == "Length") && watch.ElapsedMilliseconds < 1500,
              $"{watch.ElapsedMilliseconds}ms, {again.Count}개");

        var keyword = live.GetAsync("whi", 3).GetAwaiter().GetResult();
        Check("C# 완성: 키워드가 나온다", keyword.Any(s => s.Text == "while" && s.Kind == "키워드"), string.Join(", ", keyword.Take(5).Select(s => $"{s.Text}({s.Kind})")));
    }
}
