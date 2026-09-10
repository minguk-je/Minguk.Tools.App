using System;
using System.Collections.Generic;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 스크립트가 부르는 것들. 스크립트 안에서는 이 메서드들이 전역처럼 보인다.
/// </summary>
/// <remarks>
/// <b>부르면 곧바로 나가지 않는다.</b> 단계 목록에 적어 둘 뿐이다.
///
/// 왜 그런가
///   스크립트를 그대로 실행해 버리면 화면이 가진 것들이 무너진다 - 무엇이 어떤 순서로
///   나갈지 미리 보여 주는 것, 시작 전 대기, 반복, 중지. 스크립트를 한 번 돌려 단계를
///   받아 두면 그 뒤는 예전과 똑같이 굳혀서 돌릴 수 있다.
///
///   덤으로 <c>for</c> 문이 단계로 풀려 나온다. 열 번 도는 반복문을 쓰면 열 번치 단계가
///   생겨서 순서 미리보기에도 그대로 보인다.
///
/// 이 방식이 못 하는 것
///   실행 <b>도중</b>에 반응하는 것. "화면에 X 가 보이면 클릭" 같은 것은 단계를 적는 시점에
///   판단할 수 없다. 지금은 화면을 읽는 단계 자체가 없어서 잃는 것이 없다.
/// </remarks>
public sealed class SequenceScriptApi
{
    private readonly List<SequenceStepDefinition> _steps = [];

    /// <summary>스크립트가 적어 둔 단계들.</summary>
    public IReadOnlyList<SequenceStepDefinition> Steps => _steps;

    /// <summary>글자를 하나씩 누른다. 한글·영문·숫자·문장부호를 다룬다.</summary>
    public void Type(string text)
        => _steps.Add(new SequenceStepDefinition { Kind = SequenceStepKind.Type, Text = text ?? string.Empty });

    /// <summary>Enter 한 번.</summary>
    public void Enter()
        => _steps.Add(new SequenceStepDefinition { Kind = SequenceStepKind.Enter });

    /// <summary>한/영 을 한 번 뒤집는다.</summary>
    public void ToggleHangul()
        => _steps.Add(new SequenceStepDefinition { Kind = SequenceStepKind.ToggleHangul });

    /// <summary>마우스 버튼 한 번. 기본은 좌클릭.</summary>
    public void Click(MouseButton button = MouseButton.Left)
        => _steps.Add(new SequenceStepDefinition { Kind = SequenceStepKind.Click, Button = button });

    public void RightClick() => Click(MouseButton.Right);

    /// <summary>정해진 화면 좌표로 옮긴다.</summary>
    public void MoveTo(int x, int y)
        => _steps.Add(new SequenceStepDefinition { Kind = SequenceStepKind.MoveTo, X = x, Y = y });

    /// <param name="notches">굴릴 칸 수. 양수가 위, 음수가 아래.</param>
    public void Scroll(int notches)
        => _steps.Add(new SequenceStepDefinition { Kind = SequenceStepKind.Scroll, Notches = notches });

    /// <summary>아무것도 보내지 않고 쉰다. 단계 간격과 달리 이 자리에만 들어간다.</summary>
    public void Wait(int milliseconds)
        => _steps.Add(new SequenceStepDefinition { Kind = SequenceStepKind.Wait, DelayMs = Math.Max(0, milliseconds) });

    /// <summary>글자를 치고 Enter 까지. 자주 쓰는 짝이라 하나로 둔다.</summary>
    public void TypeLine(string text)
    {
        Type(text);
        Enter();
    }

    /// <summary>그 자리로 옮겨 클릭한다. 이동과 클릭을 따로 적는 것과 같다.</summary>
    public void ClickAt(int x, int y, MouseButton button = MouseButton.Left)
    {
        MoveTo(x, y);
        Click(button);
    }

    // ── 한글 이름 ────────────────────────────────────────────────────────
    //    C# 은 한글 식별자를 받는다. 영문 이름과 같은 것을 가리키므로 섞어 써도 된다.
    //    "글자" 처럼 우리말로 쓰는 편이 읽기 쉬운 사람이 있고, 예전 형식에서 옮겨 온
    //    사람에게도 낯이 익다. 이름을 늘리는 것뿐이라 잃는 것이 없다.
    //
    //    한/영 은 식별자에 슬래시를 못 써서 "한영" 이다.

    public void 글자(string text) => Type(text);

    public void 줄입력(string text) => TypeLine(text);

    public void 엔터() => Enter();

    public void 한영() => ToggleHangul();

    public void 클릭(MouseButton button = MouseButton.Left) => Click(button);

    public void 우클릭() => RightClick();

    public void 이동(int x, int y) => MoveTo(x, y);

    public void 이동클릭(int x, int y, MouseButton button = MouseButton.Left) => ClickAt(x, y, button);

    public void 휠(int notches) => Scroll(notches);

    public void 쉬기(int milliseconds) => Wait(milliseconds);

    internal SequencePlan ToPlan() => new() { Steps = [.. _steps] };
}
