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
/// 편집·플레이 두 화면이 똑같이 한다. 화면마다 적으면 F9 나 잠금 하나를 한쪽에서만 고치게 된다.
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
        Console = new ScriptConsole(onUi);
    }

    /// <summary>출력 칸. XAML 이 <c>Live.Console.Text</c> 로 묶는다.</summary>
    public ScriptConsole Console { get; }

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
                HoldTimeMs = player.HoldTimeMs
            };

            var api = new LiveScriptApi(host, token);
            _api = api;

            // 도는 동안만 F9 를 쥔다. 못 쥐면 알리고 그냥 돈다 - 화면의 중지 버튼이 있다.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);

            if (!_emergency.Arm(() => { linked.Cancel(); api.ReleaseAll(); player.Stop(); }, out var problem) && problem is not null)
                _notify(problem);

            progress.Report($"실시간 실행 중 - 비상 정지 {EmergencyStop.Label}");

            try
            {
                var errors = await engine.RunLiveAsync(source, api, linked.Token);

                if (errors.Count > 0)
                {
                    progress.Report($"실패: {errors[0]}");
                    Console.Print($"멈춤: {errors[0]}");
                    return false;
                }

                if (api.Outcome == LiveScriptOutcome.Stopped || linked.IsCancellationRequested)
                    return false;

                return true;
            }
            finally
            {
                api.ReleaseAll();
                _emergency.Disarm();
                Hub.WantsFrames = false;
                _api = null;
            }
        }, _activateTarget);
    }

    public void Dispose()
    {
        _api?.ReleaseAll();
        _emergency.Dispose();
    }
}
