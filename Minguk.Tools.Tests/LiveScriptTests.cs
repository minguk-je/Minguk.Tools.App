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

        // ── 조준: 화면 가운데에서 목표까지의 거리만큼 상대 이동 ──
        {
            Minguk.Tools.Capture.Input.CaptureTargetBounds.TryGet(monitor, out var bounds);
            var cx = (int)(bounds.Left + (bounds.Width / 2));
            var cy = (int)(bounds.Top + (bounds.Height / 2));

            var (errors, adapter, printed) = Run(new RoslynScriptEngine(), $"출력(조준({cx + 100}, {cy - 40})); 출력(조준({cx + 100}, {cy - 40}));", new FakeHub(monitor) { FrameTicks = 1 }, monitor, CancellationToken.None);
            var aimMoves = adapter.Calls.Where(c => c.StartsWith("MoveBy")).Select(c => c[7..].Split(',').Select(int.Parse).ToArray()).ToList();

            // 일부러 조금 모자라게 보낸다(85%) - 지나치면 반대편에서 다시 꺾어야 해서 화면이 왕복한다(실측).
            // 방향은 맞고, 넘치지는 않아야 한다.
            var sumX = aimMoves.Sum(m => m[0]);
            var sumY = aimMoves.Sum(m => m[1]);

            Check("조준은 목표 쪽으로, 넘치지 않게 조금 모자라게 보낸다",
                  errors.Count == 0 && sumX > 0 && sumX < 100 && sumX >= 70 && sumY < 0 && sumY > -40 && sumY <= -28,
                  $"{aimMoves.Count}걸음, 거리(100, -40) → 합({sumX}, {sumY})" + (errors.Count > 0 ? " / " + errors[0] : ""));

            // 사람처럼 움직이는가 - 잘게 나누고, 가운데가 빠르고 양 끝이 느리다.
            // 등속이거나 몇 걸음으로 끝나면 시야가 뚝뚝 끊긴다("팍팍 이동", 실측).
            {
                var sizes = aimMoves.Select(m => Math.Sqrt(((double)m[0] * m[0]) + ((double)m[1] * m[1]))).ToList();
                var edge = (sizes.First() + sizes.Last()) / 2;
                var middle = sizes[sizes.Count / 2];

                Check("조준은 잘게 나누고 천천히 떼어 천천히 멈춘다",
                      sizes.Count >= 10 && middle > edge * 1.5 && sizes.Max() <= 30,
                      $"{sizes.Count}걸음, 양끝 평균 {edge:0.0} · 가운데 {middle:0.0} · 가장 큰 걸음 {sizes.Max():0.0}");
            }

            Check("멀면 겨누고 false, 새 화면이 오기 전에는 기다렸다가 안 겨눈다(false)",
                  printed.Select(p => p.ToLowerInvariant()).SequenceEqual(["false", "false"]), string.Join(", ", printed));
        }

        // ── 머리 자리: 사각형 가운데가 아니라 위쪽을 겨눈다 ──
        {
            var (errors, _, printed) = Run(new RoslynScriptEngine(),
                "var m = 가장가까운몹(); 출력(m.중심x + \",\" + m.중심y + \",\" + m.머리x + \",\" + m.머리y + \",\" + m.높이);",
                new FakeHub(monitor), monitor, CancellationToken.None);

            var parts = printed.Count == 1 ? printed[0].Split(',').Select(int.Parse).ToArray() : [];
            var ok = parts.Length == 5;

            if (ok)
            {
                var (centerY, headX, headY, height) = (parts[1], parts[2], parts[3], parts[4]);
                var top = centerY - (height / 2);

                // 가로는 가운데 그대로, 세로는 위 모서리와 가운데 사이 - 꼭대기에 붙으면 머리 위 허공이다.
                ok = headX == parts[0] && headY < centerY && headY > top
                     && Math.Abs(headY - (top + (height * 0.18))) <= 1;
            }

            Check("머리 자리는 사각형 위에서 18% 내려온 곳 (가로는 가운데)", errors.Count == 0 && ok,
                  printed.Count == 1 ? $"중심·머리·높이 = {printed[0]}" : "출력이 없다");
        }

        // ── 목표 고정: 몹이 둘이어도 같은 것만 본다 ──
        {
            var hub = new TwoMobHub(monitor);

            // 같은 것을 세 번 부른다. 사이에 "가까운 쪽" 이 바뀌어도 고정한 것을 계속 줘야 한다.
            var (errors, _, printed) = Run(new RoslynScriptEngine(),
                """
                var a = 목표(); 출력(a.중심x);
                눈뒤집기();
                var b = 목표(); 출력(b.중심x);
                목표풀기();
                var c = 목표(); 출력(c.중심x);
                """.Replace("눈뒤집기();", ""), hub, monitor, CancellationToken.None);

            // 첫 번째와 두 번째는 같은 몹(고정), 세 번째는 풀었으니 다시 가장 가까운 것.
            var same = printed.Count == 3 && printed[0] == printed[1];

            Check("목표는 고정되고, 풀면 다시 고른다", errors.Count == 0 && same && printed[2] == printed[0],
                  errors.Count > 0 ? errors[0].ToString() : string.Join(" / ", printed));

            // 가까운 몹을 숨기면 남는 것은 멀리 있는 하나뿐이다. 먼 목표는 **두 번 연속 같은 자리에 보여야** 고른다 -
            // 한 프레임 반짝한 헛것으로 화면이 확 돌아 버리기 때문이다(실측: 78% 짜리 한 장에 1200,-1200 을 보냈다).
            hub.HideNear = true;

            var (missErrors, _, missPrinted) = Run(new RoslynScriptEngine(),
                """
                var a = 목표(); 출력(a is null ? "없음" : a.중심x.ToString());
                var b = 목표(); 출력(b is null ? "없음" : b.중심x.ToString());
                """,
                hub, monitor, CancellationToken.None);

            Check("먼 목표는 한 번 더 보고 고른다 (한 프레임짜리 헛것을 안 쫓게)",
                  missErrors.Count == 0 && missPrinted.Count == 2 && missPrinted[0] == "없음" && missPrinted[1] != "없음",
                  missErrors.Count > 0 ? missErrors[0].ToString() : string.Join(" → ", missPrinted));
        }

        // ── 상대이동: 작은 이동도 합이 정확하다(걸음마다 반올림해도 어긋나지 않게) ──
        {
            var (errors, adapter, _) = Run(new RoslynScriptEngine(), "상대이동(3, -4); 상대이동(-11, 0);", new FakeHub(monitor), monitor, CancellationToken.None);
            var moves = adapter.Calls.Where(c => c.StartsWith("MoveBy")).Select(c => c[7..].Split(',').Select(int.Parse).ToArray()).ToList();

            Check("상대이동은 걸음으로 나눠도 합이 정확하다",
                  errors.Count == 0 && moves.Sum(m => m[0]) == -8 && moves.Sum(m => m[1]) == -4,
                  $"{moves.Count}걸음, 합({moves.Sum(m => m[0])}, {moves.Sum(m => m[1])})" + (errors.Count > 0 ? " / " + errors[0] : ""));
        }

        // ── 조준: 가운데 가까우면 맞음(true) ──
        {
            Minguk.Tools.Capture.Input.CaptureTargetBounds.TryGet(monitor, out var bounds);
            var cx = (int)(bounds.Left + (bounds.Width / 2));
            var cy = (int)(bounds.Top + (bounds.Height / 2));

            var (errors, _, printed) = Run(new RoslynScriptEngine(), $"출력(조준({cx + 5}, {cy - 3}));", new FakeHub(monitor), monitor, CancellationToken.None);
            Check("조준: 가운데 8px 안이면 맞았다(true)", errors.Count == 0 && printed.Select(p => p.ToLowerInvariant()).SequenceEqual(["true"]), string.Join(", ", printed));
        }

        // ── 조준 배율 배우기: 한 번에 크게 안 바꾸고, 되풀이하면 참값으로 다가간다 ──
        {
            const double trueScale = 3.45;   // 픽셀 하나를 옮기는 데 드는 카운트(게임 감도). 배율이 이것에 다가가야 한다.

            var learned = new List<double>();
            var hub = new FakeHub(monitor) { FrameTicks = 1 };
            var adapter = new RecordingAdapter();
            var host = new LiveScriptHost
            {
                Service = new InputService(adapter),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = hub,
                Print = _ => { },
                Watch = (_, _) => { },
                HoldTimeMs = 1,
                AimScale = 1.0,
                AimScaleLearned = learned.Add
            };

            var api = new LiveScriptApi(host, CancellationToken.None);
            Minguk.Tools.Capture.Input.CaptureTargetBounds.TryGet(monitor, out var bounds);
            var cx = (int)(bounds.Left + (bounds.Width / 2));
            var cy = (int)(bounds.Top + (bounds.Height / 2));

            // 같은 상황을 되풀이한다: 200px 떨어진 몹을 겨누고, 보낸 카운트만큼 실제로 움직인 화면을 돌려준다.
            // 가운뎃값을 쓰므로 표본이 찰 만큼(AimSamples=9) 돌아야 배율이 움직이기 시작한다.
            for (var round = 0; round < 20; round++)
            {
                adapter.Calls.Clear();
                hub.FrameTicks = Environment.TickCount64 + (round * 1000) + 1;
                api.Aim(cx + 200, cy);

                var sent = adapter.Calls.Where(c => c.StartsWith("MoveBy")).Sum(c => int.Parse(c[7..].Split(',')[0]));
                var remaining = 200 - (int)Math.Round(sent / trueScale);

                hub.FrameTicks = Environment.TickCount64 + (round * 1000) + 500;
                api.Aim(cx + remaining, cy);
            }

            var jumped = learned.Zip(learned.Skip(1)).Any(p => p.Second > p.First * 1.5 + 0.001 || p.Second < p.First / 1.5 - 0.001);

            // 너무 가깝거나 너무 먼 조준으로는 안 배운다. 실측에서 21px 이 1.83, 831px 이 1.33 으로 나와
            // 배율을 2.4 ↔ 3.6 으로 흔들었다 - 가까운 것은 검출 떨림, 먼 것은 상한 잘림과 원근 탓이다.
            var ignored = true;
            var detail = new List<string>();

            foreach (var offset in new[] { 21, 900 })
            {
                adapter.Calls.Clear();
                hub.FrameTicks = Environment.TickCount64 + 90000 + offset;
                api.Aim(cx + offset, cy);

                // 첫 번째 조준은 <b>앞 바퀴</b>의 거리로 배운다(배우기는 늘 한 박자 늦다). 여기서부터 센다.
                var before = learned.Count;
                var sent = adapter.Calls.Where(c => c.StartsWith("MoveBy")).Sum(c => int.Parse(c[7..].Split(',')[0]));

                hub.FrameTicks = Environment.TickCount64 + 91000 + offset;
                api.Aim(cx + offset - (int)Math.Round(sent / trueScale), cy);

                if (learned.Count != before) ignored = false;

                detail.Add($"{offset}px: {before}→{learned.Count}");
            }

            Check("배율은 너무 가깝거나 너무 먼 조준으로는 안 배운다", ignored, string.Join(", ", detail));

            // 잡음 하나에 안 흔들리는가. 몹이 스스로 움직이거나 화면이 덜 돌면 "덜 움직였다" 가 되어 잰 값이
            // 크게 나오는데, 그 잡음은 늘 한쪽(위)으로만 튄다 - 한 값씩 반영하면 위로만 떠밀려 상한까지 간다
            // (실측: 3.6 에서 시작해 734번 배우는 동안 20 에 붙었고, 그러자 모든 조준이 잘려 화면이 안 돌았다).
            {
                var settled = learned[^1];

                for (var bad = 0; bad < 2; bad++)
                {
                    adapter.Calls.Clear();
                    hub.FrameTicks = Environment.TickCount64 + 50000 + (bad * 1000);
                    api.Aim(cx + 200, cy);

                    // 화면이 거의 안 돌았다고 답한다 - 잰 값이 터무니없이 커지는 상황.
                    hub.FrameTicks = Environment.TickCount64 + 50500 + (bad * 1000);
                    api.Aim(cx + 190, cy);
                }

                var after = learned[^1];

                Check("배율은 잘못 잰 값 몇 개에 흔들리지 않는다 (가운뎃값)",
                      Math.Abs(after - settled) / settled < 0.15,
                      $"{settled:0.00} → {after:0.00} (잡음 2번 뒤)");
            }

            Check("조준 배율 배우기: 한 번에 1.5배 넘게 안 바꾸고 참값(3.45)으로 다가간다",
                  learned.Count >= 4 && !jumped && learned[^1] > 2.5 && learned[^1] < 4.2,
                  string.Join(" → ", learned.Select(v => v.ToString("0.00"))));
        }

        // ── 클릭(버튼, 누르는 시간) ──
        {
            var watch = Stopwatch.StartNew();
            var (errors, adapter, _) = Run(new RoslynScriptEngine(), "클릭(80);", new FakeHub(monitor), monitor, CancellationToken.None);
            watch.Stop();
            var buttons = adapter.Calls.Where(c => c.StartsWith("Press") || c.StartsWith("Release")).ToList();

            Check("클릭(80): 좌클릭을 80ms 누르고 있다가 뗀다", errors.Count == 0 && buttons.SequenceEqual(["Press Left", "Release Left"]) && watch.ElapsedMilliseconds >= 80,
                  string.Join(", ", buttons) + $" ({watch.ElapsedMilliseconds}ms)" + (errors.Count > 0 ? " / " + errors[0] : ""));

            var (jsErrors, jsAdapter, _) = Run(new JavaScriptEngine(), "클릭('Right', 20); 클릭(); 우클릭(10); 클릭(15);", new FakeHub(monitor), monitor, CancellationToken.None);
            var jsButtons = jsAdapter.Calls.Where(c => c.StartsWith("Press") || c.StartsWith("Release")).ToList();

            Check("클릭('Right', 20) · 클릭() · 우클릭(10) · 클릭(15): 자바스크립트", jsErrors.Count == 0 && jsButtons.SequenceEqual(["Press Right", "Release Right", "Press Left", "Release Left", "Press Right", "Release Right", "Press Left", "Release Left"]),
                  string.Join(", ", jsButtons) + (jsErrors.Count > 0 ? " / " + jsErrors[0] : ""));
        }

        // ── 끌기·상대끌기·버튼누르기: 누른 채 움직였다 뗀다 ──
        {
            var (errors, adapter, _) = Run(new RoslynScriptEngine(), "상대끌기(90, 0, \"Right\"); 버튼누르기(); 버튼떼기();", new FakeHub(monitor), monitor, CancellationToken.None);
            var calls = adapter.Calls.Where(c => c.StartsWith("Press") || c.StartsWith("Release") || c.StartsWith("MoveBy")).ToList();
            var moves = calls.Where(c => c.StartsWith("MoveBy")).Select(c => int.Parse(c[7..].Split(',')[0])).ToList();

            // 걸음 수는 못 박지 않는다 - 사람처럼 움직이느라 거리에 따라 달라진다. 지킬 것은 "누른 채 움직이고 반드시 뗀다" 와 합이다.
            Check("상대끌기: 우버튼을 누른 채 나눠 움직였다 뗀다", errors.Count == 0 && calls[0] == "Press Right" && calls[^3] == "Release Right"
                  && moves.Sum() == 90 && moves.Count >= 2 && calls[^2] == "Press Left" && calls[^1] == "Release Left",
                  $"{moves.Count}걸음, 합 {moves.Sum()} · " + string.Join(", ", calls.Take(3)) + " … " + string.Join(", ", calls.TakeLast(3)) + (errors.Count > 0 ? " / " + errors[0] : ""));

            var (dragErrors, dragAdapter, _) = Run(new RoslynScriptEngine(), "끌기(300, 400);", new FakeHub(monitor), monitor, CancellationToken.None);
            var dragCalls = dragAdapter.Calls.Where(c => c.StartsWith("Press") || c.StartsWith("Release") || c.StartsWith("MoveTo")).ToList();

            Check("끌기: 누른 채 그 자리로 옮겼다 뗀다", dragErrors.Count == 0 && dragCalls[0] == "Press Left" && dragCalls[^1] == "Release Left"
                  && dragCalls.Any(c => c == "MoveTo 300,400"),
                  string.Join(", ", dragCalls) + (dragErrors.Count > 0 ? " / " + dragErrors[0] : ""));
        }

        // ── 걷기: 누르고 있다가 반드시 뗀다 ──
        {
            var (errors, adapter, _) = Run(new RoslynScriptEngine(), "걷기(\"W+A\", 30);", new FakeHub(monitor), monitor, CancellationToken.None);
            var keys = adapter.Calls.Where(c => c.StartsWith("Press") || c.StartsWith("Release")).ToList();

            Check("걷기는 키들을 누르고 있다가 거꾸로 뗀다", errors.Count == 0 && keys.SequenceEqual(["Press 87", "Press 65", "Release 65", "Release 87"]), string.Join(", ", keys) + (errors.Count > 0 ? " / " + errors[0] : ""));
        }

        // ── 호출 로그: 무엇을 불렀는지 남는다 ──
        {
            var calls = new List<ScriptCall>();
            var (errors, _, _) = Run(new RoslynScriptEngine(), "이동(10, 20); 클릭();", new FakeHub(monitor), monitor, CancellationToken.None, calls.Add);

            var names = calls.Select(c => c.Name).ToList();
            Check("호출 로그가 부른 것을 남긴다", errors.Count == 0 && names.SequenceEqual(["MoveTo", "Click"]), string.Join(", ", calls));
        }

        // ── JavaScript 중단점: 2번 줄에서 멈추고, 변수가 보이고, 계속하면 끝난다 ──
        {
            var debug = new ScriptDebugSession(a => a());
            debug.Breakpoints.Add(2);

            var paused = new ManualResetEventSlim(false);
            debug.Paused += (_, _) => paused.Set();

            var printed = new List<string>();
            IReadOnlyList<ScriptError>? errors = null;

            var run = System.Threading.Tasks.Task.Run(() =>
                errors = RunWithDebug(new JavaScriptEngine(), "var a = 1;\nvar b = a + 1;\n출력(b);", debug, monitor, printed));

            var hit = paused.Wait(5000);
            var line = debug.PausedLine;
            var locals = debug.Locals ?? string.Empty;

            debug.Continue();
            var finished = run.Wait(5000);

            Check("JS 중단점에서 멈춘다", hit && line == 2, $"멈춤 {hit}, 줄 {line}");
            Check("JS 멈춘 자리의 변수가 보인다", locals.Contains("a=1"), locals.Replace("\n", " / "));
            Check("JS 계속하면 끝까지 돈다", finished && errors is { Count: 0 } && printed.Contains("2"), $"끝 {finished}, 출력 {string.Join(",", printed)}");
        }

        // ── JavaScript 한 줄씩: 1번 줄에서 멈추고 F10 마다 다음 줄 ──
        {
            var debug = new ScriptDebugSession(a => a()) { Mode = ScriptStepMode.Step };
            var lines = new List<int>();
            var paused = new AutoResetEvent(false);
            debug.Paused += (_, _) => { lines.Add(debug.PausedLine); paused.Set(); };

            var run = System.Threading.Tasks.Task.Run(() => RunWithDebug(new JavaScriptEngine(), "var a = 1;\nvar b = 2;\n출력(a + b);", debug, monitor, []));

            for (var i = 0; i < 3 && paused.WaitOne(3000); i++) debug.StepNext();

            var finished = run.Wait(5000);
            Check("JS 한 줄씩 밟는다", finished && lines.Take(3).SequenceEqual([1, 2, 3]), string.Join(",", lines));
        }

        // ── 멈춘 채로 중지하면 그 자리에서 끝난다 ──
        {
            var debug = new ScriptDebugSession(a => a());
            debug.Breakpoints.Add(1);
            var paused = new ManualResetEventSlim(false);
            debug.Paused += (_, _) => paused.Set();

            using var cts = new CancellationTokenSource();
            var run = System.Threading.Tasks.Task.Run(() => RunWithDebug(new JavaScriptEngine(), "var a = 1;\n출력(a);", debug, monitor, [], cts.Token));

            paused.Wait(5000);
            cts.Cancel();
            var finished = run.Wait(3000);

            Check("멈춘 채로 중지하면 끝난다", finished && run.Result.Count == 0, $"끝 {finished}");
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

        // ── 실시간 C# 도 System 이 열려 있다 ──
        //    Live 네임스페이스를 WithImports 로 붙였더니 목록이 갈아 끼워져 System 이 빠졌었다.
        {
            var errors = new RoslynScriptEngine()
                .CheckLiveAsync("var a = Math.Abs(-1);\nlong b = Environment.TickCount64;\nScriptMob? c = null;")
                .GetAwaiter().GetResult();

            Check("실시간 C# 에서 Math·Environment·ScriptMob 을 짧게 쓴다", errors.Count == 0,
                errors.Count == 0 ? "오류 없음" : string.Join(" / ", errors));
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
        IScriptEngine engine, string source, IPerceptionHub hub, CaptureTarget target, CancellationToken token, Action<ScriptCall>? trace = null)
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
            Trace = trace,
            HoldTimeMs = 1
        };

        var api = new LiveScriptApi(host, token);
        var errors = engine.RunLiveAsync(source, api, debug: null, token: token).GetAwaiter().GetResult();

        return (errors, adapter, printed);
    }

    /// <summary>디버그 세션을 물려 돌린다. 멈춤은 세션의 Paused 로 알 수 있다.</summary>
    private static IReadOnlyList<ScriptError> RunWithDebug(
        IScriptEngine engine, string source, ScriptDebugSession debug, CaptureTarget target, List<string> printed, CancellationToken token = default)
    {
        var host = new LiveScriptHost
        {
            Service = new InputService(new RecordingAdapter()),
            RequiresForeground = false,
            Target = () => target,
            Hub = new FakeHub(target),
            Print = printed.Add,
            Watch = (name, value) => printed.Add($"{name}={value}"),
            HoldTimeMs = 1
        };

        var api = new LiveScriptApi(host, token);
        var result = engine.RunLiveAsync(source, api, debug, token).GetAwaiter().GetResult();

        debug.Reset();
        return result;
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
        public bool MoveMouseBy(int deltaX, int deltaY) { Calls.Add($"MoveBy {deltaX},{deltaY}"); return true; }
        public bool PressMouseButton(MouseButton button) { Calls.Add($"Press {button}"); return true; }
        public bool ReleaseMouseButton(MouseButton button) { Calls.Add($"Release {button}"); return true; }
        public bool ClickMouseButton(MouseButton button) { Calls.Add($"Click {button}"); return true; }
        public bool ScrollWheel(int delta) { Calls.Add($"Scroll {delta}"); return true; }
        public bool PressKey(ushort virtualKey) { Calls.Add($"Press {virtualKey}"); return true; }
        public bool ReleaseKey(ushort virtualKey) { Calls.Add($"Release {virtualKey}"); return true; }
        public void Dispose() { }
    }

    /// <summary>몹 하나가 화면 가운데에 있는 허브.</summary>
    /// <summary>몹 둘. 하나는 가운데 가까이, 하나는 멀리 - 목표 고정이 갈아타지 않는지 보려고.</summary>
    private sealed class TwoMobHub(CaptureTarget target) : IPerceptionHub
    {
        public bool IsCapturing => true;
        public bool IsDetecting => true;
        public CaptureTarget? Target => target;
        public bool WantsFrames { get; set; }

        public bool IsPreparingFrames => false;

        public void PreparingFrames() { }

        /// <summary>가까운 쪽을 숨긴다 - 목표가 사라졌을 때를 본다.</summary>
        public bool HideNear { get; set; }

        public DetectionSnapshot? Latest
        {
            get
            {
                var found = new List<Detection>();

                if (!HideNear) found.Add(new Detection("일반 봇", LabelBox.FromCorners(0, 0.46, 0.46, 0.54, 0.54), 0.9f));

                found.Add(new Detection("일반 봇", LabelBox.FromCorners(0, 0.10, 0.60, 0.18, 0.72), 0.85f));

                return new DetectionSnapshot(found, [.. found.Select(_ => "")], 1920, 1080, target, Environment.TickCount64);
            }
        }

        public void PublishState(bool capturing, bool detecting, CaptureTarget? target) { }
        public void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0) { }
        public void PublishFrame(byte[] bgra, int width, int height) { }
        public bool TryCropFrame(Rect ratio, out BitmapSource? crop) { crop = null; return false; }
    }

    private sealed class FakeHub(CaptureTarget target) : IPerceptionHub
    {
        public bool IsCapturing { get; set; } = true;
        public bool IsDetecting { get; set; } = true;
        public CaptureTarget? Target => target;
        public bool WantsFrames { get; set; }

        public bool IsPreparingFrames => false;

        public void PreparingFrames() { }

        /// <summary>검출이 본 프레임의 시각. 0 이면 모름(조준이 같은 화면 검사를 안 한다).</summary>
        public long FrameTicks { get; set; }

        public DetectionSnapshot? Latest => new(
            [new Detection("일반 봇", LabelBox.FromCorners(0, 0.45, 0.45, 0.55, 0.55), 0.9f)],
            ["일반봇"], 1920, 1080, target, Environment.TickCount64) { FrameTicks = FrameTicks };

        public void PublishState(bool capturing, bool detecting, CaptureTarget? target) { }
        public void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0) { }
        public void PublishFrame(byte[] bgra, int width, int height) { }
        public bool TryCropFrame(Rect ratio, out BitmapSource? crop) { crop = null; return false; }
    }
}
