using System;
using System.Collections.Generic;

namespace Minguk.Tools.Input.Targets;

/// <summary>
/// 입력을 받을 창을 찾아 준다.
/// </summary>
/// <remarks>
/// <see cref="InputBackend.PostMessage"/> 는 보낼 창을 알아야 한다. 진짜 커서를 안 움직이므로
/// "지금 포커스를 가진 창" 같은 것에 기댈 수 없고, 부르는 쪽이 정해 줘야 한다.
///
/// OS 에 닿는 것이라 인터페이스로 나눈다. 나중에 UI Automation 으로 자식 컨트롤까지
/// 짚는 구현을 더하더라도 화면과 ViewModel 은 그대로 둘 수 있다.
/// </remarks>
public interface IWindowTargetAdapter : IDisposable
{
    /// <summary>지금 어느 방법으로 찾고 있는지. 로그·화면에 보여 준다.</summary>
    string Name { get; }

    /// <summary>
    /// 눈에 보이는 최상위 창들. 제목이 있는 것만 준다.
    /// </summary>
    /// <param name="exclude">목록에서 뺄 창. 보통 이 앱 자신을 뺄 때 쓴다.</param>
    IReadOnlyList<WindowTarget> List(IntPtr exclude = default);

    /// <summary>화면 좌표 아래에 있는 최상위 창. 없으면 null.</summary>
    WindowTarget? FromPoint(int x, int y);

    /// <summary>그 핸들이 아직 살아 있는 창인지.</summary>
    bool IsAlive(IntPtr handle);
}
