using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    /// <param name="letterbox">
    /// 학습할 때 비율을 지키고 여백을 넣었는가. D-FINE·RT-DETR 공식 설정은 <c>Resize [640,640]</c> 하나뿐이라
    /// 거짓(늘리기)이다. 여기가 학습과 다르면 한 마리도 못 찾는다.
    /// </param>
    /// <returns>들인 파일 자리.</returns>
    public static string ImportOnnx(LabelDataset dataset, string sourcePath, int inputWidth = 640, int inputHeight = 640, bool letterbox = false,
                                    string? modelName = null)
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
        // 이름을 안 주면 비운다 - 옛 이름이 남으면 D-FINE 을 들였는데 "YOLO11n" 이라고 보인다. 화면은 그때 "ONNX 모델" 이라 부른다.
        manifest.ModelName = string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim();
        manifest.InputWidth = inputWidth;
        manifest.InputHeight = inputHeight;
        manifest.Letterbox = letterbox;
        manifest.TrainedAt = File.GetLastWriteTime(target);
        manifest.Classes = [.. dataset.LoadClasses().Names];

        // 밖에서 학습한 것이라 우리 쪽 학습 기록(바퀴·loss·재현율)은 남의 것이 된다. 지운다.
        manifest.Epochs = 0;
        manifest.FinalLoss = null;
        manifest.RecallFound = null;
        manifest.RecallLabels = null;
        manifest.RecallExtra = null;
        manifest.RecallThreshold = null;

        manifest.Save(target);

        Logger.Info($"ONNX 모델을 들였다: {target} ({new FileInfo(target).Length / 1_048_576.0:N1} MB, " +
                    $"입력 {inputWidth}x{inputHeight} {(letterbox ? "레터박스" : "늘리기")})");

        return target;
    }

    // ── 모델 고르기(라벨링 화면 콤보) ─────────────────────────────────────

    /// <summary>
    /// 고를 수 있는 모델들. 데이터셋 폴더의 <c>detector.&lt;이름&gt;.onnx</c>(보관본) 전부.
    /// </summary>
    /// <remarks>
    /// <b>왜 보관본을 따로 두고 복사하나</b> - 몹 찾기·하네스·들이기가 모두 한 자리(<c>detector.onnx</c>)를 본다. 그 규칙을 그대로 두고
    /// "무엇을 그 자리에 앉힐지" 만 고르게 하면 다른 곳을 하나도 안 고쳐도 된다. 시험은 YOLO11n, 배포는 D-FINE-N 으로 번갈아
    /// 쓰게 되어(2026-09-14) 명령을 외우지 않고 고를 수 있어야 했다. 보관본의 쪽지(이름·레터박스·재현율)도 같이 옮긴다.
    /// </remarks>
    /// <summary>가상 항목으로 내놓을 이름들 - 학습기가 실제로 돌릴 수 있는 이름 그대로(YoloTrainer.WeightsFor·DFineTrainer.Handles 가 안다).</summary>
    private static readonly string[] TrainableArchitectures = ["YOLO11n", "D-FINE-N"];

    public static IReadOnlyList<DetectorChoice> ListChoices(LabelDataset dataset)
    {
        var choices = new List<DetectorChoice>();

        if (Directory.Exists(dataset.Root))
        {
            foreach (var path in Directory.EnumerateFiles(dataset.Root, "detector.*.onnx").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var manifest = DetectorManifest.Load(path);
                var stem = Path.GetFileNameWithoutExtension(path)["detector.".Length..];

                choices.Add(new DetectorChoice(manifest.ModelName ?? stem, path, manifest.Summary));
            }
        }

        // 프로젝트를 막 만들어 보관본이 하나도 없으면 콤보가 비어 아무것도 못 고른다 - 그러면 첫 학습을 영영 못 누른다
        // (학습 버튼이 "콤보에서 고른 모델" 을 요구한다). 아직 없는 이름은 파일 없는 가상 항목으로 채워 준다 -
        // 학습이 끝나면 진짜 보관본이 생겨 이 자리를 대신한다(사용자, 2026-09-19).
        foreach (var name in TrainableArchitectures)
        {
            if (choices.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) continue;

            choices.Add(new DetectorChoice(name, string.Empty, "아직 학습 안 함 - 「학습」을 누르면 새로 만듭니다."));
        }

        return choices;
    }

    /// <summary>
    /// 지금 몹 찾기 자리에 앉은 것이 목록의 어느 것인지. 모르면(보관본이 없는 모델을 들였으면) null.
    /// </summary>
    /// <remarks>이름이 같고 파일 크기가 같으면 같은 것으로 본다 - 15MB 를 매번 해시하지 않는다.</remarks>
    public static DetectorChoice? CurrentChoice(LabelDataset dataset, IReadOnlyList<DetectorChoice> choices)
    {
        var current = CurrentFor(dataset);

        if (!File.Exists(current)) return null;

        var name = DetectorManifest.Load(current).ModelName;
        var length = new FileInfo(current).Length;

        // 옛 TorchSharp 모델(detector.zip)이 앉아 있으면 ONNX 가 아니라 여기서 걸러진다 - 콤보에는 없는 모델이다.
        if (!string.Equals(Path.GetExtension(current), ".onnx", StringComparison.OrdinalIgnoreCase)) return null;

        return choices.FirstOrDefault(c => new FileInfo(c.Path).Length == length
                                           && string.Equals(DetectorManifest.Load(c.Path).ModelName, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// 고른 모델을 몹 찾기 자리에 앉힌다. 보관본과 그 쪽지를 <c>detector.onnx</c> 로 복사한다.
    /// </summary>
    /// <remarks>
    /// 복사한 뒤 파일 시각을 지금으로 찍는다 - <c>File.Copy</c> 는 원본 시각을 그대로 옮기는데, 켜 둔 몹 찾기는 <b>시각이 바뀌었나</b>로
    /// 새 모델을 알아채기 때문이다(<c>RecognizingCaptureViewModelBase.MaybeReloadDetector</c>).
    /// </remarks>
    public static void Use(LabelDataset dataset, DetectorChoice choice)
    {
        var target = OnnxPathFor(dataset);

        File.Copy(choice.Path, target, overwrite: true);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow);

        var manifest = DetectorManifest.Load(choice.Path);
        manifest.Engine = DetectorEngine.Onnx;
        manifest.Save(target);

        Logger.Info($"몹 찾기 모델을 바꿨다: {choice.Name} ({Path.GetFileName(choice.Path)} → {Path.GetFileName(target)})");
    }

    /// <summary>
    /// 들인 모델을 보관본(<c>detector.&lt;이름&gt;.onnx</c>)으로도 남긴다 - 그래야 콤보에 뜨고 나중에 다시 고를 수 있다.
    /// </summary>
    /// <returns>보관본 자리. 이름이 없으면 남기지 않고 null.</returns>
    public static string? KeepAsChoice(LabelDataset dataset, string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;

        var source = OnnxPathFor(dataset);
        if (!File.Exists(source)) return null;

        var safe = string.Concat(modelName.Trim().ToLowerInvariant().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '-' : ch));
        var target = Path.Combine(dataset.Root, $"detector.{safe}.onnx");

        File.Copy(source, target, overwrite: true);
        DetectorManifest.Load(source).Save(target);

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

/// <summary>콤보에 뜨는 모델 하나.</summary>
/// <param name="Name">사람이 읽을 이름("YOLO11n", "D-FINE-N").</param>
/// <param name="Path">보관본(ONNX) 자리. 가상 항목(아직 학습 안 함)이면 빈 글.</param>
/// <param name="Summary">쪽지 한 줄(재현율 등). 가상 항목이면 안내 문구.</param>
public sealed record DetectorChoice(string Name, string Path, string Summary)
{
    /// <summary>아직 학습을 한 번도 안 해 보관본이 없는 이름뿐인 항목인가.</summary>
    public bool IsVirtual => string.IsNullOrEmpty(Path);
}
