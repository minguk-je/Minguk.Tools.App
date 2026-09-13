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

    /// <summary>
    /// 실시간 모드로 돌릴 수 있는 글인지만 본다(컴파일·문법). 돌리지는 않는다.
    /// </summary>
    /// <remarks>
    /// 계획 모드는 "돌려서 계획을 받는 것" 이 곧 검사지만, 실시간 모드는 돌리면 입력이 나간다.
    /// 타이핑이 멎을 때마다 부르는 것이라 부작용이 없어야 한다.
    /// </remarks>
    Task<IReadOnlyList<ScriptError>> CheckLiveAsync(string? source, CancellationToken token = default);

    /// <summary>
    /// 실시간 모드로 돌린다. 스크립트가 <paramref name="api"/> 를 부르면 <b>곧바로 나간다</b>. 끝날 때까지 돌아온다.
    /// </summary>
    /// <remarks>
    /// 중지·비상 정지(Pause)·<c>끝()</c> 으로 멈춘 것은 오류가 아니라 빈 목록이다. 안전장치가 막은 것과 스크립트가
    /// 터진 것은 오류로 온다. 어느 쪽인지는 <see cref="Live.LiveScriptApi.Outcome"/> 을 보고 가른다.
    /// </remarks>
    Task<IReadOnlyList<ScriptError>> RunLiveAsync(
        string? source, Live.LiveScriptApi api, Live.ScriptDebugSession? debug = null, CancellationToken token = default);

    /// <summary>줄 단위로 멈출 수 있는가(중단점·한 줄씩). C# 은 못 한다 - Roslyn 스크립트는 디버거 없이 돈다.</summary>
    bool SupportsStepping { get; }
}

/// <summary>
/// 여러 파일로 된 스크립트(프로젝트)를 다루는 엔진. 능력별 인터페이스다 - 지금은 C# 만.
/// </summary>
/// <remarks>
/// <see cref="IScriptEngine"/> 에 기본 구현(시작 파일만 보기)으로 두지 않는다. 그러면 파이썬에서 다른 파일을 조용히 버리고
/// "함수가 없다" 로만 보인다. 부르는 쪽이 <c>engine is IProjectScriptEngine</c> 로 묻고, 아니면 못 한다고 말한다.
/// </remarks>
public interface IProjectScriptEngine
{
    Task<IReadOnlyList<ScriptError>> CheckLiveAsync(ScriptUnit unit, CancellationToken token = default);

    Task<IReadOnlyList<ScriptError>> RunLiveAsync(
        ScriptUnit unit, Live.LiveScriptApi api, Live.ScriptDebugSession? debug = null, CancellationToken token = default);
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
/// <param name="File">
/// 틀린 곳이 든 파일의 전체 경로. 한 파일짜리 글이거나 모르면 null. 프로젝트에서는 편집기가 이것으로 제 파일의 오류만 고른다.
/// </param>
public readonly record struct ScriptError(int Line, string Message, string? File = null)
{
    /// <summary>오류 목록의 "파일" 칸.</summary>
    public string FileName => File is null ? string.Empty : System.IO.Path.GetFileName(File);

    public override string ToString()
    {
        var where = File is null ? string.Empty : System.IO.Path.GetFileName(File) + " ";

        return Line > 0 ? $"{where}{Line}번째 줄: {Message}" : where + Message;
    }
}

/// <summary>
/// 여러 파일로 된 스크립트 한 벌 - 컴파일에 들어가는 전부.
/// </summary>
/// <param name="EntryPath">시작 파일 전체 경로. 최상위 실행문이 여기서 돈다.</param>
/// <param name="EntryText">시작 파일의 글. 편집기에 든 것(저장 안 했을 수 있다).</param>
/// <param name="Sources">시작 파일 말고 함께 들어갈 소스 파일 전체 경로들. 적힌 순서대로 앞에 붙는다.</param>
/// <param name="References">참조할 DLL 전체 경로들.</param>
/// <param name="OpenTexts">저장 안 한 채 열려 있는 파일의 글(경로 → 글). 디스크 대신 이것을 읽는다.</param>
/// <param name="ResourceRoot">리소스를 찾는 폴더(프로젝트 폴더). 없으면 null.</param>
/// <remarks>
/// <b>저장 안 한 글을 들고 가는 이유</b> - 탭 여러 개를 고치다 실행을 누르면 사람은 화면에 보이는 글이 돈다고 여긴다.
/// 디스크만 읽으면 방금 고친 함수가 옛것으로 돈다.
/// </remarks>
public sealed record ScriptUnit(
    string EntryPath,
    string EntryText,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> References,
    IReadOnlyDictionary<string, string> OpenTexts,
    string? ResourceRoot)
{
    /// <summary>한 파일짜리. 경로를 모르면 빈 문자열.</summary>
    public static ScriptUnit Single(string text, string? path = null)
        => new(path ?? string.Empty, text, [], [], new Dictionary<string, string>(), null);

    /// <summary>같은 것을 두 번 컴파일하지 않으려고 쓰는 열쇠. 저장 안 한 글과 디스크 글의 시각까지 담는다.</summary>
    public string Fingerprint()
    {
        var builder = new System.Text.StringBuilder();

        builder.Append(EntryPath).Append('\0').Append(EntryText).Append('\0');

        foreach (var path in Sources)
        {
            builder.Append(path).Append('\0');

            if (OpenTexts.TryGetValue(path, out var open)) builder.Append(open);
            else if (System.IO.File.Exists(path)) builder.Append(System.IO.File.GetLastWriteTimeUtc(path).Ticks);

            builder.Append('\0');
        }

        foreach (var path in References)
            builder.Append(path).Append('\0').Append(System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path).Ticks : 0).Append('\0');

        return builder.ToString();
    }
}
