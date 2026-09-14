using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;

using Minguk.Tools.Capture;
using Minguk.Tools.Capture.Recording;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 녹화 - 잡고 있는 화면을 mp4 로 쓴다(사용자, 2026-09-14 "계속 게임 켜야 하니까 불편하네").
/// </summary>
/// <remarks>
/// 한 녹화 = 한 파일(조각 mp4) - 쓰는 도중에도 1초 안쪽마다 디스크에 확정돼 앱이 죽어도 그때까지는 튼다(<see cref="MediaFoundationVideoRecorder"/>).
/// 영상은 다른 플레이어로 본다 - 여기서 틀지는 않는다. 자리는 고른 프로젝트의 <c>recordings\</c>(<see cref="Vision.ProjectPaths.Recordings"/>).
/// 녹화에는 CPU 로 내린 픽셀이 필요해 리드백을 알아서 켠다(<see cref="CaptureViewModelBase.EnsureCpuReadback"/>) - 켜는 동안 세션이
/// 다시 시작되므로 녹화기는 그 뒤에 만든다(먼저 만들면 캡처 멈춤 알림에 곧바로 닫힌다).
/// </remarks>
public partial class CaptureMonitorViewModel
{
    private IVideoRecorder? _recorder;
    private DelegateCommand? _openRecordingsFolderCommand;

    /// <summary>녹화 중인가. 도구 줄의 녹화 토글이 묶인다.</summary>
    public bool IsRecording
    {
        get => GetProperty(() => IsRecording);
        set => SetProperty(() => IsRecording, value, OnIsRecordingChanged);
    }

    /// <summary>녹화 줄 - "녹화 중 00:12 · 360장" / 마지막 파일.</summary>
    public string? RecordingStatus
    {
        get => GetProperty(() => RecordingStatus);
        set => SetProperty(() => RecordingStatus, value);
    }

    /// <summary>녹화 폴더를 탐색기로 연다. 없으면 만든다.</summary>
    public DelegateCommand OpenRecordingsFolderCommand => _openRecordingsFolderCommand ??= new DelegateCommand(() => Guard(() =>
    {
        var folder = Vision.ProjectPaths.Recordings;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }), false);

    /// <summary>토글이 바뀌었다. 켜면 시작, 끄면 파일을 닫는다.</summary>
    private bool _isChangingRecording;

    private void OnIsRecordingChanged() => Guard(() =>
    {
        if (_isChangingRecording) return;

        if (IsRecording) StartRecording();
        else StopRecording();
    });

    private void StartRecording()
    {
        if (_recorder is not null) return;

        if (!IsRunning || SelectedTarget is null)
        {
            SetRecordingToggle(false);
            RecordingStatus = "캡처를 먼저 시작하세요.";
            MessengerUtility.SendMainMessage("녹화하려면 캡처를 먼저 시작하세요.");
            return;
        }

        // 리드백을 켜면 세션이 다시 시작된다 - 녹화기는 그 뒤에.
        EnsureCpuReadback("녹화하려면 픽셀이 필요합니다");

        var path = Path.Combine(Vision.ProjectPaths.Recordings, $"{DateTime.Now:yyyyMMdd-HHmmss}-{SafeName(SelectedTarget.Title)}.mp4");

        _recorder = VideoRecorderFactory.Create(path, Math.Max(1, CaptureTargetFps));

        RecordingStatus = $"녹화 중 - {Path.GetFileNameWithoutExtension(path)}";
        MessengerUtility.SendMainMessage($"녹화를 시작했습니다 - 1초 안쪽마다 디스크에 확정하며 씁니다({path}).");
    }

    /// <summary>파일을 닫는다. Finalize 가 몇 초 걸릴 수 있어 백그라운드에서 하고 끝나면 알린다.</summary>
    private void StopRecording(string? reason = null)
    {
        var recorder = _recorder;
        if (recorder is null) return;

        _recorder = null;
        SetRecordingToggle(false);
        RecordingStatus = "녹화 파일을 닫는 중...";

        _ = Task.Run(() =>
        {
            recorder.Dispose();

            var size = File.Exists(recorder.FilePath) ? new FileInfo(recorder.FilePath).Length / 1024.0 / 1024.0 : 0;
            var names = Path.GetFileName(recorder.FilePath);

            var done = recorder.FramesWritten == 0
                ? $"녹화된 프레임이 없어 파일을 만들지 않았습니다{(recorder.Error is null ? "." : $": {DescribeRecordingError(recorder.Error)}")}"
                : $"녹화 저장 - {names} · {FormatDuration(recorder.Duration)} · {recorder.FramesWritten}장" +
                  (recorder.FramesDropped > 0 ? $"(버림 {recorder.FramesDropped})" : string.Empty) +
                  $" · {size:0.#} MB" +
                  (recorder.Error is null ? string.Empty : $" · 도중 오류: {DescribeRecordingError(recorder.Error)}");

            if (reason is not null) done = $"{reason} {done}";

            DispatcherService?.BeginInvoke(() =>
            {
                RecordingStatus = done;
                MessengerUtility.SendMainMessage(done);
            });
        });
    }

    /// <summary>토글만 되돌린다(시작·끝을 다시 부르지 않고).</summary>
    private void SetRecordingToggle(bool value)
    {
        _isChangingRecording = true;
        try { IsRecording = value; }
        finally { _isChangingRecording = false; }
    }

    /// <summary>캡처 스레드. 픽셀을 복사해 넘기고 곧바로 돌아온다.</summary>
    protected override void OnFramePixels(CapturedFrameEventArgs e)
    {
        base.OnFramePixels(e);

        var recorder = _recorder;
        if (recorder is null || !e.HasPixels) return;

        recorder.TryAddFrame(e.PixelData, e.RowPitch, e.Width, e.Height, Stopwatch.GetTimestamp());

        // 크기가 바뀌었거나 쓰기가 터졌으면 더 받지 않고 닫는다.
        if (recorder.Error is { } error)
            DispatcherService?.BeginInvoke(() => Guard(() => { if (ReferenceEquals(_recorder, recorder)) StopRecording($"녹화를 멈췄습니다({DescribeRecordingError(error)})."); }));
    }

    /// <summary>캡처가 멈추면 녹화도 닫는다 - 리드백을 켜느라 잠깐 멈추는 것은 녹화기를 만들기 전이라 해당 없다.</summary>
    protected override void OnRunningStateChanged()
    {
        base.OnRunningStateChanged();

        if (!IsRunning && _recorder is not null) StopRecording("캡처가 멈춰");
    }

    /// <summary>1초 요약 줄에 녹화 상태를 붙인다.</summary>
    private void AppendRecordingStatus()
    {
        if (_recorder is not { } recorder) return;

        RecordingStatus = $"녹화 중 {FormatDuration(recorder.Duration)} · {recorder.FramesWritten}장" + (recorder.FramesDropped > 0 ? $" · 버림 {recorder.FramesDropped}" : string.Empty);
        StatusText = $"{StatusText} · {RecordingStatus}";
    }

    /// <summary>화면을 닫을 때 - 파일을 끝까지 쓰고 닫는다(캡처 멈춤 알림이 한 번 더 닫지 않게 먼저 비운다).</summary>
    private void ReleaseRecording()
    {
        var recorder = _recorder;
        _recorder = null;
        recorder?.Dispose();
    }

    /// <summary>
    /// 녹화 오류를 화면에 보일 한국어로. 우리가 만든 오류(한국어)는 그대로, Media Foundation·입출력의 영어 문장은 무엇이 문제인지 한국어로 바꾸고 코드만 붙인다(원문은 로그).
    /// </summary>
    private static string DescribeRecordingError(Exception error)
    {
        if (error.Message.Any(c => c is >= '가' and <= '힣')) return error.Message;

        var code = $"0x{error.HResult:X8}";

        return error switch
        {
            IOException or UnauthorizedAccessException => $"파일을 쓸 수 없습니다 - 디스크가 가득 찼거나 폴더에 쓸 권한이 없습니다({code})",
            _ when (uint)error.HResult is 0xC00D5212 or 0xC00D36B4 or 0xC00D36B2 => $"이 PC 의 H.264 인코더가 이 크기·형식을 받지 않습니다({code})",
            _ => $"녹화 인코더(Media Foundation) 오류입니다 - 자세한 내용은 로그를 보세요({code})"
        };
    }

    /// <summary>녹화 길이 "12:34" - 한 시간이 넘어도 분으로 센다("75:02"). TimeSpan 의 mm 은 시간 안의 분이라 넘으면 0 으로 돌아간다.</summary>
    private static string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";

    private static string SafeName(string? title)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "화면" : title.Trim();
        name = new string([.. name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)]);
        return name.Length > 40 ? name[..40] : name;
    }
}
