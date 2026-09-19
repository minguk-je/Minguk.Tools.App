using System;
using System.IO;
using System.Linq;
using System.Threading;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;

using Minguk.Tools.Capture.Recording;
using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 영상에서 뽑기 - 녹화한 영상에서 일정 간격으로 그림을 뽑아 데이터셋에 넣는다(사용자, 2026-09-15).
/// </summary>
/// <remarks>
/// 게임을 켜서 F8 로 한 장씩 담는 대신 녹화 영상 하나로 수백 장을 모은다. 넣은 그림은 라벨이 없으니 "새 그림은 자동으로" 가
/// 자동 라벨을 돌린다 - 다만 학습한 모델이 그 게임을 알 때만 점선이 뜬다. 멈춘 화면이 이어지면 앞 장과 거의 같은 장은 건너뛴다.
/// </remarks>
public partial class LabelingViewModel
{
    private CancellationTokenSource? _extractCts;

    public DelegateCommand DoExtractVideoCommand { get; private set; } = null!;

    public DelegateCommand DoCancelExtractVideoCommand { get; private set; } = null!;

    /// <summary>뽑는 간격 고르기.</summary>
    public string[] ExtractIntervalChoices { get; } = ["0.5초", "1초", "2초", "5초"];

    /// <summary>고른 간격. 저장한다(<c>ExtractInterval</c>), 기본 1초.</summary>
    public string ExtractInterval
    {
        get => GetProperty(() => ExtractInterval);
        set => SetProperty(() => ExtractInterval, value);
    }

    public bool IsExtractingVideo
    {
        get => GetProperty(() => IsExtractingVideo);
        set => SetProperty(() => IsExtractingVideo, value, () =>
        {
            DoExtractVideoCommand.RaiseCanExecuteChanged();
            DoCancelExtractVideoCommand.RaiseCanExecuteChanged();
            DoDeleteImageCommand.RaiseCanExecuteChanged();
            DoResetDatasetCommand.RaiseCanExecuteChanged();
        });
    }

    private static TimeSpan IntervalOf(string? choice) => choice switch
    {
        "0.5초" => TimeSpan.FromSeconds(0.5),
        "2초" => TimeSpan.FromSeconds(2),
        "5초" => TimeSpan.FromSeconds(5),
        _ => TimeSpan.FromSeconds(1)
    };

    private void InitializeVideoCommands()
    {
        DoExtractVideoCommand = new DelegateCommand(DoExtractVideo, () => !IsExtractingVideo, false);
        DoCancelExtractVideoCommand = new DelegateCommand(() => _extractCts?.Cancel(), () => IsExtractingVideo, false);
    }

    /// <summary>영상을 골라 간격마다 뽑아 넣는다. 끝나면 목록을 다시 읽고 첫 새 그림으로 간다.</summary>
    private void DoExtractVideo() => Guard(() =>
    {
        SaveCurrentIfDirty();

        var dataset = new LabelDataset(DatasetRoot ?? LabelDataset.DefaultRoot);
        dataset.EnsureCreated();

        var dialog = OpenFileDialogService;
        dialog.Filter = "영상 (*.mp4)|*.mp4|모든 파일 (*.*)|*.*";

        var recordings = Vision.ProjectPaths.Recordings;
        if (Directory.Exists(recordings)) dialog.InitialDirectory = recordings;

        if (!dialog.ShowDialog()) return;

        var videoPath = dialog.File.GetFullName();
        var interval = IntervalOf(ExtractInterval);
        var extractor = VideoFrameExtractorFactory.Create();

        _extractCts?.Dispose();
        _extractCts = new CancellationTokenSource();
        var token = _extractCts.Token;

        IsExtractingVideo = true;
        StatusText = $"{Path.GetFileName(videoPath)} 에서 {ExtractInterval}마다 뽑는 중...";

        _ = GuardAsync(async () =>
        {
            var name = Path.GetFileName(videoPath);

            var progress = new Progress<VideoFrameExtractProgress>(p =>
            {
                var where = p.Duration > TimeSpan.Zero
                    ? $"{p.Position:mm\\:ss}/{p.Duration:mm\\:ss} ({p.Position.TotalSeconds / p.Duration.TotalSeconds:P0})"
                    : $"{p.Position:mm\\:ss}";

                StatusText = $"{name} 에서 뽑는 중 {where} · {p.Saved}장" + (p.Skipped > 0 ? $" · 같은 장면이라 건너뜀 {p.Skipped}" : string.Empty);
            });

            VideoFrameExtractResult? result = null;
            string? stopped = null;

            try
            {
                result = await extractor.ExtractAsync(videoPath, dataset.ImageDirectory, interval, progress, token);
            }
            catch (OperationCanceledException)
            {
                stopped = "멈췄습니다 - 그때까지 뽑은 그림은 넣었습니다.";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"영상에서 뽑지 못했다: {videoPath}");
                stopped = $"영상에서 뽑지 못했습니다 - 파일이 깨졌거나 이 PC 에서 풀 수 없는 영상입니다({name}, 0x{ex.HResult:X8}).";
            }
            finally
            {
                IsExtractingVideo = false;
            }

            // 새 그림을 목록에 들이고, 첫 새 그림으로 간다 - 라벨이 없으니 "새 그림은 자동으로" 가 자동 라벨을 돌린다.
            var before = Items.Select(item => item.ImagePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

            DoReload();

            var firstNew = Items.FirstOrDefault(item => !before.Contains(item.ImagePath));
            if (firstNew is not null) SelectedItem = firstNew;

            var added = Items.Count(item => !before.Contains(item.ImagePath));

            StatusText = stopped is not null
                ? $"{stopped} (새 그림 {added}장)"
                : $"{name} 에서 {added}장을 넣었습니다" +
                  (result!.SkippedSimilar > 0 ? $" · 앞 장과 거의 같아 건너뜀 {result.SkippedSimilar}" : string.Empty) +
                  (result.SkippedExisting > 0 ? $" · 이미 있어 건너뜀 {result.SkippedExisting}" : string.Empty) +
                  (added > 0 ? " - 라벨 없는 그림이라 자동 라벨이 돕니다(학습한 모델이 있을 때)." : ".");

            MessengerUtility.SendMainMessage(StatusText);
        });
    });
}
