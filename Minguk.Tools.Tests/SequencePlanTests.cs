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

    /// <summary>계획으로 만든 시퀀스가 실제로 창에 닿는지. 순서 문구만 맞고 안 나가면 소용없다.</summary>
    private static async Task TestPlanRunAsync(TestWindow ui)
    {
        if (!_service.SupportsTyping)
        {
            Skip("계획 실행", $"{_adapter.Name} 은 스캔코드를 넣지 못한다");
            return;
        }

        var plan = new SequencePlan
        {
            Steps =
            [
                new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = "ab" },
                new SequenceStepDefinition { Kind = SequenceStepKind.Wait, DelayMs = 30 },
                new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = "c" }
            ]
        };

        var steps = plan.Build(_service, holdTimeMs: 12).Steps;

        Post(ui.Input.Clear);
        var finished = await SequenceRunner.RunOnceAsync(steps, intervalMs: 20);
        await Task.Delay(400);

        var typed = Read(() => ui.Input.Text);

        Check("계획 실행", finished && typed == "abc", $"입력란 {Describe(typed)} / 끝까지 {finished}");
    }
}
