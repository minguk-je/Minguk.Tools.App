using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Korean;
using Minguk.Tools.Input.Sequencing;
using System.Threading;

namespace Minguk.Tools.Tests;

/// <summary>
/// 입력 어댑터가 실제로 OS 를 거쳐 살아 있는 창에 입력을 전달하는지 확인한다.
/// 자기 소유의 WPF 창을 띄우고 거기에 입력을 쏜 뒤 결과를 되읽는다.
/// </summary>
internal static partial class Program
{
    private static readonly List<string> Results = [];
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 앱과 같은 설정 파일을 읽는다(%AppData%\Minguk.Tools). 이것을 안 부르면 AppSettingUtility 가 하네스 bin 폴더를 보고,
        // 데이터셋·학습 폴더가 기본값으로 떨어져 "모델이 없다" 가 된다(실측 2026-09-14).
        Minguk.Tools.Helper.UserDataPaths.Initialize();

        // 기능 모듈. 앱(App.xaml.cs)과 같은 목록을 써야 --views 가 모듈 화면·설정 페이지를 본다 -
        // 안 부르면 모듈이 안 붙은 셈이라 조용히 지나간다.
        Minguk.Tools.Modules.ToolModules.Use(new Minguk.Tools.Training.TrainingModule());

        // 어느 카드를 쓸지. libtorch 를 올리기 전에 정해야 하므로 맨 앞에서 한 번.
        // 두 학습을 동시에 돌리려면 창을 둘 열고 --gpu=0 · --gpu=1 로 나눠 준다.
        // 안 주면 자동 - libtorch 를 올리는 순간 모니터가 안 붙었거나 한가한 카드로 간다(LibTorchRuntime.Load).
        if (ArgValue(args, "--gpu=") is { } gpu)
            Minguk.Tools.Vision.Training.LibTorchRuntime.SelectGpu(int.Parse(gpu, CultureInfo.InvariantCulture));

        if (args.Contains("--calibrate")) return Calibrate.Run();
        if (args.Contains("--fallback")) return FallbackProbe.Run();
        if (args.Contains("--interception-probe")) return InterceptionProbe.Run();

        // 크기를 바꿔 학습하고 추론 시간까지 잰다. 모델을 덮어쓰므로(.bak 로 남긴다) 평소 검증 밖.
        if (args.Contains("--train")) return TrainRun.Run(args);

        // 설계 0단계: ONNX 검출기가 이 PC 에서 몇 ms 인지만 잰다. 모델 파일이 있어야 하므로 평소 검증 밖.
        if (args.Contains("--onnx-bench")) return OnnxBench.Run(args);
        if (args.Contains("--onnx-raw")) return OnnxRaw.Run(args);
        if (args.Contains("--onnx-agree")) return OnnxAgree.Run(args);

        // 글자 읽기만. --vision 전체(영상·학습 검사 포함)를 기다리지 않고 OCR 을 고칠 때.
        if (args.Contains("--ocr")) return RunOcrOnly();
        // 프로젝트의 사진·자리로 PP-OCRv5 가 몇 장 맞는지 잰다. 사진이 있어야 하므로 평소 검증 밖.
        if (args.Contains("--ocr-bench")) return OcrBench.Run(args);
        if (args.Contains("--ocr-crop")) return OcrCropCheck.Run(args);

        // 켜 둔 게임 창을 잡아 자리·칸을 읽어 본다. 게임이 떠 있어야 한다.
        if (args.Contains("--live-ocr")) return LiveOcrCheck.Run(args);

        // 설계 2단계: ONNX 검출기가 무엇을 어디서 찾는지 본다(사각형을 그려 파일로 남긴다).
        if (args.Contains("--onnx-detect")) return OnnxDetect.Run(args);

        // 설계 3단계: 셰이더로 만든 텐서가 CPU 로 만든 것과 같은 답을 내는지.
        if (args.Contains("--onnx-texture")) return OnnxTextureCheck.Run(args);

        // 설계 4단계: 밖에서 학습할 수 있게 내보내고, 학습해 온 .onnx 를 들인다.
        if (args.Contains("--export-dataset")) return DatasetTools.Export();
        if (args.Contains("--import-onnx")) return DatasetTools.Import(args);
        if (args.Contains("--use-trained")) return DatasetTools.UseTrained();
        if (args.Contains("--views")) return ViewSmokeProbe.Run();

        // 솔루션·프로젝트 모델. 임시 폴더만 만지고 화면이 없다 - --vision 처럼 안전하다.
        if (args.Contains("--solution")) return SolutionProbe.Run();

        // 옛 자리(데이터셋·프레임 저장·스크립트)를 첫 솔루션으로 옮긴다. --apply 가 없으면 보여 주기만 한다.
        if (args.Contains("--migrate")) return MigrateProbe.Run(args);

        // 스크립트 화면(VS 모양)을 화면 밖 창에 띄워 바인딩 오류를 모으고 PNG 로 찍는다. 커서는 안 가져간다.
        if (args.Contains("--script-screen")) return ScriptScreenProbe.Run(args);

        // 라벨링 화면을 화면 밖 창에 띄워 아래 학습 패널 높이가 내용에 맞는지 재고 PNG 로 찍는다.
        if (args.Contains("--labeling-screen")) return LabelingScreenProbe.Run(args);
        if (args.Contains("--environment-screen")) return EnvironmentScreenProbe.Run(args);
        if (args.Contains("--play-screen")) return PlayScreenProbe.Run(args);
        if (args.Contains("--settings-screen")) return SettingsScreenProbe.Run(args);

        // 라벨링 화면의 YOLO 학습 길을 화면 없이 돌린다. 들이기까지 하므로 --root 로 시험 폴더를 준다(GPU 를 쓴다).
        if (args.Contains("--yolo-train")) return YoloTrainProbe.Run(args);

        // 영상에서 뽑기를 실제 녹화본으로 잰다(속도). --extract-video=<mp4> --out=<폴더> [--interval=1]
        if (ArgValue(args, "--extract-video=") is { } extractVideo)
        {
            var output = ArgValue(args, "--out=") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "minguk-extract-probe");
            var seconds = double.Parse(ArgValue(args, "--interval=") ?? "1", CultureInfo.InvariantCulture);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = Minguk.Tools.Capture.Recording.VideoFrameExtractorFactory.Create()
                .ExtractAsync(extractVideo, output, TimeSpan.FromSeconds(seconds), null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();

            Console.WriteLine($"[INFO] 길이 {result.Duration.TotalSeconds:0.0}초 · 넣음 {result.SavedPaths.Count} · 비슷해 건너뜀 {result.SkippedSimilar} · 이미 있음 {result.SkippedExisting} · " +
                              $"{watch.Elapsed.TotalSeconds:0.0}초 걸림 ({watch.Elapsed.TotalSeconds / Math.Max(1, result.Duration.TotalSeconds):0.00}초/영상 1초) · {output}");
            return 0;
        }

        // 모니터 캡처 fps 를 화면 내용 없이/있게 잰다. 화면에 창을 몇 초 띄운다. --capture-fps[=모니터 번호]
        if (args.Any(a => a.StartsWith("--capture-fps", StringComparison.OrdinalIgnoreCase))) return CaptureFpsProbe.Run(ArgValue(args, "--capture-fps="));

        // 라벨 캔버스를 실제 마우스로 끈다. 커서를 몇 초 가져가므로 --views 에 안 끼운다.
        if (args.Contains("--canvas-drag")) return CanvasDragProbe.Run();

        // 영역·구역 손잡이를 실제 마우스로 끌어 끄는 동안 커서를 따라오는지 잰다. 커서를 가져간다.
        if (args.Contains("--region-drag")) return RegionDragProbe.Run(args);

        // 설정 목록 칸 표에 실제 키보드로 새 행을 적는다. 커서·키보드를 가져간다.
        if (args.Contains("--list-typing")) return ListGridTypingProbe.Run();

        // libtorch(4GB)와 학습한 모델이 있어야 도는 것이라 평소 검증에는 안 낀다.
        if (args.Contains("--detect-bench")) return DetectBench.Run();

        // 학습한 모델이 라벨을 찍은 그림에서 그 사각형을 다시 찾는지 센다. 같은 조건이라 평소 검증 밖.
        if (args.Contains("--detect-check"))
        {
            var score = ArgValue(args, "--score=") is { } s ? float.Parse(s, CultureInfo.InvariantCulture) : DetectCheck.DefaultMinimumScore;
            var root = ArgValue(args, "--root=");
            return root is null ? DetectCheck.Run(score) : DetectCheck.Run(new Minguk.Tools.Vision.Labeling.LabelDataset(root), score);
        }

        // 라벨 위 이름표 자리를 잘라 OCR 이 무엇을 읽는지 본다. 데이터셋이 있어야 해 평소 검증 밖.
        if (args.Contains("--nameplate-check"))
        {
            var count = int.Parse(ArgValue(args, "--count=") ?? "3", CultureInfo.InvariantCulture);
            return NameplateCheck.Run(ArgValue(args, "--root="), count, ArgValue(args, "--out="));
        }

        // 실시간 경로(WPF 로 줄인 PNG)와 파일 경로가 같은 답을 내는지. --scale-check[=<폴더>] [--side=640]
        if (args.Any(a => a.StartsWith("--scale-check", StringComparison.OrdinalIgnoreCase)))
        {
            var side = int.Parse(ArgValue(args, "--side=") ?? "640", CultureInfo.InvariantCulture);
            return ScaleCheck.Run(ArgValue(args, "--scale-check="), side);
        }

        // 지정한 폴더로 학습하고 곧바로 다시 찾는지 센다. 조건을 바꿔 가며 여러 번 돌릴 때.
        // --train-check=<폴더> [--epochs=20] [--size=320x180]
        if (ArgValue(args, "--train-check=") is { } trainRoot)
        {
            var epochs = int.Parse(ArgValue(args, "--epochs=") ?? "20", CultureInfo.InvariantCulture);
            var size = (ArgValue(args, "--size=") ?? "320x180").Split('x');
            double? lr = ArgValue(args, "--lr=") is { } l ? double.Parse(l, CultureInfo.InvariantCulture) : null;
            return TrainCheck.Run(trainRoot, epochs, int.Parse(size[0], CultureInfo.InvariantCulture), int.Parse(size[1], CultureInfo.InvariantCulture), lr);
        }

        // 라벨·학습 준비·추론 변환만 본다. 입력 어댑터를 안 만들므로 커서와 키보드를
        // 가져가지 않는다 - 시각 쪽만 고쳤을 때 이것만 돌리면 된다.
        if (args.Contains("--vision")) return RunVisionOnly();

        // 본보기 그림 대조만(몇 초) - 그림찾기·그림누르기를 고쳤을 때.
        if (args.Contains("--template"))
        {
            TestTemplateMatch();
            TestMaskedTemplate();
            foreach (var line in Results) Console.WriteLine(line);
            return _failures == 0 ? 0 : 1;
        }

        // 미니맵 방향 읽기만(1초 안) - 실제 게임 화면 다섯 장으로 방위·마커·눈금을 본다.
        if (args.Contains("--minimap"))
        {
            TestMinimap();
            foreach (var line in Results) Console.WriteLine(line);
            return _failures == 0 ? 0 : 1;
        }

        // 아이온2 HUD 영역을 실제 화면에서 읽는다(몇 초) · 프로젝트에 넣기.
        if (args.Contains("--hud"))
        {
            TestAion2Hud();
            foreach (var line in Results) Console.WriteLine(line);
            return _failures == 0 ? 0 : 1;
        }

        if (args.Contains("--hud-bench")) return BenchHudOcr();

        // 영역을 끌어 놓는 순간 미리보기 패널이 움직이는지(임시 솔루션, 커서 안 씀).
        if (args.Contains("--region-drop")) return RegionDropProbe.Run(int.Parse(ArgValue(args, "--width=") ?? "1600"), int.Parse(ArgValue(args, "--height=") ?? "1000"));

        if (ArgValue(args, "--apply-engines=") is { } engineProject) return ApplyHudRegions(engineProject, enginesOnly: true);

        if (ArgValue(args, "--apply-regions=") is { } hudProject) return ApplyHudRegions(hudProject);

        // 진짜 Ollama 로 판단 시간·정확도(아이온2 사냥 장면 8개) - 모델 올리기만 1~2분.
        if (args.Contains("--llm")) return LiveLlmCheck.Run(args);

        // 조준 스레드만 - 닫힌 고리 가짜 허브로 붙기·지나침·배율 배우기(약 30초). 입력은 가짜 어댑터라 안 나간다.
        if (args.Contains("--aim")) return RunAimOnly();

        // 작업공간의 진짜 프로젝트가 컴파일되는지만(돌리지 않는다): --check-project=<.mtsproj 경로>
        if (args.Any(a => a.StartsWith("--check-project=", StringComparison.OrdinalIgnoreCase)))
        {
            CheckRealProject(ArgValue(args, "--check-project=") ?? string.Empty);

            foreach (var line in Results) Console.WriteLine(line);

            return _failures == 0 ? 0 : 1;
        }

        // 같은 검증을 경로만 바꿔 돌린다. 경로마다 실제로 입력이 나가는지 따로 봐야 한다.
        var backend = ParseBackend(args);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var ui = new TestWindow();

        Console.WriteLine($"검증할 경로: {backend}");

        ui.ContentRendered += async (_, _) =>
        {
            try
            {
                // 실제 앱과 같은 조건: 전송은 백그라운드 스레드에서 이뤄진다.
                // 보내는 스레드와 받는 창의 UI 스레드가 같으면 연속 전송한 키가 유실된다.
                await Task.Run(() => RunAllAsync(ui, backend));
            }
            catch (Exception ex)
            {
                Fail("예외", ex.ToString());
            }
            finally
            {
                ui.Close();
                app.Shutdown();
            }
        };

        ui.Show();
        app.Run();

        Console.WriteLine();
        foreach (var line in Results) Console.WriteLine(line);
        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "== 전체 통과 ==" : $"== 실패 {_failures}건 ==");

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 라벨·학습 준비·추론 변환. 입력 어댑터가 필요 없는 것들이다.
    /// </summary>
    /// <remarks>
    /// 입력 쪽 검증과 갈라 둔다. 처음에는 <c>--backend=</c> 안에 얹어 두었는데, 그러면
    /// 라벨 하나 고치고 확인하려 해도 <b>커서와 키보드를 가져가는</b> 입력 어댑터 검증이
    /// 통째로 딸려 왔다. 서로 상관이 없는 것들이다.
    ///
    /// <c>--backend=</c> 로 도는 전체 검증에도 그대로 낀다 - 한 번에 다 보고 싶을 때가 있다.
    /// </remarks>
    private static void RunVision()
    {
        TestLabeling();
        TestDetection();
        TestOcr();
        TestLiveScripts();
        TestCaptureHub();
        TestCSharpCompletion();
        TestSharedHotkeys();
        TestDetectorEngine();
        TestOnnxDecoder();
        TestPreprocessorReuse();
        TestPrecisionTimer();
        TestRegionBook();
        TestRegionCells();
        TestRegionGeometry();
        TestRegionChrome();
        TestRegionCanvasSelection();
        TestTemplateMatch();
        TestMaskedTemplate();
        TestRegionMaskEditing();
        TestLlm();
        TestScriptAssistant();
        TestAion2Hud();
        TestMinimap();
        TestCompiledScript();
        TestTrainingActivity();
        TestVideoRecorder();
        TestVideoCapture();
        TestFrameRateLimiter();
        TestVideoFrameExtractor();
        TestScriptProject();
        TestScriptWorkspace();
    }

    /// <summary>시각 쪽만 돌린다. 커서와 키보드를 안 건드린다.</summary>
    internal static string? ArgValue(string[] args, string prefix)
        => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } hit ? hit[prefix.Length..] : null;

    private static int RunVisionOnly()
    {
        Console.WriteLine("시각 검증 (라벨 · 학습 준비 · 추론 변환)");
        Console.WriteLine("입력 어댑터는 만들지 않는다 - 커서와 키보드를 안 가져간다.");
        Console.WriteLine();

        try
        {
            TestScriptFiles();
            RunVision();
        }
        catch (Exception ex)
        {
            Fail("예외", ex.ToString());
        }

        foreach (var line in Results) Console.WriteLine(line);

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "== 전체 통과 ==" : $"== 실패 {_failures}건 ==");

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>글자 읽기 검사만 돌린다. 커서·키보드를 안 건드린다.</summary>
    private static int RunAimOnly()
    {
        Console.WriteLine("조준 스레드 검증");
        Console.WriteLine();

        try
        {
            if (Minguk.Tools.Capture.CaptureTarget.EnumerateMonitors().FirstOrDefault() is { } monitor)
            {
                TestAimLoop(monitor);
                TestFindText(monitor);
            }
            else Check("조준 스레드 (모니터 없음, 건너뜀)", true, "");
        }
        catch (Exception ex)
        {
            Fail("예외", ex.ToString());
        }

        foreach (var line in Results) Console.WriteLine(line);

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "== 전체 통과 ==" : $"== 실패 {_failures}건 ==");

        return _failures == 0 ? 0 : 1;
    }

    private static int RunOcrOnly()
    {
        Console.WriteLine("글자 읽기 검증");
        Console.WriteLine();

        try
        {
            TestOcr();
        }
        catch (Exception ex)
        {
            Fail("예외", ex.ToString());
        }

        foreach (var line in Results) Console.WriteLine(line);

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "== 전체 통과 ==" : $"== 실패 {_failures}건 ==");

        return _failures == 0 ? 0 : 1;
    }

    private static void Pass(string name, string detail) => Results.Add($"[PASS] {name} — {detail}");

    private static void Fail(string name, string detail)
    {
        _failures++;
        Results.Add($"[FAIL] {name} — {detail}");
    }

    /// <summary>
    /// 이 경로에는 해당하지 않는 항목. 실패가 아니다.
    /// PostMessage 는 진짜 커서를 움직이지 않고 스캔코드도 넣지 못한다 - 못 하는 것이 아니라
    /// 그 경로의 존재 이유(포커스·커서를 안 뺏는 것)에서 따라오는 성질이다.
    /// </summary>
    private static void Skip(string name, string reason) => Results.Add($"[N/A ] {name} — {reason}");

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) Pass(name, detail);
        else Fail(name, detail);
    }

    private static IInputAdapter _adapter = null!;
    private static InputBackend _backend;
    private static InputService _service = null!;

    /// <summary>--backend=Interception 처럼 지정한다. 없으면 SendInput.</summary>
    private static InputBackend ParseBackend(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith("--backend=", StringComparison.OrdinalIgnoreCase));
        if (arg is null) return InputBackend.SendInput;

        var name = arg["--backend=".Length..];

        if (!Enum.TryParse<InputBackend>(name, ignoreCase: true, out var backend))
            throw new ArgumentException($"모르는 경로다: {name}. 쓸 수 있는 것: {string.Join(", ", Enum.GetNames<InputBackend>())}");

        return backend;
    }

    private static async Task RunAllAsync(TestWindow ui, InputBackend backend)
    {
        // PostMessage 경로는 어느 창에 넣을지 알아야 한다. 검증 대상이 이 창이다.
        OurHandle = Read(() => new WindowInteropHelper(ui).Handle);

        _backend = backend;
        _adapter = InputAdapterFactory.Create(backend, () => OurHandle);
        _service = new InputService(_adapter);

        if (!_adapter.IsAvailable)
        {
            Fail("어댑터 준비", $"{_adapter.Name} 을 쓸 수 없다 - {_adapter.UnavailableReason}");
            return;
        }

        Pass("어댑터 준비", $"{_adapter.Name}, 스캔코드 {(_service.SupportsTyping ? "가능" : "불가")}, "
                          + $"진짜 커서 {(_adapter.GetCursorPosition() is null ? "안 움직임" : "움직임")}");

        TestBackendSwitching();

        SetForegroundWindow(OurHandle);
        Post(() =>
        {
            ui.Activate();
            ui.Input.Focus();
        });
        await Task.Delay(300);

        if (!Read(() => ui.Input.IsKeyboardFocused))
        {
            Fail("포커스 확보", "테스트 창의 입력란이 키보드 포커스를 못 잡아 이후 검증을 건너뛴다.");
            return;
        }
        Pass("포커스 확보", "입력란이 키보드 포커스를 가짐");

        if (_service.SupportsTyping)
        {
            await TestTypingAsync(ui);
            await TestEnterAsync(ui);
        }
        else
        {
            Skip("키보드 문자 입력", $"{_adapter.Name} 은 스캔코드를 넣지 못한다");
            Skip("Enter 키", $"{_adapter.Name} 은 스캔코드를 넣지 못한다");
        }

        await TestVirtualKeyAsync(ui);

        TestHangulMapping();
        TestKeyboardDetection();
        await TestHangulTypingAsync(ui);

        TestScriptFiles();
        RunVision();
        TestInterceptionDriver();
        TestSequencePlan();
        TestSequenceScript();
        await TestScriptEngineAsync();
        await TestSequenceAsync(ui);
        await TestPlanRunAsync(ui);
        await TestImeToggleAsync();
        await TestMouseMoveAsync();
        await TestRelativeMoveAsync();
        await TestClickAsync(ui);
        await TestWheelAsync(ui);

        _adapter.Dispose();
    }

    /// <summary>
    /// 세 경로가 각각 만들어지고 정리되는지. Interception 은 드라이버가 없으면 사용 불가로 나온다.
    /// </summary>
    private static void TestBackendSwitching()
    {
        foreach (var backend in Enum.GetValues<InputBackend>())
        {
            using var adapter = InputAdapterFactory.Create(backend, () => IntPtr.Zero);

            var scanCodes = adapter is IScanCodeInput ? "스캔코드 가능" : "스캔코드 불가";
            var state = adapter.IsAvailable ? "사용 가능" : $"사용 불가 ({adapter.UnavailableReason})";

            Console.WriteLine($"  {backend,-12} {adapter.Name,-14} {state} / {scanCodes}");
        }

        Pass("경로 세 개 생성·정리", $"{string.Join(", ", Enum.GetNames<InputBackend>())}");
    }

    /// <summary>영문 대소문자·숫자·문장부호·공백이 그대로 찍히는지.</summary>
    private static async Task TestTypingAsync(TestWindow ui)
    {
        const string expected = "Hi Claude, 42%!";

        Post(ui.Input.Clear);

        // 글자 사이에 일부러 아무 대기도 두지 않는다. 연속 전송에서 유실이 없는지가 관건이다.
        foreach (var c in expected)
        {
            await _service.TypeCharAsync(c, holdTimeMs: 12, HangulKeyMode.HangulScanCode);
        }

        await Task.Delay(400);

        var actual = Read(() => ui.Input.Text);
        Check("키보드 문자 입력", actual == expected, $"기대 \"{expected}\" / 실제 \"{actual}\"");
    }

    /// <summary>Enter 스캔코드가 줄바꿈으로 도착하는지.</summary>
    private static async Task TestEnterAsync(TestWindow ui)
    {
        Post(ui.Input.Clear);
        await _service.TapKeyAsync(ScanCodes.Enter, holdTimeMs: 12);
        await Task.Delay(250);

        var text = Read(() => ui.Input.Text);
        Check("Enter 키", text.Contains((char)10), $"입력란 내용 = {Describe(text)}");
    }

    /// <summary>
    /// 가상 키로 넣는 경로. <c>PreviewInputRouter</c> 가 실제로 쓰는 길이라 세 경로 모두 확인한다.
    /// WPF 가 주는 것이 가상 키라 미리보기 입력은 이쪽으로만 나간다.
    /// </summary>
    /// <summary>
    /// 가상 키를 눌렀을 때 글자가 되는지.
    /// </summary>
    /// <remarks>
    /// 부친 키 메시지는 대상의 메시지 루프가 TranslateMessage 로 WM_CHAR 를 만들어 준다.
    /// <b>직접 보낸 것은 그 단계를 건너뛴다</b> - 큐를 거치지 않기 때문이다.
    /// 그래서 SendMessageTimeout 경로에서는 키를 보내도 글자가 되지 않는다.
    /// 못 하는 것이 아니라 그 방식에서 따라오는 성질이라 실패로 세지 않는다.
    /// 글자를 넣는 것은 그 경로도 WM_CHAR 로 한다("계획 실행" 이 그것을 본다).
    /// </remarks>
    private static async Task TestVirtualKeyAsync(TestWindow ui)
    {
        if (_backend == InputBackend.SendMessage)
        {
            Skip("가상 키 입력", "직접 보낸 키 메시지는 TranslateMessage 를 안 거쳐 글자가 되지 않는다");
            return;
        }

        const ushort vkA = 0x41;
        const ushort vkB = 0x42;

        Post(ui.Input.Clear);

        foreach (var vk in (ushort[])[vkA, vkB])
        {
            _adapter.PressKey(vk);
            await Task.Delay(20);
            _adapter.ReleaseKey(vk);
            await Task.Delay(40);
        }

        await Task.Delay(300);

        var actual = Read(() => ui.Input.Text);
        Check("가상 키 입력", actual == "ab", $"기대 \"ab\" / 실제 \"{actual}\"");
    }

    /// <summary>
    /// 시퀀스 엔진: 담은 순서대로 나가는지, 진행 보고가 오는지, 반복과 취소가 도는지.
    /// </summary>
    private static async Task TestSequenceAsync(TestWindow ui)
    {
        if (!_service.SupportsTyping)
        {
            Skip("시퀀스 한 바퀴", $"{_adapter.Name} 은 스캔코드를 넣지 못한다");
            Skip("시퀀스 반복·취소", $"{_adapter.Name} 은 스캔코드를 넣지 못한다");
            return;
        }

        // ── 한 바퀴 ──
        var reported = new List<string>();
        var progress = new Progress<string>(reported.Add);

        var sequence = new InputSequence(_service, holdTimeMs: 12)
            .Type("ok")
            .Enter();

        Post(ui.Input.Clear);
        var finished = await SequenceRunner.RunOnceAsync(sequence.Steps, intervalMs: 20, progress);
        await Task.Delay(400);

        var typed = Read(() => ui.Input.Text);
        Check("시퀀스 한 바퀴",
              finished && typed.StartsWith("ok") && typed.Contains((char)10),
              $"순서 \"{sequence.Describe()}\" / 입력란 {Describe(typed)} / 끝까지 {finished}");

        // Progress<T> 는 동기화 컨텍스트로 넘겨 보고하므로 조금 늦게 도착한다.
        await Task.Delay(200);
        Check("진행 보고", reported.Count >= sequence.Steps.Count,
              $"단계 {sequence.Steps.Count}개, 보고 {reported.Count}건: {string.Join(",", reported)}");

        // ── 반복과 취소 ──
        var loop = new InputSequence(_service, holdTimeMs: 8).Type("x");
        using var cts = new CancellationTokenSource();

        Post(ui.Input.Clear);
        var loopTask = SequenceRunner.RunLoopAsync(loop.Steps, intervalMs: 15, token: cts.Token);

        await Task.Delay(400);
        cts.Cancel();
        await loopTask;
        await Task.Delay(300);

        var repeated = Read(() => ui.Input.Text);

        // 몇 번 돌았는지는 타이밍에 달렸다. 두 번 이상 돌았고 멈췄는지만 본다.
        var stopped = repeated.Length;
        await Task.Delay(300);
        var afterStop = Read(() => ui.Input.Text).Length;

        Check("시퀀스 반복·취소", repeated.Length >= 2 && afterStop == stopped,
              $"400ms 동안 {repeated.Length}회 반복, 취소 뒤 {stopped} -> {afterStop} (늘지 않아야 한다)");
    }

    private static string Describe(string s)
        => "\"" + s.Replace(((char)13).ToString(), "<CR>").Replace(((char)10).ToString(), "<LF>") + "\"";

    /// <summary>UI 스레드의 값을 읽어 온다 (검증은 백그라운드 스레드에서 돈다).</summary>
    private static T Read<T>(Func<T> read) => Application.Current.Dispatcher.Invoke(read);

    /// <summary>UI 스레드에서 동작을 실행한다.</summary>
    private static void Post(Action action) => Application.Current.Dispatcher.Invoke(action);

    private static IntPtr OurHandle;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
