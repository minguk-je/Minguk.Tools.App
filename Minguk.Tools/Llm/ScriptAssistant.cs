using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

using Minguk.Tools.Input.Scripting;

namespace Minguk.Tools.Llm;

/// <summary>
/// 도우미가 고른 동작 하나. <see cref="Box"/> 는 화면 기준 0~1(자리가 필요 없는 동작이면 null).
/// </summary>
public sealed record AssistantPlan(string Action, string Name, Rect? Box, string Key, int Ms, string Text, string Code, string Say)
{
    /// <summary>영역·본보기를 만들어야 하는 동작인가(누르기·나타날때까지).</summary>
    public bool NeedsRegion => Action is ScriptAssistant.Press or ScriptAssistant.WaitFor;
}

/// <summary>
/// 스크립트 도우미의 순수한 규칙 - 요청 만들기, 답 읽기, 코드 적기. 화면(<c>ScriptStudioViewModel.Assistant</c>)이 부른다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-24) "현재 화면 미리보기로 보여주고 오른쪽 위에 버튼 눌러줘. 이러면 스크립트 내용에 순차 적으로 만들어 주는거야.. 함수도 니가 미리 만들어 둔거 참고 해서".
/// 설계: <c>docs/superpowers/specs/2026-09-24-스크립트-도우미-design.md</c>.
///
/// <b>모델에는 "무슨 동작을, 어디에" 만 묻는다</b> - 동작은 스키마 <c>enum</c> 으로 묶고 코드는 여기서 적는다(<see cref="Render"/>). 작은 모델에 C# 을
/// 통째로 쓰게 하면 없는 함수를 지어낸다. 목록에 없는 일만 <see cref="Code"/> 로 자유 코드를 받고, 화면이 컴파일 검사로 거른다.
/// </remarks>
public static class ScriptAssistant
{
    public const string Press = "누르기";
    public const string WaitFor = "나타날때까지";
    public const string Key = "키";
    public const string Sleep = "쉬기";
    public const string Type = "글입력";
    public const string Code = "코드";

    public static IReadOnlyList<string> Actions { get; } = [Press, WaitFor, Key, Sleep, Type, Code];

    /// <summary>나타날때까지 기다리는 기본 시간(ms) - 모델이 안 줬을 때.</summary>
    public const int DefaultWaitMs = 5000;

    /// <summary>
    /// 요청 - 지금 화면(그림)·요청 글·이미 있는 영역·스크립트 끝 몇 줄·함수 목록을 넣고 동작 계획 스키마로 묶는다.
    /// </summary>
    /// <param name="imageWidth">보낸 그림의 가로(px) - 상자 좌표가 이 그림 기준이다.</param>
    /// <param name="boxUnits"><c>pixel</c> 또는 <c>1000</c>(<see cref="LlmSpec.BoxUnits"/>).</param>
    public static LlmRequest Build(string model, string request, byte[] image, int imageWidth, int imageHeight,
                                   IReadOnlyList<string> regionNames, string scriptTail, string boxUnits = "pixel", string? rules = null, string keepAlive = "30m")
    {
        var system = new StringBuilder();

        system.AppendLine("너는 게임 자동화 스크립트(C#)를 한 줄씩 짜는 도우미다. 사람이 게임 화면을 보며 요청하면 동작 하나를 고른다.");
        system.AppendLine();
        system.AppendLine("동작(action):");
        system.AppendLine($"- {Press}: 화면의 버튼·아이콘·메뉴를 누른다. box 와 name 이 꼭 있어야 한다.");
        system.AppendLine($"- {WaitFor}: 그것이 화면에 뜰 때까지 기다린다. box, name, ms(최대 기다림, 밀리초).");
        system.AppendLine($"- {Key}: 키보드 키 하나(key). 예: F, Space, Esc, Enter, 1, Ctrl+1.");
        system.AppendLine($"- {Sleep}: ms 밀리초 쉰다.");
        system.AppendLine($"- {Type}: 글(text)을 치고 Enter.");
        system.AppendLine($"- {Code}: 위로 안 되는 일만. code 에 C# 한두 줄 - 아래 함수만 쓴다.");
        system.AppendLine();
        system.AppendLine(boxUnits == "1000"
            ? "box 는 [x1, y1, x2, y2] - 그림 왼쪽 위 0, 오른쪽 아래 1000 인 비율. 누를 것을 꼭 맞게 감싼다."
            : $"box 는 이 그림(가로 {imageWidth}, 세로 {imageHeight} 픽셀)의 [x1, y1, x2, y2] 픽셀. 누를 것을 꼭 맞게 감싼다.");
        system.AppendLine("name 은 그것의 짧은 한글 이름(공백·점 없이, 예: 설정버튼). say 는 무엇을 하는지 한 줄.");

        if (!string.IsNullOrWhiteSpace(rules))
        {
            system.AppendLine();
            system.AppendLine("게임 규칙:");
            system.AppendLine(rules.Trim());
        }

        system.AppendLine();
        system.AppendLine("이미 있는 영역: " + (regionNames.Count > 0 ? string.Join(", ", regionNames) : "없음"));
        system.AppendLine();
        system.AppendLine("쓸 수 있는 함수: " + ApiReference());

        var user = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(scriptTail))
        {
            user.AppendLine("지금 스크립트 끝:");
            user.AppendLine(scriptTail.TrimEnd());
            user.AppendLine();
        }

        user.Append("요청: ").Append(request.Trim());

        return new LlmRequest(model, [new LlmMessage("system", system.ToString()), new LlmMessage("user", user.ToString(), [image])])
        {
            Format = Schema(),
            MaxTokens = 300,
            KeepAlive = keepAlive
        };
    }

    /// <summary>자유 코드의 컴파일 오류를 글 모델에 고치게 한다 - 답은 <c>{"code": "…"}</c>.</summary>
    public static LlmRequest BuildFix(string model, string code, IReadOnlyList<string> errors, string keepAlive = "30m")
    {
        var system = "너는 게임 자동화 스크립트(C#)를 고치는 도우미다. 컴파일 오류가 난 조각을 아래 함수만 써서 고친다. 고친 조각만 code 에 적는다.\n\n" +
                     "쓸 수 있는 함수: " + ApiReference();
        var user = $"조각:\n{code}\n\n오류:\n{string.Join("\n", errors)}";

        return new LlmRequest(model, [new LlmMessage("system", system), new LlmMessage("user", user)])
        {
            Format = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["code"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray("code")
            },
            MaxTokens = 400,
            KeepAlive = keepAlive
        };
    }

    /// <summary>
    /// 함수 목록 - 한글 이름과 인자만(「그림누르기(resource, [score], [name])」). 설명까지 넣으면 수천 토큰이라 이 PC 에서 30초 넘게 더 걸린다.
    /// </summary>
    public static string ApiReference() => string.Join(", ", ScriptApiCatalog.Entries.Select(e => e.KoreanSignature));

    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. Actions.Select(a => (JsonNode)a)]) },
            ["name"] = new JsonObject { ["type"] = "string" },
            ["box"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" } },
            ["key"] = new JsonObject { ["type"] = "string" },
            ["ms"] = new JsonObject { ["type"] = "integer" },
            ["text"] = new JsonObject { ["type"] = "string" },
            ["code"] = new JsonObject { ["type"] = "string" },
            ["say"] = new JsonObject { ["type"] = "string" }
        },
        // box·name 도 늘 적게 한다 - 필요 없는 동작이면 [0,0,0,0]·빈 글. 안 묶으면 qwen2.5vl:3b 가 「누르기」 를 고르고 상자를 빼먹었다(실측 2026-09-24).
        ["required"] = new JsonArray("action", "name", "box", "say")
    };

    /// <summary>
    /// 자리만 따로 묻는다 - 계획에 상자가 없을 때. Qwen2.5-VL 이 배운 꼴(<c>bbox_2d</c>, 영어 지시)로 묻는 편이 상자를 더 잘 낸다.
    /// </summary>
    public static LlmRequest BuildLocate(string model, string target, byte[] image, string keepAlive = "30m") => new(model,
    [
        new LlmMessage("user", $"Locate \"{target}\" in the image. Output its bbox_2d coordinates [x1, y1, x2, y2] in JSON.", [image])
    ])
    {
        Format = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["bbox_2d"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" } } },
            ["required"] = new JsonArray("bbox_2d")
        },
        MaxTokens = 60,
        KeepAlive = keepAlive
    };

    /// <summary><see cref="BuildLocate"/> 의 답 → 화면 기준 0~1 상자. 못 읽으면 null.</summary>
    public static Rect? ParseLocate(string text, int imageWidth, int imageHeight, string boxUnits = "pixel")
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start) return null;

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;

            // 모델이 목록으로 감싸 오기도 한다 - [{"bbox_2d": …}].
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];

            return root.TryGetProperty("bbox_2d", out var box) ? BoxOf(box, imageWidth, imageHeight, boxUnits) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 답을 계획으로 읽는다. 상자는 보낸 그림 기준 → 0~1. 동작에 꼭 필요한 값이 없으면 <see cref="LlmException"/> - 사람이 요청을 고쳐 다시 보낸다.
    /// </summary>
    public static AssistantPlan Parse(string text, int imageWidth, int imageHeight, string boxUnits = "pixel")
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start) throw new LlmException($"모델 답을 읽지 못했습니다 - 「{Short(text)}」");

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new LlmException($"모델 답을 읽지 못했습니다 - 「{Short(text)}」", ex);
        }

        var action = String(root, "action");

        if (!Actions.Contains(action)) throw new LlmException($"모르는 동작 「{action}」 - {string.Join(", ", Actions)} 중 하나여야 합니다.");

        var plan = new AssistantPlan(
            action,
            String(root, "name"),
            root.TryGetProperty("box", out var boxValue) ? BoxOf(boxValue, imageWidth, imageHeight, boxUnits) : null,
            String(root, "key"),
            root.TryGetProperty("ms", out var ms) && ms.TryGetInt32(out var n) ? Math.Max(0, n) : 0,
            String(root, "text"),
            String(root, "code"),
            String(root, "say"));

        // 누르기인데 상자가 없으면 그대로 준다 - 화면이 BuildLocate 로 자리만 다시 묻는다(MissingBox).
        return plan.Action switch
        {
            Key when plan.Key.Length == 0 => throw new LlmException("「키」 인데 누를 키를 못 받았습니다."),
            Code when plan.Code.Length == 0 => throw new LlmException("「코드」 인데 코드를 못 받았습니다."),
            _ => plan
        };
    }

    /// <summary>
    /// 계획 → 넣을 코드. <paramref name="name"/> 은 화면이 다듬은(겹치지 않는) 영역 이름 - 본보기 파일 이름이 된다.
    /// </summary>
    public static string Render(AssistantPlan plan, string name)
    {
        var comment = plan.Say.Length > 0 ? $"   // {plan.Say.Replace('\n', ' ')}" : string.Empty;

        return plan.Action switch
        {
            Press => $"그림누르기({Literal(name + ".png")});{comment}",
            WaitFor => $"for (var 번 = 0; 번 < {Math.Max(1, (plan.Ms > 0 ? plan.Ms : DefaultWaitMs) / 100)} && !그림있나({Literal(name + ".png")}); 번++) 쉬기(100);{comment}",
            Key => $"키({Literal(plan.Key)});{comment}",
            Sleep => $"쉬기({Math.Max(1, plan.Ms)});{comment}",
            Type => $"줄입력({Literal(plan.Text)});{comment}",
            _ => plan.Code.TrimEnd() + (plan.Say.Length > 0 ? Environment.NewLine + "// ↑ " + plan.Say.Replace('\n', ' ') : string.Empty)
        };
    }

    /// <summary>
    /// 영역·본보기 이름 - 공백·점·파일 이름에 못 쓰는 글자를 빼고, 비면 「버튼」, 이미 있으면 뒤에 번호.
    /// </summary>
    public static string CleanName(string name, Func<string, bool> taken)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars().Concat([' ', '.', '\t']).ToHashSet();
        var clean = new string([.. (name ?? string.Empty).Where(c => !bad.Contains(c))]);

        if (clean.Length == 0) clean = "버튼";
        if (clean.Length > 20) clean = clean[..20];

        if (!taken(clean)) return clean;

        for (var n = 2; ; n++)
            if (!taken($"{clean}{n}")) return $"{clean}{n}";
    }

    /// <summary>C# 문자열 글자 - 따옴표·역슬래시·줄바꿈을 막는다.</summary>
    public static string Literal(string text)
        => "\"" + (text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    /// <summary>
    /// 자리가 필요한 계획인데 상자가 없으면 <see cref="BuildLocate"/> 로 자리만 다시 묻는다. 그래도 없으면 <see cref="LlmException"/>.
    /// </summary>
    /// <param name="target">찾을 것 - 사람이 적은 요청 글을 그대로 준다(모델이 줄인 say 보다 정보가 많다).</param>
    public static async System.Threading.Tasks.Task<AssistantPlan> EnsureBoxAsync(ILlmAdapter adapter, string model, AssistantPlan plan, string target,
        byte[] image, int imageWidth, int imageHeight, string boxUnits, string keepAlive, System.Threading.CancellationToken token)
    {
        if (!plan.NeedsRegion || plan.Box is not null) return plan;

        var response = await adapter.ChatAsync(BuildLocate(model, target, image, keepAlive), token).ConfigureAwait(true);

        return ParseLocate(response.Text, imageWidth, imageHeight, boxUnits) is { } box
            ? plan with { Box = box }
            : throw new LlmException(MissingBox(plan));
    }

    /// <summary>자리를 다시 물어도 못 받았을 때의 말.</summary>
    public static string MissingBox(AssistantPlan plan)
        => $"「{plan.Action}」 인데 화면 자리를 못 찾았습니다 - 무엇을 누를지 더 또렷하게 적어 보세요(예: 오른쪽 위 톱니 모양 버튼). 영역을 직접 만들고 그림누르기를 적어도 됩니다.";

    private static Rect? BoxOf(JsonElement box, int width, int height, string boxUnits)
    {
        if (box.ValueKind != JsonValueKind.Array || box.GetArrayLength() < 4) return null;

        var v = box.EnumerateArray().Take(4).Select(e => e.TryGetDouble(out var d) ? d : double.NaN).ToArray();

        if (v.Any(double.IsNaN)) return null;

        var (sx, sy) = boxUnits == "1000" ? (1000.0, 1000.0) : (Math.Max(1, width), Math.Max(1, height));
        var x1 = Math.Clamp(Math.Min(v[0], v[2]) / sx, 0, 1);
        var y1 = Math.Clamp(Math.Min(v[1], v[3]) / sy, 0, 1);
        var x2 = Math.Clamp(Math.Max(v[0], v[2]) / sx, 0, 1);
        var y2 = Math.Clamp(Math.Max(v[1], v[3]) / sy, 0, 1);

        // 점짜리·화면 통째는 상자가 아니다 - 모델이 못 찾은 것이다.
        if (x2 - x1 < 0.004 || y2 - y1 < 0.004 || (x2 - x1 > 0.95 && y2 - y1 > 0.95)) return null;

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static string String(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;

    private static string Short(string text) => text.Length <= 120 ? text : text[..120] + "…";
}
