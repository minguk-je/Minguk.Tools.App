using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using Minguk.Tools.Llm;

namespace Minguk.Tools.Tests;

/// <summary>
/// 진짜 Ollama 로 판단 시간·정확도를 잰다 - <c>--llm [--model=qwen3:8b] [--url=http://localhost:11434] [--image=화면.png] [--vision-model=qwen2.5vl:3b]</c>.
/// </summary>
/// <remarks>
/// 장면은 아이온2(RPG) 사냥 8개 - 2026-09-24 손으로 잰 것과 같다(qwen3:8b 7/8 · 평균 3.9초, qwen3:4b 3~4/8).
/// 이 PC 에서는 모델 올리기만 1~2분이 걸린다. 입력은 안 나간다.
/// </remarks>
internal static class LiveLlmCheck
{
    private const string Rules = """
        1. 체력 30% 아래: 물약이 있으면 물약, 없으면 후퇴.
        2. 가방이 가득 찼으면 귀환.
        3. 바닥에 떨어진 아이템이 있고 가까운 몬스터가 없으면 줍기.
        4. 대화 가능한 NPC 가 가까이 있고 퀘스트 완료 표시(?)가 있으면 대화.
        5. 공격해 오는 몬스터나 가까운 사냥 대상이 있으면 공격.
        6. 아무것도 없으면 사냥터로이동.
        """;

    private static readonly string[] Actions = ["물약", "후퇴", "귀환", "줍기", "대화", "공격", "사냥터로이동"];

    private static readonly (string Situation, string Expected)[] Cases =
    [
        ("체력 22%. 마나 60%. 물약 5개. 몬스터 2(가까움, 공격 중). 바닥 아이템 없음. 가방 40/80.", "물약"),
        ("체력 25%. 마나 10%. 물약 0개. 몬스터 1(가까움, 공격 중). 가방 30/80.", "후퇴"),
        ("체력 90%. 마나 80%. 물약 12개. 몬스터 없음. 바닥 아이템 3(발밑). 가방 50/80.", "줍기"),
        ("체력 85%. 마나 70%. 물약 10개. 몬스터 없음. NPC '엘리오스'(가까움, 머리 위 ?). 가방 20/80.", "대화"),
        ("체력 70%. 마나 50%. 물약 8개. 몬스터 3(가까움, 하나가 공격 중). 바닥 아이템 1. 가방 60/80.", "공격"),
        ("체력 95%. 마나 90%. 물약 8개. 몬스터 없음. 바닥 아이템 없음. 가방 80/80.", "귀환"),
        ("체력 100%. 마나 100%. 물약 15개. 몬스터 없음. 바닥 아이템 없음. NPC 없음. 가방 10/80.", "사냥터로이동"),
        ("체력 60%. 마나 40%. 물약 6개. 몬스터 1(먼 곳, 가만히). 바닥 아이템 2(발밑). 가방 45/80.", "줍기")
    ];

    public static int Run(string[] args)
    {
        var spec = new LlmSpec
        {
            Url = Program.ArgValue(args, "--url=") ?? "http://localhost:11434",
            TextModel = Program.ArgValue(args, "--model=") ?? "qwen3:8b",
            VisionModel = Program.ArgValue(args, "--vision-model=") ?? "qwen2.5vl:3b"
        };
        var adapter = LlmAdapterFactory.Create(spec);

        Console.WriteLine($"글 판단: {spec.TextModel} @ {spec.Url}");

        var right = 0;
        var times = new System.Collections.Generic.List<double>();

        // --skip-judge: 글 판단 8개를 건너뛰고 화면 쪽만(모델 올리기를 하나 덜 한다).
        foreach (var (situation, expected) in args.Contains("--skip-judge") ? [] : Cases)
        {
            var watch = Stopwatch.StartNew();

            try
            {
                var response = adapter.ChatAsync(LlmJudge.Build(spec.TextModel, Rules, [], situation, Actions), CancellationToken.None).GetAwaiter().GetResult();
                var (action, rule) = LlmJudge.Parse(response.Text, Actions);

                watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds - response.Load.TotalMilliseconds);

                if (action == expected) right++;

                Console.WriteLine($"{(action == expected ? "O" : "X")} {watch.ElapsedMilliseconds}ms (올리기 {response.Load.TotalMilliseconds:0}ms) 기대 {expected} → {action} (규칙 {rule})");
            }
            catch (LlmException ex)
            {
                Console.WriteLine($"X {ex.Message}");
            }
        }

        Console.WriteLine(times.Count > 0
            ? $"맞음 {right}/{Cases.Length} · 올리기 뺀 평균 {times.Average():0}ms · 가장 느림 {times.Max():0}ms"
            : $"맞음 {right}/{Cases.Length}");

        if (Program.ArgValue(args, "--image=") is { } image && File.Exists(image))
        {
            Console.WriteLine();
            Console.WriteLine($"화면 물음: {spec.VisionModel} · {Path.GetFileName(image)}");

            var watch = Stopwatch.StartNew();

            try
            {
                var response = adapter.ChatAsync(
                    LlmJudge.BuildQuestion(spec.VisionModel, null,
                        "RPG 게임 화면이다. 지역 이름, 체력 숫자, 레벨, 진행 중인 퀘스트, 근처에 적이 있는지, 지금 상황(전투/대기/대화/메뉴)을 한 줄씩 적어라.",
                        File.ReadAllBytes(image)),
                    CancellationToken.None).GetAwaiter().GetResult();

                Console.WriteLine($"{watch.ElapsedMilliseconds}ms (올리기 {response.Load.TotalMilliseconds:0}ms · 읽기 {response.PromptTokens}토큰)");
                Console.WriteLine(response.Text);
            }
            catch (LlmException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        if (Program.ArgValue(args, "--image=") is { } shot && File.Exists(shot) && Program.ArgValue(args, "--ask=") is { } ask)
            AskAssistant(adapter, spec, shot, ask);

        return right == Cases.Length ? 0 : 1;
    }

    /// <summary>
    /// 스크립트 도우미 한 번 - 화면과 요청을 그림 모델에 보내 계획·코드를 받고, 찾은 상자를 그린 PNG 를 남긴다(사람이 눈으로 본다).
    /// </summary>
    private static void AskAssistant(ILlmAdapter adapter, LlmSpec spec, string image, string ask)
    {
        Console.WriteLine();
        Console.WriteLine($"도우미: 「{ask}」 · {spec.VisionModel}");

        var frame = new System.Windows.Media.Imaging.BitmapImage();
        frame.BeginInit();
        frame.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        frame.UriSource = new Uri(Path.GetFullPath(image));
        frame.EndInit();
        frame.Freeze();

        // 앱과 같게 긴 변을 줄여 보낸다.
        System.Windows.Media.Imaging.BitmapSource sent = frame;
        var longest = Math.Max(frame.PixelWidth, frame.PixelHeight);

        if (longest > spec.MaxImageSide)
        {
            var scale = (double)spec.MaxImageSide / longest;
            sent = new System.Windows.Media.Imaging.TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
            sent.Freeze();
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(sent));

        using var memory = new MemoryStream();
        encoder.Save(memory);

        var watch = Stopwatch.StartNew();

        try
        {
            var response = adapter.ChatAsync(
                ScriptAssistant.Build(spec.VisionModel, ask, memory.ToArray(), sent.PixelWidth, sent.PixelHeight, ["미니맵", "퀘스트"], "키(\"F\");", spec.BoxUnits),
                CancellationToken.None).GetAwaiter().GetResult();

            Console.WriteLine($"{watch.ElapsedMilliseconds}ms (올리기 {response.Load.TotalMilliseconds:0}ms · 읽기 {response.PromptTokens}토큰 · 쓰기 {response.OutputTokens}토큰)");
            Console.WriteLine($"답: {response.Text}");

            var plan = ScriptAssistant.Parse(response.Text, sent.PixelWidth, sent.PixelHeight, spec.BoxUnits);

            if (plan.NeedsRegion && plan.Box is null)
            {
                var again = Stopwatch.StartNew();

                plan = ScriptAssistant.EnsureBoxAsync(adapter, spec.VisionModel, plan, ask, memory.ToArray(), sent.PixelWidth, sent.PixelHeight, spec.BoxUnits, spec.KeepAlive, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Console.WriteLine($"자리만 다시 물음: {again.ElapsedMilliseconds}ms");
            }

            var name = ScriptAssistant.CleanName(plan.Name, _ => false);

            Console.WriteLine($"계획: {plan.Action} · {name} · 상자 {plan.Box}");
            Console.WriteLine($"코드: {ScriptAssistant.Render(plan, name)}");

            if (plan.Box is { } box)
            {
                var visual = new System.Windows.Media.DrawingVisual();

                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(frame, new System.Windows.Rect(0, 0, frame.PixelWidth, frame.PixelHeight));
                    dc.DrawRectangle(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.Magenta, 3),
                        new System.Windows.Rect(box.X * frame.PixelWidth, box.Y * frame.PixelHeight, box.Width * frame.PixelWidth, box.Height * frame.PixelHeight));
                }

                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(frame.PixelWidth, frame.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(visual);

                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

                var output = Path.Combine(Path.GetTempPath(), "minguk-assistant-box.png");
                using (var file = File.Create(output)) png.Save(file);

                Console.WriteLine($"[INFO] 찾은 상자를 그렸다: {output}");
            }
        }
        catch (LlmException ex)
        {
            Console.WriteLine($"{watch.ElapsedMilliseconds}ms - {ex.Message}");
        }
    }
}
