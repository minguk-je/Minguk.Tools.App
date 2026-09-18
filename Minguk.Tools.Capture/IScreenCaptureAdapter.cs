using System;

using Vortice.Direct3D11;

namespace Minguk.Tools.Capture;

/// <summary>
/// 화면을 잡아 오는 곳. 지금 구현은 <see cref="WgcCaptureSession"/>(Windows.Graphics.Capture) 하나다.
///
/// 인터페이스로 갈라 둔 이유
///   캡처 방식은 상황에 따라 갈아끼워야 한다. WGC 는 Windows 10 2004 이상이 필요하고
///   DirectX 전체화면에서는 검은 화면이 나오기도 한다. 그럴 때 DXGI 화면 복제나
///   BitBlt 로 바꿔 끼울 수 있어야 한다.
///   화면·미리보기·입력 쪽 코드는 이 인터페이스만 알면 되고, 구현이 바뀌어도 손댈 것이 없다.
///
/// 수명
///   Start() 로 시작하고 Stop() 으로 멈춘다. 다 쓰면 Dispose() 한다.
///   Dispose 뒤에는 다시 못 쓴다 — 새 인스턴스를 만들어야 한다.
/// </summary>
public interface IScreenCaptureAdapter : IDisposable
{
    /// <summary>사람이 읽을 이름. 어느 방식으로 잡고 있는지 화면에 보여 주려고 둔다.</summary>
    string Name { get; }

    /// <summary>무엇을 잡고 있는지. 창 캡처가 실패해 모니터로 갈아탔으면 그 결과가 반영된다.</summary>
    CaptureTarget Target { get; }

    bool IsRunning { get; }

    /// <summary>
    /// 캡처 상한(fps). 0 이면 제한하지 않는다.
    /// 상한을 넘는 프레임은 아예 처리하지 않으므로 리드백도 이벤트도 타지 않는다.
    /// 돌아가는 중에 바꿔도 곧바로 반영된다.
    /// </summary>
    int TargetFps { get; set; }

    /// <summary>캡처에 쓰는 D3D11 디바이스. GPU 미리보기가 공유 표면을 만들 때 필요하다.</summary>
    ID3D11Device? Device { get; }

    /// <summary>캡처에 쓰는 D3D11 컨텍스트. 프레임 콜백 안에서만 쓸 것.</summary>
    ID3D11DeviceContext? Context { get; }

    /// <summary>프레임 한 장. 핸들러가 도는 동안에만 유효하다.</summary>
    event EventHandler<CapturedFrameEventArgs>? FrameArrived;

    /// <summary>사용자에게 알릴 만한 일(대상 전환 등).</summary>
    event EventHandler<string>? Notice;

    /// <summary>
    /// 세션이 <b>스스로</b> 멈췄다(대상 창이 닫힘 등). 이유가 실린다. 그 뒤 <see cref="IsRunning"/> 은 false 다.
    /// </summary>
    /// <remarks>
    /// <see cref="Stop"/> 을 부른 쪽에는 안 난다 - 부른 쪽은 이미 안다. 화면이 이것을 받아 제 상태(시작/중지 버튼·스크립트)를 맞춘다(사용자, 2026-09-18
    /// "상대 창이 끊기면 캡처 중지·스크립트 실행도 중지"). 없던 때는 세션만 멈추고 화면은 캡처 중인 줄 알아, 스크립트가 옛 검출로 계속 돌았다.
    /// 어느 스레드에서든 날 수 있다.
    /// </remarks>
    event EventHandler<string>? Ended;

    void Start();

    void Stop();
}
