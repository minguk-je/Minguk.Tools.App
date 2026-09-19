using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using System.Windows;

using Minguk.Base.Utilities;
using Minguk.Tools.Capture;
using Minguk.Tools.Markup;
using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.ViewModels;

public abstract partial class RecognizingCaptureViewModelBase
{
    /// <summary>
    /// 얼마에 한 번 찾을지.
    /// </summary>
    /// <remarks>
    /// <b>프레임마다 돌리지 않는다.</b> 앞의 것이 아직 돌고 있으면 그 프레임은 흘린다 - 큐에 쌓으면
    /// 화면이 점점 뒤처진 답을 보여 준다(실시간에서는 늦은 답이 틀린 답이다).
    ///
    /// <b>250ms 였다.</b> 한 장에 230ms 걸리던 시절(libtorch AutoFormerV2)에 맞춘 값이다. ONNX 로 바꾸고
    /// 32ms 가 되면서 그 값이 조준을 붙잡는 쪽이 됐다 - 조준은 새 화면을 기다렸다 다시 겨누므로,
    /// 주기가 곧 "한 번 더 겨누기까지" 다. 실측 로그에서 조준이 0.5초에 한 번씩 세 번 걸려 붙었다.
    ///
    /// 그다음 80ms 였다(캡처가 10fps 이던 때 - 그 아래로는 같은 프레임을 다시 본다).
    ///
    /// <b>이제 45ms 다</b>(사용자, 2026-09-18 "프로게이머처럼 움직여줘"). 조준 스레드는 새 화면이 올 때마다 자리를 고치고, 크게 꺾은 뒤에는 <b>꺾은 결과가 담긴 화면을 보고서야</b>
    /// 마지막을 다듬는다 - 80ms(실제로는 프레임 박자에 걸려 0.1초)면 그 확인을 최대 0.1초 더 기다린다. 45ms 면 30fps 캡처에서 두 장에 한 번(66ms), 60fps 에서 세 장에 한 번(50ms).
    /// 추론은 게임과 같이 돌 때 중간값 14ms·90% 27ms(실측 로그)라 밀리지 않는다. 밀리면 어차피 그 프레임은 흘린다 - 느린 모델(Torch)로 되돌려도 안전하다.
    /// GPU 몫은 10Hz 의 14% 에서 15~20Hz 의 25% 안팎으로 는다 - 게임 fps 가 떨어지면 이 값을 올린다.
    /// </remarks>
    private const int DetectIntervalMs = 45;

    /// <summary>
    /// 추론에 넣기 전에 줄일 크기(긴 변).
    /// </summary>
    /// <remarks>
    /// 모델 파이프라인이 제 크기로 다시 맞추므로, <b>모델 크기보다 크게만 넣으면</b> 무엇을 넣든
    /// 같은 것을 본다 - 실측으로 320 모델에 160x90 부터 1920x1080 까지 다 230ms 에 같은 것을
    /// 찾았다. 줄여 저장하는 것은 PNG 로 만드는 값을 아끼기 위해서다. 0.25초마다 1920x1080 을
    /// 인코딩할 이유가 없다.
    ///
    /// <b>모델 크기보다 작게 줄이면 정확도가 떨어진다.</b> 640x360 모델에 320 을 넣으면 도로
    /// 키워 보는 꼴이다. 그래서 이 값은 바닥이고, 실제로는 모델의 InputWidth 와 큰 쪽을 쓴다.
    /// </remarks>
    private const int DetectLongestSide = 320;

    private IDetector? _detector;

    /// <summary>
    /// 모델이 바뀔 때마다(읽기·내려놓기) 하나씩 는다. 도는 검출이 시작할 때 이것을 적어 두었다가, 끝났을 때 달라졌으면 그 사이 오류는 고장이 아니다 -
    /// 모델을 바꾸는 도중에 옛 모델로 돌던 한 번이 닫힌 모델에 부딪힌 것이다(사용자, 2026-09-19 - 모델 없는 화면으로 넘어가는 순간 이것으로 검출이 꺼졌다).
    /// </summary>
    private int _detectorGeneration;

    /// <summary>GPU 에서 전처리하는 길(텐서 검출기일 때만). 캡처 장치가 바뀌면 새로 만든다.</summary>
    private Minguk.Tools.Inference.FramePreprocessor? _preprocessor;
    private LabelClasses _detectClasses = new();

    /// <summary>프레임 간 추적. <see cref="RunDetect"/> 한 곳에서만 만진다(한 번에 하나만 돈다).</summary>
    private readonly DetectionTracker _tracker = new();

    /// <summary>
    /// 읽어 둔 모델 파일의 시각. 다시 학습하면 파일이 바뀌므로 이것으로 안다.
    /// </summary>
    /// <remarks>
    /// 이게 없으면 처음 켤 때 읽은 모델을 화면을 닫을 때까지 든다. 라벨링에서 다시 학습하고
    /// 검출을 껐다 켜도 옛 모델로 찾는다 - "닫았다 열어야 하나" 가 그 말이었다.
    /// </remarks>
    private DateTime _detectorStamp;

    /// <summary>읽어 둔 모델 파일의 자리. 시각만 보면 안 된다 - 복사한 모델은 수정 시각이 원본과 같아서, 다른 폴더의 모델로 바뀌어도 모른다.</summary>
    private string? _detectorPath;

    /// <summary>
    /// 검출 모델·검출 이름·이름 붙인 자리를 읽을 폴더. 기본은 Automation Builder 에서 고른 프로젝트다.
    /// </summary>
    /// <remarks>
    /// 플레이는 고른 완성품(<c>Player\솔루션\프로젝트\*.mtsx</c>)의 폴더에서 읽게 바꾼다 - 빌드가 모델·영역을 그 옆에 같이 복사하므로
    /// Player 폴더만 다른 PC 로 옮겨도 돈다(사용자 결정 2026-09-14).
    /// </remarks>
    protected virtual string RecognitionRoot => SwitchedRecognitionRoot ?? LabelDataset.ConfiguredRoot;

    /// <summary>
    /// 스크립트의 <c>프로젝트실행()</c> 이 다른 프로젝트를 이어서 돌리는 동안 이 폴더를 <see cref="RecognitionRoot"/> 대신 쓴다.
    /// null 이면 평소대로(Automation Builder 에서 고른 프로젝트 · 플레이는 고른 완성품).
    /// </summary>
    protected string? SwitchedRecognitionRoot { get; set; }

    /// <summary>지금 찾는 중인지. 한 번에 하나만 돈다.</summary>
    private int _isDetectRunning;

    private long _lastDetectTicks;
    private string? _detectScratchPath;

    /// <summary>
    /// 마지막으로 찾은 것. 담을 때 라벨로 같이 쓴다.
    /// </summary>
    /// <remarks>
    /// 화면의 <see cref="Detections"/> 는 UI 스레드 것이라 캡처 스레드에서 못 읽는다.
    /// 찾은 순간의 목록을 그대로 들고 있다가 담기가 가져간다. 언제 찾은 것인지도 같이 -
    /// 몇 초 전 것을 지금 프레임에 붙이면 검출이 이미 다른 자리에 있다.
    /// </remarks>
    private volatile IReadOnlyList<Detection>? _latestDetections;
    private long _latestDetectionTicks;

    /// <summary>이보다 오래된 검출은 담을 때 안 붙인다. 0.25초에 한 번 찾으니 이 안이면 방금 것이다.</summary>
    private const int DetectionFreshMs = 1000;

    /// <summary>
    /// 방금 찾은 것. 없거나 오래됐으면 빈 목록.
    /// </summary>
    internal IReadOnlyList<Detection> FreshDetections
        => IsDetectionOn
           && _latestDetections is { } found
           && Environment.TickCount64 - Interlocked.Read(ref _latestDetectionTicks) <= DetectionFreshMs
            ? found
            : [];

    /// <summary>
    /// 프레임이 올 때마다 불린다. 캡처 스레드다.
    /// </summary>
    /// <remarks>
    /// 여기서 추론을 <b>기다리면 안 된다</b>. 220ms 를 잡고 있으면 그동안 프레임이 통째로 밀린다.
    /// 시간이 됐고 앞의 것이 끝났을 때만 백그라운드로 하나 띄운다 - 밀린 것을 쌓지 않는다.
    /// </remarks>
    private void MaybeDetect(CapturedFrameEventArgs e)
    {
        if (!IsDetectionOn) return;

        // 켜 둔 채 다시 학습했으면 새 모델을 읽는다. 읽는 동안 _detector 는 null 이라 아래서 걸러진다.
        MaybeReloadDetector();

        // 세대는 모델을 잡는 이 순간에 적는다 - 작업이 뜨기 전에 모델이 바뀌어도 그 오류를 가려낸다(RunDetectCore).
        var generation = Volatile.Read(ref _detectorGeneration);

        if (_detector is null) return;

        // 텐서를 받을 수 있는 검출기(ONNX)면 GPU 에 있는 프레임을 그대로 쓴다. 아니면 옛 길(PNG)이라 픽셀이 있어야 한다.
        var tensorDetector = _detector as ITensorDetector;
        var device = tensorDetector is null ? null : CaptureDevice;

        if (device is null && !e.HasPixels) return;

        var now = Environment.TickCount64;

        if (now - _lastDetectTicks < DetectIntervalMs) return;

        // 앞의 것이 아직 돌고 있으면 이 프레임은 그냥 흘린다. 큐에 쌓으면 화면이 점점
        // 뒤처진 결과를 보여 주게 된다 - 실시간에서는 늦은 답이 틀린 답이다.
        if (Interlocked.CompareExchange(ref _isDetectRunning, 1, 0) != 0) return;

        // 도는 것이 없음을 잡은 뒤에 적는다 - 앞의 것이 도는 중에 덮으면 그 오류를 잘못 가린다.
        Volatile.Write(ref _runningGeneration, generation);

        _lastDetectTicks = now;

        // 이 프레임이 들어온 시각. 검출 결과에 실어 스크립트의 조준이 "겨눈 뒤의 화면인가" 를 가린다.
        var frameTicks = now;

        if (tensorDetector is not null && device is { } gpu)
        {
            DetectFromTexture(tensorDetector, gpu, e, frameTicks);

            return;
        }

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

            // 모델이 보는 크기보다 작게 줄이면 안 된다. 640x360 모델에 320 으로 줄인 그림을 넣으면
            // 파이프라인이 도로 키워서 보는 꼴이라, 작은 검출 때문에 640 으로 올린 뜻이 실시간에서
            // 사라진다 - 실제로 재현율 검사(원본 파일)는 97% 인데 실시간은 320 을 넣고 있었다.
            var longestSide = Math.Max(DetectLongestSide, _detector?.Manifest.InputWidth ?? DetectLongestSide);

            size = FrameSnapshot.SaveScaledPng(e, _detectScratchPath, longestSide);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _isDetectRunning, 0);
            Logger.Warn(ex, "프레임을 추론용으로 못 떨어뜨렸다");

            return;
        }

        _ = System.Threading.Tasks.Task.Run(() => RunDetect(size, frameTicks));
    }

    /// <summary>
    /// GPU 에 있는 프레임을 셰이더로 바로 텐서에 넣고 찾는다. 디스크도, CPU 리드백도 안 거친다.
    /// </summary>
    /// <remarks>
    /// 옛 길은 프레임을 CPU 로 내리고(리드백) PNG 로 쓰고 다시 읽었다. 2560x1440 한 장에 그 값이 수 ms 고
    /// 캡처 fps 를 깎는다. 여기서는 캡처가 쓰는 바로 그 D3D 장치에서 줄이기·여백·정규화를 한 번에 한다.
    ///
    /// 텐서는 <b>복사해서</b> 넘긴다 - 전처리기의 버퍼는 다음 프레임에 덮이는데 추론은 다른 스레드에서 도는 중이다.
    /// </remarks>
    private void DetectFromTexture(ITensorDetector detector,
                                   (Vortice.Direct3D11.ID3D11Device Device, Vortice.Direct3D11.ID3D11DeviceContext Context) gpu,
                                   CapturedFrameEventArgs e,
                                   long frameTicks)
    {
        try
        {
            var spec = detector.InputSpec;

            // 크기·넣는 방식·장치 중 하나라도 달라졌으면 버린다. 무엇을 봐야 하는지는 전처리기가 안다.
            if (_preprocessor is not null && !_preprocessor.Matches(gpu.Device, spec))
            {
                _preprocessor.Dispose();
                _preprocessor = null;
            }

            // 파이프라인(한 프레임 늦게 읽기)은 끈다. 여기서는 0.25초에 한 번만 돌아 지난 프레임이 이미 사라졌다.
            _preprocessor ??= new Minguk.Tools.Inference.FramePreprocessor(gpu.Device, gpu.Context, spec, pipelined: false);

            if (!_preprocessor.Process(e.Texture, e.Width, e.Height))
            {
                Interlocked.Exchange(ref _isDetectRunning, 0);

                return;
            }

            var tensor = _preprocessor.Tensor.ToArray();
            var map = Minguk.Tools.Vision.Inference.Onnx.LetterboxMap.For(e.Width, e.Height, spec.Width, spec.Height, spec.Letterbox);
            var size = (e.Width, e.Height);

            _ = System.Threading.Tasks.Task.Run(() => RunDetectCore(
                () => detector.Detect(tensor, map, _detectClasses, (float)DetectMinimumScore), size, frameTicks));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _isDetectRunning, 0);
            ReportPreprocessFailure(ex);
        }
    }

    private string? _lastPreprocessFailure;
    private int _preprocessFailureCount;

    /// <summary>
    /// GPU 전처리가 터진 것을 알린다. <b>같은 것이 이어지면 한 번만 자세히 적는다.</b>
    /// </summary>
    /// <remarks>
    /// 이 길은 0.25초에 한 번 도는데, 원인이 고쳐지기 전까지는 매번 같은 이유로 터진다. 그대로 적으면
    /// 스택까지 붙은 줄이 분당 수백 개 쌓여 로그에서 다른 것을 못 찾는다(실측: 대상을 게임 창으로 바꾸자
    /// 같은 NullReferenceException 이 끝까지 도배됐다). 처음 한 번만 자세히, 그 뒤는 세어서 적는다.
    /// </remarks>
    private void ReportPreprocessFailure(Exception ex)
    {
        var signature = ex.GetType().FullName + "|" + ex.Message;

        if (signature == _lastPreprocessFailure)
        {
            _preprocessFailureCount++;

            // 10 · 100 · 1000 ... 번째에만 한 줄. 고쳐지지 않고 있다는 것은 알려야 한다.
            if (_preprocessFailureCount % 100 == 0)
                Logger.Warn($"GPU 전처리가 {_preprocessFailureCount}번째 같은 이유로 실패하고 있다: {ex.Message}");

            return;
        }

        _lastPreprocessFailure = signature;
        _preprocessFailureCount = 1;

        Logger.Warn(ex, "GPU 전처리에 실패했다");
    }

    private void RunDetect((int Width, int Height) size, long frameTicks)
        => RunDetectCore(() => _detector!.Detect(_detectScratchPath!, _detectClasses, (float)DetectMinimumScore), size, frameTicks);

    /// <summary>찾은 뒤의 일은 두 길이 같다 - 추적·이름표·허브·화면.</summary>
    /// <summary>지금 도는 검출이 잡은 모델의 세대(<see cref="_detectorGeneration"/>). 검출은 한 번에 하나라(_isDetectRunning) 칸 하나면 된다.</summary>
    private int _runningGeneration;

    private void RunDetectCore(Func<IReadOnlyList<Detection>> detect, (int Width, int Height) size, long frameTicks)
    {
        var generation = Volatile.Read(ref _runningGeneration);

        try
        {
            var watch = Stopwatch.StartNew();
            var raw = detect();

            watch.Stop();

            // 추적이 켜져 있으면 두 번 연속 보인 것만 남기고 잠깐 놓친 것은 이어 준다.
            // 누르기·자동 라벨도 이 결과를 쓴다 - 화면에 보이는 것과 누르는 것이 달라선 안 된다.
            var found = IsTrackingOn ? _tracker.Update(raw) : raw;

            // 도는 사이에 검출을 껐다 - 결과를 버린다. 안 그러면 끄면서 비운 사각형이 이 결과로 다시 그려져 남았다(사용자, 2026-09-18).
            if (!IsDetectionOn) return;

            _latestDetections = found;
            Interlocked.Exchange(ref _latestDetectionTicks, Environment.TickCount64);

            // 머리 위 이름표는 검출마다 읽지 않는다 - 게임마다 없기도 해 스크립트가 검출.이름표 를 부를 때 읽는다(사용자, 2026-09-17).
            var names = new string[found.Count];

            // 스크립트가 읽어 가는 자리. 화면(Detections)은 UI 스레드 것이라 스크립트가 못 읽는다.
            Hub.PublishDetections(found, names, size.Width, size.Height, frameTicks, raw);

            // 화면에 닿는 것은 UI 스레드에서. 컬렉션을 캡처 스레드에서 고치면 그리는 중에 터진다.
            DispatcherService?.BeginInvoke(() => Guard(() =>
            {
                Detections.Clear();

                // 이 줄이 UI 스레드에 닿기 전에 껐을 수도 있다.
                if (!IsDetectionOn) return;

                for (var i = 0; i < found.Count; i++)
                {
                    var caption = string.IsNullOrEmpty(names[i]) ? found[i].Describe : $"{found[i].Describe} 「{names[i]}」";
                    Detections.Add(new PredictedBox(found[i].Box, caption));
                }

                // 찾은 것이 바뀌었으니 누르기 버튼 상태도 다시 본다.
                ClickDetectionCommand.RaiseCanExecuteChanged();

                var how = IsTrackingOn ? "추적, " : string.Empty;

                // 걸린 시간은 화면 설정 줄에만 있었다. 다른 PC 것을 견주려면 사람이 화면을 찍어 보내야 했다 - 로그에도 남긴다.
                Logger.Debug($"검출: {found.Count}마리 · {size.Width}x{size.Height} · {watch.ElapsedMilliseconds}ms");

                DetectionStatus = found.Count == 0
                    ? $"못 찾음 ({how}{size.Width}x{size.Height}, {watch.ElapsedMilliseconds}ms)"
                    : $"{found.Count}마리 ({how}{size.Width}x{size.Height}, {watch.ElapsedMilliseconds}ms): " +
                      string.Join(", ", found.Take(3).Select((d, i) => string.IsNullOrEmpty(names[i]) ? d.Describe : $"{d.Describe} 「{names[i]}」"));
            }));
        }
        catch (Exception ex) when (Volatile.Read(ref _detectorGeneration) != generation)
        {
            // 도는 사이에 모델이 바뀌었다(다른 프로젝트로 넘어감·다시 학습) - 옛 모델의 마지막 한 번이다. 끄지 않는다.
            Logger.Debug(ex, "모델이 바뀌는 사이의 검출 한 번이 실패했다 - 무시한다");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "검출을 찾지 못했다");

            DispatcherService?.BeginInvoke(() => Guard(() =>
            {
                // 매 프레임 같은 오류를 쏟지 않는다. GPU 리셋이면 무엇을 해야 하는지(앱 다시 켜기)를 말한다.
                TurnOffDetection(Vision.Training.TrainingActivity.IsGpuLost(ex) ? Vision.Training.TrainingActivity.GpuLostMessage : $"실패: {ex.Message}");
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
    private void OnDetectionChanged() => Guard(() =>
    {
        PublishPerceptionState();

        if (!IsDetectionOn)
        {
            // 화면 사각형과 함께 마지막 결과도 버린다(허브 것은 PublishPerceptionState 가 버린다).
            _latestDetections = null;
            Detections.Clear();
            DetectionStatus = null;
            ClickDetectionCommand.RaiseCanExecuteChanged();
            _tracker.Reset();

            return;
        }

        // 학습 중에 켰으면 모델은 끝난 뒤에 읽는다 - 같은 카드에서 추론과 학습이 겹치면 GPU 가 리셋된다(실측).
        if (Vision.Training.TrainingActivity.IsBusy)
        {
            _pausedForTraining = true;
            DetectionStatus = "학습 중이라 끝나면 검출을 시작합니다.";
            StatusText = DetectionStatus;
            return;
        }

        ReloadDetectorForCurrentRoot();
    });

    /// <summary>
    /// 지금 <see cref="RecognitionRoot"/> 기준으로 모델을 읽는다(또는 다시 읽는다). <see cref="OnDetectionChanged"/> 가
    /// 켤 때 부르고, <see cref="SwitchProjectContextAsync"/> 가 다른 프로젝트로 넘어갈 때도 부른다.
    /// </summary>
    private void ReloadDetectorForCurrentRoot()
    {
        var dataset = new LabelDataset(RecognitionRoot);
        var modelPath = DetectorFiles.CurrentFor(dataset);

        if (!File.Exists(modelPath))
        {
            // 켜 둔 채 쉰다(사용자, 2026-09-19 "화면 이동하니까 검출 버튼이 꺼지는데 그냥 켜있게") - 모델 없는 화면(영웅선택 등)으로 넘어갈 때마다
            // 꺼져서 모델 있는 화면으로 돌아와도 다시 켜야 했다. 옛 프로젝트 모델·결과는 버린다(옛 몹을 지금 것으로 주면 안 된다).
            // 모델 있는 프로젝트로 넘어가면 SwitchProjectContextAsync 가, 여기서 학습해 모델이 생기면 MaybeReloadDetector 가 읽는다.
            RestWithoutModel($"이 프로젝트에는 학습한 모델이 없어 쉬는 중입니다 - 검출은 켜 둡니다({DetectorTrainer.ModelFileName} 이 생기면 알아서 읽습니다).");

            return;
        }

        // ONNX 모델은 libtorch 도, CPU 리드백도 필요 없다 - GPU 에 있는 프레임을 셰이더로 바로 쓴다.
        var engine = DetectorManifest.Load(modelPath).Engine;
        LibTorchFlavor? flavor = null;

        if (engine == DetectorEngine.Torch)
        {
            if (LibTorchRuntime.Installed is not { } installed)
            {
                TurnOffDetection("libtorch 가 없습니다. 라벨링 화면에서 학습을 한 번 돌리면 같이 준비됩니다.");

                return;
            }

            flavor = installed;

            // 픽셀이 CPU 로 안 내려오면 추론에 넣을 것이 없다. 도는 중이면 다시 시작까지 해 준다 -
            // 값만 바꾸는 것은 세션을 만들 때만 먹어서, 예전에는 켰다고 적어 놓고 헛것이었다.
            EnsureCpuReadback("추론에는 픽셀이 필요합니다");
        }

        LoadDetector(modelPath, dataset, flavor);
    }

    /// <summary>
    /// 시작 프로젝트가 바뀌었다 - 이름 붙인 자리·검출 모델·설정 창을 새 프로젝트로. <c>프로젝트실행()</c> 으로 넘어갔던 자리도 푼다
    /// (안 풀면 그 자리가 계속 이겨 콤보를 바꿔도 옛 모델을 본다).
    /// </summary>
    public override void FollowProject()
    {
        base.FollowProject();

        if (!IsInitialized) return;

        SwitchedRecognitionRoot = null;
        LoadRegions();
        RefreshSavedTemplate();

        if (IsDetectionOn) ReloadDetectorForCurrentRoot();

        FollowSettingsWindow();
    }

    /// <summary>
    /// 스크립트 실행이 다 끝났다(끝남·오류·중지) - <c>프로젝트이동</c>·<c>프로젝트실행</c> 으로 넘어갔던 모델·자리를 이 화면 본래 프로젝트로 되돌린다.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-19) "플레이 했다가 종료되면 원래 선택된 프로젝트로" - 이동은 넘어갈 때 되돌리지 않으므로(곧 다른 곳으로 가니까) 사슬 끝의
    /// 프로젝트나 중간 것이 남아, 끝난 뒤 영역 패널·검출이 엉뚱한 프로젝트를 봤다. 실행기가 멈출 때 한 번 부른다(<c>Player.RunningChanged</c>).
    /// </remarks>
    protected void ReturnToHomeProject()
    {
        if (SwitchedRecognitionRoot is null) return;

        SwitchedRecognitionRoot = null;
        LoadRegions();
        RefreshSavedTemplate();

        if (IsDetectionOn) ReloadDetectorForCurrentRoot();

        FollowSettingsWindow();
    }

    /// <summary>실행기가 멈추면(<paramref name="isRunning"/> 이 false) UI 스레드에서 본래 프로젝트로.</summary>
    protected void ReturnToHomeProjectWhenStopped(bool isRunning)
    {
        if (isRunning) return;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Guard(ReturnToHomeProject)));
    }

    /// <summary>
    /// <c>프로젝트실행</c> 으로 부른 프로젝트가 끝나 돌아왔다 - 검출 모델·이름 붙인 자리를 부른 쪽 것(<paramref name="previous"/>, null 이면 이 화면 본래 것)으로 되돌린다.
    /// </summary>
    /// <remarks>
    /// 안 되돌리면 <c>프로젝트실행</c> 다음 줄의 <c>목표()</c>·<c>읽기()</c> 가 부른 프로젝트의 모델·자리를 봤다(사용자, 2026-09-19).
    /// <c>프로젝트이동</c> 으로 넘어가는 중이면 되돌리지 않는다 - 곧 다른 프로젝트로 바뀌는데 모델을 한 번 더 올리게 된다.
    /// </remarks>
    protected async System.Threading.Tasks.Task RestoreProjectContextAsync(string? previous, Input.Scripting.Live.LiveScriptHost template)
    {
        if (template.Moves.Pending is not null) return;
        if (string.Equals(SwitchedRecognitionRoot, previous, StringComparison.OrdinalIgnoreCase)) return;

        await SwitchProjectContextAsync(previous);
    }

    /// <summary>
    /// 스크립트의 <c>프로젝트실행("사격장")</c> 이 부른다 - 이름 붙인 자리·검출 모델을 그 프로젝트 폴더 기준으로 바꾸고,
    /// 검출이 켜져 있으면 새 모델을 다 읽을 때까지 기다린다(최대 <paramref name="timeoutMs"/>).
    /// </summary>
    /// <remarks>
    /// UI 스레드 것(속성 설정·모델 로딩 시작)은 <see cref="RunOnUiBlocking"/> 로 부르고 돌아온다 - 스크립트 스레드가
    /// 그 사이 값을 반쯤 바뀐 채로 읽지 않게. 기다리는 동안은 스크립트 스레드에서 그냥 <see cref="Task.Delay"/> 한다.
    /// </remarks>
    protected async System.Threading.Tasks.Task SwitchProjectContextAsync(string? projectRoot, int timeoutMs = 15000)
    {
        RunOnUiBlocking(() =>
        {
            SwitchedRecognitionRoot = projectRoot;
            LoadRegions();

            if (IsDetectionOn) ReloadDetectorForCurrentRoot();
        });

        var deadline = Environment.TickCount64 + timeoutMs;

        while (Volatile.Read(ref _isDetectorLoading) != 0 && Environment.TickCount64 < deadline)
            await System.Threading.Tasks.Task.Delay(50);
    }

    /// <summary>그 동작을 UI 스레드에서 돌리고 끝날 때까지 기다린다. 검증 하네스처럼 서비스가 없는 자리에서도 배선은 돌아야 한다.</summary>
    private static void RunOnUiBlocking(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    /// <summary>
    /// 이름으로 <b>같은 솔루션의 옆 프로젝트 폴더</b>를 찾는다(그 프로젝트가 실제로 있는지는 안 본다 - 부르는 쪽이 본다).
    /// </summary>
    /// <remarks>
    /// 기준은 <b>지금 이 화면이 연 프로젝트</b>(<see cref="Vision.Labeling.LabelDataset.ConfiguredRoot"/>)의 위 폴더다 -
    /// <see cref="RecognitionRoot"/> 는 이미 다른 프로젝트로 바뀌어 있을 수 있어 기준으로 못 쓴다
    /// (연달아 프로젝트실행 하면 늘 처음 프로젝트의 옆에서 찾는다). 솔루션 밖(따로 연 프로젝트)이면 null.
    /// </remarks>
    protected string? FindSiblingProjectFolder(string name)
    {
        var openProjectRoot = Vision.Labeling.LabelDataset.ConfiguredRoot;
        var solutionFolder = Path.GetDirectoryName(openProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return string.IsNullOrEmpty(solutionFolder) ? null : Path.Combine(solutionFolder, name);
    }

    /// <summary>
    /// 플레이 화면의 <c>프로젝트실행("사격장")</c> - 옆 프로젝트 폴더에서 빌드된 것(<c>사격장\bin\사격장.mtsx</c>)을
    /// 찾아 읽고, 이 화면을 그 프로젝트로 바꾼다. 못 찾거나 안 빌드했으면(bin 에 .mtsx 가 없으면) null.
    /// </summary>
    protected async System.Threading.Tasks.Task<Input.Scripting.CompiledPlayable?> SwitchToSiblingProjectAsync(string name)
    {
        if (FindSiblingProjectFolder(name) is not { } targetFolder) return null;

        var mtsxPath = Path.Combine(targetFolder, "bin", name + Input.Scripting.ScriptFiles.CompiledExtension);

        if (!File.Exists(mtsxPath)) return null;

        var bytes = await File.ReadAllBytesAsync(mtsxPath);

        await SwitchProjectContextAsync(targetFolder);

        return new Input.Scripting.CompiledPlayable(bytes, targetFolder, name);
    }

    /// <summary>새 모델을 읽는 중인지. 5초 검사와 버튼이 겹쳐 두 번 읽지 않게.</summary>
    private int _isDetectorLoading;

    /// <summary>
    /// 모델을 읽는다. 이미 읽어 둔 것이 그 파일 그대로면 그냥 쓴다.
    /// </summary>
    /// <remarks>
    /// 검출을 켤 때와, 켜 둔 채 파일이 바뀐 것을 알아챘을 때 둘 다 여기로 온다.
    /// </remarks>
    private void LoadDetector(string modelPath, LabelDataset dataset, LibTorchFlavor? flavor)
    {
        var stamp = File.GetLastWriteTimeUtc(modelPath);

        if (_detector is not null && stamp == _detectorStamp && modelPath == _detectorPath)
        {
            DetectionStatus = "찾는 중...";
            return;
        }

        // 다시 학습해서 파일이 바뀌었으면 옛것을 버리고 새로 읽는다. 지금 돌고 있는 추론이
        // 옛 모델을 쓰는 중일 수 있으니 필드를 먼저 비우고, 끝나기를 기다렸다가 놓는다.
        var stale = _detector;
        _detector = null;
        Interlocked.Increment(ref _detectorGeneration);

        // 도구 줄의 짧은 글과 아래 바의 긴 글, 둘 다 쓴다. 도구 줄은 긴 한글을 안 그리는 일이 있고,
        // 사용자는 "로딩 중인지, 끝났는지, 무엇을 읽었는지" 를 물었다.
        var loadingMessage = stale is null
            ? "검출: 학습한 모델을 읽는 중... (처음 한 번, 몇 초)"
            : "검출: 다시 학습한 모델을 읽는 중...";

        DetectionStatus = stale is null ? "모델 읽는 중..." : "새 모델 읽는 중...";
        StatusText = loadingMessage;
        MessengerUtility.SendMainMessage(loadingMessage);

        if (Interlocked.CompareExchange(ref _isDetectorLoading, 1, 0) != 0) return;

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (stale is not null)
                {
                    var waited = Stopwatch.StartNew();
                    while (Volatile.Read(ref _isDetectRunning) != 0 && waited.ElapsedMilliseconds < 3000) Thread.Sleep(20);
                    stale.Dispose();
                }

                if (flavor is { } torch) LibTorchRuntime.Load(torch);

                var model = DetectorFactory.Create(modelPath);

                _detectClasses = dataset.LoadClasses();
                _detectorStamp = stamp;
                _detectorPath = modelPath;
                _tracker.Reset();   // 새 모델의 사각형을 옛 모델의 것과 이어 붙이지 않는다
                _detector = model;
                Interlocked.Increment(ref _detectorGeneration);
                _reloadSeenStamp = default;

                _isDetectorLoading = 0;

                // 무엇을 읽었는지 한 줄로. 쪽지가 있으면 몇 번째 학습에 몇 장인지까지.
                var manifest = model.Manifest;
                var loaded = $"검출 준비됐습니다 - {manifest.Describe}"
                             + (manifest.TrainCount > 0 ? $" · {manifest.TrainCount}번째 학습" : string.Empty)
                             + (manifest.Images > 0 ? $" · 그림 {manifest.Images}장 · {manifest.Epochs}바퀴" : string.Empty)
                             + (manifest.RecallFound is { } f && manifest.RecallLabels is { } l && l > 0 ? $" · 재현율 {f}/{l}" : string.Empty)
                             + $" · {_detectClasses.Count}종";

                DispatcherService?.BeginInvoke(() =>
                {
                    DetectionStatus = "모델 읽음 · 찾는 중...";
                    StatusText = loaded;
                    MessengerUtility.SendMainMessage(loaded);
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "검출 모델을 못 읽었다");
                _isDetectorLoading = 0;

                DispatcherService?.BeginInvoke(() => Guard(() =>
                {
                    TurnOffDetection($"모델을 못 읽었습니다: {ex.Message}");
                }));
            }
        });
    }

    /// <summary>파일이 바뀐 것을 처음 본 시각과 그때의 파일 시각. 2초 동안 그대로여야 읽는다.</summary>
    private DateTime _reloadSeenStamp;
    private long _reloadSeenTicks;
    private long _lastReloadCheckTicks;

    /// <summary>
    /// 켜 둔 채 다시 학습했으면 알아서 새 모델을 읽는다. 캡처 스레드에서 5초에 한 번.
    /// </summary>
    /// <remarks>
    /// 버튼을 껐다 켜야만 새 모델이 들어오면 "지금 옛 모델로 찾고 있다" 는 것을 사람이
    /// 기억하고 있어야 한다. 학습이 끝나면 zip 을 새로 쓰므로 파일 시각으로 안다.
    /// <b>쓰는 도중에 읽으면 안 된다</b> - 69MB 를 쓰는 동안 시각이 계속 바뀌므로, 같은
    /// 시각이 2초 유지된 뒤에 읽는다.
    /// </remarks>
    private void MaybeReloadDetector()
    {
        var now = Environment.TickCount64;
        if (now - _lastReloadCheckTicks < 5000) return;
        _lastReloadCheckTicks = now;

        if (Volatile.Read(ref _isDetectorLoading) != 0) return;

        // 켜 둔 채 모델 없이 쉬는 중이면(RestWithoutModel) 모델이 생겼는지만 본다. 학습 중·학습 때문에 내려놓은 동안은 안 읽는다(GPU 리셋, 실측).
        var resting = _detector is null && IsDetectionOn && !_pausedForTraining && !Vision.Training.TrainingActivity.IsBusy;

        if (_detector is null && !resting) return;

        var dataset = new LabelDataset(RecognitionRoot);
        var modelPath = DetectorFiles.CurrentFor(dataset);

        if (!File.Exists(modelPath)) return;

        if (resting)
        {
            // 방금 다 써진 파일일 수 있다 - 시각이 2초 넘게 멈춰 있을 때 읽는다(아래 다시 읽기와 같은 규칙).
            var written = File.GetLastWriteTimeUtc(modelPath);
            if ((DateTime.UtcNow - written).TotalMilliseconds < 2000) return;

            DispatcherService?.BeginInvoke(() => Guard(() => { if (IsDetectionOn && _detector is null) ReloadDetectorForCurrentRoot(); }));
            return;
        }

        var stamp = File.GetLastWriteTimeUtc(modelPath);

        // 모델 자리가 바뀌었으면(플레이에서 다른 완성품을 고름) 시각을 기다릴 것 없이 곧바로 읽는다 - 이미 다 써진 파일이다.
        if (modelPath != _detectorPath)
        {
            LibTorchFlavor? moved = DetectorManifest.Load(modelPath).Engine == DetectorEngine.Torch ? LibTorchRuntime.Installed : null;
            DispatcherService?.BeginInvoke(() => Guard(() => LoadDetector(modelPath, dataset, moved)));
            return;
        }

        if (stamp == _detectorStamp) return;

        if (stamp != _reloadSeenStamp)
        {
            _reloadSeenStamp = stamp;
            _reloadSeenTicks = now;
            return;
        }

        if (now - _reloadSeenTicks < 2000) return;

        // 다시 학습한 모델이 ONNX 면 libtorch 가 없어도 읽는다.
        LibTorchFlavor? flavor = DetectorManifest.Load(modelPath).Engine == DetectorEngine.Torch
            ? LibTorchRuntime.Installed
            : null;

        if (DetectorManifest.Load(modelPath).Engine == DetectorEngine.Torch && flavor is null) return;

        DispatcherService?.BeginInvoke(() => Guard(() => LoadDetector(modelPath, dataset, flavor)));
    }

    /// <summary>
    /// 가장 자신 있는 검출을 누른다.
    /// </summary>
    /// <remarks>
    /// <b>찾기만 하면 자동화가 아니다.</b> 찾은 사각형의 가운데를 눌러 준다.
    ///
    /// 누르는 길은 미리보기를 손으로 누를 때와 같은 것을 쓴다(<c>SendClickAsync</c>) -
    /// 두 벌로 두면 한쪽만 고쳐져 손으로는 되는데 자동으로는 안 되는 일이 생긴다.
    ///
    /// <b>입력 전달이 켜져 있어야 한다.</b> 찾는 것은 화면만 보는 일이라 대상에 아무 영향이
    /// 없지만, 누르는 것은 남의 프로그램에 실제로 들어간다. 그것을 켜는 일은 사람이 한 번
    /// 분명히 해야 한다 - 검출을 켠 것만으로 클릭이 나가면 안 된다.
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
                ReportInputForward(prepared, "검출 클릭");
                return;
            }

            var result = await SendClickAsync(screenPoint, didActivate, Minguk.Tools.Input.MouseButton.Left, "검출 클릭");

            if (result == Capture.Input.InputForwardResult.Sent)
            {
                DetectionStatus = $"{target.Caption} 을(를) 눌렀습니다 " +
                                  $"({screenPoint.X:F0}, {screenPoint.Y:F0})";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "검출을 누르지 못했다");
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
    /// 메시지를 적은 뒤에 토글을 끄면 안 된다. 끄는 순간 <see cref="OnDetectionChanged"/> 가
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
    /// <summary>검출은 켠 채 모델만 내려놓는다 - 이 프로젝트에 모델이 없을 때. 화면 사각형·허브의 옛 결과도 버린다.</summary>
    private void RestWithoutModel(string reason)
    {
        DropDetectorModel();

        _latestDetections = null;
        Detections.Clear();
        _tracker.Reset();
        ClickDetectionCommand.RaiseCanExecuteChanged();

        // 허브는 검출이 꺼질 때만 옛 결과를 버린다 - 한 번 꺼진 것으로 알렸다가 다시 켠 것으로 알린다.
        Hub.PublishState(IsRunning, false, SelectedTarget);
        PublishPerceptionState();

        DetectionStatus = reason;
        StatusText = reason;
    }

    private void TurnOffDetection(string reason)
    {
        IsDetectionOn = false;
        DetectionStatus = reason;
        StatusText = reason;
    }

    /// <summary>학습 때문에 모델을 내려놓았는가. 학습이 끝나면 이것이 켜진 화면만 다시 읽는다.</summary>
    private bool _pausedForTraining;

    /// <summary>
    /// 학습이 시작·끝났다(<see cref="Vision.Training.TrainingActivity"/>). 시작이면 GPU 모델을 내려놓고, 끝이면 켜 둔 검출을 다시 읽는다.
    /// </summary>
    /// <remarks>
    /// 3GB 카드에서 캡처·DirectML 추론과 CUDA 학습이 같이 돌다 드라이버가 GPU 를 리셋했다(실측). 토글은 켠 채로 두어 끝나면 사람이 다시 켤 것이 없다.
    /// 모델을 내려놓는 동안 <c>_detector</c> 가 null 이라 프레임 콜백은 알아서 건너뛴다(<see cref="MaybeReloadDetector"/> 도 null 이면 안 읽는다).
    /// </remarks>
    private void OnTrainingActivityChanged() => Guard(() =>
    {
        // 글자 읽기 엔진은 검출이 꺼져 있어도 GPU 를 물 수 있다 - 학습이 시작·끝나면 버려 맞는 쪽으로 다시 연다.
        DropAllOcrEngines();

        if (Vision.Training.TrainingActivity.IsBusy)
        {
            if (!IsDetectionOn || _pausedForTraining) return;

            _pausedForTraining = true;

            var stale = _detector;
            _detector = null;
            Interlocked.Increment(ref _detectorGeneration);
            _tracker.Reset();

            if (stale is not null)
            {
                // 돌고 있는 추론이 옛 모델을 쓰는 중일 수 있다 - 끝나기를 기다렸다 놓는다(LoadDetector 와 같다).
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    var waited = Stopwatch.StartNew();
                    while (Volatile.Read(ref _isDetectRunning) != 0 && waited.ElapsedMilliseconds < 3000) Thread.Sleep(20);
                    stale.Dispose();
                });
            }

            Detections.Clear();
            DetectionStatus = "학습 중이라 검출을 멈췄습니다 - 끝나면 다시 찾습니다.";
            StatusText = DetectionStatus;
            Logger.Info("학습이 시작돼 검출 모델을 내려놓았다(GPU 메모리)");
            return;
        }

        if (!_pausedForTraining) return;

        _pausedForTraining = false;

        // 학습이 끝나며 새 모델이 들어왔을 수 있다 - 켤 때와 같은 길로 지금 모델을 읽는다.
        if (IsDetectionOn) OnDetectionChanged();
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
        IsDetectionOn = false;

        DropDetectorModel();
    }

    /// <summary>읽어 둔 모델과 추론 준비물을 놓는다. 켜고 끄기(<see cref="IsDetectionOn"/>)는 건드리지 않는다.</summary>
    private void DropDetectorModel()
    {
        _detectorPath = null;
        Interlocked.Increment(ref _detectorGeneration);

        _detector?.Dispose();
        _detector = null;

        _preprocessor?.Dispose();
        _preprocessor = null;

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
