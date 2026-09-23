using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Llm;

/// <summary>
/// 로컬 Ollama(<c>/api/chat</c>)로 모델을 부른다. 그림은 base64 로 붙인다.
/// </summary>
/// <remarks>
/// <b>별도 프로세스라서 고른다</b> - 앱 안에 싣는 LLamaSharp 는 네이티브·GPU(DirectML 검출·CUDA 학습)와 부딪칠 수 있고, 모델을 바꾸거나 죽어도 앱이 같이 넘어진다.
/// Ollama 는 HTTP 라 서로 모른다. 어느 GPU 를 쓸지도 Ollama 쪽(<c>CUDA_VISIBLE_DEVICES</c>)에서 정한다.
///
/// <b>실측</b>(2026-09-24, GTX 1060 3GB 두 장 · i7-7820X · 32GB): qwen3:8b 는 3GB 에 다 안 들어가 CPU 75% 로 돌아 판단 한 번 3~4초,
/// 모델 올리기 45초~1.5분. qwen2.5vl:3b 는 그림 한 장 첫 질문 18초(1,297토큰) + 올리기 72초.
/// 그래서 기다림 상한을 넉넉히(<see cref="Timeout"/>) 두고, 멈춤(토큰)은 곧바로 먹게 한다.
/// </remarks>
public sealed class OllamaLlmAdapter : ILlmAdapter
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>앱 전체가 한 벌 - 연결을 다시 쓴다. 기다림 상한은 부를 때마다 토큰으로 건다.</summary>
    private static readonly HttpClient Shared = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;

    /// <param name="baseUrl">Ollama 주소(<c>http://localhost:11434</c>).</param>
    /// <param name="handler">검사가 가짜 서버를 꽂는다. 없으면 앱 공용 연결.</param>
    public OllamaLlmAdapter(Uri baseUrl, HttpMessageHandler? handler = null)
    {
        _baseUrl = baseUrl;
        _http = handler is null ? Shared : new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public string Name => "Ollama";

    /// <summary>한 번 부르기의 상한 - 모델 올리기(1~2분)에 그림 읽기까지 들어간다.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    public async Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(Timeout);

        using var content = new StringContent(BuildBody(request).ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;

        try
        {
            response = await _http.PostAsync(new Uri(_baseUrl, "api/chat"), content, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new LlmException($"{request.Model} 이(가) {Timeout.TotalMinutes:0}분 안에 답하지 않았습니다 - 모델이 너무 크거나 Ollama 가 멈췄습니다.");
        }
        catch (HttpRequestException ex)
        {
            Logger.Warn(ex, $"Ollama 에 못 닿았다: {_baseUrl}");
            throw new LlmException($"Ollama 에 닿지 못했습니다({_baseUrl}) - Ollama 가 켜져 있는지 보세요. 작업 표시줄 알림 영역에 라마 아이콘이 있어야 합니다.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn($"Ollama 가 거절했다 ({(int)response.StatusCode}): {text}");

                var reason = ErrorOf(text);

                throw response.StatusCode == HttpStatusCode.NotFound || reason.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? new LlmException($"모델 「{request.Model}」 이(가) 없습니다 - 명령 창에서 ollama pull {request.Model} 로 받으세요.")
                    : new LlmException($"Ollama 가 요청을 거절했습니다({(int)response.StatusCode}) - {reason}");
            }

            return ParseResponse(text);
        }
    }

    /// <summary>요청 본문. 검사가 모양을 본다.</summary>
    public static JsonObject BuildBody(LlmRequest request)
    {
        var messages = new JsonArray();

        foreach (var message in request.Messages)
        {
            var item = new JsonObject { ["role"] = message.Role, ["content"] = message.Content };

            if (message.Images is { Count: > 0 } images)
                item["images"] = new JsonArray([.. images.Select(bytes => (JsonNode)Convert.ToBase64String(bytes))]);

            messages.Add(item);
        }

        var options = new JsonObject { ["temperature"] = request.Temperature, ["num_predict"] = request.MaxTokens };

        if (request.ContextSize > 0) options["num_ctx"] = request.ContextSize;

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["stream"] = false,
            ["think"] = request.Think,
            ["keep_alive"] = request.KeepAlive,
            ["messages"] = messages,
            ["options"] = options
        };

        if (request.Format is { } format) body["format"] = format.DeepClone();

        return body;
    }

    /// <summary>답 본문 → 글·시간. 시간은 나노초로 온다.</summary>
    public static LlmResponse ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var text = root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
            ? content.GetString() ?? string.Empty
            : string.Empty;

        return new LlmResponse(
            text.Trim(),
            Nanoseconds(root, "total_duration"),
            Nanoseconds(root, "load_duration"),
            Int(root, "prompt_eval_count"),
            Int(root, "eval_count"));
    }

    private static TimeSpan Nanoseconds(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt64(out var ns) ? TimeSpan.FromTicks(ns / 100) : TimeSpan.Zero;

    private static int Int(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) ? n : 0;

    private static string ErrorOf(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            if (document.RootElement.TryGetProperty("error", out var error)) return error.GetString() ?? text;
        }
        catch (JsonException)
        {
        }

        return text;
    }
}
