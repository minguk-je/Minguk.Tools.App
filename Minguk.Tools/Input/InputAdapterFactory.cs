using System;

using Minguk.Tools.Input.Adapters;

namespace Minguk.Tools.Input;

/// <summary>고를 수 있는 입력 경로.</summary>
public enum InputBackend
{
    /// <summary>
    /// 커널 입력 큐에 넣는다. 실제 키보드·마우스와 같은 경로라 거의 모든 대상에 통하지만,
    /// 대상이 앞으로 나와야 하고 진짜 커서가 움직인다.
    /// </summary>
    SendInput,

    /// <summary>
    /// 대상 창에 입력 메시지를 직접 넣는다. 포커스도 커서도 안 뺏긴다.
    /// 대신 창 메시지를 안 보는 프로그램(Raw Input·DirectInput 등)에는 통하지 않는다.
    /// 자체 개발 중인 앱·키오스크 자동화용이다.
    /// </summary>
    PostMessage,

    /// <summary>
    /// <see cref="PostMessage"/> 와 같은 메시지를 넣되, 대상이 처리를 마칠 때까지 기다린다.
    /// 순서와 타이밍이 확실해지는 대신 대상이 느리면 그만큼 느려진다.
    /// 대상이 멈춰 있어도 영영 굳지 않도록 <c>SendMessageTimeout</c> 을 쓴다.
    /// </summary>
    SendMessage,

    /// <summary>
    /// 커널 드라이버 스택 아래에서 만든다. 진짜 장치가 보낸 입력과 구분되지 않아
    /// 주입 입력을 걸러내는 대상(RawInput·DirectInput 을 쓰는 게임 등)에도 통한다.
    /// 드라이버 설치와 재부팅이 선행되어야 하고, 안 되어 있으면 IsAvailable 이 false 다.
    /// </summary>
    Interception
}

/// <summary>
/// 쓸 입력 백엔드를 고른다.
///
/// 백엔드가 늘어도 부르는 쪽은 손대지 않는다. 여기서만 판단한다.
/// </summary>
public static class InputAdapterFactory
{
    /// <param name="backend">쓸 경로.</param>
    /// <param name="targetWindowProvider">
    /// 대상 창 핸들. <see cref="InputBackend.PostMessage"/> 는 어느 창에 넣을지 알아야 해서 필요하다.
    /// SendInput 은 쓰지 않는다.
    /// </param>
    public static IInputAdapter Create(InputBackend backend, Func<IntPtr> targetWindowProvider) => backend switch
    {
        InputBackend.PostMessage => new WindowMessageInputAdapter(targetWindowProvider),
        InputBackend.SendMessage => new WindowMessageInputAdapter(targetWindowProvider, WindowMessageDelivery.SendWithTimeout),
        InputBackend.Interception => new InterceptionInputAdapter(),
        _ => new SendInputAdapter()
    };

    /// <summary>
    /// 고른 경로를 만들되, 쓸 수 없으면 <paramref name="fallback"/> 으로 내려앉는다.
    /// </summary>
    /// <remarks>
    /// 드라이버가 없다고 아무것도 못 하게 두는 것보다 되는 경로로 내려앉는 편이 낫다.
    /// 다만 조용히 바꾸면 안 되므로 무엇이 왜 밀려났는지 함께 돌려준다.
    /// </remarks>
    public static Selection CreateWithFallback(
        InputBackend backend, InputBackend fallback, Func<IntPtr> targetWindowProvider)
    {
        var adapter = Create(backend, targetWindowProvider);

        if (adapter.IsAvailable || backend == fallback)
            return new Selection(adapter, null, null);

        var name = adapter.Name;
        var reason = adapter.UnavailableReason;
        adapter.Dispose();

        return new Selection(Create(fallback, targetWindowProvider), name, reason);
    }

    /// <param name="Adapter">실제로 쓸 어댑터.</param>
    /// <param name="FellBackFrom">밀려난 어댑터 이름. 폴백이 없었으면 null.</param>
    /// <param name="Reason">밀려난 이유. 알 수 없으면 null.</param>
    public readonly record struct Selection(IInputAdapter Adapter, string? FellBackFrom, string? Reason)
    {
        public bool FellBack => FellBackFrom is not null;
    }
}
