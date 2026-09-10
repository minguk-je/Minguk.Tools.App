using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using System.Windows;

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
    /// <b>프레임마다 돌리지 않는다.</b> 실측으로 한 장이 230ms 라, 30fps(33ms)에는
    /// 어림도 없다. 대신 0.25초에 한 번이면 몹을 찾아 누르는 데는 넉넉하다 - 몹이 그사이
    /// 크게 움직이지 않고, 어차피 찾은 뒤 행동하는 데 그만큼 걸린다.
    /// </remarks>
    private const int DetectIntervalMs = 250;

    /// <summary>
    /// 추론에 넣기 전에 줄일 크기(긴 변).
    /// </summary>
    /// <remarks>
    /// <b>정확도와는 무관하다.</b> 모델 파이프라인이 제 크기(<see cref="DetectorTrainer.InputWidth"/>)로
    /// 맞추므로 무엇을 넣든 같은 것을 본다 - 실측으로 160x90 부터 1920x1080 까지 다 230ms 에
    /// 같은 것을 찾았다.
    ///
    /// 그래도 줄여 저장하는 것은 <b>PNG 로 만드는 값</b>을 아끼기 위해서다. 0.25초마다
    /// 1920x1080 을 인코딩할 이유가 없다.
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

                // 찾은 것이 바뀌었으니 누르기 버튼 상태도 다시 본다.
                ClickDetectionCommand.RaiseCanExecuteChanged();

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
                // 매 프레임 같은 오류를 쏟지 않는다
                TurnOffDetection($"실패: {ex.Message}");
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
            ClickDetectionCommand.RaiseCanExecuteChanged();

            return;
        }

        var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);
        var modelPath = DetectorTrainer.ModelPathFor(dataset);

        if (!File.Exists(modelPath))
        {
            TurnOffDetection($"학습한 모델이 없습니다. 라벨링 화면에서 먼저 학습하세요 ({DetectorTrainer.ModelFileName}).");

            return;
        }

        if (LibTorchRuntime.Installed is not { } flavor)
        {
            TurnOffDetection("libtorch 가 없습니다. 라벨링 화면에서 학습을 한 번 돌리면 같이 준비됩니다.");

            return;
        }

        // 픽셀이 CPU 로 안 내려오면 추론에 넣을 것이 없다. 도는 중이면 다시 시작까지 해 준다 -
        // 값만 바꾸는 것은 세션을 만들 때만 먹어서, 예전에는 켰다고 적어 놓고 헛것이었다.
        EnsureCpuReadback("추론에는 픽셀이 필요합니다");

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
                    TurnOffDetection($"모델을 못 읽었습니다: {ex.Message}");
                }));
            }
        });
    });

    /// <summary>
    /// 가장 자신 있는 몹을 누른다.
    /// </summary>
    /// <remarks>
    /// <b>찾기만 하면 자동화가 아니다.</b> 찾은 사각형의 가운데를 눌러 준다.
    ///
    /// 누르는 길은 미리보기를 손으로 누를 때와 같은 것을 쓴다(<c>SendClickAsync</c>) -
    /// 두 벌로 두면 한쪽만 고쳐져 손으로는 되는데 자동으로는 안 되는 일이 생긴다.
    ///
    /// <b>입력 전달이 켜져 있어야 한다.</b> 찾는 것은 화면만 보는 일이라 대상에 아무 영향이
    /// 없지만, 누르는 것은 남의 프로그램에 실제로 들어간다. 그것을 켜는 일은 사람이 한 번
    /// 분명히 해야 한다 - 몹 찾기를 켠 것만으로 클릭이 나가면 안 된다.
    /// </remarks>
    private async void DoClickDetection()
    {
        if (!CanForwardInput || _isForwardingClick)
        {
            DetectionStatus = IsInputForwardingEnabled
                ? "지금은 누를 수 없습니다."
                : "누르려면 \"입력 전달\" 을 먼저 켜세요.";

            return;
        }

        if (Detections.Count == 0)
        {
            DetectionStatus = "누를 것이 없습니다. 먼저 찾아야 합니다.";
            return;
        }

        // 목록은 자신 있는 것부터 들어 있다. 맨 앞이 가장 확실한 것이다.
        var target = Detections[0];
        var box = target.Box;

        _isForwardingClick = true;

        try
        {
            var prepared = _inputRouter!.PrepareClickAtRatio(
                new Point(box.CenterX, box.CenterY), out var screenPoint, out var didActivate);

            if (prepared != Capture.Input.InputForwardResult.Sent)
            {
                ReportInputForward(prepared, "몹 클릭");
                return;
            }

            var result = await SendClickAsync(screenPoint, didActivate, Minguk.Tools.Input.MouseButton.Left, "몹 클릭");

            if (result == Capture.Input.InputForwardResult.Sent)
            {
                DetectionStatus = $"{target.Caption} 을(를) 눌렀습니다 " +
                                  $"({screenPoint.X:F0}, {screenPoint.Y:F0})";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "몹을 누르지 못했다");
            DetectionStatus = $"누르지 못했습니다: {ex.Message}";
        }
        finally
        {
            _isForwardingClick = false;
        }
    }

    /// <summary>
    /// 이유를 적고 끈다. <b>순서가 전부다.</b>
    /// </summary>
    /// <remarks>
    /// 메시지를 적은 뒤에 토글을 끄면 안 된다. 끄는 순간 <see cref="OnMobDetectionChanged"/> 가
    /// 다시 돌면서 <see cref="DetectionStatus"/> 를 지우므로, 방금 적은 이유가 사라진다.
    /// 실제로 그랬다 - 모델이 없어 스스로 꺼지는데 화면에는 아무 말도 안 떠서,
    /// 버튼이 아무 일도 안 하는 것처럼 보였다. 먼저 끄고 나서 적는다.
    ///
    /// 이걸 고치고도 안 떴다. 값은 들어 있는데(로그로 확인) 메뉴 바의 정적 항목이 이 문구를
    /// 안 그린다 - 짧은 "2마리 (…ms)" 는 그리면서. 타이밍(디스패처 지연)·캡처 중 여부·
    /// AutoSizeMode=Fill 까지 갈라 봤지만 <b>이유를 못 밝혔다</b>. 그래서 "왜 못 켜는지" 는
    /// 이 화면의 상태 줄(<see cref="StatusText"/>)로도 보낸다. 그 줄은 확실히 그려지고,
    /// 원래 그런 안내가 가는 자리다("요소 검사 중 - …" 도 거기 간다).
    /// 값이 들어 있는지는 로그로 먼저 확인하고 화면을 의심하는 편이 빠르다.
    /// </remarks>
    private void TurnOffDetection(string reason)
    {
        IsMobDetectionOn = false;
        DetectionStatus = reason;
        StatusText = reason;
    }

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
