using System;
using System.Threading;
using System.Threading.Tasks;

namespace Minguk.Tools.Capture.Recording;

/// <summary>
/// 영상에서 일정 간격으로 그림을 뽑아 PNG 로 떨어뜨린다 - 라벨링 화면의 "영상에서 뽑기"(사용자, 2026-09-15).
/// </summary>
/// <remarks>
/// 게임을 켜서 F8 로 한 장씩 담는 대신, 녹화해 둔 영상에서 1초마다 한 장씩 뽑아 데이터셋에 넣는다.
/// 넣은 그림은 라벨이 없으니 라벨링 화면의 "새 그림은 자동으로" 가 자동 라벨을 돌린다 - 학습한 모델이 그 게임을 알 때만 뜻이 있다.
/// </remarks>
public interface IVideoFrameExtractor
{
    /// <summary>어느 길로 푸는지(로그·화면).</summary>
    string Name { get; }

    /// <param name="videoPath">뽑을 영상.</param>
    /// <param name="outputDirectory">PNG 를 둘 폴더(데이터셋의 Images).</param>
    /// <param name="interval">뽑는 간격(영상 시각).</param>
    /// <param name="progress">한 장 처리할 때마다.</param>
    Task<VideoFrameExtractResult> ExtractAsync(string videoPath, string outputDirectory, TimeSpan interval,
                                               IProgress<VideoFrameExtractProgress>? progress, CancellationToken token);
}

/// <param name="Position">영상의 어디까지 왔나.</param>
/// <param name="Duration">영상 길이. 모르면 0.</param>
/// <param name="Saved">지금까지 저장한 장수.</param>
/// <param name="Skipped">앞 장과 거의 같아 건너뛴 장수.</param>
public readonly record struct VideoFrameExtractProgress(TimeSpan Position, TimeSpan Duration, int Saved, int Skipped);

/// <param name="SavedPaths">저장한 PNG(시간 순).</param>
/// <param name="SkippedSimilar">앞에 저장한 장과 거의 같아 건너뛴 장수(멈춘 화면).</param>
/// <param name="SkippedExisting">이미 같은 이름의 파일이 있어 건너뛴 장수(같은 영상을 다시 뽑음).</param>
/// <param name="Duration">영상 길이.</param>
public sealed record VideoFrameExtractResult(System.Collections.Generic.IReadOnlyList<string> SavedPaths, int SkippedSimilar, int SkippedExisting, TimeSpan Duration);
