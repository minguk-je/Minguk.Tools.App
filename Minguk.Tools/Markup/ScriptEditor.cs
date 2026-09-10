using System.Windows.Automation.Peers;
using ICSharpCode.AvalonEdit;

namespace Minguk.Tools.Markup;

/// <summary>
/// 시퀀스 스크립트 편집기. <see cref="TextEditor"/> 에서 자동화 피어만 바꾼 것이다.
/// </summary>
/// <remarks>
/// <b>왜 이것이 필요한가</b>
///
/// AvalonEdit 을 그냥 얹었더니 <b>창 전체의 UI 자동화 트리가 비었다.</b> 실측으로 갈렸다 -
/// 같은 화면에서 버튼이 그리드 시절에는 37개로 보였는데 편집기를 넣은 뒤로는 0개가 됐다.
/// 편집기만 안 보이는 것이 아니라 창에 있는 모든 것이 안 보인다.
///
/// 이것은 검증 도구만의 문제가 아니다. 스크린 리더에도 이 창이 텅 빈 것으로 보인다.
///
/// 원인은 AvalonEdit 이 제 몫으로 만드는 피어다. 그 피어가 트리를 훑는 도중에 막히면
/// UIA 는 그 위쪽 가지를 통째로 버린다. 그래서 평범한
/// <see cref="FrameworkElementAutomationPeer"/> 로 갈아 끼운다 -
/// 편집기 안의 <b>글</b>은 어차피 밖에서 못 읽었으므로 잃는 것이 없고,
/// 나머지 화면이 되돌아온다.
/// </remarks>
public sealed class ScriptEditor : TextEditor
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
