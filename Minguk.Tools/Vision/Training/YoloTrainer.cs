using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// YOLO(Ultralytics) 를 파이썬으로 학습해 ONNX 로 내보내고 데이터셋에 들인다. 라벨링 화면에서 "쓰는 모델" 이 YOLO 일 때 학습 버튼이 부른다.
/// </summary>
/// <remarks>
/// <b>왜 파이썬을 띄우나</b> - Ultralytics 는 파이썬뿐이다. 학습은 앱 밖 프로세스로 돌리고, 앱은 바퀴마다 줄을 읽어 진행 막대·loss 를 움직인다.
/// 환경은 학습 폴더의 <c>yolo-venv</c>(torch cu126 + ultralytics) 를 쓴다. 없으면 만들지 않고 알린다 - 2.5GB 를 버튼 한 번에
/// 받게 하면 안 된다. 처음 한 번은 <c>도구\학습-환경-준비.ps1</c> 이 만든다.
///
/// <b>내 PC 시험용이다</b> - Ultralytics 는 AGPL 이라 학습한 가중치까지 그 조건이다. 배포 모델은 D-FINE-N 이다(CLAUDE.md "모델 정책").
///
/// <b>진행 읽기</b> - 진행 막대는 <c>\r</c> 로 줄을 덮어써 줄 단위로 읽으면 바퀴 끝에만 한 줄이 온다. 그 줄(<c>3/60 1.31G 1.95 2.26 1.34 … 100%</c>)의
/// 앞 두 수가 바퀴, 다음 수가 box loss 다. 바퀴 안의 진행은 스크립트가 묶음마다 찍는 <c>BATCH</c> 줄로, 학습 묶음 그림은 바퀴마다 <c>PREVIEW</c> 줄로 온다
/// (사용자, 2026-09-15 - 라벨링 화면에서 학습하는 모습을 본다).
/// </remarks>
public static class YoloTrainer
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>파이썬 환경 자리. 학습 폴더 하나에 다 모여 있다(<see cref="TrainingPaths"/>).</summary>
    public static string VenvPython => TrainingPaths.YoloPython;

    /// <summary>학습 결과를 두는 자리.</summary>
    public static string RunsRoot => TrainingPaths.Runs;

    /// <summary>앱 출력의 스크립트 자리(csproj 가 도구\yolo-학습.py 를 Tools 아래로 복사한다).</summary>
    public static string ScriptPath => Path.Combine(AppContext.BaseDirectory, "Tools", "yolo-학습.py");

    /// <summary>학습할 수 있는가. 못 하면 사람에게 할 말을 돌려준다.</summary>
    public static string? WhyUnavailable()
    {
        if (!File.Exists(VenvPython))
            return $"YOLO 학습용 파이썬 환경이 없습니다: {Path.GetDirectoryName(Path.GetDirectoryName(VenvPython))}. " +
                   "저장소의 도구\\학습-환경-준비.ps1 을 한 번 돌리면 만들어집니다(약 2.5GB).";

        if (!File.Exists(ScriptPath))
            return $"학습 스크립트가 없습니다: {ScriptPath}. 앱을 다시 빌드하세요.";

        return null;
    }

    /// <summary>
    /// "YOLO11n" 같은 모델 이름에서 Ultralytics 가중치 이름("yolo11n")을 뽑는다. YOLO 가 아니면 null.
    /// </summary>
    public static string? WeightsFor(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;

        var match = Regex.Match(modelName.Trim(), @"^yolo(v?\d+[nsmlx])$", RegexOptions.IgnoreCase);

        return match.Success ? "yolo" + match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>고를 수 있는 학습 크기(정사각 한 변). 키우면 작은 몹을 더 찾고 학습·찾기가 느려진다(사용자, 2026-09-15 - 그래픽 카드는 나중에 바꾼다).</summary>
    public static readonly int[] ImageSizes = [480, 640, 960, 1280];

    public const int DefaultImageSize = 640;

    /// <summary>
    /// 한 번에 넣을 그림 수. 640 기준 값(n 8장 · 그 밖 4장)에서 픽셀 수에 반비례해 줄인다 - 메모리는 크기² × 묶음에 비례한다.
    /// </summary>
    /// <remarks>
    /// 3GB 카드에서 n 은 640·batch 8 이 1.3GB, s 는 batch 4 로 맞았다(실측). 그래서 n 은 960 → 4, 1280 → 2 로 같은 메모리 안팎이 된다.
    /// 그래도 넘치면 Ultralytics 가 첫 바퀴에서 묶음을 반으로 줄여 다시 한다(OOM 재시도).
    /// </remarks>
    public static int BatchFor(string weights, int imageSize)
    {
        var at640 = weights.EndsWith('n') ? 8 : 4;
        var scaled = at640 * (640.0 / imageSize) * (640.0 / imageSize);

        return Math.Clamp((int)Math.Round(scaled), 1, 16);
    }

    /// <summary>학습 → ONNX 내보내기 → 데이터셋에 들이기 → 보관본 갱신. 돌려주는 것은 들인 detector.onnx 자리.</summary>
    /// <param name="modelName">화면에 뜨는 이름("YOLO11n"). 가중치 이름은 여기서 뽑는다.</param>
    /// <param name="device">GPU 번호.</param>
    /// <param name="previews">바퀴마다 학습 묶음 그림(라벨 상자를 그린 jpg) 경로. 같은 자리의 파일이 바뀌어 끼워진다.</param>
    /// <param name="imageSize">학습·내보내기 크기(<see cref="ImageSizes"/>). 32 의 배수로 맞춘다.</param>
    public static async Task<(string ModelPath, TimeSpan Elapsed)> TrainAsync(LabelDataset dataset, string modelName, int epochs, int device,
                                                                           IProgress<string> status, IProgress<TrainingStep> steps,
                                                                           CancellationToken token, IProgress<string>? previews = null,
                                                                           int imageSize = DefaultImageSize)
    {
        if (WhyUnavailable() is { } why) throw new InvalidOperationException(why);

        var weights = WeightsFor(modelName) ?? throw new InvalidOperationException($"YOLO 모델 이름이 아니다: {modelName}");

        // YOLO 는 32 의 배수만 받는다(아니면 Ultralytics 가 알아서 올려 ONNX 크기와 어긋난다).
        imageSize = Math.Clamp((imageSize + 16) / 32 * 32, 320, 1920);
        var batch = BatchFor(weights, imageSize);

        Directory.CreateDirectory(RunsRoot);

        var start = new ProcessStartInfo(VenvPython)
        {
            WorkingDirectory = RunsRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in new[] { "-X", "utf8", "-u", ScriptPath, "--root", dataset.Root, "--runs", RunsRoot,
                                     "--model", weights, "--epochs", epochs.ToString(CultureInfo.InvariantCulture),
                                     "--batch", batch.ToString(CultureInfo.InvariantCulture), "--device", device.ToString(CultureInfo.InvariantCulture),
                                     "--imgsz", imageSize.ToString(CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(arg);

        // 묶음 그림의 상자 색을 라벨링 화면의 몹 색으로 - 사람이 고른 색, 안 고른 번호는 화면과 같은 기본 색(황금각).
        var classCount = dataset.LoadClasses().Count;
        if (classCount > 0)
        {
            var palette = dataset.LoadPalette().Snapshot(classCount);

            start.ArgumentList.Add("--colors");
            start.ArgumentList.Add(string.Join(",", palette.Select(color => $"{color.R:X2}{color.G:X2}{color.B:X2}")));
        }

        start.Environment["PYTHONUTF8"] = "1";

        var log = Path.Combine(RunsRoot, weights + ".log");
        string? onnx = null;
        string? lastError = null;
        var watch = Stopwatch.StartNew();

        using var writer = new StreamWriter(log, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };

        void OnLine(string? raw)
        {
            if (raw is null) return;

            lock (writer) writer.WriteLine(raw);

            // 진행 막대가 \r 로 덮어쓴 조각들 중 마지막이 그 줄의 최종 모습이다. 줄 앞의 터미널 제어 문자(ESC[K)는 뗀다 -
            // 붙은 채로는 "^\s*1/2" 가 안 맞아 바퀴 진행이 한 번도 안 왔다(실측).
            var line = Regex.Replace(raw.Split('\r')[^1], @"\x1B\[[0-9;?]*[A-Za-z]", string.Empty);

            if (line.StartsWith("ONNX ", StringComparison.Ordinal)) onnx = line[5..].Trim();
            else if (line.Contains("Traceback", StringComparison.Ordinal) || line.Contains("Error", StringComparison.Ordinal)) lastError = line.Trim();

            // 스크립트가 묶음마다 찍는 줄(report_progress). 진행 막대 조각 뒤에 붙어 올 수 있어 줄 머리가 아니라 줄 안에서 찾는다.
            var batch = Regex.Match(line, @"BATCH (\d+) (\d+) (\d+) (\d+) ([\d.]+)");
            if (batch.Success)
            {
                int Number(int group) => int.Parse(batch.Groups[group].Value, CultureInfo.InvariantCulture);

                var (current, total, index, count) = (Number(1), Number(2), Number(3), Number(4));
                var loss = double.Parse(batch.Groups[5].Value, CultureInfo.InvariantCulture);

                steps.Report(new TrainingStep(current - 1, total, null, Batch: index, BatchCount: count));
                status.Report($"YOLO 학습 중 - {current}/{total} 바퀴 · 묶음 {index}/{count} · box loss {loss:0.000} · {watch.Elapsed:mm\\:ss}");
                return;
            }

            var preview = Regex.Match(line, @"PREVIEW (.+\.jpg)\s*$");
            if (preview.Success)
            {
                previews?.Report(preview.Groups[1].Value.Trim());
                return;
            }

            if (line.Contains("PREVIEW_FAILED", StringComparison.Ordinal))
            {
                Logger.Warn($"학습 묶음 그림을 못 그렸다: {line}");
                return;
            }

            var epoch = Regex.Match(line, @"^\s*(\d+)/(\d+)\s+\S+G\s+([\d.]+)\s+([\d.]+)\s+([\d.]+).*100%");
            if (epoch.Success)
            {
                var done = int.Parse(epoch.Groups[1].Value, CultureInfo.InvariantCulture);
                var total = int.Parse(epoch.Groups[2].Value, CultureInfo.InvariantCulture);
                var boxLoss = double.Parse(epoch.Groups[3].Value, CultureInfo.InvariantCulture);

                // 바퀴 끝 줄 - loss 꺾은선은 여기서만 한 점 찍는다(묶음마다 찍으면 한 바퀴 안의 들쭉날쭉이 추세를 가린다).
                steps.Report(new TrainingStep(done, total, boxLoss));
                status.Report($"YOLO 학습 중 - {done}/{total} 바퀴 · box loss {boxLoss:0.000} · {watch.Elapsed:mm\\:ss}");
            }
            else if (line.StartsWith("TRAIN_SECONDS", StringComparison.Ordinal))
            {
                status.Report("학습이 끝났습니다. ONNX 로 내보내는 중...");
            }
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        status.Report($"YOLO 학습을 시작합니다 ({weights} · {imageSize}px · {epochs}바퀴 · 묶음 {batch}장 · GPU {device})...");
        Logger.Info($"YOLO 학습 시작: {weights} {imageSize}px {epochs}바퀴 batch {batch} GPU {device} · 로그 {log}");

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 멈추기를 누르면 파이썬과 그 자식(데이터 로더 작업자)까지 끈다. 안 그러면 GPU 를 물고 남는다.
        await using (token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }))
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }

        token.ThrowIfCancellationRequested();

        if (process.ExitCode != 0 || onnx is null || !File.Exists(onnx))
            throw new InvalidOperationException($"YOLO 학습이 실패했습니다(종료 {process.ExitCode}). {lastError ?? string.Empty} 로그: {log}");

        status.Report("새 모델을 들이는 중...");

        // 들이기: Ultralytics 는 비율을 지켜 여백을 넣는다(레터박스). 이름이 곧 콤보 항목이라 보관본도 갈아 끼운다.
        // 쪽지 크기도 학습한 크기로 - 몹 찾기는 ONNX 에 박힌 크기를 먼저 보지만, 줄 요약·재현율 쪽지·캡처를 줄이는 바닥이 쪽지를 본다.
        var target = DetectorFiles.ImportOnnx(dataset, onnx, imageSize, imageSize, letterbox: true, modelName);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow); // 켜 둔 몹 찾기가 시각으로 새 모델을 알아챈다
        DetectorFiles.KeepAsChoice(dataset, modelName);

        Logger.Info($"YOLO 학습 끝: {watch.Elapsed:mm\\:ss} · {onnx} → {target}");

        return (target, watch.Elapsed);
    }
}
