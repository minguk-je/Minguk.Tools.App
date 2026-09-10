using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Minguk.Base.Utilities;

using Minguk.Tools.Markup;
using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.ViewModels;

public partial class LabelingViewModel
{
    /// <summary>
    /// 읽어 둔 모델. 한 번 읽고 계속 쓴다.
    /// </summary>
    /// <remarks>
    /// 69MB 를 읽는 데 몇 초 걸린다. 그림을 넘길 때마다 다시 읽으면 못 쓴다.
    /// 다시 학습하면 <see cref="_model"/> 을 버려야 새 것을 읽는다.
    /// </remarks>
    private DetectorModel? _model;

    /// <summary>
    /// 지금 그림에서 몹을 찾아 본다.
    /// </summary>
    /// <remarks>
    /// 학습이 쓸 만해졌는지 보는 자리다. 사람이 찍은 사각형 위에 점선으로 겹쳐 그려서,
    /// 무엇을 맞히고 무엇을 놓쳤는지 한눈에 보이게 한다.
    /// </remarks>
    private void DoDetect() => Guard(() =>
    {
        if (SelectedItem is not { } item)
        {
            DetectStatus = "먼저 그림을 고르세요.";
            return;
        }

        var dataset = new LabelDataset(DatasetRoot ?? LabelDataset.DefaultRoot);
        var modelPath = DetectorTrainer.ModelPathFor(dataset);

        if (!File.Exists(modelPath))
        {
            DetectStatus = $"학습한 모델이 없습니다. 먼저 학습하세요 ({DetectorTrainer.ModelFileName}).";
            return;
        }

        if (LibTorchRuntime.Installed is not { } flavor)
        {
            DetectStatus = "libtorch 가 없습니다. 학습을 한 번 돌리면 같이 준비됩니다.";
            return;
        }

        IsDetecting = true;
        DetectStatus = "찾는 중...";

        _ = GuardAsync(async () =>
        {
            try
            {
                await RunDetectAsync(flavor, dataset, item, modelPath);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "몹을 찾지 못했다");
                DetectStatus = $"실패: {ex.Message}";
            }
            finally
            {
                IsDetecting = false;
            }
        });
    });

    private async Task RunDetectAsync(LibTorchFlavor flavor, LabelDataset dataset, LabelingRow item, string modelPath)
    {
        // 모델을 읽는 것도 찾는 것도 UI 스레드에서 하면 안 된다. 69MB 를 읽고 신경망을 돌린다.
        await Task.Run(() =>
        {
            LibTorchRuntime.Load(flavor);

            // 다시 학습했으면 파일이 바뀌었다. 읽어 둔 것을 그대로 쓰면 옛 모델로 찾는다.
            if (_model is { } loaded && loaded.ModelPath != modelPath)
            {
                loaded.Dispose();
                _model = null;
            }

            _model ??= DetectorModel.Load(modelPath);
        });

        var classes = dataset.LoadClasses();
        var found = await Task.Run(() => _model!.Detect(item.ImagePath, classes, (float)MinimumScore));

        Predictions.Clear();

        foreach (var detection in found)
            Predictions.Add(new PredictedBox(detection.Box, detection.Describe));

        var trained = _model!.Manifest.Describe;

        DetectStatus = found.Count == 0
            ? $"못 찾았습니다 (모델 {trained}, 자신 있는 정도 {MinimumScore:P0} 이상만 봅니다)."
            : $"{found.Count}마리 찾았습니다: {string.Join(", ", found.Take(4).Select(d => d.Describe))}"
              + (found.Count > 4 ? " …" : string.Empty);
    }

    /// <summary>점선을 지운다. 사람이 찍은 것은 그대로 둔다.</summary>
    private void DoClearPredictions() => Guard(() =>
    {
        Predictions.Clear();
        DetectStatus = null;
    });

    /// <summary>
    /// 그림을 넘기면 점선을 지운다.
    /// </summary>
    /// <remarks>
    /// 안 지우면 앞 그림에서 찾은 것이 다음 그림 위에 그대로 남아, 엉뚱한 자리에 몹이
    /// 있는 것처럼 보인다.
    /// </remarks>
    private void ClearPredictionsForNewImage()
    {
        if (Predictions.Count == 0) return;

        Predictions.Clear();
        DetectStatus = null;
    }

    private void ReleaseModel()
    {
        _model?.Dispose();
        _model = null;
    }
}
