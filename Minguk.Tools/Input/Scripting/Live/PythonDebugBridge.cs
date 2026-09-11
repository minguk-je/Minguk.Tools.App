using System.Threading;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 파이썬 <c>sys.settrace</c> 가 줄마다 부르는 다리. 파이썬 쪽 추적 함수가 <c>__dbg.ShouldBreak</c>·<c>__dbg.Pause</c> 로 부른다.
/// </summary>
/// <remarks>
/// 세션을 파이썬에 바로 주지 않는 이유는 토큰 때문이다 - 멈춘 채로 중지되면 토큰이 깨워야 하는데, 파이썬에서
/// 토큰을 넘기게 하면 번거롭다. 여기서 쥐고 있다가 같이 넘긴다.
/// </remarks>
public sealed class PythonDebugBridge(ScriptDebugSession session, CancellationToken token)
{
    public bool ShouldBreak(int line) => session.ShouldBreak(line);

    public void Pause(int line, string locals) => session.Pause(line, locals, token);
}
