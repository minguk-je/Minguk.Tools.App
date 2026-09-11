using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Input.Scripting;

/// <summary>완성 목록의 한 항목.</summary>
/// <param name="Text">넣을 글.</param>
/// <param name="Display">목록에 보이는 글. 보통 <paramref name="Text"/> 와 같다.</param>
/// <param name="Kind">무엇인지 - "메서드", "키워드", "클래스" 같은 한 낱말.</param>
public sealed record CompletionSuggestion(string Text, string Display, string Kind);

/// <summary>
/// 자리에 맞는 완성 목록을 준다. 언어를 아는 쪽(Roslyn)이 구현한다.
/// </summary>
/// <remarks>
/// 편집기는 이것이 있으면 여기서 받고, 없거나 빈 목록이면 API 표(<see cref="ScriptApiCatalog"/>)로 돌아간다.
/// C# 만 구현이 있다 - 파이썬·자바스크립트는 정적 분석기를 얹지 않기로 했다(무겁고 얻는 것이 적다).
/// </remarks>
public interface IScriptCompletionSource
{
    /// <summary>글 전체와 캐럿 자리로 목록을 만든다. 오래 걸릴 수 있어 비동기다. 취소되면 빈 목록.</summary>
    Task<IReadOnlyList<CompletionSuggestion>> GetAsync(string text, int position, CancellationToken token = default);
}
