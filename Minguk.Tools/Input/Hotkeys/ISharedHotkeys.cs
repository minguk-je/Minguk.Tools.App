using System;
using System.Windows.Input;

namespace Minguk.Tools.Input.Hotkeys;

/// <summary>
/// 여러 화면이 같은 전역 단축키를 나눠 쓴다. 키는 시스템에 한 번만 등록하고, 눌리면 <b>마지막에 쥐었거나 마지막에 활성화한</b> 화면이 받는다.
/// </summary>
/// <remarks>
/// <b>왜</b> - RegisterHotKey 는 한 조합을 한 창만 쥘 수 있다. 스크립트·플레이·입력 자동화가 각자 F5 를 쥐면
/// 같이 열려 있을 때 나중에 연 화면은 등록에 실패하고, 사용자는 게임에서 F5 를 눌러도 아무 일이 없는 이유를 모른다(실측).
/// 여기서는 화면이 "쥐고 싶다" 고 말하고(<see cref="Claim"/>), 탭이 활성화될 때 <see cref="HotkeyClaim.Activate"/> 로
/// 제 차례를 앞으로 당긴다. 사용자가 마지막에 본 화면이 받는 것이 사람의 기대와 같다.
/// </remarks>
public interface ISharedHotkeys : IDisposable
{
    /// <summary>
    /// 키를 쥔다. 다른 프로그램이 이미 쥐고 있어 시스템 등록이 안 되면 null 과 이유를 준다.
    /// </summary>
    /// <param name="label">상태 줄에 적을 이름 - "F5 실행/계속".</param>
    HotkeyClaim? Claim(Key key, ModifierKeys modifiers, string label, Action action, out string? problem);
}

/// <summary>화면 하나가 쥔 단축키 하나. 버리면 놓는다 - 마지막 것이 놓이면 시스템 등록도 푼다.</summary>
public sealed class HotkeyClaim : IDisposable
{
    private readonly Action<HotkeyClaim> _activate;
    private readonly Action<HotkeyClaim> _release;

    internal HotkeyClaim(Key key, ModifierKeys modifiers, string label, Action action, Action<HotkeyClaim> activate, Action<HotkeyClaim> release)
    {
        Key = key;
        Modifiers = modifiers;
        Label = label;
        Action = action;
        _activate = activate;
        _release = release;
    }

    public Key Key { get; }

    public ModifierKeys Modifiers { get; }

    public string Label { get; }

    public Action Action { get; }

    /// <summary>이 화면이 앞으로 왔다. 이제부터 이 키는 여기가 받는다.</summary>
    public void Activate() => _activate(this);

    public void Dispose() => _release(this);
}
