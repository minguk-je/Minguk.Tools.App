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
    PostMessage
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
        InputBackend.PostMessage => new PostMessageInputAdapter(targetWindowProvider),
        _ => new SendInputAdapter()
    };
}
