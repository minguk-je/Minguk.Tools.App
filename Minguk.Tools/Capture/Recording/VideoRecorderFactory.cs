using System;

namespace Minguk.Tools.Capture.Recording;

/// <summary>녹화기를 고르는 곳. 새 구현(다른 코덱·GPU 인코딩)을 넣을 때는 여기만 고친다.</summary>
public static class VideoRecorderFactory
{
    /// <param name="filePath">쓸 mp4 경로.</param>
    /// <param name="frameRate">저장할 초당 프레임(fps 콤보 값).</param>
    public static IVideoRecorder Create(string filePath, int frameRate)
        => new MediaFoundationVideoRecorder(filePath, frameRate);
}
