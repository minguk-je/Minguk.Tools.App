using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

using Minguk.Tools.Capture;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;
using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Perception;

namespace Minguk.Tools.Tests;

/// <summary>
/// 실시간 스크립트. 가짜 어댑터(누른 것을 적기만 한다)와 가짜 허브(몹 하나)를 꽂아 화면·게임 없이 돌린다.
/// </summary>
/// <remarks>
/// 여기서 보는 것 - 표의 이름이 실시간 API 에 다 있는지, C#·자바스크립트가 몹을 읽어 그 자리를 누르는지,
/// 눈이 없으면 멈추고 이유를 말하는지, 중지가 쉬기() 사이에 먹는지, 문법 검사가 돌리지 않고 줄 번호를 주는지.
/// 파이썬은 런타임(11MB)을 받아야 해서 여기서는 안 돈다.
/// <b>입력은 절대 실제로 나가지 않는다</b> - 어댑터가 가짜다.
/// </remarks>
internal static partial class Program
{
    private static void TestLiveScripts()
    {
        // ── 표와 실체가 맞는지 ──
        var liveMethods = typeof(LiveScriptApi).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ToHashSet();
        var planMethods = typeof(SequenceScriptApi).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ToHashSet();

        var missingLive = ScriptApiCatalog.LiveNames.Where(n => !liveMethods.Contains(n)).ToList();
        var missingPlan = ScriptApiCatalog.PlanNames.Where(n => !planMethods.Contains(n)).ToList();

        Check("표의 이름이 실시간 API 에 다 있다", missingLive.Count == 0, missingLive.Count == 0 ? $"{ScriptApiCatalog.LiveNames.Count}개" : string.Join(", ", missingLive));
        Check("표의 이름이 계획 API 에 다 있다", missingPlan.Count == 0, missingPlan.Count == 0 ? $"{ScriptApiCatalog.PlanNames.Count}개" : string.Join(", ", missingPlan));

        var monitor = CaptureTarget.EnumerateMonitors().FirstOrDefault();

        if (monitor is null)
        {
            Check("실시간 스크립트 (모니터 없음, 건너뜀)", true, "");
            return;
        }

        // ── C#: 몹을 읽어 그 자리를 누른다 ──
        const string csharp = """
                              var 목록 = 몹들();
                              출력("n=" + 목록.Count);
                              var 몹 = 가장가까운몹();
                              보기("대상", 몹);
                              이동(몹.중심x, 몹.중심y);
                              클릭();
                              키("F");
                              쉬기(5);
                              """;

        RunAndCheck("C#", new RoslynScriptEngine(), csharp, monitor);

        // ── JavaScript: 같은 일 ──
        const string javascript = """
                                  var 목록 = 몹들();
                                  출력("n=" + 목록.Count);
                                  var 몹 = 가장가까운몹();
                                  보기("대상", 몹);
                                  이동(몹.중심x, 몹.중심y);
                                  클릭();
                                  키("F");
                                  쉬기(5);
                                  """;

        RunAndCheck("JavaScript", new JavaScriptEngine(), javascript, monitor);

        // ── 눈이 없으면 멈추고 이유를 말한다 ──
        {
            var hub = new FakeHub(monitor) { IsDetecting = false };
            var (errors, _, _) = Run(new RoslynScriptEngine(), "var m = 몹들();", hub, monitor, CancellationToken.None);

            Check("몹 찾기가 꺼져 있으면 멈추고 이유를 말한다", errors.Count == 1 && errors[0].Message.Contains("몹 찾기"), errors.Count > 0 ? errors[0].Message : "(오류 없음)");
        }

        // ── 중지가 쉬기() 사이에 먹는다 ──
        {
            using var cts = new CancellationTokenSource(150);
            var watch = Stopwatch.StartNew();
            var (errors, _, _) = Run(new RoslynScriptEngine(), "while (!중지되었나()) 쉬기(20);", new FakeHub(monitor), monitor, cts.Token);
            watch.Stop();

            Check("중지하면 쉬기() 사이에 멈추고 오류가 아니다", errors.Count == 0 && watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds}ms, 오류 {errors.Count}");
        }

        // ── 문법 검사는 돌리지 않고 줄 번호를 준다 ──
        {
            var adapter = new RecordingAdapter();
            var cs = new RoslynScriptEngine().CheckLiveAsync("클릭();\nType(\"a\"").GetAwaiter().GetResult();
            var js = new JavaScriptEngine().CheckLiveAsync("클릭();\nfunction (").GetAwaiter().GetResult();

            Check("C# 검사가 줄 번호를 준다", cs.Count > 0 && cs[0].Line == 2, cs.Count > 0 ? cs[0].ToString() : "(오류 없음)");
            Check("JS 검사가 줄 번호를 준다", js.Count > 0 && js[0].Line == 2, js.Count > 0 ? js[0].ToString() : "(오류 없음)");
            Check("검사는 입력을 보내지 않는다", adapter.Calls.Count == 0, $"{adapter.Calls.Count}건");
        }
    }

    private static void RunAndCheck(string language, IScriptEngine engine, string source, CaptureTarget monitor)
    {
        var (errors, adapter, printed) = Run(engine, source, new FakeHub(monitor), monitor, CancellationToken.None);

        Check($"{language} 실시간: 오류 없이 돈다", errors.Count == 0, errors.Count == 0 ? "" : errors[0].ToString());
        Check($"{language} 실시간: 몹을 읽는다", printed.Contains("n=1"), string.Join(" / ", printed));

        // 이동은 부드럽게 여러 걸음으로 간다. 마지막 걸음이 몹 자리여야 하고, 그 뒤에 누름·뗌이 온다.
        var lastMove = adapter.Calls.FindLastIndex(c => c.StartsWith("MoveTo"));
        var released = adapter.Calls.FindIndex(c => c == "Release Left" || c == "Click Left");
        var atMob = lastMove >= 0 && adapter.Calls[lastMove] == "MoveTo 1280,720";
        var pressed = adapter.Calls.Contains("Press 70") && adapter.Calls.Contains("Release 70");   // F = 0x46

        Check($"{language} 실시간: 몹 자리로 옮겨 누른다", atMob && released > lastMove,
              lastMove >= 0 ? $"{adapter.Calls[lastMove]} → {string.Join(", ", adapter.Calls.Skip(lastMove + 1).Take(3))}" : "이동 없음");
        Check($"{language} 실시간: 키 이름으로 누른다", pressed, string.Join(", ", adapter.Calls.Skip(Math.Max(0, adapter.Calls.Count - 3))));

        engine.Dispose();
    }

    private static (IReadOnlyList<ScriptError> Errors, RecordingAdapter Adapter, List<string> Printed) Run(
        IScriptEngine engine, string source, FakeHub hub, CaptureTarget target, CancellationToken token)
    {
        var adapter = new RecordingAdapter();
        var printed = new List<string>();

        var host = new LiveScriptHost
        {
            Service = new InputService(adapter),
            RequiresForeground = false,
            Target = () => target,
            Hub = hub,
            Print = printed.Add,
            Watch = (name, value) => printed.Add($"{name}={value}"),
            HoldTimeMs = 1
        };

        var api = new LiveScriptApi(host, token);
        var errors = engine.RunLiveAsync(source, api, token).GetAwaiter().GetResult();

        return (errors, adapter, printed);
    }

    /// <summary>누른 것을 적기만 하는 어댑터. 실제로는 아무것도 안 나간다.</summary>
    private sealed class RecordingAdapter : IInputAdapter
    {
        public List<string> Calls { get; } = [];

        public string Name => "가짜";
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public bool RequiresForegroundTarget => false;
        public (int X, int Y)? GetCursorPosition() => (0, 0);
        public bool MoveMouseTo(int screenX, int screenY) { Calls.Add($"MoveTo {screenX},{screenY}"); return true; }
        public bool PressMouseButton(MouseButton button) { Calls.Add($"Press {button}"); return true; }
        public bool ReleaseMouseButton(MouseButton button) { Calls.Add($"Release {button}"); return true; }
        public bool ClickMouseButton(MouseButton button) { Calls.Add($"Click {button}"); return true; }
        public bool ScrollWheel(int delta) { Calls.Add($"Scroll {delta}"); return true; }
        public bool PressKey(ushort virtualKey) { Calls.Add($"Press {virtualKey}"); return true; }
        public bool ReleaseKey(ushort virtualKey) { Calls.Add($"Release {virtualKey}"); return true; }
        public void Dispose() { }
    }

    /// <summary>몹 하나가 화면 가운데에 있는 허브.</summary>
    private sealed class FakeHub(CaptureTarget target) : IPerceptionHub
    {
        public bool IsCapturing { get; set; } = true;
        public bool IsDetecting { get; set; } = true;
        public CaptureTarget? Target => target;
        public bool WantsFrames { get; set; }

        public DetectionSnapshot? Latest { get; } = new(
            [new Detection("일반 봇", LabelBox.FromCorners(0, 0.45, 0.45, 0.55, 0.55), 0.9f)],
            ["일반봇"], 1920, 1080, target, Environment.TickCount64);

        public void PublishState(bool capturing, bool detecting, CaptureTarget? target) { }
        public void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight) { }
        public void PublishFrame(byte[] bgra, int width, int height) { }
        public bool TryCropFrame(Rect ratio, out BitmapSource? crop) { crop = null; return false; }
    }
}
