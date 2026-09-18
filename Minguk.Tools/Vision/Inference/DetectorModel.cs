using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.ML;
using Microsoft.ML.Data;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Vision.Inference;

/// <summary>
/// 모델이 찾아낸 것 하나.
/// </summary>
/// <param name="Label">검출 이름.</param>
/// <param name="Box">자리. 라벨과 같은 0~1 이고, <c>ClassId</c> 도 손으로 찍은 것과 같은 번호다.</param>
/// <param name="Score">얼마나 자신 있는지 (0~1).</param>
public readonly record struct Detection(string Label, LabelBox Box, float Score)
{
    public int ClassId => Box.ClassId;

    public string Describe => $"{Label} {Score:P0}";
}

/// <summary>
/// 모델이 내놓는 열들을 받는 그릇.
/// </summary>
/// <remarks>
/// 열 이름은 <see cref="DetectorTrainer"/> 가 만든 것과 같아야 한다. 하나라도 어긋나면
/// 조용히 빈 배열이 와서 "아무것도 못 찾았다" 로 보인다 - 그래서 이름을 상수로 묶어 둔다.
/// </remarks>
internal sealed class DetectionPrediction
{
    [ColumnName(DetectorTrainer.PredictedLabelColumn)]
    public string[]? PredictedLabel { get; set; }

    [ColumnName(DetectorTrainer.PredictedBoxColumn)]
    public float[]? PredictedBoundingBoxes { get; set; }

    [ColumnName(DetectorTrainer.ScoreColumn)]
    public float[]? Score { get; set; }
}

/// <summary>
/// 학습해 둔 모델로 그림에서 검출을 찾는다.
/// </summary>
/// <remarks>
/// <b>libtorch 가 먼저 올라와 있어야 한다.</b> 모델 안에 TorchSharp 가 들어 있어서,
/// 안 올린 채로 부르면 알아보기 힘든 곳에서 터진다. <see cref="Load"/> 가 미리 막는다.
///
/// <b>왜 ONNX 가 아닌가</b>
///
/// 이 모델은 ONNX 로 못 나온다(<c>ObjectDetectionTransformer</c> 가 <c>ICanSaveOnnx</c> 를
/// 구현하지 않는다). 그래서 <c>Inference/OnnxDmlEngine</c> 이 아니라 여기로 돈다.
/// 남이 만든 <c>.onnx</c> 를 돌릴 일이 생기면 그때 그쪽을 꺼내 쓰면 된다.
///
/// <b>스레드</b>
///
/// <see cref="PredictionEngine{TSrc,TDst}"/> 는 스레드 안전하지 않다. 이 클래스도 마찬가지라
/// 프레임마다 부르는 쪽에서 한 스레드로 몰아야 한다.
///
/// <b>이것은 둘 중 하나다</b> - <see cref="IDetector"/> 의 구현이고, 쪽지의 engine 이 torch(기본)일 때 쓰인다.
/// 부르는 쪽은 <see cref="DetectorFactory"/> 로 받는다.
/// </remarks>
public sealed class DetectorModel : IDetector
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly MLContext _ml;
    private readonly PredictionEngine<TrainingSample, DetectionPrediction> _engine;

    private bool _disposed;

    private DetectorModel(MLContext ml, PredictionEngine<TrainingSample, DetectionPrediction> engine,
                          string path, DetectorManifest manifest)
    {
        _ml = ml;
        _engine = engine;

        ModelPath = path;
        Manifest = manifest;
    }

    public string ModelPath { get; }

    /// <summary>이 모델이 어떤 크기로 학습됐는지. 좌표를 되돌릴 때 쓴다.</summary>
    public DetectorManifest Manifest { get; }

    /// <summary>
    /// 모델을 읽는다. 오래 걸리므로(69MB) 백그라운드에서 부른다.
    /// </summary>
    public static DetectorModel Load(string modelPath)
    {
        if (!LibTorchRuntime.IsLoaded)
            throw new InvalidOperationException("libtorch 를 먼저 올려야 한다.");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"학습한 모델이 없습니다: {modelPath}", modelPath);

        var ml = new MLContext();
        var model = ml.Model.Load(modelPath, out _);
        var engine = ml.Model.CreatePredictionEngine<TrainingSample, DetectionPrediction>(model);

        // 학습할 때의 크기를 모델 옆 쪽지에서 읽는다. 설정에서 읽으면 크기를 바꾼 순간
        // 옛 모델의 좌표가 조용히 어긋난다.
        var manifest = DetectorManifest.Load(modelPath);

        Logger.Info($"검출 모델을 읽었다: {modelPath} ({manifest.Describe})");

        return new DetectorModel(ml, engine, modelPath, manifest);
    }

    /// <summary>
    /// 그림 하나에서 찾는다.
    /// </summary>
    /// <param name="imagePath">볼 그림. 크기는 아무래도 좋다 - 모델이 제 크기로 맞춘다.</param>
    /// <param name="minimumScore">이보다 자신 없는 것은 버린다.</param>
    /// <remarks>
    /// 모델은 <b>픽셀</b> 좌표를 내놓는다. 우리는 0~1 로 다루므로 여기서 나눈다 -
    /// 안 나누면 캔버스에 그릴 때 사각형이 전부 화면 밖으로 나간다.
    /// </remarks>
    public IReadOnlyList<Detection> Detect(string imagePath, LabelClasses classes, float minimumScore = 0.5f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 학습 때와 같은 그릇으로 넣는다. 라벨 쪽은 비워 둔다 - 예측에는 안 쓰인다.
        var prediction = _engine.Predict(new TrainingSample { ImagePath = imagePath });

        // 넣은 파일이 몇 픽셀이든 파이프라인이 학습할 때의 크기로 늘려 놓고, 망은 그것을 본다.
        // 그러니 돌아온 좌표도 그 크기의 것이다 - 파일 크기로 나누면 틀린다.
        return Convert(prediction, classes, Manifest.InputWidth, Manifest.InputHeight, minimumScore);
    }

    /// <summary>
    /// 예측을 우리 좌표로 옮긴다.
    /// </summary>
    /// <remarks>
    /// 세 배열의 길이가 서로 맞는지 본다. 사각형은 넷씩 묶여 있어 개수가 어긋나면 엉뚱한
    /// 이름이 엉뚱한 자리에 붙는데, 화면에는 그럴싸하게 그려져서 눈으로는 못 잡는다.
    /// </remarks>
    internal static IReadOnlyList<Detection> Convert(DetectionPrediction prediction,
                                                     LabelClasses classes,
                                                     int width, int height, float minimumScore)
    {
        var boxes = prediction.PredictedBoundingBoxes ?? [];
        var labels = prediction.PredictedLabel ?? [];
        var scores = prediction.Score ?? [];

        if (boxes.Length == 0) return [];

        var count = boxes.Length / 4;

        if (labels.Length < count || scores.Length < count)
        {
            Logger.Warn($"예측 길이가 안 맞는다 - 사각형 {count}개 / 이름 {labels.Length}개 / 점수 {scores.Length}개");

            count = Math.Min(count, Math.Min(labels.Length, scores.Length));
        }

        var result = new List<Detection>(count);

        for (var i = 0; i < count; i++)
        {
            if (scores[i] < minimumScore) continue;

            // 이름을 번호로 되돌린다. 손으로 찍은 사각형과 같은 색으로 그려야 어느 검출을
            // 찾았는지 한눈에 갈린다. 목록에 없는 이름이면(학습 뒤 검출을 지웠거나 한 경우)
            // 번호를 못 주므로 0 으로 둔다 - 색만 어긋나고 이름은 그대로 뜬다.
            var classId = Math.Max(classes.IndexOf(labels[i]), 0);

            // 모델은 픽셀로 준다. 0~1 로 바꾼다.
            var box = LabelBox.FromCorners(
                classId,
                boxes[(i * 4) + 0] / width,
                boxes[(i * 4) + 1] / height,
                boxes[(i * 4) + 2] / width,
                boxes[(i * 4) + 3] / height);

            if (box.IsTooSmall) continue;

            result.Add(new Detection(labels[i], box, scores[i]));
        }

        // 자신 있는 것부터. 겹쳐 그릴 때 확실한 것이 위로 오게.
        return result.OrderByDescending(d => d.Score).ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        _engine.Dispose();
    }
}
