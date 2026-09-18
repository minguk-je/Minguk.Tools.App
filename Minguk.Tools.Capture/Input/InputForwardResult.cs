namespace Minguk.Tools.Capture.Input;

/// <summary>
/// 미리보기에서 누른 것을 대상 창으로 넘긴 결과.
///
/// 실패를 bool 하나로 뭉개면 "클릭이 안 된다" 는 것만 보이고 왜 안 되는지 알 수 없다.
/// 이유별로 갈라서 상태 줄에 그대로 띄운다.
/// </summary>
public enum InputForwardResult
{
    /// <summary>대상 창으로 들어갔다.</summary>
    Sent,

    /// <summary>캡처 대상이 선택되어 있지 않다.</summary>
    NoTarget,

    /// <summary>대상이 화면 어디에 있는지 알아내지 못했다. 창이 닫혔을 때 그렇다.</summary>
    TargetGone,

    /// <summary>그림 바깥(검은 띠)을 눌렀다. 대상 위가 아니다.</summary>
    OutsideImage,

    /// <summary>OS 가 막았다. 대상이 이 앱보다 높은 권한으로 떠 있으면 그렇다(UIPI).</summary>
    Blocked,

    /// <summary>대상이 영상 파일이다. 받을 창이 없어 보내지 않았다 - 보내면 진짜 화면을 누른다.</summary>
    VideoTarget
}

/// <summary>결과를 사람이 읽을 문장으로.</summary>
public static class InputForwardResultText
{
    public static string Describe(this InputForwardResult result) => result switch
    {
        InputForwardResult.Sent => "전달함",
        InputForwardResult.NoTarget => "대상이 선택되어 있지 않다",
        InputForwardResult.TargetGone => "대상 창을 찾을 수 없다 (닫혔을 수 있다)",
        InputForwardResult.OutsideImage => "그림 바깥이다 - 대상 위를 눌러야 한다",
        InputForwardResult.Blocked => "OS 가 막았다. 대상이 관리자 권한이면 이 앱도 관리자로 실행해야 한다",
        InputForwardResult.VideoTarget => "영상은 입력을 받지 않는다 - 보내지 않았다",
        _ => string.Empty
    };
}
