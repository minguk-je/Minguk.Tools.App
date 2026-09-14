namespace Minguk.Tools.Modules;

/// <summary>
/// 문서를 담고 있는 셸. 화면이 부모로 받는다.
/// </summary>
/// <remarks>
/// 화면 바탕(<see cref="ViewModels.DocumentViewModelBase"/>)이 부모를 <c>MainViewModel</c> 로 들고 있었는데,
/// 그 타입이 셸에 있어 바탕을 모듈과 나눠 쓸 수 없었다. 실제로 쓰는 곳이 한 군데도 없었으므로
/// 인터페이스로 바꿔 내렸다 - 모듈 화면도 셸을 같은 이름으로 받는다.
///
/// 멤버는 아래 바 두 줄뿐이다. 화면이 셸을 통째로 만질 일이 생기면 그때 여기에 더한다 -
/// 지금 다 열어 두면 모듈이 셸 속을 마음대로 만지게 된다.
/// </remarks>
public interface IMainShell
{
    /// <summary>아래 바 왼쪽 줄. 일이 끝났다는 알림이 여기 남는다.</summary>
    string? MainMessage { get; set; }

    /// <summary>아래 바 오른쪽 줄.</summary>
    string? SubMessage { get; set; }
}
