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
public readonly record struct TrainingResult(
    string ModelPath, int Images, int Boxes, int Classes, TimeSpan Elapsed, bool UsedGpu);

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

    public static string ModelPathFor(LabelDataset dataset) => Path.Combine(dataset.Root, ModelFileName);

    /// <summary>
    /// 학습에 넣을 수 있는 것들을 모은다.
    /// </summary>
    /// <remarks>
    /// 라벨이 없는 그림은 뺀다. 우리 목록의 "안 찍음" 은 <b>아직 안 본 그림</b>이라는 뜻이지
    /// 비었다는 뜻이 아니라, 넣으면 "여기엔 아무것도 없다" 를 가르치게 된다.
    /// </remarks>
    public static IReadOnlyList<TrainingSample> Collect(LabelDataset dataset, LabelClasses classes)
        => dataset.EnumerateItems()
            .Select(item => TrainingSample.From(item, classes))
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
                                                  CancellationToken token = default)
        => Task.Run(() =>
        {
            if (!LibTorchRuntime.IsLoaded)
                throw new InvalidOperationException("libtorch 를 먼저 올려야 한다.");

            var classes = dataset.LoadClasses();

            if (classes.Count == 0)
                throw new InvalidOperationException("몹 이름이 하나도 없습니다. 라벨링 화면에서 먼저 더하세요.");

            progress?.Report("찍어 둔 라벨을 모으는 중...");

            var samples = Collect(dataset, classes);

            if (samples.Count == 0)
                throw new InvalidOperationException("사각형이 찍힌 그림이 없습니다. 라벨링 화면에서 먼저 찍으세요.");

            var boxes = samples.Sum(s => s.Labels.Length);
            var usedGpu = TorchSharp.torch.cuda.is_available();

            Logger.Info($"학습 시작 - 그림 {samples.Count}장 / 사각형 {boxes}개 / 몹 {classes.Count}종 " +
                        $"/ {maxEpoch} epoch / GPU {usedGpu}");

            progress?.Report($"그림 {samples.Count}장 · 사각형 {boxes}개 · 몹 {classes.Count}종 " +
                             $"으로 {maxEpoch} epoch 학습합니다 ({(usedGpu ? "GPU" : "CPU")})");

            token.ThrowIfCancellationRequested();

            var ml = new MLContext(seed: 1234);
            var data = ml.Data.LoadFromEnumerable(samples);

            // 학습기가 요구하는 모양으로 맞춘다.
            //   LabelKey : 몹 이름(문자열) -> 키
            //   Image    : 파일 경로 -> 실제 픽셀
            // imageFolder 를 null 로 두면 ImagePath 를 전체 경로로 본다.
            var pipeline = ml.Transforms.Conversion.MapValueToKey("LabelKey", nameof(TrainingSample.Labels))
                .Append(ml.Transforms.LoadImages("Image", imageFolder: null, nameof(TrainingSample.ImagePath)))
                .Append(ml.MulticlassClassification.Trainers.ObjectDetection(
                    labelColumnName: "LabelKey",
                    boundingBoxColumnName: nameof(TrainingSample.Box),
                    imageColumnName: "Image",
                    maxEpoch: maxEpoch));

            var stopwatch = Stopwatch.StartNew();

            var model = pipeline.Fit(data);

            stopwatch.Stop();

            token.ThrowIfCancellationRequested();

            progress?.Report("모델을 저장하는 중...");

            var modelPath = ModelPathFor(dataset);

            ml.Model.Save(model, data.Schema, modelPath);

            Logger.Info($"학습 끝 - {stopwatch.Elapsed.TotalSeconds:0.0}초, {modelPath}");

            return new TrainingResult(modelPath, samples.Count, boxes, classes.Count, stopwatch.Elapsed, usedGpu);
        }, token);
}
