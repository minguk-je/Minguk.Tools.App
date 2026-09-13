using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 편집기가 칠하는 낱말 종류. 강조 정의(<c>Resource/SequenceScript.*.xshd</c>)의 색 이름과 같다.
/// </summary>
/// <remarks>
/// 이름이 곧 xshd 의 <c>Color name</c> 이다 - 편집기가 이름으로 색을 꺼낸다. 색표를 두 곳에 두면 한쪽만 고쳐진다.
/// </remarks>
public enum ScriptTokenKind
{
    Keyword,
    ControlKeyword,
    Method,

    /// <summary>필드·속성·이벤트·상수·열거값. VS(ReSharper)에서 한 색이다.</summary>
    Member,

    /// <summary>지역 변수·매개변수.</summary>
    Local,

    /// <summary>클래스·구조체·인터페이스·열거형·대리자·형식 매개변수.</summary>
    Type,

    /// <summary>네임스페이스 등 본문색으로 칠할 것.</summary>
    Plain,

    String,
    Number,
    Comment
}

/// <summary>글에서 한 토막. 자리는 글 전체에서의 문자 오프셋이다.</summary>
public readonly record struct ScriptToken(int Start, int Length, ScriptTokenKind Kind);

/// <summary>
/// 글을 컴파일러로 읽어 낱말마다 종류를 준다 - 정규식으로 어림하지 않는다.
/// </summary>
/// <remarks>
/// <b>능력별 인터페이스다.</b> C# 만 된다(Roslyn). 편집기는 완성 소스에 <c>is IScriptClassifier</c> 로 물어보고,
/// 아니면 강조 정의(xshd)의 정규식 색만 쓴다. 파이썬·자바스크립트에 늘 빈 목록을 돌려주는 빈 구현을 두지 않는다.
///
/// <b>왜 필요한가</b> - 정규식은 이름의 종류를 모른다. 점 없이 쓴 필드, 스크립트 안에서 만든 함수, 형식 이름을 가리지 못해
/// VS 에서 보던 색과 달랐다. 컴파일러는 VS 가 쓰는 것과 같은 분류를 준다.
/// </remarks>
public interface IScriptClassifier
{
    /// <summary>글 전체를 분류한다. 오래 걸릴 수 있어 비동기다. 취소되면 빈 목록.</summary>
    /// <param name="filePath">이 글이 든 파일. 프로젝트면 다른 파일에 만든 함수도 제 종류로 칠한다. 모르면 null.</param>
    Task<IReadOnlyList<ScriptToken>> ClassifyAsync(string text, CancellationToken token = default, string? filePath = null);
}
