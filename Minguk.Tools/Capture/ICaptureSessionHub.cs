namespace Minguk.Tools.Capture;

/// <summary>
/// 캡처 세션을 나눠 쓰는 곳. 같은 창을 잡는 화면이 여럿이어도 실제 세션은 하나다.
/// </summary>
/// <remarks>
/// <b>왜 있나</b> - 캡처·스크립트·플레이 화면이 저마다 세션을 만들면 같은 창을 두 번 잡는다. Windows.Graphics.Capture
/// 는 그것을 허용하지만 프레임을 두 번 받고 두 번 내리는(리드백) 값이 그대로 두 배다. 세션을 하나로 두고
/// 프레임을 나눠 주면 화면이 몇 개든 값은 한 번이다.
///
/// 화면은 <see cref="Acquire"/> 로 손잡이를 받아 예전처럼 쓴다(Start·Stop·FrameArrived·Dispose). 손잡이는
/// <see cref="IScreenCaptureAdapter"/> 라 화면 코드는 세션이 공유되는지 모른다. 실제 세션은 첫 손잡이가
/// Start 할 때 만들고, 마지막 손잡이가 Stop 하면 놓는다.
///
/// 리드백은 손잡이 중 하나라도 원하면 켠다. 안 켠 세션에 리드백을 원하는 손잡이가 들어오면 세션을 새로 만든다 -
/// 리드백은 세션을 만들 때 정해지기 때문이다. 그 순간 다른 손잡이는 프레임이 잠깐 끊기고 알림을 받는다.
/// fps 상한은 손잡이 중 가장 큰 값이다.
/// </remarks>
public interface ICaptureSessionHub
{
    /// <summary>이 대상을 잡는 손잡이를 준다. 같은 대상이면 같은 세션을 나눠 쓴다.</summary>
    IScreenCaptureAdapter Acquire(CaptureTarget target, bool cpuReadback);

    /// <summary>이 대상을 잡고 있는 손잡이 수. 검증과 상태 표시용.</summary>
    int ConsumerCount(CaptureTarget target);
}
