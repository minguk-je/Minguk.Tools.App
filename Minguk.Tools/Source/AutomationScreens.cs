using System.Collections.Generic;
using System.Linq;

using Minguk.Image;

namespace Minguk.Tools.Source;

/// <summary>Automation Builder 화면 아래 탭 하나 - 이름·뷰·아이콘.</summary>
/// <param name="Title">탭에 보이는 이름.</param>
/// <param name="ViewName">열 View 의 전체 타입 이름. <c>MainViewLocator</c> 가 이 이름으로 DI 에서 꺼낸다.</param>
/// <param name="Icon">탭 아이콘(PNG 바이트).</param>
public sealed record AutomationScreen(string Title, string ViewName, object? Icon);

/// <summary>
/// 빌더 화면 아래에 늘 있는 탭들 - 만드는 일(화면캡처·라벨링·스크립트).
/// </summary>
/// <remarks>
/// 짜임(사용자, 2026-09-14): Minguk Tools(틀) → Office Automation → <b>Automation(누르는 항목)</b> → Automation 화면
/// (위에서 솔루션 → 프로젝트를 고른다) → 아래 탭 = 이 화면들. 예전에는 이것들이 왼쪽 메뉴의 항목이었다.
///
/// <b>처음부터 다 있다</b>(사용자 결정) - 고를 것 없이 눌러 옮긴다. 화면은 그 탭을 처음 누를 때 만들어지므로
/// 다 있어도 무겁지 않다. 닫기 버튼은 없다.
///
/// 모듈이 들고 오는 화면(학습환경 등)은 여기 안 든다 - 최상위 메뉴로 붙는다(<c>MainMenu</c>).
/// </remarks>
public static class AutomationScreens
{
    public static IReadOnlyList<AutomationScreen> All()
    {
        // 빌더에는 만드는 화면만 둔다. 학습환경(모듈 화면)은 최상위 메뉴로 뺐다(사용자, 2026-09-14) - 거기서 Workspace 를 정하는데
        // 빌더는 그 Workspace 에서 솔루션을 고르므로, 빌더 안에 두면 솔루션부터 골라야 Workspace 를 바꿀 수 있었다.
        var screens = new List<AutomationScreen>();

        screens.AddRange(
        [
            new("화면캡처", "Minguk.Tools.Views.CaptureMonitorView", FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/screen.png")),
            new("라벨링", "Minguk.Tools.Views.LabelingView", FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/picture-edit.png")),
            new("스크립트", "Minguk.Tools.Views.ScriptStudioView", FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/document-edit.png"))
        ]);

        return screens;
    }
}
