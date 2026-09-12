using System;
using System.IO;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 데이터셋 폴더의 모델 파일들. 지금 어느 것으로 찾을지 고르고, 밖에서 학습해 온 ONNX 를 들인다.
/// </summary>
/// <remarks>
/// <b>왜 두 파일인가</b> - 우리 학습(ML.NET)은 <c>detector.zip</c> 을 만들고, 밖에서 학습해 온 것은 <c>detector.onnx</c> 다.
/// 둘을 같은 폴더에 두고 쪽지(<see cref="DetectorManifest.Engine"/>)가 어느 쪽인지 말한다 - 그래야 새 것이 나쁘면
/// 쪽지 한 줄로 옛 것으로 돌아간다. 파일을 지우거나 이름을 바꾸게 만들면 되돌아갈 길이 사라진다.
/// </remarks>
public static class DetectorFiles
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public const string OnnxFileName = "detector.onnx";

    /// <summary>밖에서 학습해 온 ONNX 자리(있든 없든).</summary>
    public static string OnnxPathFor(LabelDataset dataset) => Path.Combine(dataset.Root, OnnxFileName);

    /// <summary>
    /// 지금 찾기에 쓸 모델. 쪽지가 ONNX 라 하고 그 파일이 있으면 그것, 아니면 우리가 학습한 zip.
    /// </summary>
    public static string CurrentFor(LabelDataset dataset)
    {
        var onnx = OnnxPathFor(dataset);
        var zip = DetectorTrainer.ModelPathFor(dataset);

        // 가져온 ONNX 가 있고 그 쪽지가 ONNX 라고 하면 그것을 쓴다. 없으면 우리가 학습한 zip.
        if (File.Exists(onnx) && DetectorManifest.Load(onnx).Engine == DetectorEngine.Onnx) return onnx;

        // zip 이 없고 onnx 만 있으면 그것으로라도 돈다 - 화면이 아무것도 못 하는 것보다 낫다.
        return File.Exists(zip) || !File.Exists(onnx) ? zip : onnx;
    }

    /// <summary>
    /// 밖에서 학습해 온 <c>.onnx</c> 를 데이터셋에 들인다. 파일을 복사하고 쪽지를 ONNX 로 바꾼다.
    /// </summary>
    /// <param name="inputWidth">모델이 받는 입력 크기. 모델이 크기를 안 박아 두었을 때(RT-DETR·D-FINE) 쓰인다.</param>
    /// <returns>들인 파일 자리.</returns>
    public static string ImportOnnx(LabelDataset dataset, string sourcePath, int inputWidth = 640, int inputHeight = 640)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException($"가져올 모델이 없습니다: {sourcePath}", sourcePath);

        Directory.CreateDirectory(dataset.Root);

        var target = OnnxPathFor(dataset);

        // 같은 파일을 그대로 다시 들이는 경우(폴더 안의 것을 고름)는 복사하지 않는다.
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, target, overwrite: true);

        // 쪽지는 이 모델 것으로 따로 쓴다(detector.onnx.json). zip 쪽 쪽지를 덮으면 옛 모델의 입력 크기가 망가진다.
        var manifest = DetectorManifest.Load(target);

        manifest.Engine = DetectorEngine.Onnx;
        manifest.InputWidth = inputWidth;
        manifest.InputHeight = inputHeight;
        manifest.TrainedAt = File.GetLastWriteTime(target);
        manifest.Classes = [.. dataset.LoadClasses().Names];

        // 밖에서 학습한 것이라 우리 쪽 학습 기록(바퀴·loss·되찾기)은 남의 것이 된다. 지운다.
        manifest.Epochs = 0;
        manifest.FinalLoss = null;
        manifest.RecallFound = null;
        manifest.RecallLabels = null;
        manifest.RecallExtra = null;
        manifest.RecallThreshold = null;

        manifest.Save(target);

        Logger.Info($"ONNX 모델을 들였다: {target} ({new FileInfo(target).Length / 1_048_576.0:N1} MB, 입력 {inputWidth}x{inputHeight})");

        return target;
    }

    /// <summary>쪽지를 우리 학습(zip) 쪽으로 되돌린다. 가져온 ONNX 가 나쁠 때 한 줄로 돌아가는 길.</summary>
    public static void UseTrainedModel(LabelDataset dataset)
    {
        var onnx = OnnxPathFor(dataset);

        if (!File.Exists(onnx)) return;

        // 가져온 것의 쪽지만 Torch 로 바꾼다 - 그러면 CurrentFor 가 zip 을 고른다. 파일은 지우지 않는다(되돌아갈 길).
        var manifest = DetectorManifest.Load(onnx);

        manifest.Engine = DetectorEngine.Torch;
        manifest.Save(onnx);

        Logger.Info("가져온 ONNX 를 쉬게 두고 우리 학습 모델(torch)로 돌아갔다.");
    }
}
