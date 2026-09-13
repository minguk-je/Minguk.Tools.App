using System;
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
/// 스크립트·플레이 두 화면이 똑같이 한다. 화면마다 적으면 F9 나 잠금 하나를 한쪽에서만 고치게 된다.
/// <see cref="Resolve"/> 가 <see cref="ScriptPlayer"/> 에 줄 문맥을 만든다: 한 바퀴 = 엔진이 스크립트를
/// 실시간 API 로 끝까지 돌리는 것. 도는 동안 F9 를 쥐고, 끝나면 놓고 누르고 있던 키를 뗀다.
/// </remarks>
public sealed class LiveScriptSession : IDisposable
{
    private readonly Func<InputService?> _service;
    private readonly Func<bool> _requiresForeground;
    private readonly Func<CaptureTarget?> _target;
    private readonly Func<IOcrEngine?> _ocr;
    private readonly Func<Task> _activateTarget;
    private readonly Action<string> _notify;
    private readonly Action<Action> _onUi;
    private readonly EmergencyStop _emergency = new();

    private LiveScriptApi? _api;

    public LiveScriptSession(
        Func<InputService?> service,
        Func<bool> requiresForeground,
        Func<CaptureTarget?> target,
        Func<IOcrEngine?> ocr,
        Func<Task> activateTarget,
        Action<Action> onUi,
        Action<string> notify)
    {
        _service = service;
        _requiresForeground = requiresForeground;
        _target = target;
        _ocr = ocr;
        _activateTarget = activateTarget;
        _notify = notify;
        _onUi = onUi;
        Console = new ScriptConsole(onUi);
        Debug = new ScriptDebugSession(onUi);
    }

    /// <summary>출력 칸. XAML 이 <c>Live.Console.Text</c> 로 묶는다.</summary>
    public ScriptConsole Console { get; }

    /// <summary>중단점·한 줄씩·멈춘 자리. XAML 이 <c>Live.Debug.*</c> 로 묶는다.</summary>
    public ScriptDebugSession Debug { get; }

    public IPerceptionHub Hub { get; init; } = PerceptionHubFactory.Default;

    /// <summary>
    /// 돌릴 문맥을 만든다. 틀린 줄이 있으면 안 돌린다. 문맥의 한 바퀴가 엔진을 돌리고, 도는 동안 F9 가 산다.
    /// </summary>
    public ScriptRunContext? Resolve(ScriptWorkbench script, ScriptPlayer player)
    {
        if (script.HasError)
        {
            MessengerUtility.SendMainMessage("스크립트에 고칠 줄이 있습니다.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(script.Text))
        {
            MessengerUtility.SendMainMessage("스크립트가 비어 있습니다.");
            return null;
        }

        var service = _service();
        if (service is null) return null;

        service.JitterMs = player.JitterMs;

        var source = script.Text;
        var engine = script.Engine;

        Debug.SupportsStepping = engine.SupportsStepping;
        Console.ClearCalls();

        // 전역 단축키는 STA(UI) 스레드에서 걸어야 한다 - 스크립트 스레드에서 걸었더니 "STA 여야 합니다" 로 실패했다.
        // BeforeRun 은 대기가 끝난 뒤 UI 스레드에서 돈다. 누르면 플레이어를 멈추고(토큰이 API 까지 이어진다) 누른 키를 뗀다.
        var beforeRun = async () =>
        {
            // 드라이버 경로는 사람이 쓰는 바로 그 마우스로 보내야 게임이 본다. 아직 못 봤으면 지금 말해 준다.
            if (service.Adapter is Minguk.Tools.Input.Adapters.InterceptionInputAdapter { SawHumanMouse: false })
                _notify("마우스를 한 번 움직여 주세요 - Interception 이 어느 마우스로 보낼지 아직 모릅니다(붙은 첫 자리로 보냅니다).");

            if (!_emergency.Arm(() => { player.Stop(); _api?.ReleaseAll(); }, out var problem) && problem is not null)
                _notify(problem);

            // 배율이 안 맞으면 조준이 목표를 지나치거나 못 미친다. 꺼 두고 돌리다 "왜 안 배우지" 로 1,003번을
            // 돌린 적이 있어(실측), 어느 쪽이든 시작할 때 한 줄로 말해 준다.
            Console.Print(player.IsAimScaleAuto
                ? $"조준 배율 {player.AimScalePercent}% 로 시작합니다 - 겨눈 결과를 보고 스스로 맞춥니다."
                : $"조준 배율 {player.AimScalePercent}% 고정입니다 - 스스로 맞추게 하려면 도구 줄의 \"자동\" 을 켜세요.");

            await _activateTarget();
        };

        return new ScriptRunContext(async (progress, token) =>
        {
            var host = new LiveScriptHost
            {
                Service = service,
                RequiresForeground = _requiresForeground(),
                Target = _target,
                Hub = Hub,
                Ocr = _ocr,
                Print = Console.Print,
                Watch = Console.Watch,
                Trace = Console.Trace,
                HoldTimeMs = player.HoldTimeMs,
                AimScale = player.AimScale,
                // 늘 물린다. 켜고 끄는 것은 IsAimScaleAuto 가 부를 때마다 본다 - 도중에 켜도 바로 먹게.
                AimScaleLearned = learned =>
                {
                    var percent = (int)Math.Round(learned * 100);

                    Console.Print($"조준 배율을 {percent}% 로 맞췄습니다.");
                    _onUi(() => player.AimScalePercent = percent);
                },
                IsAimScaleAuto = () => player.IsAimScaleAuto
            };

            var api = new LiveScriptApi(host, token);
            _api = api;

            progress.Report($"실시간 실행 중 - 비상 정지 {EmergencyStop.Label}");

            try
            {
                // 줄 단위로 멈출 수 있는 언어에만 디버그 세션을 준다. C# 은 호출 로그로 본다.
                var errors = await engine.RunLiveAsync(source, api, engine.SupportsStepping ? Debug : null, token);

                if (errors.Count > 0)
                {
                    progress.Report($"실패: {errors[0]}");
                    Console.Print($"멈춤: {errors[0]}");
                    return false;
                }

                if (api.Outcome == LiveScriptOutcome.Stopped || token.IsCancellationRequested)
                    return false;

                return true;
            }
            finally
            {
                api.ReleaseAll();
                // 단축키는 건 스레드(UI)에서 풀어야 한다.
                _onUi(_emergency.Disarm);
                Hub.WantsFrames = false;
                Debug.Reset();
                _api = null;
            }
        }, beforeRun);
    }

    public void Dispose()
    {
        _api?.ReleaseAll();
        _emergency.Dispose();
    }
}
