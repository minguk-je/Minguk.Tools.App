using System;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 스크립트가 API 를 한 번 부른 기록. 호출 로그 칸에 한 줄로 뜬다.
/// </summary>
/// <remarks>
/// C# 스크립트는 한 줄씩 밟을 수 없다(Roslyn 스크립트는 디버거 없이 돈다). 대신 무엇을 언제 불렀고 무엇이
/// 돌아왔는지가 남으면 "왜 안 눌렀지" 를 되짚을 수 있다. 자바스크립트·파이썬에서도 같이 남는다.
/// </remarks>
/// <param name="At">부른 시각.</param>
/// <param name="Name">부른 것. 영문 이름.</param>
/// <param name="Arguments">넘긴 인자를 사람이 읽을 글로.</param>
/// <param name="Result">돌아온 것. 없으면 빈 글.</param>
/// <param name="ElapsedMs">걸린 시간.</param>
public sealed record ScriptCall(DateTime At, string Name, string Arguments, string Result, double ElapsedMs)
{
    public string Time => At.ToString("HH:mm:ss.fff");

    public string Call => $"{Name}({Arguments})";

    public override string ToString() => $"{Time}  {Call} → {Result} ({ElapsedMs:0}ms)";
}
