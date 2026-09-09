using System;
using System.Collections.Generic;
using System.Windows;

namespace Minguk.Tools.Automation;

/// <summary>
/// 화면 요소 하나. 어댑터가 만들어서 돌려준다.
///
/// 좌표가 아니라 이것을 들고 다니는 게 UI 자동화의 요점이다.
/// 창이 움직이거나 크기가 바뀌어도 같은 요소를 가리키기 때문이다.
/// </summary>
public sealed class UiElement
{
    /// <summary>사람이 읽는 이름. 버튼이면 글자, 입력칸이면 레이블이 대개 여기 온다.</summary>
    public required string Name { get; init; }

    /// <summary>개발자가 붙인 식별자(WPF 의 AutomationProperties.AutomationId). 있으면 이걸로 찾는 게 가장 안정적이다.</summary>
    public required string AutomationId { get; init; }

    /// <summary>Button, Edit, CheckBox 같은 종류.</summary>
    public required string ControlType { get; init; }

    /// <summary>화면에서 차지하는 사각형. 미리보기에 표시하거나 클릭 좌표를 낼 때 쓴다.</summary>
    public Rect Bounds { get; init; }

    public bool IsEnabled { get; init; }

    public bool IsOffscreen { get; init; }

    /// <summary>
    /// 어댑터가 이 요소를 다시 찾을 때 쓰는 실제 객체.
    ///
    /// 이걸 만든 어댑터만 읽는다. 바깥에서는 손대지 않는다 —
    /// 구현이 바뀌면 여기 들어 있는 것도 통째로 바뀌기 때문이다.
    /// </summary>
    public object? NativeElement { get; init; }

    public override string ToString()
    {
        var id = string.IsNullOrEmpty(AutomationId) ? string.Empty : $" #{AutomationId}";
        var name = string.IsNullOrEmpty(Name) ? "(이름 없음)" : Name;

        return $"{ControlType}{id} \"{name}\"";
    }
}

/// <summary>
/// 창 안의 요소를 찾고 조작한다.
///
/// <see cref="Minguk.Tools.Input.IInputAdapter"/> 와 나눠 둔 이유
///   저쪽은 "화면 어느 좌표를 누른다" 는 이야기고, 이쪽은 "어느 요소를 누른다" 는 이야기다.
///   창이 움직이거나 해상도가 바뀌면 좌표는 틀어지지만 요소는 그대로다.
///   자체 개발 중인 앱을 자동화할 때는 이쪽이 훨씬 안정적이다.
///
/// 안 되는 곳
///   접근성 정보를 내놓지 않는 프로그램은 아무것도 안 보인다.
///   직접 그리는 화면(게임, 캔버스, 일부 커스텀 렌더러)이 여기 해당한다.
///   그런 대상은 좌표로 다루는 수밖에 없다.
///
/// 느리다는 점
///   호출 한 번이 프로세스 경계를 넘어간다. 수십 ms 씩 걸리는 일이 흔하다.
///   주기적으로 훑지 말 것 — 실제로 이 앱에서 2초마다 훑었더니 미리보기 fps 가 떨어졌다.
///   필요할 때 한 번씩만 부른다.
/// </summary>
public interface IUiAutomationAdapter
{
    /// <summary>사람이 읽을 이름. 지금 어느 경로로 도는지 화면에 보여 주려고 둔다.</summary>
    string Name { get; }

    /// <summary>화면 좌표 아래에 있는 요소. 미리보기를 눌러 무엇인지 알아볼 때 쓴다.</summary>
    bool TryGetElementAt(int screenX, int screenY, out UiElement element);

    /// <summary>창 하나를 요소 트리의 뿌리로 잡는다.</summary>
    bool TryGetWindowRoot(IntPtr windowHandle, out UiElement element);

    /// <summary>바로 아래 자식들. 트리를 한 겹씩 펼칠 때 쓴다.</summary>
    IReadOnlyList<UiElement> GetChildren(UiElement parent);

    /// <summary>식별자로 찾는다. 자손까지 훑는다.</summary>
    bool TryFindByAutomationId(UiElement scope, string automationId, out UiElement element);

    /// <summary>이름으로 찾는다. 자손까지 훑는다.</summary>
    bool TryFindByName(UiElement scope, string name, out UiElement element);

    /// <summary>누른다. 좌표를 거치지 않으므로 창이 가려져 있어도 된다.</summary>
    bool Invoke(UiElement element);

    /// <summary>글자를 넣는다. 한 자씩 치는 게 아니라 값을 통째로 바꾼다.</summary>
    bool SetText(UiElement element, string text);

    /// <summary>지금 값을 읽는다.</summary>
    bool TryGetText(UiElement element, out string text);

    /// <summary>키보드 포커스를 준다.</summary>
    bool FocusElement(UiElement element);
}
