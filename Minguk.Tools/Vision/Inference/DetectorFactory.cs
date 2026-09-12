using System;
using System.IO;

using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Vision.Inference;

/// <summary>
/// 모델 파일을 보고 어느 검출기로 읽을지 고른다.
/// </summary>
/// <remarks>
/// 부르는 쪽(화면·하네스)은 무엇으로 도는지 몰라도 된다. 쪽지의 <c>engine</c> 이 없으면 지금까지 학습해 둔
/// 것이므로 <see cref="DetectorEngine.Torch"/> 로 본다 - 쪽지 한 줄 때문에 있던 모델을 못 쓰게 되면 안 된다.
/// </remarks>
public static class DetectorFactory
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>모델을 읽는다. 오래 걸리므로(69MB) 백그라운드에서 부른다.</summary>
    public static IDetector Create(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"학습한 모델이 없습니다: {modelPath}", modelPath);

        var manifest = DetectorManifest.Load(modelPath);

        Logger.Debug($"검출기 고름: {manifest.Engine} ({Path.GetFileName(modelPath)})");

        return manifest.Engine switch
        {
            DetectorEngine.Onnx => throw new NotSupportedException(
                "ONNX 검출기는 아직 없습니다(설계 2단계). 쪽지의 engine 을 torch 로 두거나 옛 모델을 쓰세요."),
            _ => DetectorModel.Load(modelPath)
        };
    }
}
