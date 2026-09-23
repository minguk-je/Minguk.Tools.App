using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Tools.Capture;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Scripting.Live;
using Minguk.Tools.Llm;

namespace Minguk.Tools.Tests;

/// <summary>
/// 로컬 LLM 판단 - 요청 모양, 답 읽기, 오류 문장, 실시간 API(판단·물어보기·판단고치기)를 가짜 Ollama 로 본다. 진짜 모델은 <c>--llm</c>.
/// </summary>
/// <remarks>사용자(2026-09-24) "진짜로 로컬LLM 붙여서 해보고 싶은데" · "로컬 VLM 12GB 이상 달 예정이니까 구현은 해줘".</remarks>
internal static partial class Program
{
    private static void TestLlm()
    {
        var actions = new[] { "물약", "후퇴", "공격" };

        // ── 요청 모양 ──
        var request = LlmJudge.Build("qwen3:8b", "1. 체력 30% 아래면 물약", [new LlmExample("체력 10%", "물약"), new LlmExample("가방 가득", "귀환")],
                                     "체력 22%. 몬스터 2(가까움).", actions, image: [1, 2, 3]);
        var body = OllamaLlmAdapter.BuildBody(request);
        var messages = body["messages"]!.AsArray();
        var enumValues = body["format"]?["properties"]?["action"]?["enum"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray() ?? [];

        Check("LLM: 요청은 생각 끄고 · 답을 행동 목록 enum 으로 묶는다",
              body["think"]!.GetValue<bool>() == false && body["stream"]!.GetValue<bool>() == false && enumValues.SequenceEqual(actions),
              $"think {body["think"]} · enum [{string.Join(", ", enumValues)}]");

        Check("LLM: 규칙이 system 에 들고, 이번 목록에 있는 예시만 대화로 넣고(귀환은 뺌), 그림은 base64 로 마지막 질문에",
              messages.Count == 4 && messages[0]!["content"]!.GetValue<string>().Contains("체력 30% 아래면 물약")
              && messages[1]!["content"]!.GetValue<string>() == "체력 10%" && messages[3]!["images"]![0]!.GetValue<string>() == System.Convert.ToBase64String([1, 2, 3]),
              $"메시지 {messages.Count}개");

        // ── 답 읽기 ──
        Check("LLM: 답 읽기 - JSON · 코드 울타리 · 글에 이름 하나",
              LlmJudge.Parse("{\"rule\": 1, \"action\": \"물약\"}", actions) == ("물약", 1)
              && LlmJudge.Parse("```json\n{\"rule\":0,\"action\":\"후퇴\"}\n```", actions) == ("후퇴", 0)
              && LlmJudge.Parse("지금은 공격이 맞다", actions) == ("공격", 0), "");

        var refused = false;
        try { LlmJudge.Parse("{\"action\": \"춤추기\"}", actions); } catch (LlmException) { refused = true; }

        Check("LLM: 목록에 없는 행동은 받지 않는다(멈추고 이유를 말한다)", refused, "");

        Check("LLM: 행동 목록은 쉼표·줄바꿈으로 가르고 겹친 것은 뺀다",
              LlmJudge.SplitActions("공격, 물약,\n후퇴 , 공격").SequenceEqual(["공격", "물약", "후퇴"]), string.Join("|", LlmJudge.SplitActions("공격, 물약,\n후퇴 , 공격")));

        // ── 가짜 Ollama 에 보내기 ──
        var server = new FakeOllama();
        var adapter = new OllamaLlmAdapter(new Uri("http://localhost:11434/"), server);

        server.Reply = "{\"message\":{\"content\":\"{\\\"rule\\\":1,\\\"action\\\":\\\"물약\\\"}\"},\"total_duration\":3200000000,\"load_duration\":5000000,\"prompt_eval_count\":250,\"eval_count\":11}";
        var response = adapter.ChatAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        Check("LLM: Ollama /api/chat 로 보내고 걸린 시간·토큰을 읽는다",
              server.Paths.LastOrDefault() == "/api/chat" && response.Text.Contains("물약") && Math.Abs(response.Total.TotalMilliseconds - 3200) < 1 && response.PromptTokens == 250,
              $"{server.Paths.LastOrDefault()} · {response.Total.TotalMilliseconds:0}ms · {response.PromptTokens}토큰");

        server.Status = HttpStatusCode.NotFound;
        server.Reply = "{\"error\":\"model 'qwen9:99b' not found\"}";
        var missing = string.Empty;
        try { adapter.ChatAsync(request with { Model = "qwen9:99b" }, CancellationToken.None).GetAwaiter().GetResult(); } catch (LlmException ex) { missing = ex.Message; }

        Check("LLM: 없는 모델이면 받는 명령을 알려 준다", missing.Contains("ollama pull qwen9:99b"), missing);

        var offline = string.Empty;
        try { new OllamaLlmAdapter(new Uri("http://127.0.0.1:1/")).ChatAsync(request, CancellationToken.None).GetAwaiter().GetResult(); } catch (LlmException ex) { offline = ex.Message; }

        Check("LLM: Ollama 가 꺼져 있으면 켜라고 말한다", offline.Contains("Ollama 에 닿지 못했습니다"), offline);

        // ── 실시간 API ──
        var monitor = CaptureTarget.EnumerateMonitors().FirstOrDefault();

        if (monitor is null)
        {
            Check("LLM: 실시간 API", false, "모니터를 못 찾음");
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "MingukLlm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(Path.Combine(folder, LlmSpec.RulesFileName), "1. 가방이 가득 차면 귀환", new UTF8Encoding(false));

            var fake = new FakeLlm();
            var printed = new List<string>();
            var watched = new Dictionary<string, string>();
            var host = new LiveScriptHost
            {
                Service = new InputService(new RecordingAdapter()),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor),
                Print = printed.Add,
                Watch = (name, value) => watched[name] = value,
                HoldTimeMs = 1,
                ResourceRoot = folder,
                Llm = _ => fake
            };

            var api = new LiveScriptApi(host, CancellationToken.None);

            fake.Answer = "{\"rule\":1,\"action\":\"귀환\"}";
            var picked = api.판단("가방 80/80. 몬스터 없음.", "공격, 줍기, 귀환");

            Check("LLM: 판단() 이 규칙서(llm-rules.md)를 넣어 부르고 목록의 행동을 준다 · 보기 칸에 남긴다",
                  picked == "귀환" && fake.Last?.Messages[0].Content.Contains("가방이 가득 차면 귀환") == true && watched.GetValueOrDefault("판단", "").StartsWith("귀환"),
                  $"{picked} · 보기 「{watched.GetValueOrDefault("판단")}」");

            Check("LLM: 처음 부르면 llm.json 을 기본값(qwen3:8b · qwen2.5vl:3b)으로 만들고 알린다",
                  File.Exists(Path.Combine(folder, LlmSpec.FileName)) && fake.Last?.Model == "qwen3:8b" && printed.Any(p => p.Contains(LlmSpec.FileName)),
                  string.Join(" / ", printed));

            api.판단고치기("가방 80/80. 몬스터 2.", "귀환");
            fake.Answer = "{\"rule\":0,\"action\":\"공격\"}";
            api.판단("체력 90%. 몬스터 1.", "공격, 귀환");

            Check("LLM: 판단고치기가 적은 예시가 다음 판단의 대화로 들어간다",
                  fake.Last?.Messages.Any(m => m.Role == "user" && m.Content == "가방 80/80. 몬스터 2.") == true
                  && fake.Last.Messages.Any(m => m.Role == "assistant" && m.Content.Contains("귀환")),
                  $"메시지 {fake.Last?.Messages.Count}개");

            fake.Answer = "춤추기";
            var guarded = false;
            try { api.판단("체력 90%.", "공격, 귀환"); } catch (ScriptGuardException) { guarded = true; }

            Check("LLM: 모델이 목록 밖을 답하면 스크립트를 세우고 이유를 남긴다", guarded && api.GuardMessage?.Contains("행동을 못 골랐습니다") == true, api.GuardMessage ?? "");

            fake.Answer = "퀘스트를 받으러 NPC 에게 간다.";
            Check("LLM: 물어보기는 자유 글을 그대로 준다", new LiveScriptApi(host, CancellationToken.None).물어보기("한 줄로") == "퀘스트를 받으러 NPC 에게 간다.", "");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>받은 요청을 적어 두고 정해 둔 답을 주는 가짜 Ollama 서버.</summary>
    private sealed class FakeOllama : HttpMessageHandler
    {
        public string Reply { get; set; } = "{}";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);

            if (request.Content is not null) _ = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>모델 대신 정해 둔 답을 주는 어댑터 - 실시간 API 검사용.</summary>
    private sealed class FakeLlm : ILlmAdapter
    {
        public string Name => "가짜";

        public string Answer { get; set; } = string.Empty;

        public LlmRequest? Last { get; private set; }

        public Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken token)
        {
            Last = request;
            return Task.FromResult(new LlmResponse(Answer, TimeSpan.FromMilliseconds(5), TimeSpan.Zero, 10, 5));
        }
    }
}
