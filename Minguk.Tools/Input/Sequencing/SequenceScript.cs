using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Minguk.Tools.Input.Sequencing;

/// <summary>
/// 단계 목록을 사람이 쓰고 읽는 <b>글</b>로 바꾸고 되돌린다.
///
/// 왜 표가 아니라 글인가
///   표는 한 줄을 고쳐 넣는 데 마우스가 여러 번 필요하고, 통째로 복사해 남에게 주거나
///   diff 를 뜨는 것이 안 된다. 같은 단계 열 개를 넣는 것도 표에서는 열 번 눌러야 한다.
///   글이면 편집기가 이미 잘하는 일(복사·되돌리기·찾아 바꾸기·여러 줄 선택)이 전부 따라온다.
///
/// 형식
/// <code>
/// # 주석
/// 글자 "안녕하세요"
/// Enter
/// 한/영
/// 클릭 좌
/// 이동 1280 720
/// 휠 -3
/// 쉬기 500
/// </code>
///
/// 명령은 한글로 쓰되 영문 이름(<c>Type</c>·<c>MoveTo</c> …)도 받는다. 저장된 것을 사람이
/// 손으로 고칠 수 있어야 하는데, 어느 쪽으로 쓸지는 쓰는 사람 사정이다.
/// </summary>
public static class SequenceScript
{
    /// <param name="Line">1 부터 센 줄 번호. 사람에게 보여 줄 것이라 0 부터 세지 않는다.</param>
    public readonly record struct Error(int Line, string Message)
    {
        public override string ToString() => $"{Line}번째 줄: {Message}";
    }

    private const char CommentMark = '#';

    // ── 계획 → 글 ────────────────────────────────────────────────────────

    public static string ToText(SequencePlan plan)
        => string.Join(Environment.NewLine, plan.Steps.Select(ToLine));

    /// <summary>
    /// 계획을 C# 스크립트로 적는다.
    /// </summary>
    /// <remarks>
    /// 예전에 이 형식으로 저장해 둔 것을 한 번 옮기려고 둔다.
    /// 새로 쓰는 것은 전부 C# 이다(<see cref="Scripting.SequenceScriptEngine"/>).
    /// </remarks>
    public static string ToCSharp(SequencePlan plan)
        => string.Join(Environment.NewLine, plan.Steps.Select(ToCSharpLine));

    private static string ToCSharpLine(SequenceStepDefinition step) => step.Kind switch
    {
        SequenceStepKind.Type => $"Type({CSharpString(step.Text ?? string.Empty)});",
        SequenceStepKind.Enter => "Enter();",
        SequenceStepKind.ToggleHangul => "ToggleHangul();",
        SequenceStepKind.Click => step.Button switch
        {
            MouseButton.Right => "Click(MouseButton.Right);",
            MouseButton.Middle => "Click(MouseButton.Middle);",
            _ => "Click();"
        },
        SequenceStepKind.MoveTo => $"MoveTo({step.X}, {step.Y});",
        SequenceStepKind.Scroll => $"Scroll({step.Notches});",
        SequenceStepKind.Wait => $"Wait({step.DelayMs});",
        _ => $"// 모르는 단계: {step.Kind}"
    };

    private static string CSharpString(string text)
    {
        var sb = new StringBuilder(text.Length + 2).Append('"');

        foreach (var c in text)
        {
            if (c is '"' or '\\') sb.Append('\\');

            sb.Append(c);
        }

        return sb.Append('"').ToString();
    }

    private static string ToLine(SequenceStepDefinition step) => step.Kind switch
    {
        SequenceStepKind.Type => $"글자 {Quote(step.Text ?? string.Empty)}",
        SequenceStepKind.Enter => "Enter",
        SequenceStepKind.ToggleHangul => "한/영",
        SequenceStepKind.Click => "클릭 " + step.Button switch
        {
            MouseButton.Right => "우",
            MouseButton.Middle => "휠",
            _ => "좌"
        },
        SequenceStepKind.MoveTo => $"이동 {step.X} {step.Y}",
        SequenceStepKind.Scroll => $"휠 {step.Notches}",
        SequenceStepKind.Wait => $"쉬기 {step.DelayMs}",
        _ => $"# 모르는 단계: {step.Kind}"
    };

    /// <summary>
    /// 글자를 따옴표에 담는다. 안에 든 따옴표와 역슬래시는 앞에 역슬래시를 붙인다.
    /// </summary>
    private static string Quote(string text)
    {
        var sb = new StringBuilder(text.Length + 2).Append('"');

        foreach (var c in text)
        {
            if (c is '"' or '\\') sb.Append('\\');

            sb.Append(c);
        }

        return sb.Append('"').ToString();
    }

    // ── 글 → 계획 ────────────────────────────────────────────────────────

    /// <summary>
    /// 한 줄씩 읽어 계획을 만든다.
    /// </summary>
    /// <remarks>
    /// 틀린 줄을 만나도 멈추지 않는다. 첫 오류에서 그만두면 사용자는 고치고 다시 눌러야
    /// 다음 오류를 보게 되는데, 열 줄이 틀렸으면 열 번을 돌아야 한다.
    /// 틀린 줄은 계획에서 빼고 <paramref name="errors"/> 에 모아 한 번에 보여 준다.
    /// </remarks>
    /// <returns>틀린 줄이 하나도 없으면 true.</returns>
    public static bool TryParse(string? text, out SequencePlan plan, out IReadOnlyList<Error> errors)
    {
        var steps = new List<SequenceStepDefinition>();
        var found = new List<Error>();

        var lines = (text ?? string.Empty).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();

            if (line.Length == 0 || line[0] == CommentMark) continue;

            if (TryParseLine(line, out var step, out var message)) steps.Add(step!);
            else found.Add(new Error(i + 1, message!));
        }

        plan = new SequencePlan { Steps = steps };
        errors = found;

        return found.Count == 0;
    }

    private static bool TryParseLine(string line, out SequenceStepDefinition? step, out string? error)
    {
        step = null;
        error = null;

        if (!TrySplit(line, out var command, out var args, out error)) return false;

        switch (Normalize(command))
        {
            case "글자":
            case "type":
                if (args.Count != 1)
                {
                    error = "글자 는 따옴표에 담은 글 하나가 필요하다 - 글자 \"안녕하세요\"";
                    return false;
                }

                step = new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = args[0] };
                return true;

            case "enter":
                return NoArgs(args, SequenceStepKind.Enter, ref step, ref error);

            case "한/영":
            case "한영":
            case "togglehangul":
                return NoArgs(args, SequenceStepKind.ToggleHangul, ref step, ref error);

            case "클릭":
            case "click":
                return ParseClick(args, ref step, ref error);

            case "이동":
            case "moveto":
                if (args.Count != 2 || !TryInt(args[0], out var x) || !TryInt(args[1], out var y))
                {
                    error = "이동 은 X 와 Y 두 숫자가 필요하다 - 이동 1280 720";
                    return false;
                }

                step = new SequenceStepDefinition { Kind = SequenceStepKind.MoveTo, X = x, Y = y };
                return true;

            case "휠":
            case "scroll":
                if (args.Count != 1 || !TryInt(args[0], out var notches))
                {
                    error = "휠 은 칸 수 하나가 필요하다. 양수가 위, 음수가 아래 - 휠 -3";
                    return false;
                }

                step = new SequenceStepDefinition { Kind = SequenceStepKind.Scroll, Notches = notches };
                return true;

            case "쉬기":
            case "wait":
                if (args.Count != 1 || !TryInt(args[0], out var ms) || ms < 0)
                {
                    error = "쉬기 는 0 이상의 밀리초 하나가 필요하다 - 쉬기 500";
                    return false;
                }

                step = new SequenceStepDefinition { Kind = SequenceStepKind.Wait, DelayMs = ms };
                return true;

            default:
                error = $"모르는 명령이다: {command}  (쓸 수 있는 것: {string.Join(", ", Commands)})";
                return false;
        }
    }

    /// <summary>화면·오류 문구에 쓸 명령 목록. 한글 쪽만 보여 준다.</summary>
    public static readonly string[] Commands = ["글자", "Enter", "한/영", "클릭", "이동", "휠", "쉬기"];

    private static bool NoArgs(
        List<string> args, SequenceStepKind kind, ref SequenceStepDefinition? step, ref string? error)
    {
        if (args.Count != 0)
        {
            error = $"{SequenceStepKindNames.Of(kind)} 뒤에는 아무것도 오지 않는다";
            return false;
        }

        step = new SequenceStepDefinition { Kind = kind };
        return true;
    }

    private static bool ParseClick(List<string> args, ref SequenceStepDefinition? step, ref string? error)
    {
        // 버튼을 안 적으면 좌클릭이다. 대부분 좌클릭이라 매번 적게 하면 성가시다.
        var button = MouseButton.Left;

        if (args.Count > 1)
        {
            error = "클릭 뒤에는 버튼 하나만 온다 - 클릭 좌 / 클릭 우 / 클릭 휠";
            return false;
        }

        if (args.Count == 1)
        {
            switch (Normalize(args[0]))
            {
                case "좌": case "left": button = MouseButton.Left; break;
                case "우": case "right": button = MouseButton.Right; break;
                case "휠": case "middle": button = MouseButton.Middle; break;
                default:
                    error = $"모르는 버튼이다: {args[0]}  (좌 · 우 · 휠)";
                    return false;
            }
        }

        step = new SequenceStepDefinition { Kind = SequenceStepKind.Click, Button = button };
        return true;
    }

    private static string Normalize(string token) => token.ToLowerInvariant();

    private static bool TryInt(string token, out int value)
        => int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// 한 줄을 명령과 인자들로 가른다. 따옴표 안의 공백은 가르지 않는다.
    /// </summary>
    /// <remarks>
    /// 주석 표시(<c>#</c>)도 따옴표 밖에서만 주석이다. 안 그러면 글자 "값 #1" 을 쓸 수 없다.
    /// </remarks>
    private static bool TrySplit(string line, out string command, out List<string> args, out string? error)
    {
        command = string.Empty;
        args = [];
        error = null;

        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuote)
            {
                if (c == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\\')
                {
                    sb.Append(line[++i]);
                    continue;
                }

                if (c == '"') { inQuote = false; continue; }

                sb.Append(c);
                continue;
            }

            if (c == '"')
            {
                inQuote = true;
                quoted = true;
                continue;
            }

            if (c == CommentMark) break;   // 여기부터 줄 끝까지 주석

            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 || quoted) { tokens.Add(sb.ToString()); sb.Clear(); quoted = false; }
                continue;
            }

            sb.Append(c);
        }

        if (inQuote)
        {
            error = "따옴표가 닫히지 않았다";
            return false;
        }

        if (sb.Length > 0 || quoted) tokens.Add(sb.ToString());

        if (tokens.Count == 0)
        {
            error = "빈 줄이다";
            return false;
        }

        command = tokens[0];
        args = tokens.Skip(1).ToList();
        return true;
    }
}
