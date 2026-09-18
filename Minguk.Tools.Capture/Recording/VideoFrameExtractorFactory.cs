namespace Minguk.Tools.Capture.Recording;

/// <summary>영상에서 그림 뽑기를 고르는 곳. 다른 길(FFmpeg 등)을 붙일 때는 여기만 고친다.</summary>
public static class VideoFrameExtractorFactory
{
    public static IVideoFrameExtractor Create() => new MediaFoundationVideoFrameExtractor();
}
