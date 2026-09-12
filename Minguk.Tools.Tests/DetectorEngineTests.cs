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

            // 3) 모델 파일이 없으면 팩터리가 그렇게 말한다(엉뚱한 곳에서 터지지 않게)
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
