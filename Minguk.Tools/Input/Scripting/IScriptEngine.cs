using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 스크립트를 돌려 단계 목록을 받아 온다.
/// </summary>
/// <remarks>
/// <b>돌린다고 입력이 나가지 않는다.</b> 스크립트가 부르는 것은 <see cref="SequenceScriptApi"/> 고,
/// 그것은 단계를 적어 둘 뿐이다. 그 덕에 언어를 갈아 끼워도 그 뒤(순서 미리보기 · 시작 전 대기 ·
/// 반복 · 중지)는 손댈 것이 없다.
///
/// 언어마다 준비 비용이 크게 다르다 - C# 은 첫 컴파일이 느리고, 파이썬은 런타임을 받아 와야
/// 할 수도 있다. 그래서 <see cref="PrepareAsync"/> 를 따로 두어 화면이 뜰 때 미리 치르게 한다.
/// </remarks>
public interface IScriptEngine : IDisposable
{
    /// <summary>사람이 읽을 이름. 어느 언어로 도는지 화면·로그에 보여 준다.</summary>
    string Name { get; }

    /// <summary>이 엔진이 새 스크립트에 넣어 줄 본보기. 처음 여는 사람이 뭘 쓸지 알 수 있게.</summary>
    string SampleSource { get; }

    /// <summary>지금 돌릴 수 있는지. 준비가 안 됐으면 false.</summary>
    bool IsReady { get; }

    /// <summary>못 쓰는 이유. 쓸 수 있으면 null.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// 돌릴 준비를 한다. 이미 됐으면 아무것도 하지 않는다.
    /// </summary>
    /// <param name="progress">받아 오거나 푸는 데 시간이 걸리면 무슨 일을 하는지 알린다.</param>
    Task PrepareAsync(IProgress<string>? progress = null, CancellationToken token = default);

    /// <summary>
    /// 스크립트를 돌려 계획을 만든다.
    /// </summary>
    /// <remarks>
    /// 문법 오류든 도는 중에 터진 것이든 <c>Errors</c> 로 나온다. 둘 다 사용자가 고칠 것이고,
    /// 어느 쪽인지는 문장으로 구분된다. 오류가 있으면 계획은 비어 있다 - 반쪽짜리를 돌리는
    /// 것보다 안 돌리는 것이 낫다.
    /// </remarks>
    Task<(SequencePlan Plan, IReadOnlyList<ScriptError> Errors)> RunAsync(
        string? source, CancellationToken token = default);
}

/// <summary>스크립트 언어.</summary>
public enum ScriptLanguage
{
    /// <summary>Roslyn 으로 도는 C#. 받아 올 것이 없어 늘 쓸 수 있다.</summary>
    CSharp,

    /// <summary>Python.NET 으로 도는 파이썬. 처음 고를 때 런타임을 받아 온다.</summary>
    Python,

    /// <summary>Jint 로 도는 자바스크립트. 순수 .NET 이라 받아 올 것이 없다.</summary>
    JavaScript
}

/// <param name="Line">1 부터 센 줄 번호. 어디인지 모르면 0.</param>
public readonly record struct ScriptError(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"{Line}번째 줄: {Message}" : Message;
}
