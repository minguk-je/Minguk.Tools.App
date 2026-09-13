using Minguk.Tools.Input.Scripting;

namespace Minguk.Tools.Markup;

/// <summary>
/// 참조 창의 한 줄. VS 처럼 <c>69 : Logger.Trace(string.Empty);</c> 로 보이고 찾은 낱말은 칠한다 - 그래서 앞·낱말·뒤로 나눠 든다.
/// </summary>
public sealed class CodeLensReferenceRow
{
    public CodeLensReferenceRow(ScriptReference reference, string group)
    {
        FilePath = reference.FilePath;
        Line = reference.Line;
        Group = group;

        // 앞 공백은 걷는다 - 들여쓰기만큼 밀려 코드가 안 보인다. 칠할 자리는 걷은 만큼 당긴다.
        var text = reference.LineText;
        var trimmed = text.TrimStart();
        var cut = text.Length - trimmed.Length;
        var start = System.Math.Clamp(reference.Column - cut, 0, trimmed.Length);
        var length = System.Math.Clamp(reference.Length, 0, trimmed.Length - start);

        Before = trimmed[..start];
        Match = trimmed.Substring(start, length);
        After = trimmed[(start + length)..];
    }

    /// <summary>묶음 머리 - "경로 (개수)". 그리드가 이 칸으로 묶는다.</summary>
    public string Group { get; }

    public string FilePath { get; }

    public int Line { get; }

    public string LineLabel => $"{Line} : ";

    public string Before { get; }

    public string Match { get; }

    public string After { get; }
}
