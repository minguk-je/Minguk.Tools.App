using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using DevExpress.Mvvm;
using DevExpress.Xpf.Docking;

using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Llm;
using Minguk.Tools.Markup;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 스크립트 도우미 - 지금 미리보기 화면과 요청 글을 그림 모델에 보내 동작 하나를 받고, 영역·본보기·코드를 만들어 편집기에 한 줄씩 넣는다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-24) "현재 화면 미리보기로 보여주고 오른쪽 위에 버튼 눌러줘. 이러면 스크립트 내용에 순차 적으로 만들어 주는거야.. 함수도 니가 미리 만들어 둔거 참고 해서".
/// 설계 <c>docs/superpowers/specs/2026-09-24-스크립트-도우미-design.md</c> - 찾은 자리는 <b>영역으로 미리보기에 띄워 사람이 확인</b>한 뒤 [넣기],
/// 누르기는 <b>그림누르기 + 본보기 자동 저장</b>, 코드는 <b>동작 조립</b>(<see cref="ScriptAssistant.Render"/>)이고 「코드」 동작만 자유 코드 + 컴파일 검사.
///
/// 편집기에는 <c>Document.Insert</c> 로 넣는다 - 문서 <c>Text</c> 를 통째로 바꾸면 되돌리기(Ctrl+Z) 기록이 사라진다.
/// </remarks>
public partial class ScriptStudioViewModel
{
    private AssistantPlan? _assistantPlan;
    private NamedRegion? _assistantRegion;
    private string _assistantName = string.Empty;
    private CancellationTokenSource? _assistantCts;

    /// <summary>요청 글 - "오른쪽 위 설정 버튼 눌러줘".</summary>
    public string? AssistantRequest
    {
        get => GetProperty(() => AssistantRequest);
        set => SetProperty(() => AssistantRequest, value);
    }

    /// <summary>모델이 고른 것을 한 줄로 - "오른쪽 위 톱니 모양 설정 버튼을 누른다".</summary>
    public string? AssistantSay
    {
        get => GetProperty(() => AssistantSay);
        private set => SetProperty(() => AssistantSay, value);
    }

    /// <summary>넣을 코드. 넣기 전에 사람이 고칠 수 있다.</summary>
    public string? AssistantCode
    {
        get => GetProperty(() => AssistantCode);
        set => SetProperty(() => AssistantCode, value);
    }

    /// <summary>도우미 상태 한 줄 - 묻는 중·걸린 시간·안 된 이유.</summary>
    public string? AssistantStatus
    {
        get => GetProperty(() => AssistantStatus);
        private set => SetProperty(() => AssistantStatus, value);
    }

    /// <summary>모델에 묻는 중인가.</summary>
    public bool IsAssistantBusy
    {
        get => GetProperty(() => IsAssistantBusy);
        private set => SetProperty(() => IsAssistantBusy, value);
    }

    /// <summary>넣을 계획이 있는가.</summary>
    public bool HasAssistantPlan
    {
        get => GetProperty(() => HasAssistantPlan);
        private set => SetProperty(() => HasAssistantPlan, value);
    }

    /// <summary>[보내기](Enter) - 지금 화면과 요청을 그림 모델에 보낸다.</summary>
    public ICommand AssistantSendCommand => new DelegateCommand(DoAssistantSend, () => !IsAssistantBusy && !string.IsNullOrWhiteSpace(AssistantRequest));

    /// <summary>[넣기] - 영역이면 본보기를 저장하고, 코드를 편집기 커서 줄 다음에 넣는다.</summary>
    public ICommand AssistantInsertCommand => new DelegateCommand(DoAssistantInsert, () => HasAssistantPlan && !IsAssistantBusy);

    /// <summary>[버리기] - 계획과 만든 영역을 지운다. 묻는 중이면 멈춘다.</summary>
    public ICommand AssistantDiscardCommand => new DelegateCommand(DoAssistantDiscard, () => HasAssistantPlan || IsAssistantBusy);

    private async void DoAssistantSend() => await GuardAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(AssistantRequest)) return;

        if (!IsRunning)
        {
            AssistantStatus = "먼저 캡처를 시작하세요 - 도우미는 지금 미리보기 화면을 보고 답합니다.";
            return;
        }

        DiscardPlan();

        var root = RecognitionRoot;
        LlmSpec spec;

        try
        {
            spec = LlmSpec.LoadOrCreate(root, out var created);

            if (created) Live.Console.Print($"{LlmSpec.FileName} 을 기본값으로 만들었습니다(글 {spec.TextModel} · 그림 {spec.VisionModel}) - {root}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            AssistantStatus = $"{LlmSpec.FileName} 을 읽지 못했습니다 - {ex.Message}";
            return;
        }

        var frame = await CurrentFrameAsync(spec.MaxImageSide);

        if (frame is null)
        {
            AssistantStatus = "프레임이 안 옵니다 - 캡처가 돌고 있는지, CPU 리드백이 켜져 있는지 보세요.";
            return;
        }

        var (png, width, height) = frame.Value;
        var request = ScriptAssistant.Build(spec.VisionModel, AssistantRequest!, png, width, height,
            [.. Regions.Select(r => r.Name)], ScriptTail(15), spec.BoxUnits, LlmJudge.LoadRules(root), spec.KeepAlive);

        _assistantCts = new CancellationTokenSource();
        IsAssistantBusy = true;
        AssistantStatus = $"{spec.VisionModel} 에 묻는 중… (모델이 안 올라 있으면 처음 한 번 1~2분)";

        try
        {
            var adapter = LlmAdapterFactory.Create(spec);
            var response = await adapter.ChatAsync(request, _assistantCts.Token);
            var plan = ScriptAssistant.Parse(response.Text, width, height, spec.BoxUnits);
            var took = $"{spec.VisionModel} · {response.Total.TotalSeconds:0.0}초";

            var placed = true;

            if (plan.NeedsRegion && plan.Box is null)
            {
                AssistantStatus = $"{took} - 자리를 못 받아 자리만 다시 묻는 중…";

                try
                {
                    plan = await ScriptAssistant.EnsureBoxAsync(adapter, spec.VisionModel, plan, AssistantRequest!, png, width, height, spec.BoxUnits, spec.KeepAlive, _assistantCts.Token);
                }
                catch (LlmException ex)
                {
                    // 작은 모델은 자리를 자주 못 찍는다(실측 2026-09-24 qwen2.5vl:3b - 1024px 그림에 x 1189). 멈추지 않고 가운데에 놓아 사람이 끌어 맞추게 한다 -
                    // 이름·코드·본보기 저장은 그대로 쓴다.
                    Logger.Info($"도우미: 자리를 못 받아 가운데에 놓는다 - {ex.Message}");
                    plan = plan with { Box = new Rect(0.45, 0.45, 0.1, 0.06) };
                    placed = false;
                }
            }

            if (plan.Action == ScriptAssistant.Code)
            {
                plan = await CheckCodeAsync(plan, spec, adapter, _assistantCts.Token);
            }

            var name = string.Empty;

            if (plan.NeedsRegion && plan.Box is { } box)
            {
                name = ScriptAssistant.CleanName(plan.Name, n => RegionBook.Find(n) is not null || TemplateExists(n));
                _assistantRegion = CreateRegion(name, box);
            }

            _assistantPlan = plan;
            _assistantName = name;
            AssistantSay = plan.Say.Length > 0 ? plan.Say : plan.Action;
            AssistantCode = ScriptAssistant.Render(plan, name);
            HasAssistantPlan = true;

            AssistantStatus = plan.NeedsRegion
                ? (placed
                      ? $"{took} - 미리보기의 「{name}」 영역이 맞는지 보고, 틀리면 손잡이로 맞춘 뒤 [넣기]."
                      : $"{took} - 모델이 자리를 못 찾아 「{name}」 영역을 미리보기 가운데에 놓았습니다. 누를 것 위로 끌어 맞춘 뒤 [넣기].") +
                  (IsInputForwardingEnabled ? " 영역을 고치려면 입력 전달을 끄세요." : string.Empty)
                : $"{took} - 코드를 보고 [넣기].";

            Logger.Info($"도우미: 「{AssistantRequest}」 → {plan.Action} {name} {plan.Box} ({response.Total.TotalMilliseconds:0}ms, 읽기 {response.PromptTokens}토큰)");
        }
        catch (LlmException ex)
        {
            AssistantStatus = ex.Message;
        }
        catch (OperationCanceledException)
        {
            AssistantStatus = "묻기를 멈췄습니다.";
        }
        finally
        {
            IsAssistantBusy = false;
            _assistantCts?.Dispose();
            _assistantCts = null;
        }
    });

    private async void DoAssistantInsert() => await GuardAsync(async () =>
    {
        if (_assistantPlan is not { } plan || string.IsNullOrWhiteSpace(AssistantCode)) return;

        if (Script.IsLocked)
        {
            AssistantStatus = "스크립트가 도는 중에는 넣을 수 없습니다 - 멈춘 뒤 [넣기].";
            return;
        }

        if (!IsCSharpDocument())
        {
            AssistantStatus = "도우미는 C# 스크립트(.csx)에만 넣습니다 - 파이썬·자바스크립트 문서는 코드를 보고 손으로 옮기세요.";
            return;
        }

        if (ActiveEditor() is not { } editor)
        {
            AssistantStatus = "넣을 편집기를 못 찾았습니다 - 스크립트 파일 탭을 하나 여세요.";
            return;
        }

        var code = AssistantCode!;

        if (plan.NeedsRegion)
        {
            // 사람이 그 사이 이름을 고쳤을 수 있다 - 지금 이름으로 본보기를 저장하고 코드의 파일 이름도 바꾼다.
            var region = Regions.FirstOrDefault(r => ReferenceEquals(r, _assistantRegion))
                         ?? Regions.FirstOrDefault(r => string.Equals(r.Name, _assistantName, StringComparison.OrdinalIgnoreCase));

            if (region is null)
            {
                AssistantStatus = $"「{_assistantName}」 영역이 없어졌습니다 - 다시 [보내기] 하세요.";
                return;
            }

            SelectedCell = null;
            SelectedRegion = region;

            if (await SaveTemplateAsync() is null)
            {
                AssistantStatus = "본보기를 저장하지 못했습니다 - " + StatusText;
                return;
            }

            if (!string.Equals(region.Name, _assistantName, StringComparison.Ordinal))
                code = code.Replace(ScriptAssistant.Literal(_assistantName + ".png"), ScriptAssistant.Literal(region.Name + ".png"));
        }

        InsertAfterCaretLine(editor, code);
        Live.Console.Print($"도우미: {AssistantSay} → {code.Replace(Environment.NewLine, " ")}");

        _assistantPlan = null;
        _assistantRegion = null;
        HasAssistantPlan = false;
        AssistantRequest = string.Empty;
        AssistantStatus = "넣었습니다 - 다음 할 일을 적으세요.";
    });

    private void DoAssistantDiscard() => Guard(() =>
    {
        _assistantCts?.Cancel();
        DiscardPlan();
        AssistantStatus = "버렸습니다.";
    });

    /// <summary>넣지 않은 계획을 지운다 - 그 계획으로 만든 영역도.</summary>
    private void DiscardPlan()
    {
        if (_assistantRegion is { } region && Regions.Contains(region)) DeleteRegion(region);

        _assistantPlan = null;
        _assistantRegion = null;
        _assistantName = string.Empty;
        HasAssistantPlan = false;
        AssistantSay = null;
        AssistantCode = null;
    }

    /// <summary>
    /// 「코드」 동작 - 넣었을 때 컴파일되는지 본다. 오류가 나면 글 모델에 한 번 고치게 하고, 그래도 나면 오류를 말한다(넣을지는 사람이 정한다).
    /// </summary>
    private async Task<AssistantPlan> CheckCodeAsync(AssistantPlan plan, LlmSpec spec, ILlmAdapter adapter, CancellationToken token)
    {
        var errors = await InsertedErrorsAsync(plan.Code, token);

        if (errors.Count == 0) return plan;

        AssistantStatus = $"코드에 오류가 있어 {spec.TextModel} 에 고치게 하는 중… ({errors[0]})";

        var fixedResponse = await adapter.ChatAsync(ScriptAssistant.BuildFix(spec.TextModel, plan.Code, errors, spec.KeepAlive), token);
        var fixedCode = ReadFixedCode(fixedResponse.Text);

        if (fixedCode.Length == 0) return plan;

        var again = await InsertedErrorsAsync(fixedCode, token);

        if (again.Count > 0) Live.Console.Print($"도우미: 고친 코드에도 오류가 남았습니다 - {string.Join(" / ", again.Take(3))}");

        return plan with { Code = fixedCode };
    }

    private static string ReadFixedCode(string text)
    {
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');

            if (start < 0 || end <= start) return string.Empty;

            using var document = System.Text.Json.JsonDocument.Parse(text[start..(end + 1)]);

            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString()?.Trim() ?? string.Empty : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>지금 문서 끝에 코드를 붙였을 때 그 코드 줄에서 나는 컴파일 오류들(돌리지 않는다).</summary>
    private async Task<IReadOnlyList<string>> InsertedErrorsAsync(string code, CancellationToken token)
    {
        var engine = Script.Engine;

        if (Script.IsProject)
        {
            if (Script.Project.ActiveDocument is not { } document || Script.Project.Project is not { } project || engine is not IProjectScriptEngine projectEngine) return [];

            var baseText = document.Text.TrimEnd();
            var lines = LineCount(baseText);
            var texts = new Dictionary<string, string>(Script.Project.OpenTexts, StringComparer.OrdinalIgnoreCase)
            {
                [document.FilePath] = baseText + "\n" + code + "\n"
            };

            var errors = await projectEngine.CheckLiveAsync(project.ToUnit(texts), token);

            return [.. errors.Where(e => e.Line > lines && (e.File is null || string.Equals(System.IO.Path.GetFullPath(e.File), System.IO.Path.GetFullPath(document.FilePath), StringComparison.OrdinalIgnoreCase)))
                             .Select(e => $"{e.Line - lines}번째 줄: {e.Message}")];
        }

        var single = Script.Text.TrimEnd();
        var count = LineCount(single);
        var found = await engine.CheckLiveAsync(single + "\n" + code + "\n", token);

        return [.. found.Where(e => e.Line > count).Select(e => $"{e.Line - count}번째 줄: {e.Message}")];
    }

    private static int LineCount(string text) => text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;

    /// <summary>지금 화면을 긴 변 <paramref name="maxSide"/> 로 줄인 PNG 와 그 크기. 5초 안에 프레임이 안 오면 null.</summary>
    private async Task<(byte[] Png, int Width, int Height)?> CurrentFrameAsync(int maxSide)
    {
        Hub.WantsFrames = true;

        var deadline = Environment.TickCount64 + 5000;
        System.Windows.Media.Imaging.BitmapSource? crop = null;

        while (Environment.TickCount64 < deadline && (!Hub.TryCropFrame(new Rect(0, 0, 1, 1), out crop) || crop is null))
            await Task.Delay(50);

        if (crop is null) return null;

        var longest = Math.Max(crop.PixelWidth, crop.PixelHeight);

        if (longest > maxSide)
        {
            var scale = (double)maxSide / longest;
            var scaled = new System.Windows.Media.Imaging.TransformedBitmap(crop, new ScaleTransform(scale, scale));
            scaled.Freeze();
            crop = scaled;
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));

        using var memory = new System.IO.MemoryStream();
        encoder.Save(memory);

        return (memory.ToArray(), crop.PixelWidth, crop.PixelHeight);
    }

    /// <summary>지금 문서의 끝 몇 줄 - 모델이 앞서 한 일을 알고 이어 짜게.</summary>
    private string ScriptTail(int lines)
    {
        var text = Script.IsProject ? Script.Project.ActiveDocument?.Text ?? string.Empty : Script.Text;
        var all = text.Replace("\r\n", "\n").TrimEnd().Split('\n');

        return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
    }

    private bool IsCSharpDocument()
        => Script.IsProject
            ? Script.Project.ActiveDocument is { } document && string.Equals(System.IO.Path.GetExtension(document.FilePath), ".csx", StringComparison.OrdinalIgnoreCase)
            : Script.SelectedLanguage == ScriptLanguage.CSharp;

    /// <summary>지금 문서의 편집기 - 한 파일짜리는 화면이 든 것, 프로젝트는 그 문서 탭 안의 것.</summary>
    private ScriptEditor? ActiveEditor()
    {
        if (!Script.IsProject) return _editor;

        var dock = _dock ??= FindControl<DockLayoutManager>("DockObjectService");
        var document = Script.Project.ActiveDocument;
        var panel = dock?.GetItems().OfType<DocumentPanel>().FirstOrDefault(p => ReferenceEquals(p.DataContext, document));

        return panel is null ? null : FindDescendant<ScriptEditor>(panel);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }

        return null;
    }

    /// <summary>커서 줄 다음에 코드를 넣고 커서를 그 끝으로 - 되돌리기(Ctrl+Z) 한 번에 빠진다.</summary>
    private static void InsertAfterCaretLine(ScriptEditor editor, string code)
    {
        var document = editor.Document;
        var text = code.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        if (document.TextLength == 0)
        {
            document.Insert(0, text);
            editor.CaretOffset = text.Length;
        }
        else
        {
            var at = document.GetLineByOffset(editor.CaretOffset).EndOffset;
            var inserted = Environment.NewLine + text;

            document.Insert(at, inserted);
            editor.CaretOffset = at + inserted.Length;
        }

        editor.ScrollToLine(document.GetLineByOffset(editor.CaretOffset).LineNumber);
        editor.Focus();
    }
}
