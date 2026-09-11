using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 지정한 폴더의 데이터셋으로 학습하고 곧바로 되찾는지 센다. 앱 화면 없이.
/// </summary>
/// <remarks>
/// "학습이 되긴 되는데 실제 그림에서는 0개" 같은 것을 가르려면 조건을 하나씩 바꿔 가며
/// 여러 번 돌려 봐야 한다. 앱을 띄워 버튼을 누르는 것으로는 한 번에 15분씩 손이 묶인다.
/// 여기서는 폴더와 바퀴 수만 받아 <see cref="DetectorTrainer.TrainAsync"/> 를 바로 부른다.
///
/// 기본 데이터셋 폴더를 넘기지 않는다 - 사용자가 모은 것 위에 모델을 덮어쓴다.
/// 확인용은 다른 폴더에 심어서 돌린다.
/// </remarks>
internal static class TrainCheck
{
    public static int Run(string root, int epochs, int width, int height, double? learningRate = null, bool useImageCache = true)
    {
        var dataset = new LabelDataset(root);

        Console.WriteLine($"데이터셋: {dataset.Root}");

        if (LibTorchRuntime.Installed is not { } flavor)
        {
            Console.WriteLine("libtorch 가 없다. 앱에서 학습을 한 번 눌러 받아야 한다.");
            return 1;
        }

        LibTorchRuntime.Load(flavor);

        // 학습기 기본값. 문서에 없어서 실제 값을 찍어 본다 - 학습률이 얼마인지 모르면 실험 방향을 못 잡는다.
        var defaults = new Microsoft.ML.TorchSharp.AutoFormerV2.ObjectDetectionTrainer.Options();
        Console.WriteLine($"학습기 기본값: InitLearningRate={defaults.InitLearningRate} WeightDecay={defaults.WeightDecay} " +
                          $"MaxEpoch={defaults.MaxEpoch} Steps={defaults.Steps} ScoreThreshold={defaults.ScoreThreshold} " +
                          $"IOUThreshold={defaults.IOUThreshold} LogEveryNStep={defaults.LogEveryNStep}");

        var progress = new Progress<string>(m => Console.WriteLine("  " + m));

        try
        {
            if (learningRate is { } lr) Console.WriteLine($"학습률: {lr}");

            if (!useImageCache) Console.WriteLine("그림 캐시 끔 - 매 바퀴 원본을 읽는다");

            var result = DetectorTrainer.TrainAsync(dataset, epochs, progress, CancellationToken.None, width, height, learningRate, useImageCache: useImageCache)
                .GetAwaiter().GetResult();

            Console.WriteLine($"학습 끝: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 학습 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        Console.WriteLine();

        return DetectCheck.Run(dataset, DetectCheck.DefaultMinimumScore);
    }
}
