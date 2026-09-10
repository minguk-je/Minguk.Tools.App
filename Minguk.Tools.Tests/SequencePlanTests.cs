using System;
using System.Linq;
using System.Threading.Tasks;
using Minguk.Tools.Input;
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
                new SequenceStepDefinition { Kind = SequenceStepKind.Enter }
            ]
        };

        var steps = plan.Build(_service, holdTimeMs: 12).Steps;

        Post(ui.Input.Clear);
        var finished = await SequenceRunner.RunOnceAsync(steps, intervalMs: 20);
        await Task.Delay(400);

        var typed = Read(() => ui.Input.Text);

        // Shift 가 필요한 글자(C·!)를 넣어 두었다. 조합키가 빠지면 여기서 드러난다.
        Check("계획 실행 (영문·Shift·Enter)",
              finished && typed.StartsWith("abC!") && typed.Contains((char)10),
              $"입력란 {Describe(typed)} / 끝까지 {finished}");
    }
}
