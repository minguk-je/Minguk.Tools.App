namespace Minguk.Tools.Input;

/// <summary>
/// 대상 창의 IME 변환 모드(한글이냐 영문이냐)를 직접 바꿀 수 있는 경로.
/// </summary>
/// <remarks>
/// <b>왜 따로 뺐는가</b>
///
/// 한/영 전환은 원래 한/영 키를 눌러서 한다. 그 키는 커널 입력 큐를 거쳐야 하므로
/// 스캔코드를 넣을 수 있는 경로에서만 된다. 그래서 창 메시지 경로는 한/영 전환을 못 한다고
/// 적어 두었는데, <b>다른 길이 있다</b> — 창의 기본 IME 윈도우에 <c>WM_IME_CONTROL</c> 을
/// 보내는 것이다. 키를 흉내 내는 것이 아니라 IME 에게 직접 말한다.
///
/// <c>WM_INPUTLANGCHANGE</c> 와는 다른 층이다. 그쪽은 <b>입력 언어</b>(자판)를 바꾸는 것이고,
/// 이것은 그 언어 안에서의 <b>변환 모드</b>다. 한국어 자판을 쓰는 중에 한/영 을 누르는 것은 후자다.
///
/// 대상 창을 아는 경로만 할 수 있다. 그래서 <see cref="IScanCodeInput"/> ·
/// <see cref="ICharacterInput"/> 과 같은 결로 능력별 인터페이스로 둔다.
/// </remarks>
public interface IImeControl
{
    /// <summary>대상 창의 IME 가 지금 한글 모드인지. 읽지 못하면 false 를 돌려준다.</summary>
    bool TryGetHangulMode(out bool isHangul);

    /// <summary>대상 창의 IME 를 한글/영문 모드로 바꾼다.</summary>
    bool TrySetHangulMode(bool hangul);
}
