using System;
using System.IO;
using System.Text.Json;

using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 모델 쪽지의 <c>engine</c> 칸. 없으면 지금까지 학습한 것이므로 torch 로 봐야 한다 -
/// 여기가 틀리면 있던 모델이 "지원하지 않는다" 로 안 읽힌다.
/// </summary>
internal static partial class Program
{
    private static void TestDetectorEngine()
    {
        var folder = Path.Combine(Path.GetTempPath(), "minguk-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var modelPath = Path.Combine(folder, "detector.zip");

            // 1) engine 칸이 없는 옛 쪽지
            File.WriteAllText(DetectorManifest.PathFor(modelPath), """{ "inputWidth": 640, "inputHeight": 360 }""");
            var old = DetectorManifest.Load(modelPath);

            Check("쪽지에 engine 이 없으면 torch 로 본다", old.Engine == DetectorEngine.Torch && old.InputWidth == 640,
                  $"{old.Engine}, {old.InputWidth}x{old.InputHeight}");

            // 2) 적어 두면 그대로 읽고, 저장했다 읽어도 같다
            File.WriteAllText(DetectorManifest.PathFor(modelPath), """{ "engine": "Onnx", "inputWidth": 640, "inputHeight": 640 }""");
            var onnx = DetectorManifest.Load(modelPath);

            onnx.Save(modelPath);
            var round = DetectorManifest.Load(modelPath);

            Check("engine 은 적은 대로 읽히고 저장해도 남는다", onnx.Engine == DetectorEngine.Onnx && round.Engine == DetectorEngine.Onnx,
                  $"읽음 {onnx.Engine}, 저장 뒤 {round.Engine}, 쪽지 {File.ReadAllText(DetectorManifest.PathFor(modelPath)).Replace(Environment.NewLine, " ")[..Math.Min(60, File.ReadAllText(DetectorManifest.PathFor(modelPath)).Length)]}");

            // 3) 쪽지는 모델마다 따로다 - 가져오기가 옛 모델의 입력 크기를 덮으면 안 된다(실측: 되찾기 70%→0%)
            {
                var zipPath = Path.Combine(folder, "detector.zip");
                var onnxPath = Path.Combine(folder, "detector.onnx");

                File.WriteAllText(zipPath, "가짜");
                File.WriteAllText(onnxPath, "가짜");

                new DetectorManifest { Engine = DetectorEngine.Torch, InputWidth = 640, InputHeight = 360 }.Save(zipPath);
                new DetectorManifest { Engine = DetectorEngine.Onnx, InputWidth = 640, InputHeight = 640 }.Save(onnxPath);

                var zipManifest = DetectorManifest.Load(zipPath);
                var onnxManifest = DetectorManifest.Load(onnxPath);

                var dataset = new Minguk.Tools.Vision.Labeling.LabelDataset(folder);
                var current = Minguk.Tools.Vision.Training.DetectorFiles.CurrentFor(dataset);

                Minguk.Tools.Vision.Training.DetectorFiles.UseTrainedModel(dataset);
                var afterBack = Minguk.Tools.Vision.Training.DetectorFiles.CurrentFor(dataset);

                Check("쪽지는 모델마다 따로 · 되돌리면 학습한 것으로 간다",
                      zipManifest.InputHeight == 360 && onnxManifest.InputHeight == 640
                      && current == onnxPath && afterBack == zipPath
                      && DetectorManifest.Load(zipPath).InputHeight == 360,
                      $"zip {zipManifest.InputWidth}x{zipManifest.InputHeight}, onnx {onnxManifest.InputWidth}x{onnxManifest.InputHeight}, " +
                      $"고른 것 {Path.GetFileName(current)} → {Path.GetFileName(afterBack)}");

                File.Delete(zipPath);
                File.Delete(onnxPath);
                File.Delete(DetectorManifest.PathFor(onnxPath));
            }

            // 4) 모델 파일이 없으면 팩터리가 그렇게 말한다(엉뚱한 곳에서 터지지 않게)
            var missing = false;

            try { Minguk.Tools.Vision.Inference.DetectorFactory.Create(Path.Combine(folder, "없는모델.zip")).Dispose(); }
            catch (FileNotFoundException) { missing = true; }
            catch (Exception) { }

            Check("모델이 없으면 팩터리가 FileNotFound 로 막는다", missing, missing ? "" : "다른 예외가 났다");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }
}
