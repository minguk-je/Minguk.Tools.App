using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Llm;

/// <summary>대화 한 줄 - 역할(system·user·assistant)과 글, 그림(PNG·JPEG 바이트)을 붙일 수 있다.</summary>
public sealed record LlmMessage(string Role, string Content, IReadOnlyList<byte[]>? Images = null);

/// <summary>모델 한 번 부르기.</summary>
/// <param name="Model">모델 이름(<c>qwen3:8b</c>).</param>
/// <param name="Messages">대화.</param>
public sealed record LlmRequest(string Model, IReadOnlyList<LlmMessage> Messages)
{
    /// <summary>답을 묶을 JSON 스키마. null 이면 자유 글.</summary>
    public JsonObject? Format { get; init; }

    /// <summary>답 토큰 상한.</summary>
    public int MaxTokens { get; init; } = 200;

    /// <summary>0 이면 같은 물음에 같은 답.</summary>
    public double Temperature { get; init; }

    /// <summary>
    /// 생각(추론) 모드. 기본 끔 - qwen3 는 켜 두면 답 앞에 긴 추론을 써 몇 배 느려진다(실측 2026-09-24).
    /// </summary>
    public bool Think { get; init; }

    /// <summary>다 쓴 뒤 모델을 올려 둘 시간(<c>30m</c>). 내렸다 다시 올리면 1~2분 걸린다.</summary>
    public string KeepAlive { get; init; } = "30m";

    /// <summary>문맥 크기(토큰). 0 이면 서버 기본.</summary>
    public int ContextSize { get; init; }
}

/// <summary>모델의 답과 걸린 시간.</summary>
/// <param name="Text">답 글(스키마를 줬으면 JSON 글).</param>
/// <param name="Load">모델을 올리는 데 든 시간 - 이미 올라 있으면 0 에 가깝다.</param>
public sealed record LlmResponse(string Text, TimeSpan Total, TimeSpan Load, int PromptTokens, int OutputTokens);

/// <summary>모델을 부르지 못했다 - 사람이 읽을 한국어 이유를 든다. 원문은 로그에.</summary>
public sealed class LlmException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// 언어 모델(LLM·VLM)을 부르는 길. 구현은 <see cref="OllamaLlmAdapter"/>(로컬 Ollama), 고르는 곳은 <see cref="LlmAdapterFactory"/>.
/// </summary>
/// <remarks>
/// 사용자(2026-09-24) "진짜로 로컬LLM 붙여서 해보고 싶은데" · "로컬 VLM 12GB 이상 달 예정이니까 구현은 해줘". 글 모델과 그림 모델을 같은 길로 부른다 -
/// 그림은 <see cref="LlmMessage.Images"/> 에 붙인다. 나중에 Claude API 같은 바깥 모델도 이 인터페이스의 구현 하나로 더한다.
/// </remarks>
public interface ILlmAdapter
{
    /// <summary>어느 길인가(<c>Ollama</c>).</summary>
    string Name { get; }

    Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken token);
}
