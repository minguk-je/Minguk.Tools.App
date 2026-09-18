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

        var korean = live.GetAsync("검출", 2).GetAwaiter().GetResult();
        Check("C# 완성: 한글 이름과 실시간 전용이 나온다", korean.Any(s => s.Text == "검출들") && korean.Any(s => s.Text == "검출기다리기"),
              string.Join(", ", korean.Where(s => s.Text.StartsWith("검출")).Select(s => s.Text)));

        var planOnly = plan.GetAsync("검출", 2).GetAwaiter().GetResult();
        Check("C# 완성: 계획 모드에는 실시간 전용이 안 나온다", planOnly.All(s => s.Text != "검출들") && planOnly.Any(s => s.Text == "MoveTo" || s.Text == "이동" || s.Text == "Type"),
              $"{planOnly.Count}개");

        const string member = "var 검출 = 가장가까운검출();\n검출.중";
        var members = live.GetAsync(member, member.Length).GetAwaiter().GetResult();
        Check("C# 완성: 지역 변수의 멤버가 나온다", members.Any(s => s.Text == "중심x"), string.Join(", ", members.Take(6).Select(s => s.Text)));

        watch.Restart();
        var again = live.GetAsync("var s = \"a\"; s.Le", 17).GetAwaiter().GetResult();
        watch.Stop();
        Check("C# 완성: string 멤버가 나오고 두 번째부터는 빠르다", again.Any(s => s.Text == "Length") && watch.ElapsedMilliseconds < 1500,
              $"{watch.ElapsedMilliseconds}ms, {again.Count}개");

        var keyword = live.GetAsync("whi", 3).GetAwaiter().GetResult();
        Check("C# 완성: 키워드가 나온다", keyword.Any(s => s.Text == "while" && s.Kind == "키워드"), string.Join(", ", keyword.Take(5).Select(s => $"{s.Text}({s.Kind})")));

        TestCSharpClassification((IScriptClassifier)live);
    }

    /// <summary>
    /// 컴파일러 분류가 VS 와 같은 종류를 주는지. 정규식으로는 못 가리던 것들(점 없는 필드, 스크립트 안 함수, 형식)을 본다.
    /// </summary>
    private static void TestCSharpClassification(IScriptClassifier classifier)
    {
        const string source = """
            const int 간격 = 1000;
            long 다음 = 0;
            void 탄약보기(int 문턱)
            {
                long 지금 = Environment.TickCount64;
                if (지금 < 다음) return;
                다음 = 지금 + 간격;
            }
            while (!중지되었나())
            {
                var 검출 = 목표();
                if (검출 is null) continue;
                if (조준(검출)) 클릭();
                var x = 검출.머리x; // 머리
                탄약보기(3);
                출력("끝");
            }
            """;

        var watch = Stopwatch.StartNew();
        var tokens = classifier.ClassifyAsync(source).GetAwaiter().GetResult();
        watch.Stop();

        ScriptTokenKind? KindAt(string word, int occurrence = 0)
        {
            var index = -1;
            for (var i = 0; i <= occurrence; i++) index = source.IndexOf(word, index + 1, StringComparison.Ordinal);
            if (index < 0) return null;

            return tokens.FirstOrDefault(t => t.Start == index && t.Length == word.Length) is { Length: > 0 } hit ? hit.Kind : null;
        }

        var expected = new (string Word, int Occurrence, ScriptTokenKind Kind)[]
        {
            ("while", 0, ScriptTokenKind.ControlKeyword),
            ("return", 0, ScriptTokenKind.ControlKeyword),
            ("continue", 0, ScriptTokenKind.ControlKeyword),
            ("var", 0, ScriptTokenKind.Keyword),
            ("long", 0, ScriptTokenKind.Keyword),
            ("조준", 0, ScriptTokenKind.Method),         // 전역 API
            ("탄약보기", 1, ScriptTokenKind.Method),     // 스크립트 안에서 만든 함수를 부른 곳 - 정규식은 이름 뒤 ( 로만 알았다
            ("검출", 1, ScriptTokenKind.Local),
            ("문턱", 0, ScriptTokenKind.Local),          // 매개변수
            ("다음", 1, ScriptTokenKind.Member),         // 스크립트 최상위 변수는 제출 클래스의 필드다 - 점 없이 쓴 필드
            ("머리x", 0, ScriptTokenKind.Member),
            ("TickCount64", 0, ScriptTokenKind.Member),
            ("Environment", 0, ScriptTokenKind.Type),
            ("1000", 0, ScriptTokenKind.Number),
            ("\"끝\"", 0, ScriptTokenKind.String),
            ("// 머리", 0, ScriptTokenKind.Comment),
        };

        var wrong = expected
            .Select(e => (e.Word, e.Kind, Got: KindAt(e.Word, e.Occurrence)))
            .Where(e => e.Got != e.Kind)
            .Select(e => $"{e.Word}: {e.Got?.ToString() ?? "없음"} (기대 {e.Kind})")
            .ToList();

        Check("C# 분류: 낱말 종류가 VS 기준과 같다", wrong.Count == 0,
              wrong.Count == 0 ? $"{expected.Length}개 맞음, 토막 {tokens.Count}개, {watch.ElapsedMilliseconds}ms" : string.Join(" / ", wrong));
    }
}
