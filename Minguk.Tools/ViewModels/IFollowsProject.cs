namespace Minguk.Tools.ViewModels;

/// <summary>
/// 솔루션 화면 아래 탭 중 <b>시작 프로젝트를 바꿔도 닫히지 않고 그 자리에서 따라가는</b> 화면.
/// </summary>
/// <remarks>
/// 사용자(2026-09-19) "프로젝트 선택하면 화면캡처 탭은 왜 사라졌다 나타나는거야" - 예전에는 프로젝트를 바꾸면 아래 탭을 모두 닫고 다시 열어
/// 탭이 번쩍이고 캡처가 끊기고 모델을 다시 올렸다. VS 처럼 탭은 그대로 두고 화면마다 새 프로젝트 자리만 다시 읽는다.
///
/// 솔루션 화면이 세 단계로 부른다 - 하나라도 막으면 바꾸지 않고 콤보를 되돌린다.
/// <list type="number">
/// <item><see cref="ProjectSwitchBlocker"/> - 묻지 않고 막을 이유만(스크립트가 도는 중, 학습 중). 모든 화면에 먼저 묻는다.</item>
/// <item><see cref="PrepareProjectSwitch"/> - 옛 프로젝트 것을 저장한다. 사람에게 물을 수 있고 취소면 false.</item>
/// <item><see cref="FollowProject"/> - 시작 프로젝트가 바뀐 뒤 새 자리로 다시 읽는다.</item>
/// </list>
/// 안 연 탭(초기화 전)은 셋 다 할 일이 없다 - 처음 뜰 때 새 프로젝트를 읽는다.
/// </remarks>
public interface IFollowsProject
{
    /// <summary>지금 바꾸면 안 되는 이유. 없으면 null.</summary>
    string? ProjectSwitchBlocker();

    /// <summary>바꾸기 직전 - 옛 프로젝트 것을 저장한다. 사람이 그만두면 false.</summary>
    bool PrepareProjectSwitch();

    /// <summary>시작 프로젝트가 바뀐 뒤 - 새 프로젝트 자리로 다시 읽는다.</summary>
    void FollowProject();
}
