using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 학습한 모델이 라벨을 찍어 둔 그림에서 그 사각형을 다시 찾는지 센다.
/// </summary>
/// <remarks>
/// 학습이 "끝났습니다" 로 끝나도 아무것도 못 찾는 모델일 수 있다. 라벨이 어긋났거나,
/// 크기가 작아 뭉개졌거나, 바퀴가 모자란 경우다. 화면에서 한 장씩 <c>찾아보기</c> 를 눌러
/// 보는 대신 전부 돌려 숫자로 본다.
///
/// 학습에 쓴 그림으로 재는 것이라 <b>외운 것</b>도 맞은 것으로 센다. 그러니 여기서 못 찾으면
/// 확실히 문제고, 다 찾았다고 새 장면에서도 찾는다는 뜻은 아니다. 새 장면은 캡처 화면의
/// 몹 찾기로 본다.
///
/// libtorch(4GB)와 모델이 있어야 돈다. <c>--detect-check</c> 로 따로 부른다.
/// </remarks>
internal static class DetectCheck
{
    /// <summary>앱의 "자신 있는 정도" 기본값과 같다. 다르게 두면 하네스 숫자와 화면이 안 맞는다.</summary>
    public const float DefaultMinimumScore = 0.5f;

    public static int Run() => Run(new LabelDataset(LabelDataset.ConfiguredRoot), DefaultMinimumScore);

    /// <param name="minimumScore">문턱. 낮추면 놓친 것의 점수가 얼마였는지 보인다 - 아예 못 보는지, 자신이 없을 뿐인지 가른다.</param>
    public static int Run(float minimumScore) => Run(new LabelDataset(LabelDataset.ConfiguredRoot), minimumScore);

    /// <param name="dataset">확인용 데이터셋을 다른 폴더에 두고 볼 때. 기본 폴더는 사용자가 모은 것이라 거기서 실험하지 않는다.</param>
    public static int Run(LabelDataset dataset, float minimumScore)
    {
        var MinimumScore = minimumScore;

        Console.WriteLine($"데이터셋: {dataset.Root}");

        if (LibTorchRuntime.Installed is not { } flavor)
        {
            Console.WriteLine("libtorch 가 없다. 앱에서 학습을 한 번 눌러 받아야 한다.");
            return 1;
        }

        LibTorchRuntime.Load(flavor);

        var modelPath = DetectorTrainer.ModelPathFor(dataset);

        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"학습한 모델이 없다: {modelPath}");
            return 1;
        }

        using var model = DetectorModel.Load(modelPath);

        Console.WriteLine($"모델: {model.Manifest.Describe}");
        Console.WriteLine($"기준: 자신 있는 정도 {MinimumScore:P0} 이상 · 겹침(IoU) {DetectionMatch.MatchIou:0.0} 이상이면 찾은 것");
        Console.WriteLine();

        var classes = dataset.LoadClasses();
        var items = dataset.EnumerateItems().Where(item => item.HasLabel).ToList();

        var totalLabels = 0;
        var totalFound = 0;
        var totalExtra = 0;
        var times = new List<double>();

        foreach (var item in items)
        {
            var labels = LabelFile.Load(item.LabelPath, out _).ToList();

            var watch = Stopwatch.StartNew();
            var detections = model.Detect(item.ImagePath, classes, MinimumScore);
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);

            // 짝짓기는 앱과 같은 계산이다(DetectionMatch). 놓친 것의 크기만 여기서 따로 적는다.
            var match = DetectionMatch.Match(labels, detections);
            var found = match.Found;
            var extra = match.Extra;

            var misses = new List<string>();
            foreach (var label in labels)
            {
                var best = 0d;
                foreach (var detection in detections) best = Math.Max(best, DetectionMatch.Iou(label, detection.Box));
                if (best < DetectionMatch.MatchIou)
                    misses.Add($"{label.Width * 1920:0}x{label.Height * 1080:0}px" + (best > 0 ? $" 겹침 {best:0.00}" : string.Empty));
            }

            totalLabels += labels.Count;
            totalFound += found;
            totalExtra += extra;

            var mark = found == labels.Count && extra == 0 ? "PASS" : found == labels.Count ? "WARN" : "FAIL";
            var scores = string.Join(" ", detections.Select(d => $"{d.Score:P0}"));

            Console.WriteLine($"[{mark}] {item.Name}  라벨 {labels.Count} · 찾음 {found} · 헛것 {extra}  ({watch.ElapsedMilliseconds:N0} ms)  {scores}"
                              + (misses.Count > 0 ? "  놓침: " + string.Join(", ", misses) : string.Empty));
        }

        times.Sort();

        Console.WriteLine();
        Console.WriteLine($"== 라벨 {totalLabels}개 중 {totalFound}개 찾음 ({(totalLabels == 0 ? 0 : 100.0 * totalFound / totalLabels):0}%) · 헛것 {totalExtra}개 · 한 장 중앙값 {(times.Count == 0 ? 0 : times[times.Count / 2]):N0} ms ==");

        return totalLabels > 0 && totalFound == totalLabels ? 0 : 1;
    }

}
