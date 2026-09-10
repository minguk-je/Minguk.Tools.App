using System;
using System.Threading;
using System.Threading.Tasks;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.ViewModels;

public partial class LabelingViewModel
{
    private CancellationTokenSource? _trainingCts;

    /// <summary>
    /// 누르기 전에 알아야 할 것을 적는다.
    /// </summary>
    /// <remarks>
    /// 2.2GB 를 받고 나서 "GPU 가 없어 못 씁니다" 라고 하면 안 된다. 받기 전에 말한다.
    /// </remarks>
    private void UpdateTrainingNotice() => Guard(() =>
    {
        if (!LibTorchRuntime.HasCudaDriver)
        {
            TrainingNotice =
                "CUDA 를 쓸 수 있는 NVIDIA 드라이버가 없습니다. CPU 로도 돌릴 수는 있지만 " +
                "실측으로 29배 느려(그림 8장 1 epoch 이 248초 대 8.6초) 실제 데이터셋에는 못 씁니다.";
            return;
        }

        if (LibTorchRuntime.Installed is null)
        {
            TrainingNotice =
                $"학습을 처음 누르면 libtorch 를 받습니다({LibTorchRuntime.DescribeSize(LibTorchFlavor.Cuda)}). " +
                "한 번만 받고 다음부터는 바로 시작합니다.";
            return;
        }

        TrainingNotice = null;
    });

    /// <summary>
    /// 학습시킨다. libtorch 가 없으면 먼저 받는다.
    /// </summary>
    /// <remarks>
    /// 받는 것부터 학습까지 한 흐름으로 둔다. "받기" 버튼을 따로 두면 사람이 그것을 먼저
    /// 눌러야 한다는 것을 알아야 하고, 안 눌렀을 때 학습 버튼이 왜 안 되는지도 설명해야 한다.
    /// </remarks>
    private void DoTrain() => Guard(() =>
    {
        // 찍던 것을 먼저 저장한다. 안 하면 방금 찍은 사각형이 학습에 안 들어간다.
        SaveCurrentIfDirty();

        var flavor = LibTorchRuntime.Installed ?? LibTorchRuntime.Recommended;

        if (!LibTorchRuntime.IsInstalled(flavor) && !ConfirmDownload(flavor)) return;

        _trainingCts?.Dispose();
        _trainingCts = new CancellationTokenSource();

        IsTraining = true;
        TrainingStatus = "준비하는 중...";

        // 지난번 그래프는 지운다. 남겨 두면 이번 loss 가 지난번 꼬리에 이어 붙어 어디서부터가 이번인지 안 보인다.
        LossHistory.Clear();
        TrainEpochsDone = 0;
        TrainEpochsTotal = TrainEpochs;
        TrainPercent = 0;

        _ = GuardAsync(async () =>
        {
            try
            {
                await RunTrainingAsync(flavor, _trainingCts.Token);
            }
            catch (OperationCanceledException)
            {
                TrainingStatus = "학습을 멈췄습니다.";
            }
            catch (Exception ex)
            {
                // 여기서 예외 창을 띄우지 않는다. 몇 시간짜리 일이라 사람이 자리를 비웠을 수 있고,
                // 돌아왔을 때 무엇이 잘못됐는지 화면에 남아 있는 편이 낫다.
                Logger.Error(ex, "학습에 실패했다");
                TrainingStatus = $"실패: {ex.Message}";
            }
            finally
            {
                IsTraining = false;
                UpdateTrainingNotice();
            }
        });
    });

    private async Task RunTrainingAsync(LibTorchFlavor flavor, CancellationToken token)
    {
        var progress = new Progress<string>(message => TrainingStatus = message);

        if (!LibTorchRuntime.IsInstalled(flavor))
            await LibTorchRuntime.EnsureInstalledAsync(flavor, progress, token);

        TrainingStatus = "libtorch 를 올리는 중...";

        // 네이티브를 올리는 것은 몇 초 걸린다(2GB 를 읽는다). UI 스레드에서 하면 화면이 멈춘다.
        await Task.Run(() => LibTorchRuntime.Load(flavor), token);

        var dataset = new LabelDataset(DatasetRoot ?? LabelDataset.DefaultRoot);

        var (width, height) = ParseInputSize();

        // Progress<T> 는 만든 스레드(UI)로 돌아와서 부른다. 컬렉션을 여기서 고쳐도 된다.
        var steps = new Progress<TrainingStep>(step =>
        {
            TrainEpochsDone = step.EpochsDone;
            TrainEpochsTotal = step.MaxEpoch;
            TrainPercent = step.Fraction * 100;

            if (step.Loss is { } loss) LossHistory.Add(loss);
        });

        var result = await DetectorTrainer.TrainAsync(dataset, TrainEpochs, progress, token, width, height, steps: steps);

        TrainEpochsDone = TrainEpochsTotal;
        TrainPercent = 100;

        TrainingStatus =
            $"끝났습니다 - 그림 {result.Images}장 · 사각형 {result.Boxes}개 · 몹 {result.Classes}종 을 " +
            $"{result.InputSize} 로 {result.Elapsed.TotalMinutes:0.0}분 동안 " +
            $"{(result.UsedGpu ? "GPU" : "CPU")} 로 학습했습니다. " +
            $"{System.IO.Path.GetFileName(result.ModelPath)}";

        // 다시 학습했으니 읽어 둔 모델은 옛것이다. 버려야 다음 찾아보기가 새 것을 읽는다.
        ReleaseModel();

        MessengerUtility.SendMainMessage($"학습이 끝났습니다: {result.ModelPath}");
    }

    /// <summary>
    /// 2.2GB 를 받기 전에 물어본다.
    /// </summary>
    /// <remarks>
    /// 버튼 한 번에 2GB 가 나가면 안 된다. 종량제 회선일 수도 있다.
    /// </remarks>
    private bool ConfirmDownload(LibTorchFlavor flavor)
    {
        var size = LibTorchRuntime.DescribeSize(flavor);

        var extra = flavor == LibTorchFlavor.Cpu
            ? "\n\nGPU 를 못 찾아 CPU 판을 받습니다. 실제 데이터셋을 학습시키기에는 너무 느립니다."
            : string.Empty;

        // 서비스가 없으면 GetService 가 null 이 아니라 예외로 터진다. 그것을 Guard 가 삼키면
        // 버튼을 눌러도 아무 일이 안 일어난 것처럼 보인다 - 실제로 한 번 그렇게 막혔다.
        // 여기서 잡아 화면에 남긴다.
        IMessageBoxService service;

        try
        {
            service = MessageBoxService;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "MessageBoxService 를 못 얻었다");
            TrainingStatus = "확인 창을 띄우지 못했습니다. 화면에 DXMessageBoxService 가 선언돼 있는지 보세요.";

            return false;
        }

        var answer = service.ShowMessage(
            $"학습에 필요한 libtorch 를 받습니다. ({size}){extra}\n\n" +
            $"받는 곳: {LibTorchRuntime.RootDirectory}\n" +
            "한 번만 받고 다음부터는 바로 시작합니다.\n\n계속할까요?",
            "libtorch 를 받습니다",
            MessageButton.OKCancel,
            MessageIcon.Question);

        return answer == MessageResult.OK;
    }

    /// <summary>
    /// 화면에서 고른 크기를 숫자로. 못 읽으면 기본값.
    /// </summary>
    /// <remarks>
    /// 목록에서 고르게 해 두었으므로 어긋날 일이 없지만, 설정 파일을 손으로 고칠 수 있다.
    /// 그때 터지는 대신 기본값으로 돈다.
    /// </remarks>
    private (int Width, int Height) ParseInputSize()
    {
        var parts = (SelectedInputSize ?? string.Empty).Split('x');

        if (parts.Length == 2
            && int.TryParse(parts[0], out var width)
            && int.TryParse(parts[1], out var height)
            && width > 0 && height > 0)
            return (width, height);

        return (DetectorTrainer.DefaultInputWidth, DetectorTrainer.DefaultInputHeight);
    }

    /// <summary>
    /// 멈춘다.
    /// </summary>
    /// <remarks>
    /// 받는 중이면 곧바로 멈춘다. 학습 중이면 <b>지금 도는 epoch 이 끝나야</b> 듣는다 -
    /// 학습기가 중간에 멈춰 주지 않는다. 화면에 그렇게 적어 둔다.
    /// </remarks>
    private void DoCancelTrain() => Guard(() =>
    {
        _trainingCts?.Cancel();

        TrainingStatus = "멈추는 중... (학습 중이면 지금 바퀴가 끝나야 멈춥니다)";
    });
}
