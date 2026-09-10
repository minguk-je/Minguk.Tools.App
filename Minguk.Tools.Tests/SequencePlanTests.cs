using System;
using System.Linq;
using System.Threading.Tasks;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Korean;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.Tests;

/// <summary>
/// 적어 둔 단계(<see cref="SequencePlan"/>)가 저장되고, 되읽히고, 적은 순서 그대로
/// 실행 시퀀스가 되는지.
/// </summary>
/// <remarks>
/// 화면이 단계를 데이터로 들고 있으므로 여기가 틀리면 사용자가 짠 순서와 실제로 나가는 순서가
/// 어긋난다. 그런데 어긋나도 입력은 그럴싸하게 나가서 눈으로는 알아채기 어렵다.
/// </remarks>
internal static partial class Program
{
    private static void TestSequencePlan()
    {
        // ── 적은 순서대로 나오는지 ──
        var plan = new SequencePlan
        {
            Steps =
            [
                new SequenceStepDefinition { Kind = SequenceStepKind.MoveTo, X = 100, Y = 200 },
                new SequenceStepDefinition { Kind = SequenceStepKind.Click },
                new SequenceStepDefinition { Kind = SequenceStepKind.Wait, DelayMs = 250 },
                new SequenceStepDefinition { Kind = SequenceStepKind.Scroll, Notches = -3 }
            ]
        };

        // 마우스 단계만 담았다. 스캔코드를 못 넣는 경로에서도 네 단계가 그대로 나와야 한다.
        var order = plan.Build(_service, holdTimeMs: 10).Describe();

        Check("계획 → 시퀀스 순서",
              order == "이동(100,200) → 좌클릭 → 250ms 쉬기 → 휠 아래",
              $"\"{order}\"");

        // ── 저장하고 되읽기 ──
        var restored = SequencePlan.FromJson(plan.ToJson(), out var error);

        var same = error is null
                   && restored.Steps.Count == plan.Steps.Count
                   && restored.Steps.Zip(plan.Steps).All(p => p.First.Describe() == p.Second.Describe());

        Check("계획 저장·복구", same,
              error is not null
                  ? $"읽지 못했다: {error}"
                  : $"{restored.Steps.Count}단계 / {string.Join(" → ", restored.Steps.Select(s => s.Describe()))}");

        // ── 깨진 것을 만나면 ──
        //     설정 파일은 사람이 손댈 수 있다. 여기서 터지면 화면이 아예 안 뜬다.
        var broken = SequencePlan.FromJson("{ 이건 JSON 이 아니다", out var brokenError);

        Check("깨진 계획은 기본값으로",
              brokenError is not null && broken.Steps.Count > 0,
              brokenError is null ? "오류를 알리지 않았다" : $"기본값 {broken.Steps.Count}단계로 되돌림");

        // ── 종류를 이름으로 적는지 ──
        //     숫자로 적으면 나중에 열거형 순서를 바꿨을 때 저장된 것이 다른 뜻이 된다.
        var json = plan.ToJson();

        Check("종류를 이름으로 저장", json.Contains("\"MoveTo\"") && json.Contains("\"Scroll\""),
              json.Length > 160 ? json[..160] + "…" : json);
    }

    /// <summary>
    /// 스크립트가 계획과 온전히 오가는지, 틀린 줄을 제대로 짚는지.
    /// </summary>
    /// <remarks>
    /// 이제 사용자가 손으로 쓰는 것이 이 글이다. 여기가 틀리면 적은 것과 나가는 것이
    /// 어긋나는데, 어긋나도 그럴싸하게 나가서 눈으로는 알아채기 어렵다.
    /// </remarks>
    private static void TestSequenceScript()
    {
        // ── 한 바퀴 돌아 제자리로 ──
        var plan = new SequencePlan
        {
            Steps =
            [
                new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = "안녕 \"큰따옴표\"" },
                new SequenceStepDefinition { Kind = SequenceStepKind.Enter },
                new SequenceStepDefinition { Kind = SequenceStepKind.ToggleHangul },
                new SequenceStepDefinition { Kind = SequenceStepKind.Click, Button = MouseButton.Right },
                new SequenceStepDefinition { Kind = SequenceStepKind.MoveTo, X = -1920, Y = 360 },
                new SequenceStepDefinition { Kind = SequenceStepKind.Scroll, Notches = -3 },
                new SequenceStepDefinition { Kind = SequenceStepKind.Wait, DelayMs = 250 }
            ]
        };

        var text = SequenceScript.ToText(plan);
        var ok = SequenceScript.TryParse(text, out var back, out var errors);

        var same = ok
                   && back.Steps.Count == plan.Steps.Count
                   && back.Steps.Zip(plan.Steps).All(p => p.First.Describe() == p.Second.Describe());

        Check("스크립트 왕복", same,
              same ? $"{plan.Steps.Count}줄이 그대로 돌아왔다"
                   : $"오류 {errors.Count}건 / 돌아온 것 {string.Join(" → ", back.Steps.Select(s => s.Describe()))}");

        // ── 주석과 빈 줄은 세지 않는다 ──
        const string withNoise = """
                                 # 이건 주석
                                 글자 "가"

                                 Enter   # 뒤에 붙은 주석
                                 """;

        SequenceScript.TryParse(withNoise, out var trimmed, out _);
        Check("주석·빈 줄 건너뛰기", trimmed.Steps.Count == 2,
              $"{trimmed.Steps.Count}단계: {string.Join(" → ", trimmed.Steps.Select(s => s.Describe()))}");

        // ── 따옴표 안의 # 은 주석이 아니다 ──
        SequenceScript.TryParse("글자 \"값 #1\"", out var hashInside, out _);
        Check("따옴표 안의 # 은 글자",
              hashInside.Steps.Count == 1 && hashInside.Steps[0].Text == "값 #1",
              hashInside.Steps.Count == 1 ? $"[{hashInside.Steps[0].Text}]" : "단계가 안 생겼다");

        // ── 틀린 줄은 번호와 함께, 한 번에 모아서 ──
        //     첫 오류에서 멈추면 열 줄 틀렸을 때 열 번을 돌아야 한다.
        const string broken = """
                              글자 "괜찮은 줄"
                              이동 열
                              춤춰
                              쉬기 -5
                              """;

        var good = SequenceScript.TryParse(broken, out var partial, out var found);

        Check("틀린 줄을 한 번에 모은다",
              !good && found.Count == 3 && partial.Steps.Count == 1
              && found[0].Line == 2 && found[1].Line == 3 && found[2].Line == 4,
              $"오류 {found.Count}건 (줄 {string.Join(",", found.Select(e => e.Line))}), 살아남은 단계 {partial.Steps.Count}개");

        // ── 닫히지 않은 따옴표 ──
        SequenceScript.TryParse("글자 \"안 닫음", out _, out var unclosed);
        Check("안 닫힌 따옴표를 잡는다", unclosed.Count == 1,
              unclosed.Count == 1 ? unclosed[0].ToString() : $"오류 {unclosed.Count}건");
    }

    /// <summary>
    /// 스크립트 엔진이 글을 단계로 바꾸는지.
    /// </summary>
    /// <remarks>
    /// 스크립트는 돌아도 <b>입력이 나가지 않는다</b> - 부른 것이 단계로 적힐 뿐이다.
    /// 그 덕에 순서 미리보기·반복·중지가 그대로 살아 있다. 여기서 보는 것이 그 성질이다.
    ///
    /// 반복문이 단계로 풀리는지도 함께 본다. 풀리지 않으면 순서 미리보기가 거짓말을 한다.
    /// </remarks>
    private static async Task TestScriptEngineAsync()
    {
        await TestOneEngineAsync(ScriptLanguage.CSharp);
        await TestOneEngineAsync(ScriptLanguage.JavaScript);

        // 파이썬은 첫 실행 때 11MB 를 받아 온다. 이미 받아 둔 PC 에서만 잰다 -
        // 검증 하나 때문에 네트워크를 타고 내려받게 하지 않는다.
        if (PythonRuntimeInstaller.IsInstalled) await TestOneEngineAsync(ScriptLanguage.Python);
        else Skip("스크립트 엔진 (Python)", "런타임이 아직 안 깔려 있다 - 화면에서 한 번 고르면 받아 온다");
    }

    /// <summary>
    /// 한 언어를 잰다. 세 언어가 <b>같은 것을 같은 이름으로</b> 해야 한다.
    /// </summary>
    /// <remarks>
    /// 언어마다 다른 잣대를 대면 "이 언어에서는 원래 그래" 가 쌓인다. 문법만 다르고
    /// 되는 일은 같아야 언어를 오갈 수 있다.
    /// </remarks>
    private static async Task TestOneEngineAsync(ScriptLanguage language)
    {
        using var engine = ScriptEngineFactory.Create(language);

        await engine.PrepareAsync();

        if (!engine.IsReady)
        {
            Skip($"스크립트 엔진 ({language})", engine.UnavailableReason ?? "준비되지 않았다");
            return;
        }

        var semi = language == ScriptLanguage.Python ? "" : ";";

        // ── 부른 대로 단계가 되는지 ──
        var (plan, errors) = await engine.RunAsync(string.Join(Environment.NewLine,
        [
            $"Type(\"가\"){semi}",
            $"Enter(){semi}",
            $"MoveTo(100, 200){semi}",
            $"Click(MouseButton.Right){semi}",
            $"Wait(250){semi}"
        ]));

        var order = string.Join(" → ", plan.Steps.Select(s => s.Describe()));

        Check($"스크립트 → 단계 ({language})",
              errors.Count == 0 && order == "글자 \"가\" → Enter → 이동 (100, 200) → 우클릭 → 250ms 쉬기",
              errors.Count > 0 ? errors[0].ToString() : order);

        // ── 반복문이 풀리는지 ──
        var loop = language == ScriptLanguage.Python
            ? "for i in range(3):\n    Type(\"x\")"
            : "for (var i = 0; i < 3; i++) Type(\"x\");";

        var (unrolled, loopErrors) = await engine.RunAsync(loop);

        Check($"반복문이 단계로 풀린다 ({language})",
              loopErrors.Count == 0 && unrolled.Steps.Count == 3,
              loopErrors.Count > 0 ? loopErrors[0].ToString() : $"{unrolled.Steps.Count}단계");

        // ── 한글 이름도 되는지 ──
        var (korean, koreanErrors) = await engine.RunAsync(
            string.Join(Environment.NewLine, [$"글자(\"안녕\"){semi}", $"엔터(){semi}"]));

        Check($"한글 이름 ({language})", koreanErrors.Count == 0 && korean.Steps.Count == 2,
              koreanErrors.Count > 0 ? koreanErrors[0].ToString() : $"{korean.Steps.Count}단계");

        // ── 사용자가 함수를 만들어 써도 되는지 ──
        //     이 화면에서 짜는 것이 결국 프로그램이라, 되풀이되는 것을 묶을 수 있어야 한다.
        var define = language switch
        {
            ScriptLanguage.Python => "def 두번(s):\n    Type(s)\n    Type(s)\n\n두번(\"ab\")",
            ScriptLanguage.JavaScript => "function 두번(s) { Type(s); Type(s); }\n두번(\"ab\");",
            _ => "void 두번(string s) { Type(s); Type(s); }\n두번(\"ab\");"
        };

        var (custom, customErrors) = await engine.RunAsync(define);

        Check($"사용자 정의 함수 ({language})", customErrors.Count == 0 && custom.Steps.Count == 2,
              customErrors.Count > 0 ? customErrors[0].ToString()
                                     : string.Join(" → ", custom.Steps.Select(s => s.Describe())));

        // ── 틀린 글은 줄 번호와 함께 ──
        var (broken, brokenErrors) = await engine.RunAsync($"Type(\"ok\"){semi}{Environment.NewLine}이건 문법이 아니다 (((");

        // 줄 번호까지 맞히는 것은 언어마다 다르다. 오류를 내고 계획을 안 만드는 것만 본다.
        Check($"문법 오류를 잡는다 ({language})",
              brokenErrors.Count > 0 && broken.Steps.Count == 0,
              brokenErrors.Count > 0 ? brokenErrors[0].ToString() : "오류를 안 냈다");

        // ── 도는 중에 터지면 아무것도 안 남긴다 ──
        //     반쪽짜리 시퀀스를 돌리는 것보다 안 돌리는 것이 낫다.
        var raise = language switch
        {
            ScriptLanguage.Python => "Type(\"a\")\nraise Exception(\"일부러\")",
            ScriptLanguage.JavaScript => "Type(\"a\");\nthrow new Error(\"일부러\");",
            _ => "Type(\"a\");\nthrow new System.Exception(\"일부러\");"
        };

        var (half, runErrors) = await engine.RunAsync(raise);

        Check($"도는 중에 터지면 계획을 버린다 ({language})",
              runErrors.Count > 0 && half.Steps.Count == 0,
              runErrors.Count > 0 ? runErrors[0].ToString() : "오류를 안 냈다");
    }

    /// <summary>
    /// 한/영 전환이 실제로 뒤집히는지.
    /// </summary>
    /// <remarks>
    /// 스캔코드 경로는 한/영 키를 누르고, 창 메시지 경로는 대상 창의 IME 에게 직접 말한다
    /// (WM_IME_CONTROL). 길은 다르지만 결과는 같아야 한다.
    ///
    /// 입력 언어가 한국어가 아니면 IME 자체가 없어 읽지도 쓰지도 못한다. 그때는 건너뛴다 -
    /// 환경 탓이지 코드 탓이 아니다.
    /// </remarks>
    private static async Task TestImeToggleAsync()
    {
        if (!_service.SupportsHangulToggle)
        {
            Skip("한/영 전환", $"{_adapter.Name} 은 한/영 을 뒤집지 못한다");
            return;
        }

        var by = _adapter is IImeControl ? "IME 에 직접" : "한/영 키";

        if (!KoreanKeyboardInfo.TryGetForegroundHangulMode(out var before))
        {
            Skip("한/영 전환", "이 창의 IME 상태를 읽지 못한다 - 입력 언어가 한국어가 아닐 수 있다");
            return;
        }

        var sequence = new InputSequence(_service, holdTimeMs: 60).ToggleHangul();

        if (sequence.Steps.Count == 0)
        {
            Fail("한/영 전환", $"{_adapter.Name} 이 한/영 을 뒤집는다고 했는데 단계가 안 담겼다");
            return;
        }

        await SequenceRunner.RunOnceAsync(sequence.Steps, intervalMs: 0);
        await Task.Delay(300);

        KoreanKeyboardInfo.TryGetForegroundHangulMode(out var after);

        // 뒤집은 뒤에는 원래대로 돌려놓는다. 다음 검증이 엉뚱한 모드에서 돌면 안 된다.
        await SequenceRunner.RunOnceAsync(sequence.Steps, intervalMs: 0);
        await Task.Delay(300);

        KoreanKeyboardInfo.TryGetForegroundHangulMode(out var restored);

        Check($"한/영 전환 ({by})", after != before && restored == before,
              $"{Mode(before)} -> {Mode(after)} -> {Mode(restored)}");
    }

    private static string Mode(bool isHangul) => isHangul ? "한글" : "영문";

    /// <summary>
    /// 계획으로 만든 시퀀스가 실제로 창에 닿는지. 순서 문구만 맞고 안 나가면 소용없다.
    /// </summary>
    /// <remarks>
    /// 영문과 Enter 만 쓴다 - <b>세 경로 모두에서 돌아야 하기 때문이다.</b>
    /// 스캔코드를 못 넣는 경로(PostMessage)도 가상 키로는 이것들을 넣을 수 있다.
    /// 한때 그 경로에서 이 검증을 통째로 건너뛰었는데, 그래서 "Enter 가 빠진다" 는
    /// 잘못된 믿음을 오래 들고 있었다.
    /// </remarks>
    private static async Task TestPlanRunAsync(TestWindow ui)
    {
        var plan = new SequencePlan
        {
            Steps =
            [
                new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = "ab" },
                new SequenceStepDefinition { Kind = SequenceStepKind.Wait, DelayMs = 30 },
                new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = "C!" },
                new SequenceStepDefinition { Kind = SequenceStepKind.Enter },
                new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = "한글" }
            ]
        };

        var steps = plan.Build(_service, holdTimeMs: 12).Steps;

        Post(ui.Input.Clear);
        var finished = await SequenceRunner.RunOnceAsync(steps, intervalMs: 20);
        await Task.Delay(400);

        var typed = Read(() => ui.Input.Text);

        // Shift 가 필요한 글자(C·!)를 넣어 두었다. 조합키가 빠지면 여기서 드러난다.
        // 한글은 경로에 따라 갈린다 - 넣을 수 있다고 한 경로에서만 도착해야 한다.
        var hangulExpected = _service.CanTypeWithoutScanCode('한') || _service.SupportsTyping;
        var hangulArrived = typed.Contains("한글");

        Check("계획 실행 (영문·Shift·Enter·한글)",
              finished && typed.StartsWith("abC!") && typed.Contains((char)10)
              && hangulArrived == hangulExpected,
              $"입력란 {Describe(typed)} / 한글 기대 {hangulExpected} 실제 {hangulArrived} / 끝까지 {finished}");
    }
}
