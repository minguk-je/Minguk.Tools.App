using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 라벨링 화면의 YOLO 학습 길(<see cref="YoloTrainer"/>)을 화면 없이 돌린다 - <c>--yolo-train --root=&lt;폴더&gt; [--epochs=2] [--name=YOLO11n]</c>.
/// </summary>
/// <remarks>
/// <b>--root 를 꼭 준다.</b> 들이기까지 하므로 앱이 쓰는 데이터셋에 돌리면 그 자리의 YOLO11n 이 짧게 학습한 것으로 바뀐다.
/// 시험은 사진·라벨을 가리키기만 하는 <c>Datasets\몹-yolo</c> 에서 한다. 보는 것: 바퀴마다 진행이 오나, ONNX 가 나오나, 들인 쪽지·보관본이 맞나.
/// </remarks>
internal static class YoloTrainProbe
{
    public static int Run(string[] args)
    {
        var root = Program.ArgValue(args, "--root=");

        if (root is null || !Directory.Exists(root))
        {
            Console.WriteLine("--root=<시험 데이터셋 폴더> 가 있어야 한다. 앱이 쓰는 폴더에는 돌리지 마라 - 모델이 바뀐다.");
            return 1;
        }

        var epochs = int.TryParse(Program.ArgValue(args, "--epochs="), NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) ? e : 2;
        var name = Program.ArgValue(args, "--name=") ?? "YOLO11n";
        var dataset = new LabelDataset(root);

        if (YoloTrainer.WhyUnavailable() is { } why)
        {
            Console.WriteLine($"[FAIL] {why}");
            return 1;
        }

        var reports = 0;
        var batches = 0;
        var lastFraction = 0.0;
        var fractionWentBack = false;
        var previews = new System.Collections.Generic.List<(string Path, long Size)>();
        var status = new SyncProgress<string>(message => { if (!message.Contains("묶음", StringComparison.Ordinal)) Console.WriteLine($"  상태: {message}"); });
        var steps = new SyncProgress<TrainingStep>(step =>
        {
            if (step.Fraction + 1e-9 < lastFraction) fractionWentBack = true;
            lastFraction = step.Fraction;

            if (step.IsBatch)
            {
                batches++;
                if (step.Batch == step.BatchCount) Console.WriteLine($"  묶음 {step.Batch}/{step.BatchCount} (바퀴 {step.EpochsDone + 1}) · 진행 {step.Fraction:P0}");
                return;
            }

            reports++;
            Console.WriteLine($"  바퀴 {step.EpochsDone}/{step.MaxEpoch} · loss {step.Loss:0.000}");
        });
        var preview = new SyncProgress<string>(path =>
        {
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            previews.Add((path, size));
            Console.WriteLine($"  묶음 그림: {path} ({size:N0}바이트)");
        });

        var (modelPath, elapsed) = YoloTrainer.TrainAsync(dataset, name, epochs, 0, status, steps, CancellationToken.None, preview).GetAwaiter().GetResult();

        var manifest = DetectorManifest.Load(modelPath);
        var choice = DetectorFiles.CurrentChoice(dataset, DetectorFiles.ListChoices(dataset));
        var failures = 0;

        Console.WriteLine($"[INFO] {elapsed:mm\\:ss} · {modelPath} · 쪽지 {manifest.Summary}");

        if (reports != epochs) { Console.WriteLine($"[FAIL] 바퀴 진행이 {epochs}번이 아니라 {reports}번 왔다"); failures++; }
        else Console.WriteLine($"[PASS] 바퀴마다 진행이 왔다 ({reports}번)");

        // 묶음 진행 - 바퀴마다 묶음 수만큼, 진행률은 뒤로 가지 않는다(바퀴 끝 알림과 섞여도).
        if (batches < epochs || fractionWentBack) { Console.WriteLine($"[FAIL] 묶음 진행이 모자라거나 진행률이 뒤로 갔다 (묶음 {batches}번, 뒤로 {fractionWentBack})"); failures++; }
        else Console.WriteLine($"[PASS] 묶음마다 진행이 왔고 진행률이 앞으로만 간다 (묶음 {batches}번)");

        // 묶음 그림 - 바퀴마다 한 장, 같은 자리에 바꿔 끼운 jpg.
        var jpegOk = previews.Count > 0 && File.Exists(previews[^1].Path) && File.ReadAllBytes(previews[^1].Path) is [0xFF, 0xD8, ..];
        if (previews.Count != epochs || !jpegOk || previews.Any(p => p.Size < 10_000)) { Console.WriteLine($"[FAIL] 묶음 그림이 바퀴마다 오지 않았거나 jpg 가 아니다 ({previews.Count}장)"); failures++; }
        else Console.WriteLine($"[PASS] 바퀴마다 학습 묶음 그림이 왔다 ({previews.Count}장, 마지막 {previews[^1].Size:N0}바이트)");

        if (manifest is not { Engine: DetectorEngine.Onnx, Letterbox: true } || manifest.ModelName != name) { Console.WriteLine("[FAIL] 들인 쪽지가 ONNX·레터박스·이름이 아니다"); failures++; }
        else Console.WriteLine("[PASS] ONNX · 비율 · 이름으로 들였다");

        if (choice is null || choice.Name != name) { Console.WriteLine("[FAIL] 콤보 보관본이 새 모델을 가리키지 않는다"); failures++; }
        else Console.WriteLine($"[PASS] 콤보 보관본이 새 모델이다: {Path.GetFileName(choice.Path)}");

        return failures;
    }

    /// <summary>콘솔에는 동기화 문맥이 없어 <see cref="Progress{T}"/> 가 스레드풀로 흩어진다 - 받은 자리에서 바로 부른다.</summary>
    private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
