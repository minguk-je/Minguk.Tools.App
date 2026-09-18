namespace Minguk.Tools.Input.Sequencing;

/// <summary>
/// 시퀀스 한 단계가 무엇을 하는지.
///
/// 왜 열거형인가
///   단계를 <see cref="InputStep"/> 로 바로 만들면 실행은 되지만 저장·편집을 할 수 없다.
///   실행되는 것(<see cref="InputStep"/>, 델리게이트)과 적어 두는 것(<see cref="SequenceStepDefinition"/>,
///   데이터)을 나누고, 그 사이를 <see cref="SequencePlan.Build"/> 가 잇는다.
/// </summary>
public enum SequenceStepKind
{
    /// <summary>글자를 하나씩 누른다. <see cref="SequenceStepDefinition.Text"/> 를 쓴다.</summary>
    Type,

    /// <summary>Enter 한 번.</summary>
    Enter,

    /// <summary>한/영 한 번.</summary>
    ToggleHangul,

    /// <summary>마우스 버튼 한 번. <see cref="SequenceStepDefinition.Button"/> 을 쓴다.</summary>
    Click,

    /// <summary>정해진 좌표로 옮긴다. <see cref="SequenceStepDefinition.X"/>·<see cref="SequenceStepDefinition.Y"/> 를 쓴다.</summary>
    MoveTo,

    /// <summary>휠을 굴린다. <see cref="SequenceStepDefinition.Notches"/> 를 쓴다.</summary>
    Scroll,

    /// <summary>아무것도 보내지 않고 쉰다. <see cref="SequenceStepDefinition.DelayMs"/> 를 쓴다.</summary>
    Wait
}

/// <summary>
/// 종류를 사람이 읽는 이름으로.
/// </summary>
/// <remarks>
/// 열거형 이름을 그대로 보여 주면 목록이 Type · Enter · ToggleHangul 로 나온다.
/// 이름은 저장 형식이라 함부로 못 바꾸므로, 보여 주는 이름은 여기서 따로 든다.
/// <see cref="SequenceStepDefinition.Describe"/> 도 이미 한글로 말하므로 같은 결이다.
/// </remarks>
/// <summary>
/// 이 종류가 스캔코드를 타는지.
/// </summary>
/// <remarks>
/// 스캔코드를 못 넣는 경로(PostMessage)에서는 이런 단계가 시퀀스에 담기지 않는다.
/// 부르는 쪽이 "몇 개가 빠질 것인지" 를 미리 말해 줄 수 있어야 해서 여기 둔다 -
/// 만들고 나서 개수를 세면 왜 빠졌는지는 알 수 없다.
/// </remarks>
public static class SequenceStepKinds
{
    /// <summary>
    /// 이 단계가 스캔코드 경로에서만 되는지.
    /// </summary>
    /// <remarks>
    /// 글자는 <see cref="ICharacterInput"/> 로도 들어간다 - <b>한글까지</b>. 그 길은 완성된
    /// 음절을 그대로 주므로 IME 를 거치지 않는다. 그래서 남는 것은 한/영 <b>전환</b>뿐이다.
    /// IME 의 상태를 바꾸는 것은 스캔코드로만 통한다.
    /// </remarks>
    public static bool NeedsScanCode(SequenceStepDefinition step)
        => step.Kind == SequenceStepKind.ToggleHangul;
}

public static class SequenceStepKindNames
{
    public static string Of(SequenceStepKind kind) => kind switch
    {
        SequenceStepKind.Type => "글자",
        SequenceStepKind.Enter => "Enter",
        SequenceStepKind.ToggleHangul => "한/영",
        SequenceStepKind.Click => "클릭",
        SequenceStepKind.MoveTo => "이동",
        SequenceStepKind.Scroll => "휠",
        SequenceStepKind.Wait => "쉬기",
        _ => kind.ToString()
    };
}
