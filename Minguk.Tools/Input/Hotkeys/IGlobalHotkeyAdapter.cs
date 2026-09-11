using System;
using System.Windows.Input;

namespace Minguk.Tools.Input.Hotkeys;

/// <summary>
/// 다른 창이 포커스를 가진 상태에서도 눌리는 단축키.
///
/// 왜 필요한가
///   입력을 대상 창으로 보내려면 그 창이 앞에 있어야 한다. 그런데 이 앱의 버튼은
///   이 앱이 앞에 있어야 누를 수 있다. 둘을 동시에 만족할 수 없어서, 앱 밖에서
///   시작·중지할 수단이 필요하다.
///
/// 되지 않는 경우
///   다른 프로그램이 같은 조합을 먼저 쥐고 있으면 등록이 실패한다. 흔한 일이라
///   조용히 넘기지 말고 <see cref="TryRegister"/> 의 false 를 사용자에게 알려야 한다.
///   관리자 권한으로 뜬 창 위에서는 눌러도 이 앱까지 오지 않을 수 있다.
/// </summary>
public interface IGlobalHotkeyAdapter : IDisposable
{
    /// <summary>사람이 읽을 이름. 지금 어느 경로로 도는지 보여 주려고 둔다.</summary>
    string Name { get; }

    /// <summary>
    /// 단축키 하나를 등록한다.
    /// </summary>
    /// <param name="onPressed">
    /// 눌렸을 때 할 일. 메시지 훅이 UI 스레드에서 부르므로 화면을 그대로 만져도 된다.
    /// </param>
    /// <returns>등록했으면 true. 다른 프로그램이 이미 쥐고 있으면 false.</returns>
    bool TryRegister(Key key, ModifierKeys modifiers, Action onPressed);

    /// <summary>조합 하나를 되돌린다. 나눠 쓰던 화면이 다 떠났을 때.</summary>
    bool Unregister(Key key, ModifierKeys modifiers);

    /// <summary>등록한 것을 모두 되돌린다. 놓아 주지 않으면 앱이 살아 있는 동안 조합이 잠긴다.</summary>
    void UnregisterAll();
}
