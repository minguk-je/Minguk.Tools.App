using System;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>스크립트가 <c>끝()</c> 을 불렀다. 오류가 아니라 정상 종료다.</summary>
public sealed class ScriptStoppedException() : Exception("스크립트가 끝을 불렀다");

/// <summary>
/// 안전장치가 막았다 - 대상 창이 앞에 없는데 입력을 보내려 했거나, 눈(캡처·몹 찾기)이 없는데 화면을 읽으려 했다.
/// </summary>
/// <remarks>
/// 조용히 빈 값을 주면 스크립트는 왜 아무것도 못 찾는지 알 수 없다. 멈추고 이유를 그대로 띄운다.
/// </remarks>
public sealed class ScriptGuardException(string message) : Exception(message);
