namespace Minguk.Tools.Input.Sequencing;

/// <summary>
/// 단계 하나를 적어 둔 것. 실행하는 물건이 아니라 <b>저장·편집되는 데이터</b>다.
///
/// 왜 한 클래스에 모든 칸을 두는가
///   종류마다 클래스를 나누면(TypeStep · MoveStep …) 다형성은 깔끔해지지만, 종류를 바꾸는
///   순간 다른 객체가 되어 버려 "이 줄의 종류만 바꾼다" 를 표현할 수 없다.
///   칸 몇 개가 노는 대신 종류를 값으로 드는 쪽이 편집에 맞다.
///   쓰지 않는 칸은 <see cref="Describe"/> 도 <see cref="SequenceScript"/> 도 무시한다.
///
/// 사람이 읽고 쓰는 형태는 <see cref="SequenceScript"/> 가, 저장되는 형태는
/// <see cref="SequencePlan"/> 이 맡는다. 칸을 지우거나 이름을 바꾸면 예전에 적어 둔 것을
/// 못 읽으므로, 늘리기만 한다.
/// </summary>
public sealed class SequenceStepDefinition
{
    public SequenceStepKind Kind { get; set; }

    /// <summary><see cref="SequenceStepKind.Type"/> 이 누를 글자들.</summary>
    public string? Text { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>굴릴 칸 수. 양수가 위, 음수가 아래.</summary>
    public int Notches { get; set; } = -1;

    /// <summary>쉬는 시간.</summary>
    public int DelayMs { get; set; } = 500;

    public MouseButton Button { get; set; } = MouseButton.Left;

    /// <summary>사람이 읽는 한 줄 요약. 종류가 쓰는 칸만 읽는다.</summary>
    public string Describe() => Kind switch
    {
        SequenceStepKind.Type => string.IsNullOrEmpty(Text) ? "글자 (비어 있음)" : $"글자 \"{Text}\"",
        SequenceStepKind.Enter => "Enter",
        SequenceStepKind.ToggleHangul => "한/영",
        SequenceStepKind.Click => Button switch
        {
            MouseButton.Right => "우클릭",
            MouseButton.Middle => "휠클릭",
            _ => "좌클릭"
        },
        SequenceStepKind.MoveTo => $"이동 ({X}, {Y})",
        SequenceStepKind.Scroll => Notches < 0 ? $"휠 아래 {-Notches}칸" : $"휠 위 {Notches}칸",
        SequenceStepKind.Wait => $"{DelayMs}ms 쉬기",
        _ => Kind.ToString()
    };
}
