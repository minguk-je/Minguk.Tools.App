using System;
using System.Globalization;
using System.IO;
using System.Linq;

using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 데이터셋 내보내기와 ONNX 모델 들이기(설계 4단계).
/// <c>--export-dataset</c> · <c>--import-onnx --model=경로.onnx [--size=640x640] [--fit=늘리기|비율]</c> · <c>--use-trained</c>
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

        // 넣는 방식은 모델 파일에 안 적혀 있다. 기본은 늘리기(D-FINE·RT-DETR 공식 설정), YOLO 계열이면 --fit=비율.
        var fit = Program.ArgValue(args, "--fit=") ?? "늘리기";
        var letterbox = fit is "비율" or "letterbox" or "레터박스";

        var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);

        // 들이기 전에 본다. 뒤에 보면 못 쓸 모델이 이미 자리에 앉은 뒤라, 쓰던 것까지 같이 잃는다.
        if (!CheckProviders(dataset, modelPath, width, height, letterbox) && !args.Contains("--그래도"))
        {
            Console.WriteLine("들이지 않았다. 그래도 들이려면 --그래도 를 붙여라.");
            return 1;
        }

        var target = DetectorFiles.ImportOnnx(dataset, modelPath, width, height, letterbox);

        Console.WriteLine($"들였다: {target} (입력 {width}x{height} · {(letterbox ? "비율 지켜 여백" : "늘려 맞춤")})");
        Console.WriteLine($"지금 쓰는 모델: {DetectorFiles.CurrentFor(dataset)}");
        Console.WriteLine();
        Console.WriteLine("되찾기를 견주려면: --detect-check");
        Console.WriteLine("옛 모델로 돌아가려면: --use-trained");

        return 0;
    }

    /// <summary>
    /// 들인 모델을 GPU 와 CPU 로 한 장씩 돌려 <b>같은 답</b>이 나오는지 본다. 어긋나면 크게 알린다.
    /// </summary>
    /// <remarks>
    /// 여기서 막지 않으면 다음에 알게 되는 자리가 <c>--detect-check</c> 의 "0% 찾음" 이다. 그 숫자는
    /// 모델이 덜 배운 것처럼 보여 엉뚱한 데(바퀴 수·사진 수)를 파게 만든다 - 실제로 그랬다.
    /// 들이는 자리에서 한 장만 돌려 보면 30초로 끝난다.
    /// </remarks>
    private static bool CheckProviders(LabelDataset dataset, string modelPath, int width, int height, bool letterbox)
    {
        var image = Directory.EnumerateFiles(Path.Combine(dataset.Root, "images"), "*.png").FirstOrDefault();

        if (image is null)
        {
            Console.WriteLine("데이터셋에 그림이 없어 GPU·CPU 견주기는 건너뛴다.");
            return true;
        }

        var spec = new Minguk.Tools.Inference.TensorSpec { Width = width, Height = height, Letterbox = letterbox };
        var (agrees, worst, detail) = OnnxAgree.Check(modelPath, OnnxRaw.BuildTensor(image, spec), spec);

        if (agrees)
        {
            Console.WriteLine($"GPU·CPU 견주기: 같은 답 (가장 큰 차이 {worst:P1})");
            Console.WriteLine();

            return true;
        }

        Console.WriteLine($"!! GPU 와 CPU 의 답이 다르다 (가장 큰 차이 {worst:P1}) - 이대로는 못 찾는다 !!");
        Console.WriteLine(detail);
        Console.WriteLine();
        Console.WriteLine("모델이 아니라 실행 공급자 탓이다. 이것을 돌려 고친 파일을 다시 들여라:");
        Console.WriteLine("  python 도구/onnx-DirectML-고치기.py <내보낸.onnx>");
        Console.WriteLine();

        return false;
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
