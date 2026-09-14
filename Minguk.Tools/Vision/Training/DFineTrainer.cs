using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Base.Utilities;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// D-FINE 을 파이썬으로 학습해 ONNX 로 내보내고 데이터셋에 들인다. 라벨링 화면에서 "쓰는 모델" 이 D-FINE 일 때 학습 버튼이 부른다.
/// </summary>
/// <remarks>
/// <b>왜 밖의 저장소를 부르나</b> - D-FINE 은 학습 코드가 제 저장소에 있다(Apache-2.0). 우리가 옮겨 적으면 저쪽이 올라갈 때마다 따라가야 한다.
/// 그래서 저장소·가상환경·사전학습 가중치를 한 폴더(`Vision.DFineRoot`, 기본 <c>D:\Minguk.Tools.학습</c>)에 두고 앱은 그것을 돌린다.
///
/// <b>네 걸음이다.</b> (1) 데이터셋을 COCO 로 내보내고 설정의 자리·몹 수를 지금 것으로 고친다, (2) <c>train.py</c>,
/// (3) <c>export_onnx.py</c>, (4) <c>도구/onnx-DirectML-고치기.py</c>. 넷째를 빼먹으면 DirectML 이 MatMul 을 틀리게 계산해
/// 점수가 0.06 으로 내려앉고 한 마리도 못 찾는다(CLAUDE.md). 들일 때는 <b>늘리기</b>다 - D-FINE 공식 설정이 그렇다.
///
/// 오래 걸린다(98장 60바퀴가 GTX 1060 에서 1시간 45분). 그래도 앱에 둔 이유는 YOLO 냐 D-FINE 이냐를 쓰는 사람이 고르게 하려는 것이다
/// (사용자 결정 2026-09-14). 진행은 저쪽이 찍는 <c>Epoch: [3/60]</c> 줄로 읽는다.
/// </remarks>
public static class DFineTrainer
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>저장소·가상환경·가중치가 든 학습 폴더. 설정 하나로 옮긴다(<see cref="TrainingPaths"/>).</summary>
    public static string Root => TrainingPaths.Root;

    public static string RepositoryPath => TrainingPaths.DFineRepository;

    public static string VenvPython => TrainingPaths.DFinePython;

    /// <summary>COCO 사전학습 가중치. 여기서 이어 배운다 - 98장으로 처음부터는 어림도 없다.</summary>
    public static string PretrainedWeights => TrainingPaths.DFineWeights;

    /// <summary>우리 데이터셋용 설정. 몹 수·사진 자리는 학습 직전에 다시 쓴다.</summary>
    public static string ConfigPath => Path.Combine(RepositoryPath, "configs", "dfine", "custom", "dfine_hgnetv2_n_mob.yml");

    public static string DatasetConfigPath => Path.Combine(RepositoryPath, "configs", "dataset", "mob_detection.yml");

    /// <summary>학습 결과가 떨어지는 곳(설정의 output_dir).</summary>
    public static string OutputPath => Path.Combine(RepositoryPath, "output", "mob_n");

    /// <summary>DirectML 고치기 스크립트. csproj 가 도구\ 에서 앱 출력의 Tools 아래로 복사한다.</summary>
    public static string DirectMlFixScript => Path.Combine(AppContext.BaseDirectory, "Tools", "onnx-DirectML-고치기.py");

    /// <summary>이 이름이면 D-FINE 으로 학습한다.</summary>
    public static bool Handles(string? modelName)
        => modelName is not null && modelName.Trim().StartsWith("D-FINE", StringComparison.OrdinalIgnoreCase);

    /// <summary>학습할 수 있는가. 못 하면 사람에게 할 말을 돌려준다.</summary>
    public static string? WhyUnavailable()
    {
        if (!File.Exists(VenvPython)) return Missing($"파이썬 환경이 없습니다: {VenvPython}");
        if (!File.Exists(Path.Combine(RepositoryPath, "train.py"))) return Missing($"저장소가 없습니다: {RepositoryPath}");
        if (!File.Exists(ConfigPath)) return Missing($"우리 설정이 없습니다: {ConfigPath}");
        if (!File.Exists(PretrainedWeights)) return Missing($"사전학습 가중치가 없습니다: {PretrainedWeights}");
        if (!File.Exists(DirectMlFixScript)) return $"DirectML 고치기 스크립트가 없습니다: {DirectMlFixScript}. 앱을 다시 빌드하세요.";

        return null;
    }

    /// <summary>없는 것을 말하고 어떻게 갖추는지 한 줄로 알려 준다 - 사람이 다음에 무엇을 할지 알아야 한다.</summary>
    private static string Missing(string what)
        => $"D-FINE 학습 환경이 없습니다 - {what}. 저장소의 도구\\학습-환경-준비.ps1 -What dfine 를 한 번 돌리면 갖춰집니다(약 3GB). " +
           $"다른 자리에 이미 있으면 설정 {TrainingPaths.RootSettingKey} 를 그 폴더로 바꾸세요.";

    /// <summary>학습 → ONNX 내보내기 → DirectML 고치기 → 들이기. 돌려주는 것은 들인 detector.onnx 자리.</summary>
    public static async Task<(string ModelPath, TimeSpan Elapsed)> TrainAsync(LabelDataset dataset, string modelName, int epochs, int device,
                                                                              IProgress<string> status, IProgress<TrainingStep> steps,
                                                                              CancellationToken token)
    {
        if (WhyUnavailable() is { } why) throw new InvalidOperationException(why);

        var watch = Stopwatch.StartNew();

        // (1) 우리 데이터셋을 COCO 로 내보내고 설정이 그것을 가리키게 한다. 사진을 더 담았거나 몹을 늘렸으면 여기서 따라온다.
        status.Report("데이터셋을 COCO 로 내보내는 중...");
        var coco = DatasetExport.WriteCoco(dataset);
        RewriteDatasetConfig(dataset, coco);

        var log = Path.Combine(YoloTrainer.RunsRoot, "dfine.log");
        Directory.CreateDirectory(YoloTrainer.RunsRoot);

        using var writer = new StreamWriter(log, append: false, new UTF8Encoding(false)) { AutoFlush = true };

        // (2) 학습. 바퀴 수는 -u 로 설정을 덮는다(train.py 가 yaml 을 그렇게 고친다).
        status.Report($"D-FINE 학습을 시작합니다 ({epochs}바퀴 · GPU {device}) - 98장 60바퀴가 이 카드로 1시간 45분입니다...");

        await RunAsync("train.py",
            ["-c", ConfigPath, "--use-amp", "--seed=0", "-t", PretrainedWeights, "-u", $"epochs={epochs.ToString(CultureInfo.InvariantCulture)}"],
            device, writer, line => ReportProgress(line, watch, status, steps), token);

        var checkpoint = Path.Combine(OutputPath, "best_stg2.pth");
        if (!File.Exists(checkpoint)) checkpoint = Path.Combine(OutputPath, "best_stg1.pth");
        if (!File.Exists(checkpoint)) throw new InvalidOperationException($"학습 결과를 못 찾았습니다: {OutputPath}. 로그: {log}");

        // (3) ONNX 로 내보낸다. 나오는 자리는 체크포인트의 .pth 를 .onnx 로 바꾼 이름이다.
        status.Report("ONNX 로 내보내는 중...");
        await RunAsync(Path.Combine("tools", "deployment", "export_onnx.py"), ["-c", ConfigPath, "-r", checkpoint], device, writer, null, token);

        var exported = Path.ChangeExtension(checkpoint, ".onnx");
        if (!File.Exists(exported)) throw new InvalidOperationException($"내보낸 ONNX 를 못 찾았습니다: {exported}. 로그: {log}");

        // (4) DirectML 이 먹을 수 있게 고친다. 빼먹으면 터지지도 않고 답만 틀려 한 마리도 못 찾는다.
        status.Report("DirectML 이 먹게 고치는 중...");
        var fixedPath = Path.Combine(Path.GetDirectoryName(exported)!, Path.GetFileNameWithoutExtension(exported) + "-dml.onnx");

        await RunAsync(DirectMlFixScript, [exported, fixedPath], device, writer, null, token, inRepository: false);

        if (!File.Exists(fixedPath)) throw new InvalidOperationException($"고친 ONNX 를 못 찾았습니다: {fixedPath}. 로그: {log}");

        // 들이기: D-FINE 은 늘려 넣어 학습한다(공식 설정의 변환이 Resize 하나뿐이다).
        status.Report("새 모델을 들이는 중...");

        var target = DetectorFiles.ImportOnnx(dataset, fixedPath, 640, 640, letterbox: false, modelName);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        DetectorFiles.KeepAsChoice(dataset, modelName);

        Logger.Info($"D-FINE 학습 끝: {watch.Elapsed:hh\\:mm\\:ss} · {fixedPath} → {target}");

        return (target, watch.Elapsed);
    }

    /// <summary>
    /// 설정이 지금 데이터셋을 가리키게 고친다. 사진 자리·주석 파일·몹 수 세 줄이다.
    /// </summary>
    /// <remarks>
    /// 폴더를 바꿔 가며 학습할 수 있고(시험용 데이터셋), 몹을 더하면 <c>num_classes</c> 가 달라진다. 안 고치면 저쪽은 옛 자리를
    /// 읽어 "파일이 없다" 로 죽거나, 더 나쁘게는 몹 수가 어긋난 채로 학습해 번호가 밀린다.
    /// </remarks>
    private static void RewriteDatasetConfig(LabelDataset dataset, string cocoPath)
    {
        var text = File.ReadAllText(DatasetConfigPath);
        var images = Path.Combine(dataset.Root, "images").Replace('\\', '/');
        var annotations = cocoPath.Replace('\\', '/');
        var classes = Math.Max(dataset.LoadClasses().Count, 1);

        text = Regex.Replace(text, @"(?m)^(\s*img_folder:\s*).*$", "${1}" + images);
        text = Regex.Replace(text, @"(?m)^(\s*ann_file:\s*).*$", "${1}" + annotations);
        text = Regex.Replace(text, @"(?m)^(num_classes:\s*)\d+", "${1}" + classes.ToString(CultureInfo.InvariantCulture));

        File.WriteAllText(DatasetConfigPath, text, new UTF8Encoding(false));

        Logger.Info($"D-FINE 설정을 맞췄다: {images} · {annotations} · 몹 {classes}종");
    }

    /// <summary>진행 줄에서 바퀴와 loss 를 읽는다. D-FINE 은 <c>Epoch: [3/60]  [ 10/24]  eta: ...  loss: 12.3 (12.9)</c> 로 찍는다.</summary>
    private static void ReportProgress(string line, Stopwatch watch, IProgress<string> status, IProgress<TrainingStep> steps)
    {
        var epoch = Regex.Match(line, @"Epoch:\s*\[(\d+)/(\d+)\]");
        if (!epoch.Success) return;

        var done = int.Parse(epoch.Groups[1].Value, CultureInfo.InvariantCulture);
        var total = int.Parse(epoch.Groups[2].Value, CultureInfo.InvariantCulture);

        var loss = Regex.Match(line, @"\bloss:\s*([\d.]+)");
        double? value = loss.Success && double.TryParse(loss.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : null;

        // 바퀴 안의 진행은 막대에 안 싣는다 - 끝난 바퀴로만 센다. 대신 상태 줄에는 지금 줄을 그대로 보인다.
        steps.Report(new TrainingStep(done, total, value));
        status.Report($"D-FINE 학습 중 - {done}/{total} 바퀴{(value is { } v ? $" · loss {v:0.00}" : string.Empty)} · {watch.Elapsed:hh\\:mm\\:ss}");
    }

    /// <summary>저장소의 파이썬 스크립트를 돌린다. 출력은 로그에 다 남기고, 줄마다 <paramref name="onLine"/> 을 부른다.</summary>
    private static async Task RunAsync(string script, string[] arguments, int device, StreamWriter writer, Action<string>? onLine,
                                       CancellationToken token, bool inRepository = true)
    {
        var start = new ProcessStartInfo(VenvPython)
        {
            WorkingDirectory = RepositoryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        start.ArgumentList.Add("-X");
        start.ArgumentList.Add("utf8");
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(inRepository ? Path.Combine(RepositoryPath, script) : script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        // 한국어 윈도우의 cp949 로 설정 파일을 읽다 터진다(실측). 카드는 프로세스에 하나만 보인다.
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["CUDA_VISIBLE_DEVICES"] = device.ToString(CultureInfo.InvariantCulture);
        start.Environment["CUDA_DEVICE_ORDER"] = "PCI_BUS_ID";

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };

        void Handle(string? raw)
        {
            if (raw is null) return;

            var line = Regex.Replace(raw.Split('\r')[^1], @"\x1B\[[0-9;?]*[A-Za-z]", string.Empty);

            lock (writer) writer.WriteLine(line);
            onLine?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) => Handle(e.Data);
        process.ErrorDataReceived += (_, e) => Handle(e.Data);

        Logger.Info($"D-FINE: {Path.GetFileName(script)} {string.Join(' ', arguments)}");

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 멈추기를 누르면 파이썬과 그 자식(데이터 로더 작업자)까지 끈다. 안 그러면 GPU 를 물고 남는다.
        await using (token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }))
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }

        token.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(script)} 가 실패했습니다(종료 {process.ExitCode}). 로그: {Path.Combine(YoloTrainer.RunsRoot, "dfine.log")}");
    }
}
