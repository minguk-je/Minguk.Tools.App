using System;

namespace Minguk.Tools.Capture.Recording;

/// <summary>
/// 캡처 프레임을 동영상 파일로 쓴다. 화면(ViewModel)은 이것만 안다 - 구현(Media Foundation)이 바뀌어도 부르는 쪽은 그대로다.
/// </summary>
/// <remarks>
/// <b>왜 녹화하나</b>(사용자, 2026-09-14) - 매번 게임을 켜서 확인하기 불편하다. 녹화해 두면 영상은 다른 플레이어로 본다.
/// <see cref="TryAddFrame"/> 는 <b>캡처 스레드</b>에서 불린다 - 기다리면 안 된다. 픽셀을 복사해 쓰기 스레드에 넘기고 곧바로 돌아온다.
/// 쓰기가 밀리면 그 프레임은 버리고 <see cref="FramesDropped"/> 를 센다(캡처를 막지 않는다).
/// </remarks>
public interface IVideoRecorder : IDisposable
{
    /// <summary>어느 길로 쓰는지(로그·화면).</summary>
    string Name { get; }

    /// <summary>쓰는 파일. 녹화 하나 = 파일 하나. 쓰는 도중에도 디스크에 확정해 가며 써서, 앱이 죽어도 그때까지는 튼다.</summary>
    string FilePath { get; }

    int FramesWritten { get; }

    int FramesDropped { get; }

    /// <summary>첫 프레임부터 마지막 프레임까지.</summary>
    TimeSpan Duration { get; }

    /// <summary>쓰다가 난 오류. 나면 더 받지 않는다.</summary>
    Exception? Error { get; }

    /// <summary>
    /// 프레임 한 장(BGRA 8888, 위에서 아래로). 캡처 스레드에서 부르고 곧바로 돌아온다. 받았으면 true, 버렸거나 끝났으면 false.
    /// </summary>
    /// <param name="timestamp"><see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> 값 - 영상의 시각이 된다(프레임이 고르지 않아도 제 시각에 나온다).</param>
    bool TryAddFrame(IntPtr pixels, int rowPitch, int width, int height, long timestamp);

    /// <summary>받아 둔 것을 다 쓰고 파일을 닫는다. 몇 초 걸릴 수 있어 UI 스레드에서 부르지 않는다.</summary>
    void Finish();
}
