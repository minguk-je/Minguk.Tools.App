using System;

using Vortice.Direct3D11;

namespace Minguk.Tools.Capture;

/// <summary>
/// 프레임 한 장. <see cref="WgcCaptureSession.FrameArrived"/> 핸들러가 도는 동안에만 유효하다.
///
/// <see cref="Texture"/> 와 <see cref="PixelData"/> 는 핸들러가 끝나면 무효가 된다.
/// 밖으로 들고 나가려면 그 안에서 직접 복사해야 한다. 이렇게 잡아 둔 이유는,
/// 프레임마다 버퍼를 할당하면 초당 수백 장에서 GC 가 파이프라인을 갉아먹기 때문이다.
/// </summary>
public sealed class CapturedFrameEventArgs : EventArgs
{
    /// <summary>시작 이후 몇 번째 프레임인지. 1 부터.</summary>
    public required long FrameId { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>
    /// DWM 이 이 프레임을 만든 시각부터 이 핸들러에 들어오기까지. 이게 실제 캡처 지연이다.
    /// </summary>
    public required double LatencyMs { get; init; }

    /// <summary>GPU→CPU 리드백(스테이징 복사 + Map)에 든 시간. 리드백을 끄면 0.</summary>
    public required double ReadbackMs { get; init; }

    /// <summary>프레임 도착부터 핸들러 호출 직전까지, 이 클래스가 쓴 시간.</summary>
    public required double ProcessMs { get; init; }

    /// <summary>GPU 에 올라와 있는 프레임 원본. 추론을 GPU 에서 돌린다면 이걸 그대로 넘기면 된다.</summary>
    public required ID3D11Texture2D Texture { get; init; }

    /// <summary>
    /// CPU 로 내린 픽셀(BGRA 8888). 리드백을 켰을 때만 <see cref="IntPtr.Zero"/> 가 아니다.
    /// 한 줄의 바이트 수는 <see cref="RowPitch"/> 이고, Width*4 보다 클 수 있다(GPU 정렬).
    /// </summary>
    public IntPtr PixelData { get; init; }

    public int RowPitch { get; init; }

    public bool HasPixels => PixelData != IntPtr.Zero;
}
