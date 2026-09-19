using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Tools.Capture;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;
using Minguk.Tools.Vision.Perception;

namespace Minguk.Tools.Tests;

/// <summary>
/// 프로젝트실행(함수처럼 돌아온다)과 프로젝트이동(끝내고 넘어간다, 쌓이지 않는다) - 사용자, 2026-09-19.
/// </summary>
/// <remarks>
/// 가짜 "프로젝트" 는 이름 → 스크립트 글. 화면이 주는 <see cref="LiveScriptHost.RunProject"/> 자리에 그 글을 새 API 로 돌리는 것을 꽂는다.
/// 쌓였는지는 RunProject 안에 몇 겹 들어가 있는지(깊이)로 본다 - 이동은 늘 1겹, 실행 안의 실행은 2겹.
/// </remarks>
internal static partial class Program
{
    /// <summary>
    /// 조준 배율 두 가지 안전장치(사용자, 2026-09-19) - 조준 모드에서 배율이 너무 낮으면 한 번 말해 주고, 일반(메뉴) 모드는 배율을 배우지 않는다.
    /// </summary>
    private static void TestAimScaleGuards(CaptureTarget monitor)
    {
        (List<string> Printed, List<double> Learned, IReadOnlyList<ScriptError> Errors) Run(string source, double scale)
        {
            var printed = new List<string>();
            var learned = new List<double>();

            var host = new LiveScriptHost
            {
                Service = new InputService(new RecordingAdapter()),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor) { FrameTicks = 1 },
                Print = printed.Add,
                Watch = (_, _) => { },
                HoldTimeMs = 1,
                AimScale = scale,
                AimScaleLearned = learned.Add,
                IsAimScaleAuto = () => true
            };

            using var api = new LiveScriptApi(host, CancellationToken.None);
            var errors = new RoslynScriptEngine().RunLiveAsync(source, api, null, CancellationToken.None).GetAwaiter().GetResult();

            return (printed, learned, errors);
        }

        // 조준 모드, 배율 10% - 두 번 겨눠도 경고는 한 번.
        var (low, _, lowErrors) = Run("조준(100, 100); 조준(100, 100);", 0.1);
        var warnings = low.Count(p => p.Contains("조준 배율이"));

        Check("조준 배율이 너무 낮으면(10%) 조준 모드에서 한 번만 알려 준다", lowErrors.Count == 0 && warnings == 1, $"경고 {warnings}번 · {low.FirstOrDefault(p => p.Contains("조준 배율이"))}");

        // 정상 배율(100%)이면 말이 없다.
        var (normal, _, _) = Run("조준(100, 100);", 1.0);

        Check("조준 배율이 정상(100%)이면 알리지 않는다", !normal.Any(p => p.Contains("조준 배율이")), $"출력 {normal.Count}줄");

        // 일반(메뉴) 모드는 낮은 배율이어도 말하지 않고 배우지도 않는다. 가짜 어댑터의 커서는 (0,0) 이라 그 자리를 겨누면 바로 닿는다.
        var (menu, learned, menuErrors) = Run("마우스모드(\"일반\"); 조준(0, 0);", 0.1);

        Check("일반(메뉴) 모드에서는 낮은 배율이어도 알리지 않고 배율을 배우지도 않는다",
              menuErrors.Count == 0 && !menu.Any(p => p.Contains("조준 배율이")) && learned.Count == 0,
              $"오류 {menuErrors.Count} · 출력 {menu.Count}줄 · 배운 것 {learned.Count}번");

        // 표본이 두 무리로 갈리면(3.5 넷 · 16 다섯) 가운뎃값(16)으로 뛰지 않는다. 한 무리로 모이면(3.5 아홉) 배운다 - 사격장 실측 2026-09-19.
        List<double> Feed(double start, params double[] measured)
        {
            var got = new List<double>();
            var host = new LiveScriptHost
            {
                Service = new InputService(new RecordingAdapter()),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor),
                Print = _ => { },
                Watch = (_, _) => { },
                AimScale = start,
                AimScaleLearned = got.Add,
                IsAimScaleAuto = () => true
            };

            using var api = new LiveScriptApi(host, CancellationToken.None);
            var learn = typeof(LiveScriptApi).GetMethod("LearnSampleCore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            // 100px 에서 350 카운트를 보냈더니 350/m px 만큼 좁혀졌다 = 잰 값 m.
            foreach (var m in measured) learn.Invoke(api, [100.0, 100.0 - (350.0 / m), 350.0]);

            return got;
        }

        var split = Feed(3.5, 3.5, 3.4, 16, 3.6, 17, 3.5, 15, 16, 18);
        var agree = Feed(2.0, 3.5, 3.4, 3.6, 3.5, 3.3, 3.7, 3.5, 3.4, 3.6);

        Check("조준 배율: 표본 9개 중 7개가 한쪽이어야 옮긴다 - 두 무리로 갈리면 그대로, 한 무리로 모이면 배운다",
              split.Count == 0 && agree.Count > 0 && Math.Abs(agree[^1] - 3.0) < 0.05,
              $"갈림 {(split.Count == 0 ? "그대로" : string.Join(" → ", split.Select(v => v.ToString("0.00"))))} · 모임 2.00 → {string.Join(" → ", agree.Select(v => v.ToString("0.00")))}");
    }

    /// <summary>
    /// 명중확인(사용자, 2026-09-19 "조준 후 맞췄는지 못맞췄는지") - 조준점 둘레에 히트 마커가 뜨면 참, 안 뜨면 기다림만큼 보고 거짓.
    /// </summary>
    private static void TestHitConfirmed(CaptureTarget monitor)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "minguk-hit-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Resources"));

        try
        {
            const int width = 640, height = 360;

            // 회색 바탕에 조준점(가운데 작은 점) - 마커가 없는 화면.
            byte[] Frame(bool marker)
            {
                var pixels = new byte[width * height * 4];

                for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = pixels[i + 1] = pixels[i + 2] = 60; pixels[i + 3] = 255; }

                void Dot(int x, int y, byte v) { var k = ((y * width) + x) * 4; pixels[k] = pixels[k + 1] = pixels[k + 2] = v; }

                for (var d = -2; d <= 2; d++) for (var e = -2; e <= 2; e++) Dot((width / 2) + d, (height / 2) + e, 200);

                // 히트 마커 - 가운데에서 조금 떨어진 네 대각선 획(X).
                if (marker)
                    for (var r = 8; r <= 18; r++)
                        for (var t = 0; t < 2; t++)
                        {
                            Dot((width / 2) + r + t, (height / 2) + r, 255); Dot((width / 2) - r - t, (height / 2) + r, 255);
                            Dot((width / 2) + r + t, (height / 2) - r, 255); Dot((width / 2) - r - t, (height / 2) - r, 255);
                        }

                return pixels;
            }

            // 본보기 - 마커가 찍힌 화면의 가운데 44x44.
            {
                var marked = Frame(marker: true);
                var source = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, marked, width * 4);
                var crop = new System.Windows.Media.Imaging.CroppedBitmap(source, new System.Windows.Int32Rect((width / 2) - 22, (height / 2) - 22, 44, 44));
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));
                using var file = System.IO.File.Create(System.IO.Path.Combine(root, "Resources", "히트마커.png"));
                encoder.Save(file);
            }

            (bool Hit, long Ms, IReadOnlyList<ScriptError> Errors) Check(bool marker)
            {
                var hub = new Minguk.Tools.Vision.Perception.PerceptionHub { WantsFrames = true };
                hub.PublishFrame(Frame(marker), width, height);

                var host = new LiveScriptHost
                {
                    Service = new InputService(new RecordingAdapter()),
                    RequiresForeground = false,
                    Target = () => monitor,
                    Hub = hub,
                    Print = _ => { },
                    Watch = (_, _) => { },
                    HoldTimeMs = 1,
                    ResourceRoot = root
                };

                using var api = new LiveScriptApi(host, CancellationToken.None);
                var hit = false;
                IReadOnlyList<ScriptError> errors = [];

                // 첫 호출은 본보기 파일 읽기·JIT 가 끼므로 한 번 데우고 두 번째를 잰다.
                try { api.HitConfirmed("히트마커.png", 0); } catch (Exception) { }

                var watch = System.Diagnostics.Stopwatch.StartNew();

                try { hit = api.HitConfirmed("히트마커.png", 300); }
                catch (Exception ex) { errors = [new ScriptError(0, ex.Message)]; }

                return (hit, watch.ElapsedMilliseconds, errors);
            }

            var withMarker = Check(marker: true);
            var without = Check(marker: false);

            Program.Check("명중확인: 조준점 둘레에 히트 마커가 뜨면 바로 참, 안 뜨면 기다림(300ms)만큼 보고 거짓",
                  withMarker.Errors.Count == 0 && without.Errors.Count == 0 && withMarker.Hit && withMarker.Ms < 150 && !without.Hit && without.Ms >= 280,
                  $"마커 있음 {withMarker.Hit}({withMarker.Ms}ms) · 없음 {without.Hit}({without.Ms}ms)" + (withMarker.Errors.Count + without.Errors.Count > 0 ? $" · {withMarker.Errors.Concat(without.Errors).First().Message}" : ""));
        }
        finally
        {
            try { System.IO.Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// 체력바(사용자, 2026-09-19 "체력바로 해줘") - 검출 위 체력바의 찬 몫을 재고, 쏜 뒤 줄면 명중. 칸 색은 오버워치 사격장 실측 값.
    /// </summary>
    private static void TestHealthBar(CaptureTarget monitor)
    {
        Minguk.Tools.Capture.Input.CaptureTargetBounds.TryGet(monitor, out var bounds);

        const int width = 640, height = 360;

        // 바탕(파랑 섞인 회색) · 봇 몸은 (320,160) 60x80 · 체력바는 그 위 y 100~107, 10칸(9px + 1px 틈).
        byte[] Frame(int filledCells)
        {
            var pixels = new byte[width * height * 4];

            void Put(int x, int y, byte r, byte g, byte b) { var k = ((y * width) + x) * 4; pixels[k] = b; pixels[k + 1] = g; pixels[k + 2] = r; pixels[k + 3] = 255; }

            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) Put(x, y, 113, 122, 176);

            for (var cell = 0; cell < 10; cell++)
                for (var y = 100; y < 108; y++)
                    for (var x = 270 + (cell * 10); x < 270 + (cell * 10) + 9; x++)
                        if (cell < filledCells) Put(x, y, 255, 66, 107); else Put(x, y, 111, 76, 113);

            // 봇 테두리(빨강) - 짧은 빨강이 바로 잡히지 않는지.
            for (var y = 120; y < 200; y++) { Put(290, y, 230, 60, 90); Put(349, y, 230, 60, 90); }

            return pixels;
        }

        // 프레임 픽셀 → 화면 픽셀(대상 창 크기로 늘어난다).
        var mob = new ScriptDetection("일반 봇", 0.9,
            (int)Math.Round(bounds.Left + (320.0 / width * bounds.Width)), (int)Math.Round(bounds.Top + (160.0 / height * bounds.Height)),
            (int)Math.Round(60.0 / width * bounds.Width), (int)Math.Round(80.0 / height * bounds.Height), string.Empty);

        // 색·범위는 프로젝트 폴더의 healthbar.json - 엔진에는 게임 색이 없다(사용자, 2026-09-19 "공통 로직").
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "minguk-healthbar-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(root);
        new Minguk.Tools.Vision.HealthBars.HealthBarSpec { Filled = [255, 66, 107], Empty = [110, 75, 112], Tolerance = 45 }
            .Save(System.IO.Path.Combine(root, Minguk.Tools.Vision.HealthBars.HealthBarSpec.FileName));

        LiveScriptApi Api(PerceptionHub hub, string? resourceRoot = null) => new(new LiveScriptHost
        {
            Service = new InputService(new RecordingAdapter()),
            RequiresForeground = false,
            Target = () => monitor,
            Hub = hub,
            Print = _ => { },
            Watch = (_, _) => { },
            HoldTimeMs = 1,
            ResourceRoot = resourceRoot ?? root
        }, CancellationToken.None);

        var reads = new List<string>();
        var readOk = true;

        foreach (var cells in new[] { 10, 6, 1, 0 })
        {
            var hub = new PerceptionHub();
            hub.PublishState(capturing: true, detecting: false, target: monitor);
            hub.WantsFrames = true;
            hub.PublishFrame(Frame(cells), width, height);

            using var api = Api(hub);
            var value = api.HealthBar(mob);

            reads.Add($"{cells}칸 {value?.ToString("0.00") ?? "없음"}");

            // 찬 칸이 없으면 모른다(null) - 빈 바와 바가 없는 화면을 못 가르고, 0 으로 주면 쏜 뒤 "그대로" 로 빗나감이 세졌다.
            readOk &= cells == 0 ? value is null : value is { } v && Math.Abs(v - (cells / 10.0)) <= 0.05;
        }

        Program.Check("체력바: 검출 위 체력바의 찬 몫을 칸 수대로 읽는다(봇 테두리의 짧은 빨강은 안 잡고, 찬 칸이 없으면 모름)", readOk, string.Join(" · ", reads));

        // 쏜 뒤 줄면 명중, 그대로면 빗나감.
        (bool? Hit, long Ms) Shoot(int before, int after)
        {
            var hub = new PerceptionHub();
            hub.PublishState(capturing: true, detecting: false, target: monitor);
            hub.WantsFrames = true;
            hub.PublishFrame(Frame(before), width, height);

            using var api = Api(hub);
            var start = api.HealthBar(mob);

            // 쏜 뒤 0.1초 뒤에 화면이 바뀐다(입력이 화면에 오르기까지).
            var later = System.Threading.Tasks.Task.Run(async () => { await System.Threading.Tasks.Task.Delay(100); hub.PublishFrame(Frame(after), width, height); });

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var hit = api.HitByHealthBar(mob, start, 300);
            later.Wait();

            return (hit, watch.ElapsedMilliseconds);
        }

        var hit = Shoot(6, 4);
        var miss = Shoot(6, 6);
        var overlay = Shoot(6, 9);   // 맞힌 직후 빨간 데미지 숫자가 바에 겹쳐 는 것처럼 보이는 경우

        Program.Check("명중했나: 쏜 뒤 체력바가 줄면 참(곧바로), 그대로면 기다림(300ms)만큼 보고 거짓",
              hit.Hit == true && hit.Ms < 250 && miss.Hit == false && miss.Ms >= 280,
              $"줄어듦 {hit.Hit}({hit.Ms}ms) · 그대로 {miss.Hit}({miss.Ms}ms)");

        Program.Check("명중했나: 쏜 뒤 는 것처럼만 보이면(겹친 효과) 빗나감이 아니라 모름(null)", overlay.Hit is null, $"{overlay.Hit?.ToString() ?? "null"}");

        // 쏘기 전 체력바를 모르면(못 찾음) 모른다고 한다 - 빗나감으로 세면 안 된다.
        {
            using var api = Api(new PerceptionHub());
            Program.Check("명중했나: 쏘기 전 값을 모르면 null(빗나감으로 안 센다)", api.HitByHealthBar(mob, null, 50) is null, "");
        }

        // 프로젝트 폴더에 healthbar.json 이 없으면 무엇을 적을지 말하고 멈춘다.
        {
            var bare = System.IO.Path.Combine(root, "빈프로젝트");
            System.IO.Directory.CreateDirectory(bare);

            var hub = new PerceptionHub();
            hub.PublishState(capturing: true, detecting: false, target: monitor);
            hub.WantsFrames = true;
            hub.PublishFrame(Frame(6), width, height);

            using var api = Api(hub, bare);
            string message = "(멈추지 않았다)";
            try { api.HealthBar(mob); } catch (Exception ex) { message = ex.Message; }

            Program.Check("체력바: 프로젝트 폴더에 healthbar.json 이 없으면 무엇을 적을지 말하고 멈춘다", message.Contains("healthbar.json") && message.Contains("filled"), message);
        }

        // 못 읽은 순간의 조각은 프로젝트 폴더 진단\체력바\ 에 남는다 - 바가 없는 화면.
        {
            var hub = new PerceptionHub();
            hub.PublishState(capturing: true, detecting: false, target: monitor);
            hub.WantsFrames = true;

            var plain = new byte[width * height * 4];
            for (var i = 0; i < plain.Length; i += 4) { plain[i] = 176; plain[i + 1] = 122; plain[i + 2] = 113; plain[i + 3] = 255; }
            hub.PublishFrame(plain, width, height);

            using var api = Api(hub);
            var value = api.HealthBar(mob);
            var folder = System.IO.Path.Combine(root, "진단", "체력바");
            var saved = System.IO.Directory.Exists(folder) ? System.IO.Directory.GetFiles(folder, "*_없음.png").Length : 0;   // 앞 검사의 0칸(찬 칸 없음)도 모름이라 한 장 남는다

            Program.Check("체력바: 못 읽으면 null 이고 그 조각을 프로젝트 폴더 진단\\체력바 에 남긴다", value is null && saved == 2, $"값 {value?.ToString() ?? "null"} · 남은 조각 {saved}장");
        }

        try { System.IO.Directory.Delete(root, true); } catch (Exception) { }
    }

    private static void TestProjectMoves(CaptureTarget monitor)
    {
        TestAimScaleGuards(monitor);
        TestHitConfirmed(monitor);
        TestHealthBar(monitor);

        (List<string> Printed, IReadOnlyList<ScriptError> Errors, int MaxDepth) RunFlow(string start, Dictionary<string, string> projects)
            => RunFlowWith(start, name => projects.TryGetValue(name, out var text) ? text : null);

        (List<string> Printed, IReadOnlyList<ScriptError> Errors, int MaxDepth) RunFlowWith(string start, Func<string, string?> lookup)
        {
            var printed = new List<string>();
            var engine = new RoslynScriptEngine();
            var depth = 0;
            var maxDepth = 0;

            LiveScriptHost? host = null;

            async Task<IReadOnlyList<ScriptError>> RunProject(string name, LiveScriptHost template, CancellationToken token)
            {
                if (lookup(name) is not { } text) return [new ScriptError(0, $"'{name}' 없음")];

                maxDepth = Math.Max(maxDepth, Interlocked.Increment(ref depth));

                try
                {
                    return await engine.RunLiveAsync(text, new LiveScriptApi(template.WithResourceRoot(null), token), null, token);
                }
                finally
                {
                    Interlocked.Decrement(ref depth);
                }
            }

            host = new LiveScriptHost
            {
                Service = new InputService(new RecordingAdapter()),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor),
                Print = printed.Add,
                Watch = (_, _) => { },
                HoldTimeMs = 1,
                RunProject = RunProject
            };

            var api = new LiveScriptApi(host, CancellationToken.None);

            // 화면 실행기(LiveScriptSession)가 하는 그대로 - 처음 것을 돌리고 이동이 남아 있으면 반복문으로 넘어간다.
            var errors = host.Moves.RunWithMovesAsync(
                () => engine.RunLiveAsync(start, api, null, CancellationToken.None),
                name => RunProject(name, host, CancellationToken.None),
                printed.Add,
                CancellationToken.None).GetAwaiter().GetResult();

            return (printed, errors, maxDepth);
        }

        // 넘어가기만 하는 흐름 - 쌓이지 않는다(늘 1겹), 이동 뒤 줄은 안 돈다.
        {
            var (printed, errors, maxDepth) = RunFlow("출력(\"메인\"); 프로젝트이동(\"영웅\"); 출력(\"메인 뒤\");", new()
            {
                ["영웅"] = "출력(\"영웅\"); 프로젝트이동(\"사격장\"); 출력(\"영웅 뒤\");",
                ["사격장"] = "출력(\"사격장\");"
            });

            var shown = string.Join(",", printed.Where(p => !p.StartsWith("프로젝트 '", StringComparison.Ordinal)));

            Check("프로젝트이동: 지금 것을 끝내고 넘어간다 - 이동 뒤 줄은 안 돌고, 쌓이지 않는다(늘 1겹)",
                  errors.Count == 0 && shown == "메인,영웅,사격장" && maxDepth == 1,
                  $"{shown} · 깊이 {maxDepth} · 오류 {errors.Count}");
        }

        // 돌고 도는 흐름 - 사격장 → 메인 → 사격장 … 을 30바퀴 돌아도 1겹(프로젝트실행으로 했으면 60겹).
        {
            var mains = 0;

            var (printed, errors, maxDepth) = RunFlowWith("프로젝트이동(\"사격장\");", name => name switch
            {
                "사격장" => "출력(\"사격장\"); 프로젝트이동(\"메인\");",
                "메인" => ++mains < 30 ? "프로젝트이동(\"사격장\");" : "출력(\"그만\");",
                _ => null
            });

            var rounds = printed.Count(p => p == "사격장");

            Check("프로젝트이동: 돌고 도는 흐름(30바퀴)도 쌓이지 않는다",
                  errors.Count == 0 && rounds == 30 && printed.Last() == "그만" && maxDepth == 1,
                  $"사격장 {rounds}번 · 깊이 {maxDepth} · 오류 {errors.Count}");
        }

        // 프로젝트실행 - 함수처럼 기다렸다가 다음 줄로 돌아온다. 부른 쪽의 끝()은 그 프로젝트만 끝낸다.
        {
            var (printed, errors, maxDepth) = RunFlow("프로젝트실행(\"공용\"); 출력(\"돌아옴\");", new()
            {
                ["공용"] = "출력(\"공용\"); 끝(); 출력(\"공용 뒤\");"
            });

            Check("프로젝트실행: 기다렸다가 다음 줄로 돌아오고, 그 안의 끝()은 그 프로젝트만 끝낸다",
                  errors.Count == 0 && string.Join(",", printed) == "공용,돌아옴" && maxDepth == 1,
                  $"{string.Join(",", printed)} · 깊이 {maxDepth}");
        }

        // 프로젝트실행 안에서 이동 - 쌓인 것을 모두 걷고(부른 쪽 다음 줄도 안 돈다) 맨 바깥에서 넘어간다.
        {
            var (printed, errors, maxDepth) = RunFlow("프로젝트실행(\"영웅\"); 출력(\"메인 뒤\");", new()
            {
                ["영웅"] = "출력(\"영웅\"); 프로젝트이동(\"사격장\");",
                ["사격장"] = "출력(\"사격장\");"
            });

            var shown = string.Join(",", printed.Where(p => !p.StartsWith("프로젝트 '", StringComparison.Ordinal)));

            Check("프로젝트실행 안에서 이동하면 부른 쪽도 끝나고 맨 바깥에서 넘어간다",
                  errors.Count == 0 && shown == "영웅,사격장" && maxDepth == 1,
                  $"{shown} · 깊이 {maxDepth}");
        }
    }
}
