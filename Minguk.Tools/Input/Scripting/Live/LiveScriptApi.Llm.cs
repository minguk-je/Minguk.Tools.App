using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Minguk.Tools.Capture.Input;
using Minguk.Tools.Llm;

namespace Minguk.Tools.Input.Scripting.Live;

/// <summary>
/// 로컬 언어 모델로 판단하기 - 상황을 글로 모아 넘기고 행동 목록 중 하나를 받는다. 화면을 그대로 보여 묻기도 한다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-24) "진짜로 로컬LLM 붙여서 해보고 싶은데" · "둘 다"(글 요약 + 화면 보기) · "로컬 VLM 12GB 이상 달 예정이니까 구현은 해줘".
///
/// <b>나누는 선</b> - 모델은 느리다(이 PC 에서 글 판단 3~4초, 그림 판단 10초 이상). 체력이 30% 아래면 물약처럼 <b>분명한 규칙은 스크립트가</b>
/// 곧바로 처리하고, 모델에는 규칙으로 못 가르는 것만 묻는다. 조준·회피 같은 순간 반응은 모델에 맡기지 않는다.
///
/// <b>글자는 OCR 이 읽어서 준다</b> - 3B 그림 모델은 같은 화면에서 지역 이름·체력·레벨을 자주 틀렸다(실측 2026-09-24).
/// <c>상황요약("지역, 퀘스트, 체력, 검출")</c> 이 이름 붙인 자리를 읽어 글로 묶는다. 그림 모델은 그림으로만 알 수 있는 것에 쓴다.
///
/// 모델·주소는 프로젝트 폴더의 <see cref="LlmSpec.FileName"/>, 게임 규칙은 <see cref="LlmSpec.RulesFileName"/>, 고쳐 준 판단은 <see cref="LlmSpec.ExamplesFileName"/>.
/// </remarks>
public partial class LiveScriptApi
{
    // ── 상황 요약 ────────────────────────────────────────────────────────

    /// <summary>
    /// 이름 붙인 자리들을 읽어 「이름: 읽은 글」 줄로 묶는다. 이름 「검출」 은 지금 검출을 이름별 수·자리로 요약한다.
    /// </summary>
    public string SituationSummary(string names) => Traced("SituationSummary", Quote(names), () => SituationSummaryCore(names));

    // ── 판단 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 상황(글)을 보고 행동 목록(쉼표로) 중 하나를 고른다 - 글 모델(<c>llm.json</c> 의 textModel).
    /// </summary>
    public string Judge(string situation, string actions)
        => Traced("Judge", $"{Quote(ShortText(situation))}, {Quote(actions)}", () => JudgeCore(situation, actions, screen: false, regionName: null));

    /// <summary>
    /// 지금 화면(또는 이름 붙인 자리)을 보여 주고 행동 목록 중 하나를 고른다 - 그림 모델(visionModel). <paramref name="question"/> 에 읽은 글을 같이 넣으면 좋다.
    /// </summary>
    public string JudgeScreen(string question, string actions, string? regionName = null)
        => Traced("JudgeScreen", $"{Quote(ShortText(question))}, {Quote(actions)}", () => JudgeCore(question, actions, screen: true, regionName));

    /// <summary>글 모델에 자유롭게 묻는다. 답은 글.</summary>
    public string Ask(string question) => Traced("Ask", Quote(ShortText(question)), () => AskCore(question, screen: false, regionName: null));

    /// <summary>지금 화면(또는 이름 붙인 자리)을 보여 주고 자유롭게 묻는다. 답은 글.</summary>
    public string AskScreen(string question, string? regionName = null)
        => Traced("AskScreen", Quote(ShortText(question)), () => AskCore(question, screen: true, regionName));

    /// <summary>
    /// 틀린 판단을 고친다 - 「이 상황이면 이 행동」 을 프로젝트에 적어 두고, 다음 판단부터 예시로 넣는다.
    /// </summary>
    public void CorrectJudgement(string situation, string action) => Traced("CorrectJudgement", $"{Quote(ShortText(situation))}, {Quote(action)}", () =>
    {
        var root = _host.ResourceRoot ?? throw Guard("판단고치기는 프로젝트로 돌릴 때만 씁니다 - 고친 것을 프로젝트 폴더에 적습니다.");

        LlmJudge.AppendExample(root, new LlmExample(situation.Trim(), action.Trim()));
        Print($"판단을 고쳐 적었습니다 → {action} ({LlmSpec.ExamplesFileName})");
    });

    /// <summary>이 실행 동안 쓸 게임 규칙 - <c>llm-rules.md</c> 대신. 빈 글이면 파일로 돌아간다.</summary>
    public void SetJudgeRules(string rules) => Traced("SetJudgeRules", Quote(ShortText(rules)), () => _llmRules = string.IsNullOrWhiteSpace(rules) ? null : rules);

    public string 상황요약(string 자리들) => SituationSummary(자리들);

    public string 판단(string 상황, string 행동들) => Judge(상황, 행동들);

    public string 화면판단(string 질문, string 행동들) => JudgeScreen(질문, 행동들);

    public string 화면판단(string 질문, string 행동들, string 자리) => JudgeScreen(질문, 행동들, 자리);

    public string 물어보기(string 질문) => Ask(질문);

    public string 화면물어보기(string 질문) => AskScreen(질문);

    public string 화면물어보기(string 질문, string 자리) => AskScreen(질문, 자리);

    public void 판단고치기(string 상황, string 행동) => CorrectJudgement(상황, 행동);

    public void 판단규칙(string 규칙) => SetJudgeRules(규칙);

    // ── 속 ──────────────────────────────────────────────────────────────

    private string? _llmRules;
    private LlmSpec? _llmSpec;
    private DateTime _llmSpecStamp;
    private ILlmAdapter? _llmAdapter;

    /// <summary>이번 실행에서 한 번이라도 부른 모델 - 처음 부를 때만 "올리는 중" 을 알린다.</summary>
    private readonly HashSet<string> _llmWarm = new(StringComparer.OrdinalIgnoreCase);

    private string JudgeCore(string situation, string actions, bool screen, string? regionName)
    {
        var list = LlmJudge.SplitActions(actions);

        if (list.Count == 0) throw Guard("고를 행동이 없습니다 - 판단(상황, \"공격, 물약, 후퇴\") 처럼 쉼표로 적으세요.");

        var spec = LlmSpecNow();
        var model = screen ? spec.VisionModel : spec.TextModel;
        var image = screen ? ScreenImage(regionName, spec.MaxImageSide) : null;
        var examples = _host.ResourceRoot is { } root ? LlmJudge.LoadExamples(root, spec.Examples) : [];
        var request = LlmJudge.Build(model, RulesNow(), examples, situation ?? string.Empty, list, image, spec.KeepAlive);
        var response = Call(spec, request);

        try
        {
            var (action, rule) = LlmJudge.Parse(response.Text, list);

            Watch(screen ? "화면판단" : "판단", $"{action}{(rule > 0 ? $" (규칙 {rule})" : string.Empty)} · {response.Total.TotalMilliseconds:0}ms");
            Logger.Debug($"판단 [{model}] {ShortText(situation ?? string.Empty)} → {action} (규칙 {rule}, {response.Total.TotalMilliseconds:0}ms, 읽기 {response.PromptTokens}·쓰기 {response.OutputTokens}토큰)");

            return action;
        }
        catch (LlmException ex)
        {
            throw Guard(ex.Message);
        }
    }

    private string AskCore(string question, bool screen, string? regionName)
    {
        var spec = LlmSpecNow();
        var model = screen ? spec.VisionModel : spec.TextModel;
        var image = screen ? ScreenImage(regionName, spec.MaxImageSide) : null;

        return Call(spec, LlmJudge.BuildQuestion(model, RulesNow(), question ?? string.Empty, image, spec.KeepAlive)).Text;
    }

    /// <summary>모델을 부른다 - 멈춤(F6·Pause)이 곧바로 먹는다. 처음 부르는 모델이면 오래 걸린다고 먼저 알린다.</summary>
    private LlmResponse Call(LlmSpec spec, LlmRequest request)
    {
        ThrowIfStopping();

        if (_llmWarm.Add(request.Model))
            Print($"LLM: {request.Model} 을(를) 부릅니다 - 올라 있지 않으면 처음 한 번 1~2분 걸립니다.");

        _llmAdapter ??= _host.Llm?.Invoke(spec) ?? LlmAdapterFactory.Create(spec);

        try
        {
            return _llmAdapter.ChatAsync(request, _token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            ThrowIfStopping();
            throw;
        }
        catch (LlmException ex)
        {
            throw Guard(ex.Message);
        }
    }

    /// <summary>이 실행이 쓸 설정 - 프로젝트 폴더의 llm.json(바뀌면 다시 읽는다). 없으면 기본값으로 만들어 둔다.</summary>
    private LlmSpec LlmSpecNow()
    {
        if (_host.ResourceRoot is not { } root) return _llmSpec ??= new LlmSpec();

        var path = System.IO.Path.Combine(root, LlmSpec.FileName);

        if (!System.IO.File.Exists(path))
        {
            var fresh = new LlmSpec();

            try
            {
                fresh.Save(path);
                Print($"{LlmSpec.FileName} 을 기본값으로 만들었습니다(글 {fresh.TextModel} · 그림 {fresh.VisionModel}) - 모델을 바꾸려면 이 파일을 고치세요.");
            }
            catch (System.IO.IOException ex)
            {
                Logger.Warn(ex, $"LLM 설정을 못 적었다: {path}");
            }

            _llmSpec = fresh;
            _llmSpecStamp = System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path) : default;
            _llmAdapter = null;

            return fresh;
        }

        var stamp = System.IO.File.GetLastWriteTimeUtc(path);

        if (_llmSpec is null || stamp != _llmSpecStamp)
        {
            try
            {
                _llmSpec = LlmSpec.Load(path) ?? new LlmSpec();
                _llmSpecStamp = stamp;
                _llmAdapter = null;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.IO.IOException)
            {
                Logger.Warn(ex, $"LLM 설정을 못 읽었다: {path}");
                throw Guard($"{LlmSpec.FileName} 을 읽지 못했습니다 - {ex.Message}");
            }
        }

        return _llmSpec!;
    }

    /// <summary>게임 규칙 - 스크립트가 준 것, 없으면 프로젝트 폴더의 llm-rules.md.</summary>
    private string? RulesNow() => _llmRules ?? (_host.ResourceRoot is { } root ? LlmJudge.LoadRules(root) : null);

    /// <summary>지금 화면(또는 자리)을 긴 변 <paramref name="maxSide"/> 로 줄인 PNG.</summary>
    private byte[] ScreenImage(string? regionName, int maxSide)
    {
        var area = new Rect(0, 0, 1, 1);

        if (regionName is not null)
        {
            var book = _host.Regions?.Invoke() ?? throw Guard("영역 목록이 없습니다 - 화면에서 데이터셋 폴더를 골라야 합니다.");
            var found = book.Resolve(regionName) ?? throw Guard(MissingRegion(book, regionName));

            _host.Hub.TryGetFrameSize(out var frameWidth, out var frameHeight);
            area = Vision.Regions.RegionTargets.Bounds(Vision.Regions.RegionTargets.Of(found.Region, found.Cell)[0], frameWidth, frameHeight);
        }

        System.Windows.Media.Imaging.BitmapSource crop = CropFor(area);
        var longest = Math.Max(crop.PixelWidth, crop.PixelHeight);

        if (longest > maxSide)
        {
            var scale = (double)maxSide / longest;
            var scaled = new System.Windows.Media.Imaging.TransformedBitmap(crop, new System.Windows.Media.ScaleTransform(scale, scale));
            scaled.Freeze();
            crop = scaled;
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));

        using var memory = new System.IO.MemoryStream();
        encoder.Save(memory);

        return memory.ToArray();
    }

    private string SituationSummaryCore(string names)
    {
        var lines = new List<string>();

        foreach (var name in LlmJudge.SplitActions(names))
        {
            if (name is "검출" or "Detections")
            {
                lines.Add("검출: " + DetectionSummary());
                continue;
            }

            var text = ReadAtCore(name).Text;

            lines.Add($"{name}: {(text.Length > 0 ? text : "(없음)")}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>「늑대 2(가장 가까운 것 왼쪽 가까이) · 허수아비 1(가운데 멀리)」. 검출이 꺼져 있으면 그렇게 말한다(멈추지 않는다).</summary>
    private string DetectionSummary()
    {
        var hub = _host.Hub;

        if (!hub.IsCapturing || !hub.IsDetecting) return "(검출 꺼짐)";

        var mobs = DetectionsCore();

        if (mobs.Count == 0) return "없음";

        var target = _host.Target();
        var bounds = target is not null && CaptureTargetBounds.TryGet(target, out var b) ? b : Rect.Empty;

        return string.Join(" · ", mobs
            .GroupBy(m => m.Name)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                var nearest = bounds.IsEmpty ? g.First() : g.OrderBy(m => Distance(m, bounds)).First();

                return bounds.IsEmpty ? $"{g.Key} {g.Count()}" : $"{g.Key} {g.Count()}(가장 가까운 것 {SpotWords(nearest, bounds)})";
            }));
    }

    private static double Distance(ScriptDetection mob, Rect bounds)
    {
        var dx = mob.CenterX - (bounds.X + (bounds.Width / 2));
        var dy = mob.CenterY - (bounds.Y + (bounds.Height / 2));

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>화면 가운데 기준 가로 자리와 크기로 가늠한 거리 - 「왼쪽 가까이」.</summary>
    private static string SpotWords(ScriptDetection mob, Rect bounds)
    {
        var x = (mob.CenterX - bounds.X) / bounds.Width;
        var side = x < 0.4 ? "왼쪽" : x > 0.6 ? "오른쪽" : "가운데";
        var near = mob.Height / bounds.Height > 0.25 ? "가까이" : "멀리";

        return $"{side} {near}";
    }

    private static string ShortText(string text) => text.Length <= 60 ? text.Replace('\n', ' ') : text[..60].Replace('\n', ' ') + "…";
}
