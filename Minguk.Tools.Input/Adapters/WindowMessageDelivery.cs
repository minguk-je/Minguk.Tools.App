namespace Minguk.Tools.Input.Adapters;

/// <summary>
/// 창 메시지를 어떻게 건넬지.
/// </summary>
/// <remarks>
/// 같은 어댑터가 두 가지로 돈다. 넣는 메시지는 같고 건네는 방식만 다르므로 구현을 나누지 않는다.
/// </remarks>
public enum WindowMessageDelivery
{
    /// <summary>
    /// 큐에 넣고 곧바로 돌아온다. 대상이 언제 처리하는지는 모른다.
    /// 대상이 멈춰 있어도 이쪽은 안 멈춘다.
    /// </summary>
    Post,

    /// <summary>
    /// 대상이 처리를 마칠 때까지 기다린다(한도 있음). 순서와 타이밍이 확실해지는 대신
    /// 대상이 느리면 그만큼 느려진다.
    /// </summary>
    SendWithTimeout
}
