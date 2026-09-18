namespace Minguk.Tools.Input;

/// <summary>
/// 키를 가상 키가 아니라 스캔코드로 넣을 수 있는 어댑터.
///
/// 왜 <see cref="IInputAdapter"/> 에 넣지 않고 갈라 두는가
///   PostMessage 경로는 이걸 제대로 할 수 없다. lParam 에 스캔코드를 실어 보낼 수는 있지만
///   대상 IME 는 그렇게 온 한/영 전환을 받지 않는다. 못 하는 것을 늘 false 만 돌려주는 빈
///   메서드로 두면 부르는 쪽이 되는 줄 알고 쓰게 된다. 아예 구현하지 않도록 갈라 둔다.
///   필요한 쪽은 `adapter is IScanCodeInput` 으로 물어보고, 아니면 못 한다고 말해야 한다.
///
/// 가상 키와 무엇이 다른가
///   가상 키는 "무슨 글자인가", 스캔코드는 "키보드 어느 자리인가" 다.
///   자리로 찍으므로 키보드 레이아웃이나 지금 IME 상태와 무관하게 같은 키가 눌린다.
///
/// 이게 있어야 되는 것들
///   한/영 전환(스캔코드 0xF2) — 가상 키로는 눌러도 IME 가 반응하지 않는 경우가 많다.
///   한글 입력 — 두벌식은 자판 "자리" 배열이라 자리로 찍어야 조합이 만들어진다.
///   게임 — DirectInput/RawInput 을 쓰는 쪽은 스캔코드를 본다.
/// </summary>
public interface IScanCodeInput
{
    /// <param name="scanCode">Scan Code Set 1 값. 키의 물리적 위치다.</param>
    /// <param name="extended">방향키·오른쪽 Alt 처럼 E0 확장 키면 true.</param>
    /// <returns>실제로 들어갔으면 true. false 는 OS 가 거부했다는 뜻이다(대개 UIPI).</returns>
    bool PressScanCode(ushort scanCode, bool extended);

    /// <inheritdoc cref="PressScanCode"/>
    bool ReleaseScanCode(ushort scanCode, bool extended);
}
