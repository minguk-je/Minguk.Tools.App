using System;
using System.Globalization;
using System.IO;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 데이터셋 내보내기와 ONNX 모델 들이기(설계 4단계).
/// <c>--export-dataset</c> · <c>--import-onnx --model=경로.onnx [--size=640x640]</c> · <c>--use-trained</c>
/// </summary>
internal static class DatasetTools
{
    /// <summary>밖에서 학습할 수 있게 data.yaml(YOLO) 과 coco.json(DETR) 을 쓴다.</summary>
    public static int Export()
    {
        var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);

        Console.WriteLine($"데이터셋: {dataset.Root}");
        Console.WriteLine($"  YOLO : {DatasetExport.WriteYolo(dataset)}");
        Console.WriteLine($"  COCO : {DatasetExport.WriteCoco(dataset)}");
        Console.WriteLine();
        Console.WriteLine("학습하는 법은 docs/ONNX-모델-학습.md 에 적어 두었다.");

        return 0;
    }

    /// <summary>밖에서 학습해 온 .onnx 를 데이터셋에 들이고 쪽지를 ONNX 로 바꾼다.</summary>
    public static int Import(string[] args)
    {
        var modelPath = Program.ArgValue(args, "--model=");

        if (modelPath is null || !File.Exists(modelPath))
        {
            Console.WriteLine("--model=<경로.onnx> 가 있어야 한다.");
            return 1;
        }

        var size = (Program.ArgValue(args, "--size=") ?? "640x640").Split('x');
        var width = int.Parse(size[0], CultureInfo.InvariantCulture);
        var height = int.Parse(size.Length > 1 ? size[1] : size[0], CultureInfo.InvariantCulture);

        var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);
        var target = DetectorFiles.ImportOnnx(dataset, modelPath, width, height);

        Console.WriteLine($"들였다: {target}");
        Console.WriteLine($"지금 쓰는 모델: {DetectorFiles.CurrentFor(dataset)}");
        Console.WriteLine();
        Console.WriteLine("되찾기를 견주려면: --detect-check");
        Console.WriteLine("옛 모델로 돌아가려면: --use-trained");

        return 0;
    }

    /// <summary>쪽지를 우리 학습 모델로 되돌린다.</summary>
    public static int UseTrained()
    {
        var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);

        DetectorFiles.UseTrainedModel(dataset);
        Console.WriteLine($"지금 쓰는 모델: {DetectorFiles.CurrentFor(dataset)}");

        return 0;
    }
}
