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
                     && Math.Abs(headY - (top + (height * ScriptMob.HeadFraction))) <= 1;
            }

            Check($"머리 자리는 사각형 위에서 {ScriptMob.HeadFraction:P0} 내려온 곳 (가로는 가운데)", errors.Count == 0 && ok,
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

        // ── 목표 흔들림: 같은 몹의 사각형이 프레임마다 위아래로 흔들려도 목표의 머리는 조금만 움직인다 ──
        // 실측 - 가만히 있는 봇의 사각형 가운데가 세로 중간값 17px 흔들려, 크게 돈 뒤 흔들린 머리로 한 번 더 움직였다(사용자, 2026-09-18 "돌고 멈췄다가 머리로").
        {
            var hub = new JitterHub(monitor);
            var (errors, _, printed) = Run(new RoslynScriptEngine(),
                "for (var i = 0; i < 8; i++) { var m = 목표(); 출력(m.머리y); }", hub, monitor, CancellationToken.None);

            var heads = printed.Select(int.Parse).ToList();
            var rawSwing = hub.RawHeadSwing(monitor);
            var steps = heads.Zip(heads.Skip(1), (a, b) => Math.Abs(b - a)).ToList();
            var ok = errors.Count == 0 && heads.Count == 8 && steps.Skip(2).All(d => d <= rawSwing * 0.5);

            Check("목표의 머리는 사각형 흔들림을 반 넘게 따라가지 않는다", ok,
                  errors.Count > 0 ? errors[0].ToString() : $"사각형 머리 흔들림 {rawSwing}px · 목표 머리 {string.Join(" ", heads)}");
        }

        TestAimLoop(monitor);

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

        // ── 몹.이름표: 부를 때만 화면을 읽는다(안 부르면 프레임도 안 청한다) ──
        {
            var quiet = new FakeHub(monitor);
            var (quietErrors, _, _) = Run(new RoslynScriptEngine(), "foreach (var m in 몹들()) 출력(m.이름);", quiet, monitor, CancellationToken.None);

            var asking = new FakeHub(monitor);
            var calls = new List<ScriptCall>();
            var (askErrors, _, _) = Run(new RoslynScriptEngine(), "출력(몹들()[0].이름표);", asking, monitor, CancellationToken.None, calls.Add);

            Check("몹.이름표: 안 부르면 화면을 안 읽고, 부르면 그때 몹 위를 읽으러 간다",
                  quietErrors.Count == 0 && !quiet.WantsFrames && asking.WantsFrames && calls.Any(c => c.Name == "Nameplate")
                  && askErrors.Count == 1 && askErrors[0].Message.Contains("프레임"),
                  $"안 부름: 프레임 청함 {quiet.WantsFrames} · 부름: 청함 {asking.WantsFrames} · 호출 {string.Join(",", calls.Select(c => c.Name))} · {(askErrors.Count > 0 ? askErrors[0].Message : "(오류 없음)")}");
        }

        // ── 일시정지: 쉬는 도중에도 멈추고, 누르던 키를 떼고, 계속하면 남은 만큼 쉬고 끝난다. 멈춘 채 중지해도 끝난다 ──
        foreach (var stopWhilePaused in new[] { false, true })
        {
            var gate = new ScriptPauseGate();
            var adapter = new RecordingAdapter();
            var printed = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var host = new LiveScriptHost
            {
                Service = new InputService(adapter),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor),
                Print = printed.Enqueue,
                Watch = (_, _) => { },
                PauseGate = gate,
                HoldTimeMs = 1
            };

            using var cts = new CancellationTokenSource();
            var api = new LiveScriptApi(host, cts.Token);
            var run = System.Threading.Tasks.Task.Run(() => new RoslynScriptEngine().RunLiveAsync("누르기(\"W\"); 쉬기(600); 떼기(\"W\"); 출력(\"끝\");", api, null, cts.Token).GetAwaiter().GetResult());

            var pressedDeadline = Environment.TickCount64 + 15000;
            while (!adapter.Calls.Contains("Press 87") && Environment.TickCount64 < pressedDeadline) Thread.Sleep(10);
            Thread.Sleep(150);
            gate.Pause();
            Thread.Sleep(800);

            var stillRunning = !run.IsCompleted;
            var released = adapter.Calls.Contains("Release 87");
            var noticed = printed.Any(p => p.StartsWith("일시정지"));

            if (stopWhilePaused)
            {
                cts.Cancel();
                var finished = run.Wait(3000);
                Check("일시정지한 채 중지하면 끝난다", stillRunning && finished && run.Result.Count == 0 && !printed.Contains("끝"), $"멈춰 있었음 {stillRunning} · 끝 {finished}");
            }
            else
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                gate.Resume();
                var finished = run.Wait(5000);

                Check("일시정지: 쉬기 도중에 멈추고 누르던 키를 떼고, 계속하면 남은 만큼 쉬고 끝난다",
                    stillRunning && released && noticed && finished && run.Result.Count == 0 && printed.Contains("끝") && watch.ElapsedMilliseconds >= 300,
                    $"멈춰 있었음 {stillRunning} · 뗌 {released} · 알림 {noticed} · 끝 {finished} · 계속 뒤 {watch.ElapsedMilliseconds}ms · {string.Join(", ", adapter.Calls.Where(c => c.Contains(" 87")))}");
            }
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

    /// <summary>조준 스레드(<see cref="AimLoop"/>) - 닫힌 고리 가짜 허브(<see cref="SimHub"/>)로 붙기·달리는 몹·지나침·배율 배우기를 본다. <c>--aim</c> 은 이것만 돌린다.</summary>
    private static void TestAimLoop(CaptureTarget monitor)
    {
        // ── 조준 스레드: 몹을 주면 멈추지 않고 따라가 붙고, 움직이는 몹도 쫓고, 목표풀기면 선다(사용자, 2026-09-18 "돌고 멈췄다가 머리로" 가 여전히 부자연스러워 스레드로) ──
        {
            const double scale = 3.5;

            // 가만히 있는 몹 300px 옆 - 붙을 때까지 마우스가 쉬지 않고 움직여야 한다.
            {
                var adapter = new RecordingAdapter();
                var hub = new SimHub(monitor, adapter, scale) { OffsetPx = 300 };
                var printed = new List<string>();
                var host = new LiveScriptHost { Service = new InputService(adapter), RequiresForeground = false, Target = () => monitor, Hub = hub, Print = printed.Add, Watch = (_, _) => { }, HoldTimeMs = 1, AimScale = scale };

                using (var api = new LiveScriptApi(host, CancellationToken.None))
                {
                    var errors = new RoslynScriptEngine().RunLiveAsync("for (var i = 0; i < 12; i++) { var m = 목표(); if (m is null) { 출력(\"없음\"); break; } 출력(조준(m)); }", api, debug: null, token: CancellationToken.None).GetAwaiter().GetResult();

                    List<(long Ticks, int Dx, int Dy)> moves;
                    lock (adapter.Moves) moves = adapter.Moves.ToList();
                    var final = Math.Abs(hub.TrueOffset(Environment.TickCount64));

                    // 걸음마다의 참 거리(그 걸음까지 보낸 것 반영). 다가가는 동안(30px 밖)은 쉬지 않아야 하고, 반대편으로 지나치면 안 된다.
                    var trail = moves.Select(m => (m.Ticks, Offset: hub.TrueOffset(m.Ticks))).ToList();
                    var approaching = trail.TakeWhile(p => p.Offset > 30).ToList();
                    var gaps = approaching.Zip(approaching.Skip(1), (a, b) => b.Ticks - a.Ticks).ToList();
                    var longestGap = gaps.Count > 0 ? gaps.Max() : 0;
                    var overshoot = trail.Count > 0 ? Math.Max(0, -trail.Min(p => p.Offset)) : 0;
                    var reach = trail.FirstOrDefault(p => p.Offset <= 20).Ticks is var at and > 0 ? at - trail[0].Ticks : -1;
                    var flick = trail.FirstOrDefault(p => p.Offset <= 40).Ticks is var flickAt and > 0 ? flickAt - trail[0].Ticks : -1;
                    var biggest = moves.Count > 0 ? moves.Max(m => Math.Abs(m.Dx)) : 0;
                    var hits = printed.Count(p => p == "True");

                    // 크게 꺾기는 빠르게(300px 의 몸 안쪽 40px 까지 0.2초 안 - 시간 상수 95ms 시절에는 0.25초를 넘겼다), 남긴 8% 는 확인 화면을 보고 다듬어 0.45초 안에 머리 20px.
                    Check("조준(몹): 다가가는 동안 8ms 박자로 쉬지 않고(가장 긴 틈 40ms 아래, 한 걸음 120 카운트 아래) 0.2초 안에 40px 까지 꺾고, 지나치지 않고(15px 아래) 0.45초 안에 20px, 끝에는 15px 안에 붙고 맞았다고 한다",
                          errors.Count == 0 && final <= 15 && moves.Count >= 15 && longestGap <= 40 && biggest <= 120 && overshoot <= 15 && flick is > 0 and <= 200 && reach is > 0 and <= 450 && hits >= 3,
                          errors.Count > 0 ? errors[0].ToString() : $"걸음 {moves.Count} · 가장 긴 틈 {longestGap}ms · 가장 큰 걸음 {biggest} · 40px 까지 {flick}ms · 20px 까지 {reach}ms · 지나침 {overshoot:0}px · 남은 {final:0}px · 맞음 {hits}/{printed.Count}");
                }
            }

            // 옆으로 달리는 몹(200px/s) - 속도를 배워 따라잡고 붙어 있는다.
            {
                var adapter = new RecordingAdapter();
                var hub = new SimHub(monitor, adapter, scale) { OffsetPx = 150, VelocityPxPerMs = 0.2 };
                var printed = new List<string>();
                var host = new LiveScriptHost { Service = new InputService(adapter), RequiresForeground = false, Target = () => monitor, Hub = hub, Print = printed.Add, Watch = (_, _) => { }, HoldTimeMs = 1, AimScale = scale };

                using (var api = new LiveScriptApi(host, CancellationToken.None))
                {
                    var samples = new List<double>();
                    // 붙이기 전에 화면 두 장을 본다 - 진짜 앱은 화면이 계속 흐르지만 가짜 허브는 부를 때 만든다. 붙이는 순간 직전 장과 견줘 봇의 속도를 미리 안다.
                    var errors = new RoslynScriptEngine().RunLiveAsync("몹들(); 쉬기(120); 몹들(); var until = Environment.TickCount64 + 2000; while (Environment.TickCount64 < until) { var m = 목표(); if (m is null) { 쉬기(20); continue; } 조준(m); }", api, debug: null, token: CancellationToken.None).GetAwaiter().GetResult();

                    // 마지막 0.6초 동안의 참 거리 - 앞은 따라잡는 중이라 뺀다.
                    var now = Environment.TickCount64;
                    List<(long Ticks, int Dx, int Dy)> recent;
                    lock (adapter.Moves) recent = adapter.Moves.Where(m => now - m.Ticks <= 600).ToList();
                    foreach (var m in recent) samples.Add(Math.Abs(hub.TrueOffset(m.Ticks)));

                    var worst = samples.Count > 0 ? samples.Max() : double.NaN;

                    // 따라잡는 시간 - 150px 옆에서 달아나는 봇의 머리 20px 안에 처음 드는 때. 확인 화면을 기다리며 서 있으면 그동안 봇이 달아난다(실측: 100~200px 꺾기가 거리의 20% 모자란 채 멈춤).
                    List<(long Ticks, int Dx, int Dy)> all;
                    lock (adapter.Moves) all = adapter.Moves.ToList();
                    var caught = all.Count > 0 && all.FirstOrDefault(m => Math.Abs(hub.TrueOffset(m.Ticks)) <= 20).Ticks is var catchAt and > 0 ? catchAt - all[0].Ticks : -1;

                    // 0.3초쯤 걸린다 - 프레임 박자(0.1초)에 걸리는 자리에 따라 0.35 를 살짝 넘기도 해서 0.45 로 둔다.
                    Check("조준(몹): 200px/s 로 달아나는 몹을 0.45초 안에 머리 20px 까지 따라잡고, 마지막 0.6초 동안 머리 너비(15px) 안에 붙어 있는다",
                          errors.Count == 0 && samples.Count > 0 && worst <= 15 && caught is > 0 and <= 450,
                          errors.Count > 0 ? errors[0].ToString() : $"20px 까지 {caught}ms · 마지막 0.6초 표본 {samples.Count} · 가장 먼 {worst:0}px · 평균 {(samples.Count > 0 ? samples.Average() : double.NaN):0}px · 궤적(40ms) {Trail(all.Select(m => (m.Ticks, hub.TrueOffset(m.Ticks))).ToList())}");
                }
            }

            // 목표풀기 뒤에는 서고, 목표() 는 스레드가 예측한 자리(같은 몹)를 준다.
            {
                var adapter = new RecordingAdapter();
                var hub = new SimHub(monitor, adapter, scale) { OffsetPx = 200 };
                var printed = new List<string>();
                var host = new LiveScriptHost { Service = new InputService(adapter), RequiresForeground = false, Target = () => monitor, Hub = hub, Print = printed.Add, Watch = (_, _) => { }, HoldTimeMs = 1, AimScale = scale };

                using (var api = new LiveScriptApi(host, CancellationToken.None))
                {
                    var errors = new RoslynScriptEngine().RunLiveAsync("var a = 목표(); 조준(a); var b = 목표(); 출력(b.이름); 목표풀기(); 쉬기(150); 출력(\"멈춤\"); 쉬기(150);", api, debug: null, token: CancellationToken.None).GetAwaiter().GetResult();

                    long last;
                    lock (adapter.Moves) last = adapter.Moves.Count > 0 ? adapter.Moves[^1].Ticks : 0;
                    var stopped = Environment.TickCount64 - last >= 120;

                    Check("목표풀기 뒤에는 조준 스레드가 서고, 목표() 는 붙잡은 몹을 준다",
                          errors.Count == 0 && printed.Count == 2 && printed[0] == "일반 봇" && adapter.Moves.Count > 0 && stopped,
                          errors.Count > 0 ? errors[0].ToString() : $"출력 {string.Join("/", printed)} · 걸음 {adapter.Moves.Count} · 마지막 걸음 뒤 {Environment.TickCount64 - last}ms");
                }
            }
            // 배율이 15% 과해도(자동 배우기가 위로 튄 실측 상황) 크게 지나치지 않는다 - 휙 도는 동안의 화면은 덜 믿고, 느려진 뒤의 화면으로 마저 맞춘다.
            {
                var adapter = new RecordingAdapter();
                var hub = new SimHub(monitor, adapter, scale) { OffsetPx = 300 };
                var host = new LiveScriptHost { Service = new InputService(adapter), RequiresForeground = false, Target = () => monitor, Hub = hub, Print = _ => { }, Watch = (_, _) => { }, HoldTimeMs = 1, AimScale = scale * 1.15 };

                using (var api = new LiveScriptApi(host, CancellationToken.None))
                {
                    var errors = new RoslynScriptEngine().RunLiveAsync("for (var i = 0; i < 12; i++) { var m = 목표(); if (m is null) break; 조준(m); }", api, debug: null, token: CancellationToken.None).GetAwaiter().GetResult();

                    List<(long Ticks, int Dx, int Dy)> moves;
                    lock (adapter.Moves) moves = adapter.Moves.ToList();

                    var trail = moves.Select(m => hub.TrueOffset(m.Ticks)).ToList();
                    var overshoot = trail.Count > 0 ? Math.Max(0, -trail.Min()) : 0;
                    var final = Math.Abs(hub.TrueOffset(Environment.TickCount64));

                    // 300px 의 15% 는 45px - 모형만 믿고 끝까지 가면 그만큼 지나친다.
                    Check("조준(몹): 배율이 15% 과해도 300px 조준에서 30px 넘게 지나치지 않고 15px 안에 붙는다",
                          errors.Count == 0 && overshoot <= 30 && final <= 15,
                          errors.Count > 0 ? errors[0].ToString() : $"지나침 {overshoot:0}px · 남은 {final:0}px");
                }
            }

            // 옆에 다른 봇이 서 있고 붙잡은 봇이 네 장에 한 번 검출에서 빠진다 - 빠진 장에서 옆 봇으로 건너뛰면 안 된다(실측 2026-09-18: 가만히 겨눈 프레임 쌍의 7% 에서 본 자리가 60px 넘게 뛰었다).
            {
                var adapter = new RecordingAdapter();
                var hub = new SimHub(monitor, adapter, scale) { OffsetPx = 150, DecoyGapPx = 120, DropEvery = 4 };
                var host = new LiveScriptHost { Service = new InputService(adapter), RequiresForeground = false, Target = () => monitor, Hub = hub, Print = _ => { }, Watch = (_, _) => { }, HoldTimeMs = 1, AimScale = scale };

                using (var api = new LiveScriptApi(host, CancellationToken.None))
                {
                    var errors = new RoslynScriptEngine().RunLiveAsync("var until = Environment.TickCount64 + 1500; while (Environment.TickCount64 < until) { var m = 목표(); if (m is null) { 쉬기(20); continue; } 조준(m); }", api, debug: null, token: CancellationToken.None).GetAwaiter().GetResult();

                    List<(long Ticks, int Dx, int Dy)> moves;
                    lock (adapter.Moves) moves = adapter.Moves.ToList();

                    var trail = moves.Select(m => (m.Ticks, Offset: hub.TrueOffset(m.Ticks))).ToList();
                    var reach = trail.FirstOrDefault(p => Math.Abs(p.Offset) <= 20).Ticks is var at and > 0 ? at - trail[0].Ticks : -1;
                    var after = reach > 0 ? trail.Where(p => p.Ticks - trail[0].Ticks >= reach).ToList() : [];
                    var strayed = after.Count > 0 ? after.Max(p => Math.Abs(p.Offset)) : double.NaN;

                    Check("조준(몹): 옆(120px)에 다른 봇이 있고 붙잡은 봇이 네 장에 한 번 안 보여도 옆 봇으로 건너뛰지 않는다(붙은 뒤 25px 안)",
                          errors.Count == 0 && moves.Count >= 10 && reach is > 0 and <= 500 && strayed <= 25,
                          errors.Count > 0 ? errors[0].ToString() : $"걸음 {moves.Count} · 20px 까지 {reach}ms · 붙은 뒤 가장 먼 {strayed:0}px · 궤적(40ms) {Trail(trail)}");
                }
            }

            // 앱처럼 화면용 추적기를 거친 사각형(Found)과 날것(Raw)을 같이 준다 - 추적기는 화면을 돌리는 동안 옛 자리에 끌려 늦는다. 조준이 그것을 보면 "몹이 달아났다" 로 읽고 지나친다
            // (실측 2026-09-18: -159px 에서 +75px 로 지나친 뒤 1초에 걸쳐 돌아왔다). 조준 스레드는 날것을 봐야 한다.
            {
                var adapter = new RecordingAdapter();
                var hub = new SimHub(monitor, adapter, scale) { OffsetPx = 300, UseTracker = true };
                var host = new LiveScriptHost { Service = new InputService(adapter), RequiresForeground = false, Target = () => monitor, Hub = hub, Print = _ => { }, Watch = (_, _) => { }, HoldTimeMs = 1, AimScale = scale };

                using (var api = new LiveScriptApi(host, CancellationToken.None))
                {
                    // 추적기는 두 번 보여야 내놓는다 - 목표() 가 몹을 줄 때까지 기다렸다 붙는다.
                    var errors = new RoslynScriptEngine().RunLiveAsync("var until = Environment.TickCount64 + 1500; while (Environment.TickCount64 < until) { var m = 목표(); if (m is null) { 쉬기(20); continue; } 조준(m); }", api, debug: null, token: CancellationToken.None).GetAwaiter().GetResult();

                    List<(long Ticks, int Dx, int Dy)> moves;
                    lock (adapter.Moves) moves = adapter.Moves.ToList();

                    var trail = moves.Select(m => (m.Ticks, Offset: hub.TrueOffset(m.Ticks))).ToList();
                    var overshoot = trail.Count > 0 ? Math.Max(0, -trail.Min(p => p.Offset)) : 0;
                    var reach = trail.FirstOrDefault(p => p.Offset <= 20).Ticks is var at and > 0 ? at - trail[0].Ticks : -1;
                    var final = Math.Abs(hub.TrueOffset(Environment.TickCount64));

                    // 붙은 뒤의 흔들림 - 검출 떨림(±5px)을 그대로 따라다니면 안 된다.
                    var settledTrail = reach > 0 ? trail.Where(p => p.Ticks - trail[0].Ticks >= reach + 250).ToList() : [];
                    // 붙은 뒤 걸음이 하나도 없으면(쉬는 중) 흔들림도 없다.
                    var wobble = settledTrail.Count > 0 ? settledTrail.Max(p => Math.Abs(p.Offset)) : 0;

                    Check("조준(몹): 화면용 추적기가 늦은 사각형을 줘도(날것을 본다) 300px 조준에서 15px 넘게 지나치지 않고 15px 안에 붙고, 붙은 뒤 10px 넘게 흔들리지 않는다",
                          errors.Count == 0 && moves.Count >= 15 && overshoot <= 15 && final <= 15 && reach is > 0 and <= 600 && wobble <= 10,
                          errors.Count > 0 ? errors[0].ToString() : $"걸음 {moves.Count} · 20px 까지 {reach}ms · 지나침 {overshoot:0}px · 남은 {final:0}px · 붙은 뒤 흔들림 {wobble:0}px · 궤적(40ms) {Trail(trail)}");
                }
            }

            // 배율이 틀려도 스레드가 붙고, 멈춰 선 뒤 보낸 총량 ÷ 줄어든 거리로 배율을 배운다(닻 방식 - 움직이는 중의 프레임으로는 안 잰다).
            {
                var adapter = new RecordingAdapter();
                var hub = new SimHub(monitor, adapter, scale) { OffsetPx = 150 };
                var learned = new List<double>();
                var host = new LiveScriptHost { Service = new InputService(adapter), RequiresForeground = false, Target = () => monitor, Hub = hub, Print = p => { if (p == "죽음") hub.Respawn(); }, Watch = (_, _) => { }, HoldTimeMs = 1, AimScale = 2.0, AimScaleLearned = learned.Add };

                using (var api = new LiveScriptApi(host, CancellationToken.None))
                {
                    // 붙어 일곱 번 쏜 뒤(멈춰 선 프레임이 나오게 - 진짜 스크립트도 붙은 채 여러 발 쏜다) 놓고(봇이 죽어 새 봇이 150px 옆에 나온다) 다시 붙기를 되풀이한다 - 붙어 멈춰 선 프레임에서 표본 하나.
                    var errors = new RoslynScriptEngine().RunLiveAsync(
                        "for (var round = 0; round < 12; round++) { var until = Environment.TickCount64 + 3000; var hits = 0; while (Environment.TickCount64 < until) { var m = 목표(); if (m is null) { 쉬기(20); continue; } if (조준(m) && ++hits >= 7) { 목표풀기(); 출력(\"죽음\"); 쉬기(80); break; } } }",
                        api, debug: null, token: CancellationToken.None).GetAwaiter().GetResult();

                    var last = learned.Count > 0 ? learned[^1] : double.NaN;

                    Check("조준(몹): 배율 2.0 으로 시작해도 붙고, 붙을 때마다 표본을 모아 참값(3.5) 쪽으로 배운다",
                          errors.Count == 0 && learned.Count >= 1 && last >= 2.9 && last <= 4.2 && hub.Spawns >= 8,
                          errors.Count > 0 ? errors[0].ToString() : $"새 봇 {hub.Spawns}번 · 배움 {learned.Count}번 → {string.Join(" ", learned.Select(v => v.ToString("0.00")))}");
                }
            }
        }
    }

    /// <summary>참 거리의 궤적을 40ms 간격으로 - 실패했을 때 어떻게 움직였는지 보게.</summary>
    private static string Trail(IReadOnlyList<(long Ticks, double Offset)> trail)
    {
        var parts = new List<string>();
        long next = 0;

        foreach (var (ticks, offset) in trail)
        {
            if (ticks < next) continue;

            parts.Add($"{ticks - trail[0].Ticks}:{offset:0}");
            next = ticks + 40;
        }

        return string.Join(" ", parts);
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

        using var api = new LiveScriptApi(host, token);
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
        public bool MoveMouseBy(int deltaX, int deltaY)
        {
            Calls.Add($"MoveBy {deltaX},{deltaY}");
            lock (Moves) Moves.Add((Environment.TickCount64, deltaX, deltaY));
            return true;
        }

        /// <summary>상대 이동만 시각과 함께. 닫힌 고리 가짜 허브(<see cref="SimHub"/>)가 "그만큼 돌아간 화면" 을 만드는 데 쓴다.</summary>
        public List<(long Ticks, int Dx, int Dy)> Moves { get; } = [];
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
        public void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0, IReadOnlyList<Detection>? raw = null) { }
        public void PublishFrame(byte[] bgra, int width, int height) { }
        public bool TryCropFrame(Rect ratio, out BitmapSource? crop) { crop = null; return false; }
        public bool TryGetFrameSize(out int width, out int height) { width = height = 0; return false; }
    }

    /// <summary>
    /// 닫힌 고리 가짜 허브 - 몹 하나가 화면 가운데에서 <c>OffsetPx</c> 떨어져 있고 <c>VelocityPxPerMs</c> 로 옆으로 움직인다.
    /// 어댑터로 보낸 카운트만큼(÷배율) 화면이 돌아 몹이 가까워진 것으로 답하되, 프레임은 <c>FrameMs</c> 마다 한 장이고 <c>LatencyMs</c> 앞의 입력까지만 반영한다.
    /// </summary>
    private sealed class SimHub(CaptureTarget target, RecordingAdapter adapter, double scale) : IPerceptionHub
    {
        public double OffsetPx { get; init; } = 300;
        public double VelocityPxPerMs { get; init; }
        // 실측(오버워치, 2026-09-18)에 맞춘 값 - 몹 찾기 0.1초 주기, 보낸 입력이 화면에 보이기까지 약 90ms, 사각형은 가만히 있어도 몇 px 흔들린다.
        public int FrameMs { get; init; } = 100;
        public int LatencyMs { get; init; } = 90;
        public double JitterPx { get; init; } = 5;

        private readonly Random _random = new(7);

        /// <summary>
        /// 지금 봇이 죽고 새 봇이 <see cref="OffsetPx"/> 만큼 옆에 나온다. 배율 배우기 검사가 스크립트의 <c>출력("죽음")</c> 으로 부른다.
        /// 예전에는 "붙은 뒤 마우스가 50ms 서 있으면 죽음" 으로 짐작했는데, 조준이 크게 꺾은 뒤 확인 화면을 기다리며 서 있는 것(0.1~0.2초)을 죽음으로 읽어 붙는 도중에 봇이 바뀌었다.
        /// </summary>
        public void Respawn()
        {
            _shift += OffsetPx;
            Spawns++;
        }

        /// <summary>새 봇이 나온 횟수.</summary>
        public int Spawns { get; private set; }

        /// <summary>
        /// 앱처럼 <see cref="DetectionSnapshot.Found"/> 는 화면용 추적기(<see cref="DetectionTracker"/>)를 거친 것으로, 날것은 <see cref="DetectionSnapshot.Raw"/> 로 준다.
        /// 추적기는 옛 화면 좌표와 섞고 못 이으면 옛 사각형을 내줘, 화면을 돌리는 동안에는 늦은 자리다 - 조준이 그것을 보면 지나친다.
        /// </summary>
        public bool UseTracker { get; init; }

        /// <summary>0 이 아니면 붙잡을 봇 옆(이만큼 px)에 다른 봇이 하나 더 서 있다 - 같은 크기, 늘 보인다.</summary>
        public double DecoyGapPx { get; init; }

        /// <summary>0 이 아니면 이 장수마다 한 번 붙잡을 봇이 검출에서 빠진다(옆 봇은 그대로) - 날것 검출은 가끔 한 장씩 놓친다.</summary>
        public int DropEvery { get; init; }

        private int _frames;
        private readonly DetectionTracker _tracker = new();
        private double _shift;
        private readonly long _start = Environment.TickCount64;
        private long _frameTicks;
        private DetectionSnapshot? _frame;

        public bool IsCapturing => true;
        public bool IsDetecting => true;
        public CaptureTarget? Target => target;
        public bool WantsFrames { get; set; }
        public bool IsPreparingFrames => false;
        public void PreparingFrames() { }

        /// <summary>지금 몹이 가운데에서 얼마나 떨어져 있는가(px) - 보낸 것을 모두 반영한 참값.</summary>
        public double TrueOffset(long now) => OffsetAt(now, now);

        /// <summary><paramref name="now"/> 의 몹 자리에서 <paramref name="inputsUntil"/> 까지 보낸 입력만큼 돈 화면으로 본 거리.</summary>
        private double OffsetAt(long now, long inputsUntil)
        {
            double sent;
            lock (adapter.Moves) sent = adapter.Moves.Where(m => m.Ticks <= inputsUntil).Sum(m => (double)m.Dx);
            return OffsetPx + _shift + (VelocityPxPerMs * (now - _start)) - (sent / scale);
        }

        public DetectionSnapshot? Latest
        {
            get
            {
                var now = Environment.TickCount64;

                if (_frame is not null && now - _frameTicks < FrameMs) return _frame;

                _frameTicks = now;
                Minguk.Tools.Capture.Input.CaptureTargetBounds.TryGet(target, out var bounds);

                // 화면은 통째로 늦다 - 우리 입력뿐 아니라 몹의 자리도 LatencyMs 앞의 것이다(진짜 게임이 그렇다 - 입력만 늦게 하면 달리는 몹을 앞서 겨눠야 하는 몫이 검사에서 빠진다).
                var offset = OffsetAt(now - LatencyMs, now - LatencyMs);
                var cx = 0.5 + ((offset + ((_random.NextDouble() - 0.5) * 2 * JitterPx)) / bounds.Width);
                var w = 60 / bounds.Width;
                var h = 120 / bounds.Height;

                // 세로로도 돈다 - 안 돌면 스레드가 머리(가운데보다 38px 위)를 향해 끝없이 올려 보낸다.
                double sentY;
                lock (adapter.Moves) sentY = adapter.Moves.Where(m => m.Ticks <= now - LatencyMs).Sum(m => (double)m.Dy);
                var cy = 0.5 - (((sentY / scale) + ((_random.NextDouble() - 0.5) * 2 * JitterPx)) / bounds.Height);
                var found = new List<Detection>();

                if (DropEvery <= 0 || ++_frames % DropEvery != 0)
                    found.Add(new("일반 봇", LabelBox.FromCorners(0, cx - (w / 2), cy - (h / 2), cx + (w / 2), cy + (h / 2)), 0.9f));

                if (DecoyGapPx != 0)
                {
                    var dx = cx + (DecoyGapPx / bounds.Width);
                    found.Add(new("일반 봇", LabelBox.FromCorners(0, dx - (w / 2), cy - (h / 2), dx + (w / 2), cy + (h / 2)), 0.9f));
                }

                if (UseTracker)
                {
                    var tracked = _tracker.Update(found);

                    _frame = new DetectionSnapshot(tracked, tracked.Select(_ => string.Empty).ToArray(), 1920, 1080, target, now) { FrameTicks = now, Raw = found, Previous = _frame is null ? null : _frame with { Previous = null } };
                    return _frame;
                }

                _frame = new DetectionSnapshot(found, found.Select(_ => string.Empty).ToArray(), 1920, 1080, target, now) { FrameTicks = now, Previous = _frame is null ? null : _frame with { Previous = null } };
                return _frame;
            }
        }

        public void PublishState(bool capturing, bool detecting, CaptureTarget? target) { }
        public void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0, IReadOnlyList<Detection>? raw = null) { }
        public void PublishFrame(byte[] bgra, int width, int height) { }
        public bool TryCropFrame(Rect ratio, out BitmapSource? crop) { crop = null; return false; }
        public bool TryGetFrameSize(out int width, out int height) { width = height = 0; return false; }
    }

    /// <summary>가운데 가까운 몹 하나의 사각형이 부를 때마다 위·아래로 번갈아 흔들린다(가로·크기는 그대로).</summary>
    private sealed class JitterHub(CaptureTarget target) : IPerceptionHub
    {
        private const double Swing = 0.02;
        private int _calls;

        public bool IsCapturing => true;
        public bool IsDetecting => true;
        public CaptureTarget? Target => target;
        public bool WantsFrames { get; set; }
        public bool IsPreparingFrames => false;
        public void PreparingFrames() { }

        /// <summary>위·아래 두 사각형의 머리 차이(px).</summary>
        public int RawHeadSwing(CaptureTarget monitor)
        {
            Minguk.Tools.Capture.Input.CaptureTargetBounds.TryGet(monitor, out var bounds);
            return (int)Math.Round(Swing * 2 * bounds.Height);
        }

        public DetectionSnapshot? Latest
        {
            get
            {
                var dy = (_calls++ % 2 == 0 ? -Swing : Swing);
                var found = new List<Detection> { new("일반 봇", LabelBox.FromCorners(0, 0.47, 0.40 + dy, 0.53, 0.60 + dy), 0.9f) };
                return new DetectionSnapshot(found, [""], 1920, 1080, target, Environment.TickCount64);
            }
        }

        public void PublishState(bool capturing, bool detecting, CaptureTarget? target) { }
        public void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0, IReadOnlyList<Detection>? raw = null) { }
        public void PublishFrame(byte[] bgra, int width, int height) { }
        public bool TryCropFrame(Rect ratio, out BitmapSource? crop) { crop = null; return false; }
        public bool TryGetFrameSize(out int width, out int height) { width = height = 0; return false; }
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
        public void PublishDetections(IReadOnlyList<Detection> found, IReadOnlyList<string> names, int frameWidth, int frameHeight, long frameTicks = 0, IReadOnlyList<Detection>? raw = null) { }
        public void PublishFrame(byte[] bgra, int width, int height) { }
        public bool TryCropFrame(Rect ratio, out BitmapSource? crop) { crop = null; return false; }
        public bool TryGetFrameSize(out int width, out int height) { width = height = 0; return false; }
    }
}
