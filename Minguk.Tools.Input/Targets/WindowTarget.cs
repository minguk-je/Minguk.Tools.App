using System;

namespace Minguk.Tools.Input.Targets;

/// <summary>
/// 입력을 받을 창 하나.
/// </summary>
/// <param name="Handle">최상위 창 핸들.</param>
/// <param name="Title">창 제목. 비어 있을 수 있다.</param>
/// <param name="ProcessName">어느 프로그램인지. 제목이 비었을 때 이것으로라도 알아본다.</param>
/// <remarks>
/// 핸들은 창이 닫히면 죽는다. 목록을 만든 뒤 시간이 지났으면 쓰기 전에 살아 있는지 물어야 한다
/// (<see cref="IWindowTargetAdapter.IsAlive"/>). 죽은 핸들에 메시지를 보내면 조용히 사라진다.
/// </remarks>
public readonly record struct WindowTarget(IntPtr Handle, string Title, string ProcessName)
{
    /// <summary>목록에 보여 줄 한 줄. 제목이 없으면 프로세스 이름만이라도 보인다.</summary>
    public string Display => string.IsNullOrWhiteSpace(Title)
        ? $"({ProcessName})"
        : $"{Title}  —  {ProcessName}";
}
