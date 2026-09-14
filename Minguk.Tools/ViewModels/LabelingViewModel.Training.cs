using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Base.Utilities;

using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 학습 칸. "쓰는 모델" 콤보로 몹 찾기 모델을 고르고, YOLO 가 골라져 있으면 그것을 다시 학습한다.
/// </summary>
/// <remarks>
/// 2026-09-14 에 옛 TorchSharp 학습(AutoFormerV2 · detector.zip)을 이 화면에서 뺐다 - 크기 고르기·따라가기·그림별 loss·libtorch 받기 안내가
/// 딸려 있었는데 시험은 YOLO11n, 배포는 D-FINE-N 으로 정하고 나니 쓰지 않는 것이 화면을 헷갈리게만 했다. 엔진(DetectorTrainer·libtorch)은
/// 하네스와 "ONNX 가 없을 때" 대체 경로가 쓰므로 남았다.
/// </remarks>
public partial class LabelingViewModel
{
    private CancellationTokenSource? _trainingCts;

    /// <summary>학습할 수 있는 모델이 골라져 있는가. YOLO(Ultralytics)와 D-FINE 둘 다 앱에서 돌린다.</summary>
    private bool CanTrain => !IsTraining && SelectedModelChoice is { } choice && IsTrainable(choice.Name);

    /// <summary>이 모델을 앱이 학습할 수 있는가. 둘 다 밖의 파이썬을 띄우는 길이다(YOLO 는 yolo-venv, D-FINE 은 제 저장소).</summary>
    private static bool IsTrainable(string? modelName)
        => YoloTrainer.WeightsFor(modelName) is not null || DFineTrainer.Handles(modelName);

    /// <summary>고른 모델을 못 돌리는 이유. 다 갖춰져 있으면 null.</summary>
    private static string? WhyCannotTrain(string modelName)
        => DFineTrainer.Handles(modelName) ? DFineTrainer.WhyUnavailable() : YoloTrainer.WhyUnavailable();

    /// <summary>
    /// 콤보에 고른 YOLO 모델을 다시 학습한다.
    /// </summary>
    /// <remarks>
    /// 끝나면 새 모델이 몹 찾기 자리와 콤보 보관본에 들어가고, 학습 그림을 다시 찾아 목록에 적는다.
    /// YOLO 는 <see cref="YoloTrainer"/>(yolo-venv), D-FINE 은 <see cref="DFineTrainer"/>(제 저장소)로 간다 - 어느 쪽을 쓸지는 사람이 고른다
    /// (사용자 결정 2026-09-14). 환경이 없으면 무엇이 없는지 상태 줄에 적는다.
    /// </remarks>
    private void DoTrain() => Guard(() =>
    {
        // 찍던 것을 먼저 저장한다. 안 하면 방금 찍은 사각형이 학습에 안 들어간다.
        SaveCurrentIfDirty();

        if (SelectedModelChoice is not { } choice || !IsTrainable(choice.Name)) return;

        if (WhyCannotTrain(choice.Name) is { } why)
        {
            TrainingStatus = why;
            return;
        }

        _trainingCts?.Dispose();
        _trainingCts = new CancellationTokenSource();

        IsTraining = true;
        LossHistory.Clear();
        TrainEpochsDone = 0;
        TrainEpochsTotal = TrainEpochs;
        TrainBatchDone = 0;
        TrainBatchTotal = 0;
        TrainPercent = 0;
        TrainingBatchImage = null;

        // "GPU 1 - ..." 이면 그 번호. 자동이면 모니터가 안 붙은·지금 덜 바쁜 카드(GpuProbe) - 예전에는 늘 0번이라
        // 게임·캡처·몹 찾기가 도는 카드에 학습을 얹어 GPU 가 리셋됐다(실측 2026-09-14). 고를 근거가 없으면 0.
        var device = SelectedGpuOption is { } gpu && System.Text.RegularExpressions.Regex.Match(gpu, @"^GPU (\d+)") is { Success: true } m
            ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : GpuProbe.PickForTraining(GpuProbe.List())?.Index ?? 0;

        Logger.Info($"학습 GPU: {device} ({SelectedGpuOption ?? "자동"})");

        var token = _trainingCts.Token;

        _ = GuardAsync(async () =>
        {
            try
            {
                var dataset = new LabelDataset(DatasetRoot ?? LabelDataset.DefaultRoot);
                var status = new Progress<string>(message => TrainingStatus = message);
                var steps = new Progress<TrainingStep>(step =>
                {
                    TrainEpochsDone = step.EpochsDone;
                    TrainEpochsTotal = step.MaxEpoch;
                    TrainPercent = step.Fraction * 100;

                    // 묶음 알림이면 묶음 칸을 채우고, 바퀴 끝 알림이면 다음 바퀴를 기다리며 비운다.
                    TrainBatchDone = step.IsBatch ? step.Batch : 0;
                    if (step.IsBatch) TrainBatchTotal = step.BatchCount;

                    if (step.Loss is { } loss) LossHistory.Add(loss);
                });
                var previews = new Progress<string>(path => _ = LoadTrainingBatchAsync(path));

                // 둘은 걸리는 시간이 크게 다르다 - YOLO11n 은 5분, D-FINE-N 은 1시간 45분(98장 60바퀴, GTX 1060).
                // 학습하는 동안 켜 둔 스크립트·플레이 화면이 몹 찾기 모델을 내려놓는다(TrainingActivity) - 같은 카드에서 겹치면 GPU 가 리셋됐다.
                string modelPath;
                TimeSpan elapsed;

                using (TrainingActivity.Begin())
                {
                    (modelPath, elapsed) = DFineTrainer.Handles(choice.Name)
                        ? await DFineTrainer.TrainAsync(dataset, choice.Name, TrainEpochs, device, status, steps, token)
                        : await YoloTrainer.TrainAsync(dataset, choice.Name, TrainEpochs, device, status, steps, token, previews);
                }

                TrainEpochsDone = TrainEpochsTotal;
                TrainBatchDone = TrainBatchTotal;
                TrainPercent = 100;
                TrainingStatus = $"끝났습니다 - {choice.Name} 을 {elapsed.TotalMinutes:0.0}분 동안 학습해 몹 찾기에 넣었습니다.";

                ReleaseModel();
                RefreshModelSummary();
                MessengerUtility.SendMainMessage($"{choice.Name} 학습이 끝났습니다. 켜 둔 스크립트·플레이 화면도 몇 초 안에 새 모델로 찾습니다.");

                // 재현율은 덤이다 - 여기서 실패해도 학습은 된 것이다. 예전에는 "학습에 실패" 로 적혀 새 모델이 들어간 줄 몰랐다.
                var finished = TrainingStatus;

                try
                {
                    await RunSelfCheckAsync(dataset, modelPath, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.Error(ex, "학습 뒤 재현율을 재지 못했다");
                    TrainingStatus = $"{finished}  재현율은 못 쟀습니다: " +
                                     (TrainingActivity.IsGpuLost(ex) ? TrainingActivity.GpuLostMessage : ex.Message);
                }
            }
            catch (OperationCanceledException)
            {
                TrainingStatus = "학습을 멈췄습니다. 쓰던 모델은 그대로입니다.";
            }
            catch (Exception ex)
            {
                // 예외 창을 띄우지 않는다. 사람이 자리를 비웠을 수 있고, 돌아왔을 때 무엇이 잘못됐는지 화면에 남아 있는 편이 낫다.
                Logger.Error(ex, "학습에 실패했다");
                TrainingStatus = TrainingActivity.IsGpuLost(ex) ? $"실패: {TrainingActivity.GpuLostMessage}" : $"실패: {ex.Message}";
            }
            finally
            {
                IsTraining = false;
            }
        });
    });

    /// <summary>파이썬이 바꿔 끼운 학습 묶음 그림을 읽어 띄운다. 끄고 있어도 읽어 둔다 - 켜는 순간 마지막 묶음이 보이게.</summary>
    /// <remarks>
    /// 파일을 잡지 않게 바이트로 읽어 메모리에서 푼다(OnLoad) - 쥐고 있으면 다음 바퀴에 파이썬이 바꿔 끼우지 못한다.
    /// 1280px jpg 라 푸는 데 수십 ms 여서 스레드풀에서 하고, 늦게 끝난 옛 그림이 새 그림을 덮지 않게 순번을 본다.
    /// </remarks>
    private async Task LoadTrainingBatchAsync(string path)
    {
        var order = Interlocked.Increment(ref _trainingBatchOrder);

        try
        {
            var image = await Task.Run(() =>
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();

                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new System.IO.MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            });

            if (order == Volatile.Read(ref _trainingBatchOrder)) TrainingBatchImage = image;
        }
        catch (Exception ex)
        {
            // 그림 한 장 못 읽었다고 학습을 흔들지 않는다. 다음 바퀴에 또 온다.
            Logger.Warn(ex, $"학습 묶음 그림을 읽지 못했다: {path}");
        }
    }

    private int _trainingBatchOrder;

    /// <summary>
    /// 학습에 쓴 그림마다 모델로 다시 찾아 몇 개를 다시 찾았는지 목록에 적는다.
    /// </summary>
    /// <remarks>
    /// 하네스 <c>--detect-check</c> 와 같은 계산(<see cref="DetectionMatch"/>)이다. 학습에 쓴
    /// 그림이라 외운 것도 맞은 것으로 센다. 그러니 여기서 못 찾은 그림은 라벨이 틀렸거나 장면이
    /// 애매한 것이고, 다 찾았다는 것이 새 장면에서도 찾는다는 뜻은 아니다.
    /// </remarks>
    private async Task RunSelfCheckAsync(LabelDataset dataset, string modelPath, CancellationToken token)
    {
        var rows = Items.Where(row => row.HasLabel).ToList();
        if (rows.Count == 0) return;

        foreach (var row in Items)
        {
            row.Recognition = null;
            row.RecognitionIsPoor = false;
        }

        var finished = TrainingStatus;
        var classes = dataset.LoadClasses();
        var threshold = (float)MinimumScore;

        var total = new DetectionMatch.Result(0, 0, 0);

        await Task.Run(() =>
        {
            using var model = DetectorFactory.Create(modelPath);

            for (var i = 0; i < rows.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var row = rows[i];
                var labels = LabelFile.Load(row.LabelPath, out _);
                var found = model.Detect(row.ImagePath, classes, threshold);
                var match = DetectionMatch.Match(labels, found);

                total = new DetectionMatch.Result(total.Found + match.Found, total.Labels + match.Labels, total.Extra + match.Extra);

                var index = i + 1;
                DispatcherService?.BeginInvoke(() =>
                {
                    row.Recognition = match.Describe;
                    row.RecognitionIsPoor = !match.IsComplete || match.Extra > 0;
                    TrainingStatus = $"{finished}  재현율 재는 중 {index}/{rows.Count}...";
                });
            }
        }, token);

        var rate = total.Labels == 0 ? 0 : 100.0 * total.Found / total.Labels;

        TrainingStatus = $"{finished}  재현율: 라벨 {total.Labels}개 중 {total.Found}개 ({rate:0}%)" +
                         (total.Extra > 0 ? $" · 헛것 {total.Extra}개" : string.Empty) +
                         $" (신뢰도 {MinimumScore:P0} 기준)";

        // 재현율 결과를 쪽지에도 남긴다. 다음에 화면을 열었을 때 "지난 모델이 얼마나 찾았나" 가 보여야 한다.
        try
        {
            var manifest = DetectorManifest.Load(modelPath);
            manifest.RecallFound = total.Found;
            manifest.RecallLabels = total.Labels;
            manifest.RecallExtra = total.Extra;
            manifest.RecallThreshold = MinimumScore;
            manifest.Save(modelPath);

            // 콤보는 보관본 쪽지를 읽는다 - 지금 자리에 앉은 모델의 보관본에도 적는다.
            if (DetectorFiles.CurrentChoice(dataset, DetectorFiles.ListChoices(dataset)) is { } kept)
                manifest.Save(kept.Path);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "재현율 결과를 쪽지에 적지 못했다");
        }

        RefreshModelSummary();
    }

    /// <summary>
    /// "쓰는 모델" 콤보와 옆 줄을 다시 채운다. 폴더를 바꿀 때, 학습 뒤, 재현율 뒤, 모델을 고른 뒤.
    /// </summary>
    /// <remarks>
    /// 줄은 <b>몹 찾기가 실제로 쓰는 모델</b>(<see cref="DetectorFiles.CurrentFor"/>)이다. 예전에는 이 화면의 학습 결과(detector.zip)만 읽어,
    /// 몹 찾기는 YOLO11n(ONNX)으로 도는데 줄에는 "640x360 · 재현율 70%" 가 떠서 70% 짜리가 도는 것처럼 보였다(2026-09-14).
    /// </remarks>
    private void RefreshModelSummary()
    {
        var dataset = new LabelDataset(DatasetRoot ?? LabelDataset.DefaultRoot);
        var current = DetectorFiles.CurrentFor(dataset);

        ModelSummary = System.IO.File.Exists(current)
            ? DetectorManifest.Load(current).Summary
            : "아직 쓸 모델이 없습니다.";

        // 채우는 동안 고른 것이 바뀌어도 모델을 옮기지 않는다(_isRefreshingModels).
        _isRefreshingModels = true;
        try
        {
            var choices = DetectorFiles.ListChoices(dataset);

            ModelChoices.Clear();
            foreach (var choice in choices) ModelChoices.Add(choice);

            SelectedModelChoice = DetectorFiles.CurrentChoice(dataset, choices);
        }
        finally
        {
            _isRefreshingModels = false;
        }
    }

    /// <summary>콤보를 다시 채우는 중인가. 그동안 고른 것이 바뀌는 것은 사람이 고른 것이 아니다.</summary>
    private bool _isRefreshingModels;

    /// <summary>
    /// 콤보에서 모델을 고르면 그것을 몹 찾기 자리에 앉힌다.
    /// </summary>
    /// <remarks>
    /// 이 화면의 찾아보기가 들고 있던 모델도 놓는다 - 안 놓으면 옛 모델로 계속 찾는다.
    /// 배포 전에 D-FINE-N 으로 돌렸는지는 줄에 뜨는 이름으로 본다(YOLO 는 AGPL 이라 넘기는 모델에 못 쓴다).
    /// </remarks>
    private void OnSelectedModelChoiceChanged() => Guard(() =>
    {
        DoTrainCommand.RaiseCanExecuteChanged();

        if (_isRefreshingModels || SelectedModelChoice is not { } choice) return;

        var dataset = new LabelDataset(DatasetRoot ?? LabelDataset.DefaultRoot);
        if (DetectorFiles.CurrentChoice(dataset, ModelChoices) == choice) return;

        DetectorFiles.Use(dataset, choice);
        ReleaseModel();

        StatusText = $"몹 찾기 모델을 {choice.Name} 로 바꿨습니다. 켜 둔 스크립트·플레이 화면도 몇 초 안에 따라옵니다.";
        MessengerUtility.SendMainMessage(StatusText);

        RefreshModelSummary();
    });

    /// <summary>멈춘다. 파이썬 학습 프로세스를 통째로 끈다 - 쓰던 모델은 그대로다.</summary>
    private void DoCancelTrain() => Guard(() =>
    {
        _trainingCts?.Cancel();

        TrainingStatus = "멈추는 중...";
    });
}
