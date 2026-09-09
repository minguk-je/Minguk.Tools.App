using System;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Input.Sequencing;

/// <summary>
/// 시퀀스의 한 단계.
///
/// 왜 이름을 함께 드는가
///   지금 무엇을 하고 있는지 화면·로그에 보여 줘야 한다. 동작만 들고 있으면
///   부르는 쪽이 "몇 번째 단계" 말고는 말할 것이 없다.
/// </summary>
/// <param name="Symbol">진행 표시에 쓸 짧은 이름. 글자 하나일 수도, "좌클릭" 일 수도 있다.</param>
/// <param name="RunAsync">
/// 실제 동작. 단계가 스스로 더 자세한 진행을 알리고 싶으면 넘겨받은 progress 를 쓴다
/// (이동처럼 한 단계가 매번 다른 곳으로 가는 경우).
/// </param>
public readonly record struct InputStep(string Symbol, Func<IProgress<string>?, CancellationToken, Task> RunAsync)
{
    /// <summary>진행을 스스로 알릴 필요가 없는 단계용.</summary>
    public static InputStep Of(string symbol, Func<Task> run)
        => new(symbol, (_, _) => run());
}
