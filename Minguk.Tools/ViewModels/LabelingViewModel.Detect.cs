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
    private IDetector? _model;

    /// <summary>
    /// 지금 그림에서 몹을 찾아 본다.
    /// </summary>
    /// <remarks>
    /// 학습이 쓸 만해졌는지 보는 자리다. 사람이 찍은 사각형 위에 점선으로 겹쳐 그려서,
    /// 무엇을 맞히고 무엇을 놓쳤는지 한눈에 보이게 한다.
    /// </remarks>
    private void DoDetect() => Guard(() => StartDetect(automatic: false));

    /// <summary>
    /// 지금 그림에서 찾기 시작한다. 자동(그림을 넘겨서)이면 못 돌리는 이유를 상태 줄에 쓰지 않는다 - 넘길 때마다 같은 말이 뜬다.
    /// </summary>
    private void StartDetect(bool automatic)
    {
        if (SelectedItem is not { } item)
        {
            if (!automatic) DetectStatus = "먼저 그림을 고르세요.";
            return;
        }

        var dataset = new LabelDataset(DatasetRoot ?? LabelDataset.DefaultRoot);
        var modelPath = DetectorFiles.CurrentFor(dataset);

        if (!File.Exists(modelPath))
        {
            if (!automatic) DetectStatus = $"학습한 모델이 없습니다. 먼저 학습하세요 ({DetectorTrainer.ModelFileName}).";
            return;
        }

        // libtorch 는 옛 TorchSharp 모델에만 필요하다. ONNX(YOLO·D-FINE)까지 막으면 libtorch 없는 PC 에서 자동 라벨이 영영 안 돈다.
        LibTorchFlavor? flavor = null;

        if (DetectorManifest.Load(modelPath).Engine == DetectorEngine.Torch)
        {
            if (LibTorchRuntime.Installed is not { } installed)
            {
                if (!automatic) DetectStatus = "libtorch 가 없습니다. 학습을 한 번 돌리면 같이 준비됩니다.";
                return;
            }

            flavor = installed;
        }

        IsDetecting = true;
        DetectStatus = automatic ? "새 그림이라 자동으로 찾는 중..." : "찾는 중...";

        _ = GuardAsync(async () =>
        {
            try
            {
                await RunDetectAsync(flavor, dataset, item, modelPath, automatic);
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
    }

    /// <summary>
    /// 라벨 없는 그림으로 넘어왔다 - 잠깐 기다렸다가 아직 그 그림에 머물러 있으면 자동 라벨을 돌린다.
    /// </summary>
    /// <remarks>
    /// 순번으로 늦게 온 부탁을 버린다 - 방향키로 열 장을 넘기면 마지막 한 장만 찾는다. 앞 찾기가 도는 중이면 끝나기를 기다린다.
    /// 기다리는 사이 사람이 사각형을 찍었거나 점선이 이미 있으면 안 돌린다.
    /// </remarks>
    private async Task AutoDetectSoonAsync(LabelingRow item)
    {
        var order = ++_autoDetectOrder;

        await Task.Delay(AutoDetectDelayMs);

        while (IsDetecting)
        {
            if (order != _autoDetectOrder) return;
            await Task.Delay(100);
        }

        if (order != _autoDetectOrder || !ReferenceEquals(SelectedItem, item) || !AutoDetectNewImages) return;
        if (Boxes.Count > 0 || Predictions.Count > 0) return;

        if (IsTraining)
        {
            DetectStatus = "학습하는 동안은 자동 라벨을 쉽니다(같은 그래픽 카드를 쓰면 멈출 수 있습니다).";
            return;
        }

        StartDetect(automatic: true);
    }

    /// <summary>그림에 멈춘 뒤 자동 라벨까지 기다리는 시간. 방향키 반복(초당 30번 남짓)보다 길게.</summary>
    private const int AutoDetectDelayMs = 250;

    /// <summary>자동 라벨 부탁의 순번. UI 스레드에서만 만진다.</summary>
    private int _autoDetectOrder;

    private async Task RunDetectAsync(LibTorchFlavor? flavor, LabelDataset dataset, LabelingRow item, string modelPath, bool automatic = false)
    {
        // 모델을 읽는 것도 찾는 것도 UI 스레드에서 하면 안 된다. 69MB 를 읽고 신경망을 돌린다.
        await Task.Run(() =>
        {
            if (flavor is { } torch) LibTorchRuntime.Load(torch);

            // 다시 학습했으면 파일이 바뀌었다. 읽어 둔 것을 그대로 쓰면 옛 모델로 찾는다.
            if (_model is { } loaded && loaded.ModelPath != modelPath)
            {
                loaded.Dispose();
                _model = null;
            }

            _model ??= DetectorFactory.Create(modelPath);
        });

        var classes = dataset.LoadClasses();
        var found = await Task.Run(() => _model!.Detect(item.ImagePath, classes, (float)MinimumScore));

        // 찾는 사이 다른 그림으로 넘어갔으면 버린다 - 안 버리면 앞 그림의 점선이 지금 그림 위에 엉뚱하게 그려진다.
        if (!ReferenceEquals(SelectedItem, item))
        {
            DetectStatus = null;
            return;
        }

        Predictions.Clear();

        foreach (var detection in found)
            Predictions.Add(new PredictedBox(detection.Box, detection.Describe));

        var trained = _model!.Manifest.Describe;
        var prefix = automatic ? "자동 라벨: " : string.Empty;

        DetectStatus = found.Count == 0
            ? $"{prefix}못 찾았습니다 (모델 {trained}, 신뢰도 {MinimumScore:P0} 이상만 봅니다)."
            : $"{prefix}{found.Count}마리 찾았습니다: {string.Join(", ", found.Take(4).Select(d => d.Describe))}"
              + (found.Count > 4 ? " …" : string.Empty)
              + (automatic ? " - 맞으면 라벨 확정" : string.Empty);
    }

    /// <summary>
    /// 모델이 찾은 점선을 전부 라벨로 굳힌다.
    /// </summary>
    /// <remarks>
    /// 모델이 웬만큼 찾기 시작하면 사람이 할 일은 그리는 것이 아니라 <b>고치는 것</b>이 된다.
    /// 전부 받고 틀린 것을 지우는 편이, 맞는 것을 하나씩 고르는 것보다 빠르다. 하나씩
    /// 고르고 싶으면 캔버스에서 점선을 누르면 그것만 옮겨진다.
    /// 문턱 아래 것은 애초에 점선으로 안 그려졌으니 여기 안 들어온다.
    /// </remarks>
    private void DoAdoptPredictions() => Guard(() =>
    {
        if (Predictions.Count == 0) return;

        var count = Predictions.Count;

        foreach (var prediction in Predictions) Boxes.Add(prediction.Box);

        Predictions.Clear();
        SelectedBoxIndex = Boxes.Count - 1;
        DetectStatus = $"자동 라벨 {count}개를 확정했습니다. 틀린 것은 골라서 지우세요.";
    });

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
