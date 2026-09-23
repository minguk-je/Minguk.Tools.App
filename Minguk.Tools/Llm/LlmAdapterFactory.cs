using System;

namespace Minguk.Tools.Llm;

/// <summary>
/// <see cref="LlmSpec.Provider"/> 로 모델 길을 고른다. 새 길(Claude API 등)은 여기만 고친다.
/// </summary>
public static class LlmAdapterFactory
{
    public static ILlmAdapter Create(LlmSpec spec)
        => spec.Provider.Trim().ToLowerInvariant() switch
        {
            "ollama" => new OllamaLlmAdapter(spec.BaseUri) { Timeout = TimeSpan.FromSeconds(spec.TimeoutSeconds) },
            _ => throw new LlmException($"llm.json 의 provider 「{spec.Provider}」 를 모릅니다 - 지금은 ollama 만 됩니다.")
        };
}
