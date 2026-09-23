using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Llm;

/// <summary>고쳐 준 판단 하나 - 이 상황이면 이 행동이 맞다.</summary>
public sealed record LlmExample(
    [property: JsonPropertyName("situation")] string Situation,
    [property: JsonPropertyName("action")] string Action);

/// <summary>판단 한 번의 결과 - 고른 행동과 모델이 댄 규칙 번호, 걸린 시간.</summary>
public sealed record LlmJudgement(string Action, int Rule, LlmResponse Response)
{
    public override string ToString() => Rule > 0 ? $"{Action} (규칙 {Rule})" : Action;
}

/// <summary>
/// 판단 요청을 만들고 답을 읽는다 - 모델을 부르는 길(<see cref="ILlmAdapter"/>)과 떼어 둔 순수한 규칙. 검사가 가짜 답으로 본다.
/// </summary>
/// <remarks>
/// <b>행동은 목록에서만 고르게 한다</b> - 답을 JSON 스키마의 <c>enum</c> 으로 묶어 목록 밖 글을 못 쓴다. 작은 모델이 엉뚱한 키를 누르게 할 길을 막는다.
///
/// <b>규칙 번호를 먼저 적게 한다</b>(실측 2026-09-24, 아이온2 사냥 장면 8개): 행동만 내게 하면 qwen3:4b 3/8, 규칙 번호를 먼저 적게 하면 4/8,
/// qwen3:8b 는 7/8. 번호 하나로만 답하게 하면(토큰을 아끼려고) 1/8 로 오히려 나빴다. 그래서 <c>{"rule": n, "action": "..."}</c> 꼴이다.
///
/// <b>고쳐 준 예시</b>(<see cref="LlmSpec.ExamplesFileName"/>)를 최근 것부터 몇 개 넣는다 - 모델을 다시 가르치지 않고 쓸수록 나아지는 곳이다.
/// </remarks>
public static class LlmJudge
{
    private static readonly JsonSerializerOptions ExampleOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>「공격, 물약, 후퇴」 → 목록. 쉼표·줄바꿈·세로줄로 가른다. 빈 것·겹친 것은 뺀다.</summary>
    public static IReadOnlyList<string> SplitActions(string? actions)
        => [.. (actions ?? string.Empty)
            .Split([',', '\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// 판단 요청 - 규칙서·예시·상황을 넣고 행동 목록으로 답을 묶는다. 그림을 주면 그림 모델용(화면판단).
    /// </summary>
    public static LlmRequest Build(string model, string? rules, IReadOnlyList<LlmExample> examples, string situation, IReadOnlyList<string> actions,
                                   byte[]? image = null, string keepAlive = "30m")
    {
        if (actions.Count == 0) throw new LlmException("고를 행동이 없습니다 - 판단(상황, \"공격, 물약, 후퇴\") 처럼 쉼표로 적으세요.");

        var system = new StringBuilder();

        system.AppendLine(image is null
            ? "너는 게임 자동 플레이의 판단 담당이다. 상황을 보고 행동 목록 중 하나만 고른다."
            : "너는 게임 자동 플레이의 판단 담당이다. 게임 화면과 질문을 보고 행동 목록 중 하나만 고른다.");

        if (!string.IsNullOrWhiteSpace(rules))
        {
            system.AppendLine();
            system.AppendLine("규칙(번호가 작을수록 먼저):");
            system.AppendLine(rules.Trim());
        }

        system.AppendLine();
        system.AppendLine($"행동 목록: {string.Join(", ", actions)}");
        system.AppendLine("먼저 해당하는 규칙 번호(rule, 없으면 0)를 적고 행동(action)을 고른다.");

        var messages = new List<LlmMessage> { new("system", system.ToString()) };

        // 고쳐 준 예시 - 대화처럼 넣으면 작은 모델도 모양을 따라 한다. 이번 행동 목록에 있는 것만(없는 행동을 예시로 보이면 헷갈린다).
        foreach (var example in examples.Where(e => actions.Contains(e.Action, StringComparer.Ordinal)))
        {
            messages.Add(new LlmMessage("user", example.Situation));
            messages.Add(new LlmMessage("assistant", new JsonObject { ["rule"] = 0, ["action"] = example.Action }.ToJsonString(ExampleOptions)));
        }

        messages.Add(new LlmMessage("user", situation, image is null ? null : [image]));

        return new LlmRequest(model, messages)
        {
            Format = Schema(actions),
            MaxTokens = 60,
            KeepAlive = keepAlive
        };
    }

    /// <summary>자유 글 물음 - <c>물어보기</c>·<c>화면물어보기</c>.</summary>
    public static LlmRequest BuildQuestion(string model, string? rules, string question, byte[]? image = null, string keepAlive = "30m")
    {
        var messages = new List<LlmMessage>();

        if (!string.IsNullOrWhiteSpace(rules)) messages.Add(new LlmMessage("system", "게임 규칙:\n" + rules.Trim()));

        messages.Add(new LlmMessage("user", question, image is null ? null : [image]));

        return new LlmRequest(model, messages) { MaxTokens = 400, KeepAlive = keepAlive };
    }

    /// <summary><c>{"rule": 정수, "action": 목록 중 하나}</c>.</summary>
    public static JsonObject Schema(IReadOnlyList<string> actions) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["rule"] = new JsonObject { ["type"] = "integer" },
            ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. actions.Select(a => (JsonNode)a)]) }
        },
        ["required"] = new JsonArray("rule", "action")
    };

    /// <summary>
    /// 답을 읽어 행동을 고른다. 목록에 없으면 <see cref="LlmException"/> - 모르는 행동을 흘려 보내지 않는다.
    /// </summary>
    /// <remarks>스키마로 묶어도 모델이 앞뒤에 공백·코드 울타리를 붙이는 일이 있어 첫 <c>{</c> 부터 끝 <c>}</c> 까지만 읽는다.</remarks>
    public static (string Action, int Rule) Parse(string text, IReadOnlyList<string> actions)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start >= 0 && end > start)
        {
            try
            {
                using var document = JsonDocument.Parse(text[start..(end + 1)]);
                var root = document.RootElement;
                var action = root.TryGetProperty("action", out var a) ? a.GetString()?.Trim() ?? string.Empty : string.Empty;
                var rule = root.TryGetProperty("rule", out var r) && r.TryGetInt32(out var n) ? n : 0;

                if (actions.FirstOrDefault(x => string.Equals(x, action, StringComparison.Ordinal)) is { } exact) return (exact, rule);
            }
            catch (JsonException)
            {
            }
        }

        // 스키마 없이 글로만 답한 모델 - 목록의 행동 이름이 딱 하나 들어 있으면 그것.
        var mentioned = actions.Where(x => text.Contains(x, StringComparison.Ordinal)).ToList();

        if (mentioned.Count == 1) return (mentioned[0], 0);

        throw new LlmException($"모델 답에서 행동을 못 골랐습니다 - 「{Shorten(text)}」. 행동 목록: {string.Join(", ", actions)}");
    }

    // ── 규칙서 · 예시 파일 ───────────────────────────────────────────────

    /// <summary>프로젝트 폴더의 규칙서. 없으면 null.</summary>
    public static string? LoadRules(string root)
    {
        var path = Path.Combine(root, LlmSpec.RulesFileName);

        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    /// <summary>고쳐 준 예시 - 최근 것 <paramref name="most"/> 개(오래된 것부터 차례로). 망가진 줄은 건너뛴다.</summary>
    public static IReadOnlyList<LlmExample> LoadExamples(string root, int most)
    {
        var path = Path.Combine(root, LlmSpec.ExamplesFileName);

        if (most <= 0 || !File.Exists(path)) return [];

        var examples = new List<LlmExample>();

        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                if (JsonSerializer.Deserialize<LlmExample>(line) is { Situation.Length: > 0, Action.Length: > 0 } example) examples.Add(example);
            }
            catch (JsonException)
            {
            }
        }

        return examples.Count <= most ? examples : examples[^most..];
    }

    /// <summary>고친 판단을 한 줄 붙인다.</summary>
    public static void AppendExample(string root, LlmExample example)
        => File.AppendAllText(Path.Combine(root, LlmSpec.ExamplesFileName), JsonSerializer.Serialize(example, ExampleOptions) + "\n", new UTF8Encoding(false));

    private static string Shorten(string text) => text.Length <= 120 ? text : text[..120] + "…";
}
