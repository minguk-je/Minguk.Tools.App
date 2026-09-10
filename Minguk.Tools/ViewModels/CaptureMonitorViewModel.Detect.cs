using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using Minguk.Tools.Capture;
using Minguk.Tools.Markup;
using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.ViewModels;

public partial class CaptureMonitorViewModel
{
    /// <summary>
    /// 얼마에 한 번 찾을지.
    /// </summary>
    /// <remarks>
    /// <b>프레임마다 돌리지 않는다.</b> 실측으로 320x180 한 장이 220ms 라, 30fps(33ms)에는
    /// 어림도 없다. 대신 0.25초에 한 번이면 몹을 찾아 누르는 데는 넉넉하다 - 몹이 그사이
    /// 크게 움직이지 않고, 어차피 찾은 뒤 행동하는 데 그만큼 걸린다.
    /// </remarks>
    private const int DetectIntervalMs = 250;

    /// <summary>
    /// 추론에 넣기 전에 줄일 크기(긴 변).
    /// </summary>
    /// <remarks>
    /// 검출망이 넣은 그림을 그대로 보므로 값이 픽셀 수에 비례한다. 1920 그대로면 4.7초,
    /// 320 이면 220ms 다. 학습한 크기(320x200) 근처여야 잘 찾는 것도 같이 확인했다 -
    /// 너무 줄이면(160) 놓치기 시작한다.
    /// </remarks>
    private const int DetectLongestSide = 320;

    private DetectorModel? _detector;
    private LabelClasses _detectClasses = new();

    /// <summary>지금 찾는 중인지. 한 번에 하나만 돈다.</summary>
    private int _isDetectRunning;

    private long _lastDetectTicks;
    private string? _detectScratchPath;

    /// <summary>
    /// 프레임이 올 때마다 불린다. 캡처 스레드다.
    /// </summary>
    /// <remarks>
    /// 여기서 추론을 <b>기다리면 안 된다</b>. 220ms 를 잡고 있으면 그동안 프레임이 통째로 밀린다.
    /// 시간이 됐고 앞의 것이 끝났을 때만 백그라운드로 하나 띄운다 - 밀린 것을 쌓지 않는다.
    /// </remarks>
    private void MaybeDetect(CapturedFrameEventArgs e)
    {
        if (!IsMobDetectionOn || _detector is null || !e.HasPixels) return;

        var now = Environment.TickCount64;

        if (now - _lastDetectTicks < DetectIntervalMs) return;

        // 앞의 것이 아직 돌고 있으면 이 프레임은 그냥 흘린다. 큐에 쌓으면 화면이 점점
        // 뒤처진 결과를 보여 주게 된다 - 실시간에서는 늦은 답이 틀린 답이다.
        if (Interlocked.CompareExchange(ref _isDetectRunning, 1, 0) != 0) return;

        _lastDetectTicks = now;

        // 픽셀은 이 콜백이 돌아가면 사라진다. 여기서 바로 파일로 떨어뜨린다(줄여서).
        (int Width, int Height) size;

        try
        {
            if (_detectScratchPath is null)
            {
                _detectScratchPath = Path.Combine(Path.GetTempPath(), $"minguk-detect-{Environment.ProcessId}.png");

                // 앱이 강제로 죽으면 정리가 안 돈다. 실제로 두 개가 남아 있었다.
                // 프로세스마다 이름을 나누는 것은 두 벌이 동시에 돌 때 서로 덮어쓰지
                // 않기 위해서고, 대신 켤 때 옛것을 치운다.
                SweepStaleScratch();
            }

            size = FrameSnapshot.SaveScaledPng(e, _detectScratchPath, DetectLongestSide);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _isDetectRunning, 0);
            Logger.Warn(ex, "프레임을 추론용으로 못 떨어뜨렸다");

            return;
        }

        _ = System.Threading.Tasks.Task.Run(() => RunDetect(size));
    }

    private void RunDetect((int Width, int Height) size)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            var found = _detector!.Detect(_detectScratchPath!, _detectClasses, (float)DetectMinimumScore);

            watch.Stop();

            // 화면에 닿는 것은 UI 스레드에서. 컬렉션을 캡처 스레드에서 고치면 그리는 중에 터진다.
            DispatcherService?.BeginInvoke(() => Guard(() =>
            {
                Detections.Clear();

                foreach (var detection in found)
                    Detections.Add(new PredictedBox(detection.Box, detection.Describe));

                DetectionStatus = found.Count == 0
                    ? $"못 찾음 ({size.Width}x{size.Height}, {watch.ElapsedMilliseconds}ms)"
                    : $"{found.Count}마리 ({size.Width}x{size.Height}, {watch.ElapsedMilliseconds}ms): " +
                      string.Join(", ", found.Take(3).Select(d => d.Describe));
            }));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "몹을 찾지 못했다");

            DispatcherService?.BeginInvoke(() => Guard(() =>
            {
                DetectionStatus = $"실패: {ex.Message}";
                IsMobDetectionOn = false;   // 매 프레임 같은 오류를 쏟지 않는다
            }));
        }
        finally
        {
            Interlocked.Exchange(ref _isDetectRunning, 0);
        }
    }

    /// <summary>
    /// 켜고 끈다. 켤 때 모델을 읽는다.
    /// </summary>
    /// <remarks>
    /// 모델 읽기는 2.9초(68MB)라 UI 스레드에서 하면 화면이 멈춘다. 백그라운드로 보낸다.
    /// </remarks>
    private void OnMobDetectionChanged() => Guard(() =>
    {
        if (!IsMobDetectionOn)
        {
            Detections.Clear();
            DetectionStatus = null;

            return;
        }

        var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);
        var modelPath = DetectorTrainer.ModelPathFor(dataset);

        if (!File.Exists(modelPath))
        {
            DetectionStatus = $"학습한 모델이 없습니다. 라벨링 화면에서 먼저 학습하세요 ({DetectorTrainer.ModelFileName}).";
            IsMobDetectionOn = false;

            return;
        }

        if (LibTorchRuntime.Installed is not { } flavor)
        {
            DetectionStatus = "libtorch 가 없습니다. 라벨링 화면에서 학습을 한 번 돌리면 같이 준비됩니다.";
            IsMobDetectionOn = false;

            return;
        }

        if (!EnableCpuReadback)
        {
            // 픽셀이 CPU 로 안 내려오면 추론에 넣을 것이 없다. 알아서 켜 주고 그렇게 적는다.
            EnableCpuReadback = true;
            DetectionStatus = "CPU 리드백을 켰습니다 - 추론에는 픽셀이 필요합니다.";
        }

        if (_detector is not null)
        {
            DetectionStatus = "찾는 중...";
            return;
        }

        DetectionStatus = "모델을 읽는 중... (처음 한 번, 몇 초 걸립니다)";

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                LibTorchRuntime.Load(flavor);

                var model = DetectorModel.Load(modelPath);

                _detectClasses = dataset.LoadClasses();
                _detector = model;

                DispatcherService?.BeginInvoke(() => DetectionStatus = "찾는 중...");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "검출 모델을 못 읽었다");

                DispatcherService?.BeginInvoke(() => Guard(() =>
                {
                    DetectionStatus = $"모델을 못 읽었습니다: {ex.Message}";
                    IsMobDetectionOn = false;
                }));
            }
        });
    });

    /// <summary>지난번에 죽으면서 남긴 임시 파일을 치운다. 지금 쓰는 것은 건드리지 않는다.</summary>
    private void SweepStaleScratch()
    {
        try
        {
            foreach (var stale in Directory.EnumerateFiles(Path.GetTempPath(), "minguk-detect-*.png"))
            {
                if (string.Equals(stale, _detectScratchPath, StringComparison.OrdinalIgnoreCase)) continue;

                try { File.Delete(stale); }
                catch (IOException) { }   // 다른 벌이 쓰고 있는 것이다. 그쪽이 치운다.
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "옛 임시 파일을 못 치웠다");
        }
    }

    private void ReleaseDetector()
    {
        IsMobDetectionOn = false;

        _detector?.Dispose();
        _detector = null;

        if (_detectScratchPath is not null)
        {
            try
            {
                if (File.Exists(_detectScratchPath)) File.Delete(_detectScratchPath);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "추론용 임시 파일을 못 지웠다");
            }
        }
    }
}
