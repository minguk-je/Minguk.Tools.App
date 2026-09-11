using Minguk.Tools.Input.Scripting.Live;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 언어에 맞는 완성을 고른다. C# 만 Roslyn 이 있고, 나머지는 null - 편집기가 API 표로 돌아간다.
/// </summary>
public static class ScriptCompletionSourceFactory
{
    /// <param name="isLive">실시간 모드면 전역이 LiveScriptApi, 아니면 SequenceScriptApi. 그 차이가 곧 완성 목록의 차이다.</param>
    public static IScriptCompletionSource? Create(ScriptLanguage language, bool isLive) => language switch
    {
        ScriptLanguage.CSharp => new RoslynCompletionSource(isLive ? typeof(LiveScriptApi) : typeof(SequenceScriptApi)),
        _ => null
    };
}
