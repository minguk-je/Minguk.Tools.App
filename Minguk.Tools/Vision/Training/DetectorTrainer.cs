using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.ML;
using Microsoft.ML.TorchSharp;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Training;

/// <param name="ModelPath">만들어진 모델 파일.</param>
/// <param name="Images">학습에 실제로 쓴 그림 수.</param>
/// <param name="Boxes">학습에 실제로 쓴 사각형 수.</param>
/// <param name="Classes">몹 종류 수.</param>
/// <param name="Elapsed">걸린 시간.</param>
/// <param name="UsedGpu">GPU 로 돌았는지.</param>
/// <param name="InputSize">모델이 실제로 본 크기.</param>
public readonly record struct TrainingResult(
    string ModelPath, int Images, int Boxes, int Classes, TimeSpan Elapsed, bool UsedGpu, string InputSize);

/// <summary>
/// 찍어 둔 라벨로 몹 검출 모델을 학습시킨다.
/// </summary>
/// <remarks>
/// <b>왜 C# 인가</b>
///
/// <see cref="Microsoft.ML.TorchSharp"/> 의 AutoFormerV2 검출기를 쓴다. 파이썬은 안 끼어든다 -
/// TorchSharp 는 PyTorch 의 C++ 알맹이(libtorch)에 붙인 .NET 바인딩이다.
///
/// <b>GPU 가 있어야 한다</b>
///
/// 실측: 그림 8장 1 epoch 이 CPU 248.2초 / CUDA(GTX 1060) 8.6초. 29배다. 300장 20 epoch 이면
/// CPU 로는 수백 시간이라 아예 못 쓴다. GPU 가 없으면 <see cref="TrainAsync"/> 가 미리 막는다.
///
/// <b>ONNX 로는 못 내보낸다</b>
///
/// <c>ObjectDetectionTransformer</c> 가 <c>ICanSaveOnnx</c> 를 구현하지 않는다. 저장되는 것은
/// ML.NET 의 <c>model.zip</c>(약 69MB)뿐이고, 추론도 이것으로 해야 한다.
/// 그래서 <c>Inference/OnnxDmlEngine</c> 은 이 길에 쓰이지 않는다.
/// </remarks>
public static class DetectorTrainer
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>학습 결과를 두는 이름. 데이터셋 폴더 안에 둔다 - 그 라벨로 만든 것이므로.</summary>
    public const string ModelFileName = "detector.zip";

    /// <summary>
    /// 모델이 실제로 보는 크기. <b>학습과 추론이 같아야 한다.</b>
    /// </summary>
    /// <remarks>
    /// AutoFormerV2 는 넣은 그림을 줄이지 않고 그대로 본다. 그래서 크기를 안 맞추면 두 가지가
    /// 한꺼번에 어긋난다 - 1920x1080 으로 학습하면 값이 픽셀 수에 비례해 <b>몇십 배</b> 걸리고,
    /// 실시간 쪽은 320 으로 줄여 넣으므로 <b>학습한 것과 다른 크기</b>를 보게 된다.
    /// 실측으로 그 어긋남은 검출을 망친다(320x200 으로 학습한 모델이 1080p 에서 2개 중 1개만 찾았다).
    ///
    /// 그래서 크기 맞추기를 <b>모델 파이프라인 안에</b> 넣는다. 저장된 모델이 그것을 들고 다니므로
    /// 추론 쪽은 아무것도 안 해도 학습 때와 같은 것을 보게 된다.
    ///
    /// 16:9 로 잡은 것은 게임 화면이 대개 그 비율이라, 늘려 맞출 때 찌그러짐이 거의 없어서다.
    /// 담아 두는 그림 자체는 원본 크기 그대로 둔다 - 나중에 다른 크기로 다시 학습할 수 있고,
    /// 라벨은 0~1 이라 크기를 안 탄다.
    /// </remarks>
    public const int DefaultInputWidth = 320;

    public const int DefaultInputHeight = 180;

    /// <summary>
    /// 화면에서 고를 수 있는 크기들. 16:9 로만 둔다.
    /// </summary>
    /// <remarks>
    /// 실측한 한 장 값(GTX 1060): 320x180 = 220ms · 480x270 = 439ms · 640x360 = 587ms ·
    /// 960x540 = 1,064ms. 학습 시간도 같은 비율로 늘어난다. 작은 몹을 놓칠 때만 키운다.
    /// </remarks>
    public static readonly (int Width, int Height)[] InputSizes =
    [
        (320, 180),
        (480, 270),
        (640, 360),
        (960, 540)
    ];

    /// <summary>모델이 내놓는 열 이름들. 추론 쪽(<see cref="Inference.DetectorModel"/>)과 같아야 한다.</summary>
    public const string PredictedLabelColumn = "PredictedLabel";

    public const string PredictedBoxColumn = "PredictedBoundingBoxes";

    public const string ScoreColumn = "Score";

    public static string ModelPathFor(LabelDataset dataset) => Path.Combine(dataset.Root, ModelFileName);

    /// <summary>
    /// 학습에 넣을 수 있는 것들을 모은다.
    /// </summary>
    /// <remarks>
    /// 라벨이 없는 그림은 뺀다. 우리 목록의 "안 찍음" 은 <b>아직 안 본 그림</b>이라는 뜻이지
    /// 비었다는 뜻이 아니라, 넣으면 "여기엔 아무것도 없다" 를 가르치게 된다.
    /// </remarks>
    public static IReadOnlyList<TrainingSample> Collect(LabelDataset dataset, LabelClasses classes,
                                                       int inputWidth = DefaultInputWidth,
                                                       int inputHeight = DefaultInputHeight)
        => dataset.EnumerateItems()
            .Select(item => TrainingSample.From(item, classes, inputWidth, inputHeight))
            .OfType<TrainingSample>()
            .ToArray();

    /// <summary>
    /// 학습시킨다. 오래 걸리므로 반드시 백그라운드에서 부른다.
    /// </summary>
    /// <remarks>
    /// <b>취소는 epoch 사이에만 듣는다.</b> 학습기가 중간에 멈춰 주지 않아서, 한 epoch 이
    /// 끝나기 전에는 누른 것이 안 먹는다. 화면에서 그렇게 적어 둔다.
    /// </remarks>
    public static Task<TrainingResult> TrainAsync(LabelDataset dataset,
                                                  int maxEpoch,
                                                  IProgress<string>? progress = null,
                                                  CancellationToken token = default,
                                                  int inputWidth = DefaultInputWidth,
                                                  int inputHeight = DefaultInputHeight)
        => Task.Run(() =>
        {
            if (!LibTorchRuntime.IsLoaded)
                throw new InvalidOperationException("libtorch 를 먼저 올려야 한다.");

            var classes = dataset.LoadClasses();

            if (classes.Count == 0)
                throw new InvalidOperationException("몹 이름이 하나도 없습니다. 라벨링 화면에서 먼저 더하세요.");

            progress?.Report("찍어 둔 라벨을 모으는 중...");

            var samples = Collect(dataset, classes, inputWidth, inputHeight);

            if (samples.Count == 0)
                throw new InvalidOperationException("사각형이 찍힌 그림이 없습니다. 라벨링 화면에서 먼저 찍으세요.");

            var boxes = samples.Sum(s => s.Labels.Length);
            var usedGpu = TorchSharp.torch.cuda.is_available();

            Logger.Info($"학습 시작 - 그림 {samples.Count}장 / 사각형 {boxes}개 / 몹 {classes.Count}종 " +
                        $"/ {maxEpoch} epoch / GPU {usedGpu}");

            progress?.Report($"그림 {samples.Count}장 · 사각형 {boxes}개 · 몹 {classes.Count}종 " +
                             $"을 {inputWidth}x{inputHeight} 로 {maxEpoch} epoch 학습합니다 " +
                             $"({(usedGpu ? "GPU" : "CPU")})");

            token.ThrowIfCancellationRequested();

            var ml = new MLContext(seed: 1234);
            var data = ml.Data.LoadFromEnumerable(samples);

            // 학습기가 요구하는 모양으로 맞춘다.
            //   LabelKey : 몹 이름(문자열) -> 키
            //   Image    : 파일 경로 -> 실제 픽셀
            // imageFolder 를 null 로 두면 ImagePath 를 전체 경로로 본다.
            var pipeline = ml.Transforms.Conversion.MapValueToKey("LabelKey", nameof(TrainingSample.Labels))
                .Append(ml.Transforms.LoadImages("Image", imageFolder: null, nameof(TrainingSample.ImagePath)))

                // 넣기 전에 크기를 맞춘다. Fill 은 비율을 안 지키고 늘려 채우는데, 그래야
                // 0~1 라벨이 그대로 곱해져 맞는다(축마다 따로 늘어난다). IsoPad 로 여백을
                // 두면 라벨 쪽에서도 같은 여백을 계산해 줘야 해서 어긋나기 쉽다.
                .Append(ml.Transforms.ResizeImages(
                    "Image", inputWidth, inputHeight, "Image",
                    Microsoft.ML.Transforms.Image.ImageResizingEstimator.ResizingKind.Fill))
                .Append(ml.MulticlassClassification.Trainers.ObjectDetection(
                    new Microsoft.ML.TorchSharp.AutoFormerV2.ObjectDetectionTrainer.Options
                    {
                        LabelColumnName = "LabelKey",
                        BoundingBoxColumnName = nameof(TrainingSample.Box),
                        ImageColumnName = "Image",
                        MaxEpoch = maxEpoch,

                        // 학습기 안에도 점수 문턱이 있고 모델에 같이 저장된다. 기본 0.5 로 두면
                        // 그 아래는 모델이 아예 내놓지 않아, 화면의 "자신 있는 정도" 를 1% 로
                        // 내려도 0건이다 - 실제로 그랬다(14장 · 20 epoch 모델이 27개 중 0개).
                        // 낮게 잡아 두고 거르는 일은 화면 쪽 문턱이 한다. 그래야 못 찾는
                        // 것이 "아예 못 보는지" "자신이 없을 뿐인지" 갈린다.
                        ScoreThreshold = 0.1
                    }))

                // 예측을 번호가 아니라 몹 이름으로 내놓게 한다. 이걸 빼면 추론 쪽이 번호를
                // 받아 classes.txt 로 다시 찾아야 하는데, 그러면 학습할 때의 목록과 그때의
                // 목록이 어긋났을 때 조용히 다른 몹 이름이 붙는다.
                .Append(ml.Transforms.Conversion.MapKeyToValue(
                    PredictedLabelColumn, PredictedLabelColumn));

            var stopwatch = Stopwatch.StartNew();

            var model = pipeline.Fit(data);

            stopwatch.Stop();

            token.ThrowIfCancellationRequested();

            progress?.Report("모델을 저장하는 중...");

            var modelPath = ModelPathFor(dataset);

            ml.Model.Save(model, data.Schema, modelPath);

            // 어떤 크기로 학습했는지 모델 옆에 남긴다. 추론이 이걸 보고 좌표를 되돌린다 -
            // 설정에서 읽으면 크기를 바꾼 순간 옛 모델의 좌표가 조용히 어긋난다.
            new DetectorManifest
            {
                InputWidth = inputWidth,
                InputHeight = inputHeight,
                TrainedAt = DateTime.Now,
                Images = samples.Count,
                Boxes = boxes,
                Epochs = maxEpoch,
                Classes = [.. classes.Names]
            }.Save(modelPath);

            Logger.Info($"학습 끝 - {stopwatch.Elapsed.TotalSeconds:0.0}초, {inputWidth}x{inputHeight}, {modelPath}");

            return new TrainingResult(modelPath, samples.Count, boxes, classes.Count,
                                      stopwatch.Elapsed, usedGpu, $"{inputWidth}x{inputHeight}");
        }, token);
}
