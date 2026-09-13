using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Input.Scripting;

/// <summary>선언 하나의 참조 수 - 편집기가 그 줄 위에 "참조 N개" 로 그린다(VS 의 CodeLens).</summary>
/// <param name="Offset">선언 이름이 시작하는 자리(이 글 안의 오프셋). 참조 창을 열 때 다시 넘긴다.</param>
/// <param name="Line">선언이 있는 줄(1부터).</param>
/// <param name="Name">선언 이름.</param>
/// <param name="Count">참조 수(선언 자신은 뺀다).</param>
public readonly record struct ScriptLens(int Offset, int Line, string Name, int Count);

/// <summary>참조 한 곳 - 참조 창의 한 줄.</summary>
/// <param name="FilePath">파일 전체 경로. 한 파일짜리 글이면 빈 글.</param>
/// <param name="Line">줄(1부터).</param>
/// <param name="Column">낱말이 시작하는 열(0부터) - 칠할 자리.</param>
/// <param name="Length">낱말 길이.</param>
/// <param name="LineText">그 줄의 글(앞뒤 공백 없이 보이지만 Column 은 원래 줄 기준).</param>
public readonly record struct ScriptReference(string FilePath, int Line, int Column, int Length, string LineText);

/// <summary>
/// 선언의 참조를 센다·찾는다(VS 의 CodeLens "참조 N개" 와 그 창). 능력별 인터페이스 - C#(Roslyn)만.
/// </summary>
/// <remarks>
/// 편집기는 완성 소스에 <c>is IScriptReferenceFinder</c> 로 묻는다. 파이썬·JS 는 참조를 셀 컴파일러가 없어 표시를 안 한다.
/// 프로젝트면 같은 프로젝트의 <b>모든</b> 소스(시작 파일 포함)에서 센다 - 도우미 파일의 함수를 시작 파일이 몇 번 부르는지가 궁금한 것이다.
/// </remarks>
public interface IScriptReferenceFinder
{
    /// <summary>이 글의 선언마다 참조 수. 오래 걸릴 수 있어 비동기다. 취소되면 빈 목록.</summary>
    Task<IReadOnlyList<ScriptLens>> GetLensesAsync(string text, CancellationToken token = default, string? filePath = null);

    /// <summary>이 글의 <paramref name="offset"/> 자리 선언을 부르는 곳 전부. 파일·줄 순.</summary>
    Task<IReadOnlyList<ScriptReference>> FindReferencesAsync(string text, int offset, CancellationToken token = default, string? filePath = null);
}
