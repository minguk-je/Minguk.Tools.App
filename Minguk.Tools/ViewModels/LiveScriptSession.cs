using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Base.Utilities;
using Minguk.Tools.Capture;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;
using Minguk.Tools.Vision.Ocr;
using Minguk.Tools.Vision.Perception;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 화면이 실시간 스크립트를 돌릴 때 필요한 것을 한데 묶은 것 - API 에 빌려 줄 것들, 비상 정지, 출력 칸.
/// </summary>
/// <remarks>
/// 스크립트·플레이 두 화면이 똑같이 한다. 화면마다 적으면 비상 정지나 잠금 하나를 한쪽에서만 고치게 된다.
/// <see cref="Resolve"/> 가 <see cref="ScriptPlayer"/> 에 줄 문맥을 만든다: 한 바퀴 = 엔진이 스크립트를
/// 실시간 API 로 끝까지 돌리는 것. 도는 동안 비상 정지(Pause)를 쥐고, 끝나면 놓고 누르고 있던 키를 뗀다.
/// </remarks>
public sealed class LiveScriptSession : IDisposable
{
    private readonly Func<InputService?> _service;
    private readonly Func<bool> _requiresForeground;
    private readonly Func<CaptureTarget?> _target;
    private readonly Func<IOcrEngine?> _ocr;
    private readonly Func<Minguk.Tools.Vision.Regions.NamedRegion?, IOcrEngine?>? _ocrFor;
    private readonly Func<Minguk.Tools.Vision.Regions.RegionBook?> _regions;
    private readonly Func<Task> _activateTarget;
    private readonly Action<string> _notify;
    private readonly Action<Action> _onUi;
    private readonly Func<string, LiveScriptHost, CancellationToken, Task<IReadOnlyList<ScriptError>>>? _runProject;
    private readonly Action? _prepareRecognition;
    private readonly EmergencyStop _emergency = new();

    private LiveScriptApi? _api;

    public LiveScriptSession(
        Func<InputService?> service,
        Func<bool> requiresForeground,
        Func<CaptureTarget?> target,
        Func<IOcrEngine?> ocr,
        Func<Minguk.Tools.Vision.Regions.RegionBook?> regions,
        Func<Task> activateTarget,
        Action<Action> onUi,
        Action<string> notify,
        Func<Minguk.Tools.Vision.Regions.NamedRegion?, IOcrEngine?>? ocrFor = null,
        Func<string, LiveScriptHost, CancellationToken, Task<IReadOnlyList<ScriptError>>>? runProject = null,
        Action? prepareRecognition = null)
    {
        _prepareRecognition = prepareRecognition;
        _service = service;
        _requiresForeground = requiresForeground;
        _target = target;
        _ocr = ocr;
        _ocrFor = ocrFor;
        _regions = regions;
        _activateTarget = activateTarget;
        _notify = notify;
        _onUi = onUi;
        _runProject = runProject;
        Console = new ScriptConsole(onUi);
        Debug = new ScriptDebugSession(onUi);
    }

    /// <summary>출력 칸. XAML 이 <c>Live.Console.Text</c> 로 묶는다.</summary>
    public ScriptConsole Console { get; }

    /// <summary>중단점·한 줄씩·멈춘 자리. XAML 이 <c>Live.Debug.*</c> 로 묶는다.</summary>
    public ScriptDebugSession Debug { get; }

    /// <summary>화면의 일시정지 - 스크립트가 다음 API 호출에서 멈춘다. 소스·빌드한 것 모두.</summary>
    public ScriptPauseGate PauseGate { get; } = new();

    public IPerceptionHub Hub { get; init; } = PerceptionHubFactory.Default;

    /// <summary>
    /// 돌릴 문맥을 만든다. 틀린 줄이 있으면 안 돌린다. 문맥의 한 바퀴가 엔진을 돌리고, 도는 동안 비상 정지(Pause)가 산다.
    /// </summary>
    public ScriptRunContext? Resolve(ScriptWorkbench script, ScriptPlayer player)
    {
        // 빌드된 것(.mtsx)은 소스가 없다 - IL 을 로드해 돌린다. 검사할 글이 없으니 HasError 는 안 본다.
        if (script.Compiled is { } compiled) return ResolveCompiled(compiled, player);

        if (script.HasError)
            return Refuse($"스크립트에 고칠 줄이 있습니다 - {FirstLine(script.ErrorText)}");

        // 프로젝트면 시작할 때 전체를 한 벌로 굳힌다(저장 안 한 탭 포함). 도는 중에 탭을 고쳐도 그 바퀴는 안 바뀐다.
        var unit = script.IsProject ? script.Project.ToUnit() : null;
        var engine = script.Engine;

        if (unit is not null)
        {
            if (engine is not IProjectScriptEngine)
                return Refuse($"{engine.Name} 은(는) 프로젝트를 돌리지 못합니다. 프로젝트는 C# 으로 씁니다.");

            if (string.IsNullOrEmpty(unit.EntryPath))
                return Refuse("시작 파일이 없습니다 - 탐색기에서 .csx 를 오른쪽 눌러 '시작 파일로' 를 고르세요.");
        }
        else if (string.IsNullOrWhiteSpace(script.Text))
            return Refuse("스크립트가 비어 있습니다.");

        var service = _service();
        if (service is null) return Refuse("입력 경로가 아직 없습니다 - 화면이 다 뜬 뒤 다시 누르세요.");

        service.JitterMs = player.JitterMs;

        var source = script.Text;

        Debug.SupportsStepping = engine.SupportsStepping;
        Console.ClearCalls();

        var beforeRun = MakeBeforeRun(player, service);

        return new ScriptRunContext(async (progress, token) =>
        {
            var host = BuildHost(player, service, unit?.ResourceRoot, token);

            var api = new LiveScriptApi(host, token);
            _api = api;

            progress.Report($"실시간 실행 중 - 비상 정지 {EmergencyStop.Label}");

            try
            {
                // 줄 단위로 멈출 수 있는 언어에만 디버그 세션을 준다. C# 은 호출 로그로 본다.
                var debug = engine.SupportsStepping ? Debug : null;

                // 끝에서 프로젝트이동을 불렀으면 그 프로젝트를 여기(맨 바깥)서 이어 돌린다 - 쌓이지 않는다.
                var errors = await host.Moves.RunWithMovesAsync(
                    () => unit is not null && engine is IProjectScriptEngine projectEngine
                        ? projectEngine.RunLiveAsync(unit, api, debug, token)
                        : engine.RunLiveAsync(source, api, debug, token),
                    MoveRunner(host, token),
                    Console.Print,
                    token);

                return Finish(progress, errors, api, token);
            }
            finally
            {
                Cleanup(api);
            }
        }, beforeRun, token =>
        {
            PrepareRecognition();

            // 캐시가 풀렸으면(10분 안 씀) 여기서 컴파일해 둔다 - 실행이 같은 열쇠로 캐시에서 꺼낸다.
            return unit is not null && engine is IProjectScriptEngine projectEngine
                ? projectEngine.CheckLiveAsync(unit, token)
                : engine.CheckLiveAsync(source, token);
        }, IsTargetInFront);
    }

    /// <summary>화면에 글자 읽기 준비(리드백·모델 깨우기)를 청한다. UI 스레드에서 - 시작 요청이 UI 에서 온다.</summary>
    private void PrepareRecognition()
    {
        try
        {
            _prepareRecognition?.Invoke();
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "글자 읽기 준비에 실패했다 - 첫 읽기에서 다시 한다");
        }
    }

    /// <summary>대상 창이 이미 앞에 있는가. 영상·잡은 것 없음은 아니다(대기를 그대로 둔다).</summary>
    private bool IsTargetInFront()
        => _target() is { Kind: CaptureTargetKind.Window, Handle: var handle }
           && handle != IntPtr.Zero
           && ForegroundWindow.Handle != IntPtr.Zero
           && ForegroundWindow.IsInFront(handle);

    /// <summary>
    /// 실행을 거절한다 - 이유를 상태 줄·출력 창·로그 셋에 다 남긴다.
    /// </summary>
    /// <remarks>
    /// 게임 화면에서 F5 를 누르면 상태 줄을 못 본다 - 실패 소리("뿌우")만 나고 왜 안 도는지 알 길이 없었다(사용자, 2026-09-19). 로그에는 "문맥이 없다" 뿐이었다.
    /// </remarks>
    private ScriptRunContext? Refuse(string reason)
    {
        MessengerUtility.SendMainMessage(reason);
        Console.Print($"실행하지 않았습니다: {reason}");
        NLog.LogManager.GetCurrentClassLogger().Info($"실행 거절: {reason}");

        return null;
    }

    private static string FirstLine(string? text)
        => (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;

    /// <summary>
    /// 빌드된 것(.mtsx)을 돌릴 문맥. 소스 경로와 host·비상 정지·끝맺음을 그대로 나눠 쓰고, 실행만 IL 로 한다.
    /// </summary>
    private ScriptRunContext? ResolveCompiled(CompiledPlayable compiled, ScriptPlayer player)
    {
        var service = _service();
        if (service is null) return Refuse("입력 경로가 아직 없습니다 - 화면이 다 뜬 뒤 다시 누르세요.");

        service.JitterMs = player.JitterMs;

        Debug.SupportsStepping = false;
        Console.ClearCalls();

        var beforeRun = MakeBeforeRun(player, service);

        return new ScriptRunContext(async (progress, token) =>
        {
            var host = BuildHost(player, service, compiled.ResourceRoot, token);

            progress.Report($"실시간 실행 중(빌드됨: {compiled.Name}) - 비상 정지 {EmergencyStop.Label}");

            LiveScriptApi? api = null;

            try
            {
                var errors = await host.Moves.RunWithMovesAsync(
                    () => CompiledScriptRunner.RunAsync(compiled.Assembly, host, created => { _api = created; api = created; }, token),
                    MoveRunner(host, token),
                    Console.Print,
                    token);

                return Finish(progress, errors, api, token);
            }
            finally
            {
                Cleanup(api);
            }
        }, beforeRun, _ =>
        {
            PrepareRecognition();
            return Task.CompletedTask;
        }, IsTargetInFront);
    }

    /// <summary>프로젝트이동이 남긴 프로젝트를 돌리는 길 - 화면이 준 <see cref="LiveScriptHost.RunProject"/>(소스 또는 빌드된 것). 없으면 이동을 못 한다.</summary>
    private static Func<string, Task<IReadOnlyList<ScriptError>>>? MoveRunner(LiveScriptHost host, CancellationToken token)
        => host.RunProject is { } run ? name => run(name, host, token) : null;

    /// <summary>실행 결과를 화면에 알리고, 계속 돌릴지(true) 멈출지(false)를 정한다.</summary>
    private bool Finish(IProgress<string> progress, IReadOnlyList<ScriptError> errors, LiveScriptApi? api, CancellationToken token)
    {
        if (errors.Count > 0)
        {
            progress.Report($"실패: {errors[0]}");
            Console.Print($"멈춤: {errors[0]}");
            return false;
        }

        if (api?.Outcome == LiveScriptOutcome.Stopped || token.IsCancellationRequested)
            return false;

        return true;
    }

    /// <summary>한 바퀴가 끝날 때마다 - 누른 키를 떼고, 단축키를 풀고(UI 스레드), 프레임 복사를 끈다.</summary>
    private void Cleanup(LiveScriptApi? api)
    {
        api?.ReleaseAll();
        api?.Dispose();
        _onUi(_emergency.Disarm);
        Hub.WantsFrames = false;
        Debug.Reset();
        _api = null;
    }

    /// <summary>
    /// 대기가 끝난 뒤 UI 스레드에서 한 번 도는 준비. 비상 정지 단축키를 걸고(STA 여야 한다), 배율을 알리고, 대상 창을 앞으로.
    /// </summary>
    private Func<Task> MakeBeforeRun(ScriptPlayer player, InputService service) => async () =>
    {
        // 드라이버 경로는 사람이 쓰는 바로 그 마우스로 보내야 게임이 본다. 아직 못 봤으면 지금 말해 준다(영상이면 보내지 않으니 해당 없다).
        if (!IsVideoTarget && service.Adapter is Minguk.Tools.Input.Adapters.InterceptionInputAdapter { SawHumanMouse: false })
            _notify("마우스를 한 번 움직여 주세요 - Interception 이 어느 마우스로 보낼지 아직 모릅니다(붙은 첫 자리로 보냅니다).");

        if (!_emergency.Arm(() => { player.Stop(); _api?.ReleaseAll(); }, out var problem) && problem is not null)
            _notify(problem);

        if (IsVideoTarget)
        {
            Console.Print("대상이 영상입니다 - 키·클릭·조준은 보내지 않고 호출 로그에만 남깁니다. 검출 좌표는 영상 픽셀이고, 조준 배율은 배우지 않습니다.");
            return;
        }

        // 배율이 안 맞으면 조준이 목표를 지나치거나 못 미친다. 꺼 두고 돌리다 "왜 안 배우지" 로 1,003번을
        // 돌린 적이 있어(실측), 어느 쪽이든 시작할 때 한 줄로 말해 준다.
        Console.Print(player.IsAimScaleAuto
            ? $"조준 배율 {player.AimScalePercent}% 로 시작합니다 - 겨눈 결과를 보고 스스로 맞춥니다."
            : $"조준 배율 {player.AimScalePercent}% 고정입니다 - 스스로 맞추게 하려면 도구 줄의 \"자동\" 을 켜세요.");

        await _activateTarget();
    };

    /// <summary>잡은 대상이 영상 파일인가. 그러면 입력을 보내지 않는다 - 보내면 앞에 있는 진짜 창으로 들어간다.</summary>
    private bool IsVideoTarget => _target() is { Kind: CaptureTargetKind.Video };

    /// <summary>API 에 빌려 줄 것들을 한데 묶는다. 소스·빌드된 것 두 경로가 똑같이 쓴다 - 한쪽만 고쳐 어긋나지 않게.</summary>
    /// <remarks>대상이 영상이면 한 바퀴마다 보내지 않는 경로로 바꾸고 배율 배우기를 끈다 - 화면이 안 따라 움직여 배율이 끝없이 커진다.</remarks>
    private LiveScriptHost BuildHost(ScriptPlayer player, InputService service, string? resourceRoot, CancellationToken token)
        => IsVideoTarget
            ? BuildHostCore(player, new InputService(InputAdapterFactory.CreateSilent()) { JitterMs = service.JitterMs }, resourceRoot, learnsAim: false)
            : BuildHostCore(player, service, resourceRoot, learnsAim: true);

    private LiveScriptHost BuildHostCore(ScriptPlayer player, InputService service, string? resourceRoot, bool learnsAim) => new()
    {
        Service = service,
        RequiresForeground = learnsAim && _requiresForeground(),
        Target = _target,
        Hub = Hub,
        Ocr = _ocr,
        OcrFor = _ocrFor,
        Regions = _regions,
        ResourceRoot = resourceRoot,
        RunProject = _runProject,
        Print = Console.Print,
        Watch = Console.Watch,
        Trace = Console.Trace,
        PauseGate = PauseGate,
        HoldTimeMs = player.HoldTimeMs,
        AimScale = player.AimScale,
        // 늘 물린다. 켜고 끄는 것은 IsAimScaleAuto 가 부를 때마다 본다 - 도중에 켜도 바로 먹게.
        AimScaleLearned = learnsAim
            ? learned =>
            {
                var percent = (int)Math.Round(learned * 100);

                Console.Print($"조준 배율을 {percent}% 로 맞췄습니다.");
                _onUi(() => player.AimScalePercent = percent);
            }
            : null,
        IsAimScaleAuto = () => learnsAim && player.IsAimScaleAuto
    };

    public void Dispose()
    {
        PauseGate.Resume();
        _api?.ReleaseAll();
        _emergency.Dispose();
    }
}
