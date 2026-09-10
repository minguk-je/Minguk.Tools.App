namespace Minguk.Tools.Input;

/// <summary>
/// 글자를 <b>글자 그대로</b> 넣을 수 있는 경로.
/// </summary>
/// <remarks>
/// <b>왜 따로 뺐는가</b>
///
/// 창 메시지를 부치는 경로(PostMessage)는 <c>PressKey(VK_SHIFT)</c> 를 보내도 대상 스레드의
/// 키 상태가 바뀌지 않는다. 그래서 대상이 다음 키를 해석할 때 Shift 가 안 눌린 것으로 보고
/// <c>C</c> 를 <c>c</c> 로, <c>!</c> 를 <c>1</c> 로 넣는다. 실측으로 확인했다.
///
/// <c>WM_CHAR</c> 는 그 해석 단계를 건너뛰고 글자를 그대로 준다. 대신 키가 눌렸다는 사실은
/// 전하지 않으므로 단축키 같은 것에는 못 쓴다 - 그래서 <see cref="IInputAdapter.PressKey"/> 를
/// 대신하는 것이 아니라 글자 넣기에만 쓴다.
///
/// 커널 입력 큐를 쓰는 경로(SendInput·Interception)는 이것이 필요 없다. 그쪽은 진짜 키 상태를
/// 바꾸므로 Shift 가 그대로 먹는다. 그래서 <see cref="IScanCodeInput"/> 처럼 능력별로 나눠 두고,
/// 필요한 쪽이 <c>adapter is ICharacterInput</c> 으로 물어본다.
/// </remarks>
public interface ICharacterInput
{
    /// <summary>글자 하나를 그대로 넣는다.</summary>
    bool SendCharacter(char c);
}
