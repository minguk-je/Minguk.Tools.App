using System;
using System.Collections.Generic;
using System.Linq;

using Minguk.Tools.Capture;

using Vortice.Direct3D11;

namespace Minguk.Tools.Tests;

/// <summary>
/// 공유 캡처 세션. 가짜 세션 공장을 꽂아, 같은 창을 잡는 손잡이 둘이 실제 세션 하나를 나눠 쓰는지 본다.
/// </summary>
/// <remarks>
/// 여기서 보는 것 - 둘이 시작해도 세션은 하나, 프레임은 둘 다 받고, 하나가 멈춰도 세션은 살고, 둘 다 멈추면 놓고,
/// 리드백 없는 세션에 리드백을 원하는 손잡이가 오면 새로 만들고, fps 는 큰 값이고, 다른 대상은 다른 세션.
/// </remarks>
internal static partial class Program
{
    private static void TestCaptureHub()
    {
        var made = new List<FakeSession>();
        var hub = CaptureSessionHubFactory.Create((target, readback) =>
        {
            var session = new FakeSession(target, readback);
            made.Add(session);
            return session;
        });

        var game = new CaptureTarget { Kind = CaptureTargetKind.Window, Handle = 1234, Title = "게임" };
        var other = new CaptureTarget { Kind = CaptureTargetKind.Monitor, Handle = 1, Title = "모니터" };

        var a = hub.Acquire(game, cpuReadback: false);
        var b = hub.Acquire(game, cpuReadback: false);

        var framesA = 0;
        var framesB = 0;
        a.FrameArrived += (_, _) => framesA++;
        b.FrameArrived += (_, _) => framesB++;

        a.TargetFps = 30;
        b.TargetFps = 60;
        a.Start();
        b.Start();

        Check("같은 창을 둘이 잡아도 세션은 하나", made.Count == 1 && made[0].IsRunning, $"세션 {made.Count}개, 손잡이 {hub.ConsumerCount(game)}개");
        Check("fps 는 손잡이 중 큰 값", made[0].TargetFps == 60, $"{made[0].TargetFps}");

        made[0].Emit();
        Check("프레임은 도는 손잡이 전부가 받는다", framesA == 1 && framesB == 1, $"a {framesA}, b {framesB}");

        a.Stop();
        made[0].Emit();
        Check("하나가 멈춰도 세션은 살고 나머지만 받는다", made[0].IsRunning && framesA == 1 && framesB == 2, $"a {framesA}, b {framesB}, 세션 {made[0].IsRunning}");

        b.Stop();
        Check("둘 다 멈추면 세션을 놓는다", !made[0].IsRunning && made[0].IsDisposed, $"도는 중 {made[0].IsRunning}, 놓음 {made[0].IsDisposed}");

        // 리드백: 없는 세션에 원하는 손잡이가 오면 새로 만든다.
        a.Start();
        var c = hub.Acquire(game, cpuReadback: true);
        var noticed = false;
        a.Notice += (_, m) => noticed = m.Contains("리드백");
        c.Start();

        Check("리드백을 원하는 손잡이가 오면 세션을 새로 만든다", made.Count == 3 && made[2].HasReadback && made[2].IsRunning && made[1].IsDisposed,
              $"세션 {made.Count}개, 마지막 리드백 {made.LastOrDefault()?.HasReadback}");
        Check("그 사이에 다른 손잡이는 알림을 받는다", noticed, noticed ? "" : "알림 없음");

        // 다른 대상은 다른 세션
        var d = hub.Acquire(other, cpuReadback: false);
        d.Start();
        Check("다른 대상은 다른 세션", made.Count == 4 && made[3].Target.Kind == CaptureTargetKind.Monitor, $"세션 {made.Count}개");

        a.Dispose(); b.Dispose(); c.Dispose(); d.Dispose();
        Check("손잡이를 다 놓으면 세션도 다 놓는다", made.All(s => s.IsDisposed) && hub.ConsumerCount(game) == 0, $"남은 손잡이 {hub.ConsumerCount(game)}");
    }

    /// <summary>프레임을 내보내는 척만 하는 세션.</summary>
    private sealed class FakeSession(CaptureTarget target, bool readback) : IScreenCaptureAdapter
    {
        public bool HasReadback { get; } = readback;
        public bool IsDisposed { get; private set; }

        public string Name => "가짜";
        public CaptureTarget Target { get; } = target;
        public bool IsRunning { get; private set; }
        public int TargetFps { get; set; }
        public ID3D11Device? Device => null;
        public ID3D11DeviceContext? Context => null;

        public event EventHandler<CapturedFrameEventArgs>? FrameArrived;
        public event EventHandler<string>? Notice;

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Dispose() => IsDisposed = true;

        public void Emit() => FrameArrived?.Invoke(this, new CapturedFrameEventArgs
        {
            FrameId = 1, Width = 16, Height = 16, LatencyMs = 0, ReadbackMs = 0, ProcessMs = 0, Texture = null!
        });

        public void Say(string message) => Notice?.Invoke(this, message);
    }
}
