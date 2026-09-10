using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Minguk.Base.Utilities;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using Minguk.Tools.Helper;
using System.Windows.Input;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Adapters;
using Minguk.Tools.Input.Hotkeys;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Sequencing;
using Minguk.Tools.Input.Targets;

namespace Minguk.Tools.ViewModels;

/// <summary>커맨드가 실제로 하는 일.</summary>
public partial class InputAutomationViewModel
{
    /// <summary>
    /// 고른 경로를 끼운다. 쓸 수 없으면 SendInput 으로 내려앉되, 무엇이 왜 밀려났는지 표시한다.
    /// </summary>
    private void ApplyBackend() => Guard(() =>
    {
        // 고른 대상 창을 넘긴다. 창이 닫혔으면 IntPtr.Zero 를 주어 어댑터가 예전처럼
        // "마지막 좌표 아래 창" 으로 되돌아가게 한다 - 죽은 핸들에 보내면 조용히 사라진다.
        var selection = InputAdapterFactory.CreateWithFallback(
            SelectedInputBackend, InputBackend.SendInput, ResolveTargetWindow);

        // 이전 어댑터를 버리지 않으면 드라이버 컨텍스트가 그대로 샌다.
        _adapter?.Dispose();
        _adapter = selection.Adapter;
        _service = new InputService(_adapter) { JitterMs = JitterMs };

        if (selection.FellBack)
        {
            // 조용히 다른 경로로 보내면 안 된다. 콤보 선택도 실제 경로에 맞춘다.
            SelectedInputBackend = InputBackend.SendInput;
            AdapterStatus = $"{selection.FellBackFrom} 을 쓸 수 없다 - {selection.Reason} → {_adapter.Name} 으로 바꿨다";
            Logger.Warn($"{selection.FellBackFrom} 사용 불가: {selection.Reason}");
            return;
        }

        // 문구가 사실과 어긋나면 읽는 사람이 잘못 믿는다. 두 번 겪었다 -
        // "글자 입력 불가" 라고 적어 두었다가 PostMessage 로는 아무것도 못 넣는 줄 알았고,
        // 다음엔 "한글 불가" 라고 적어 두었다가 한글도 되는 것을 오래 몰랐다.
        AdapterStatus = $"{_adapter.Name} · 한글·영문 입력"
                      + $" · 한/영 전환 {(_service.SupportsHangulToggle ? "가능" : "불가")}"
                      + $" · 진짜 커서 {(_adapter.GetCursorPosition() is null ? "안 움직임" : "움직임")}";
    });

    /// <summary>
    /// 편집기 색을 지금 테마에 맞춘다.
    /// </summary>
    /// <remarks>
    /// 색은 경량 테마 팔레트에서 읽는다(<see cref="SequenceScriptHighlighting"/>).
    /// AvalonEdit 은 경량 테마를 안 타므로 이렇게 옮겨 주지 않으면 테마를 바꿔도 이 편집기만
    /// 그대로 남는다.
    /// </remarks>
    private void ApplyEditorTheme() => Guard(() =>
    {
        EditorBackground = SequenceScriptHighlighting.Background;
        EditorForeground = SequenceScriptHighlighting.Foreground;
        EditorLineNumberForeground = SequenceScriptHighlighting.LineNumberForeground;
        EditorBorder = SequenceScriptHighlighting.Border;
        Highlighting = SequenceScriptHighlighting.Current;
    });

    /// <summary>
    /// 테마가 바뀌면 편집기 색을 다시 잰다.
    /// </summary>
    /// <remarks>
    /// <see cref="LightweightThemeManager.CurrentThemeChanged"/> 를 듣는다. 테마 <b>이름</b>이
    /// 바뀌는 이벤트가 아니라 실제로 새 팔레트가 들어온 뒤에 오는 것이라, 그때 읽으면 새 색이다.
    ///
    /// 정적 이벤트라 화면이 닫힐 때 반드시 풀어야 한다. 안 풀면 닫은 화면이 앱이 살아 있는
    /// 동안 계속 붙들려 있는다.
    /// </remarks>
    private void OnApplicationThemeChanged(object? sender, EventArgs e)
    {
        if (DispatcherService is { } dispatcher)
        {
            dispatcher.BeginInvoke(ApplyEditorTheme);
            return;
        }

        // 화면 밖(검증 하네스 같은 곳)에서는 서비스가 없다. 그래도 배선은 돌아야 한다.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(ApplyEditorTheme);
    }

    /// <summary>
    /// Interception 을 고를 때 드라이버가 준비됐는지 보고, 아니면 무엇을 해야 하는지 적는다.
    /// </summary>
    /// <remarks>
    /// 어댑터가 "쓸 수 없다" 고만 하면 사용자는 무엇을 해야 할지 모른다. 설치가 안 된 것과
    /// 설치는 됐는데 재부팅을 안 한 것은 할 일이 다르다 - 그것을 갈라 적는다.
    /// </remarks>
    private void UpdateDriverNotice() => Guard(() =>
    {
        if (SelectedInputBackend != InputBackend.Interception)
        {
            DriverNotice = null;
            CanInstallDriver = false;
            return;
        }

        var state = InterceptionDriver.GetState();

        if (state == InterceptionDriverState.Ready)
        {
            DriverNotice = null;
            CanInstallDriver = false;
            return;
        }

        DriverNotice = InterceptionDriver.Describe(state)
                       + (state == InterceptionDriverState.NotInstalled
                           ? " 설치하려면 관리자 권한이 필요하고, 설치 뒤 Windows 를 다시 시작해야 합니다."
                           : string.Empty);

        // 재부팅만 남았으면 다시 설치할 이유가 없다.
        CanInstallDriver = state is InterceptionDriverState.NotInstalled or InterceptionDriverState.Unknown
                           && InterceptionDriver.HasInstaller;
    });

    /// <summary>
    /// 드라이버를 설치한다. 설치 프로그램을 관리자로 띄운다.
    /// </summary>
    /// <remarks>
    /// 앱 전체를 관리자로 올리지 않는다 - 그러면 탐색기에서 파일을 끌어다 놓을 수 없고
    /// 늘 UAC 를 거쳐 켜야 한다. 설치할 때만 올린다.
    /// </remarks>
    private void DoInstallDriver() => Guard(() =>
    {
        CanInstallDriver = false;
        DriverNotice = "설치 중입니다. 관리자 권한을 물어보면 허용해 주세요...";

        _ = GuardAsync(async () =>
        {
            var result = await InterceptionDriver.InstallAsync();

            DriverNotice = result.Message;
            UpdateDriverNotice();

            if (result.NeedsReboot)
            {
                // 재부팅해야 한다는 것은 한 번 더 분명히 말한다. 안내 줄만으로는 지나치기 쉽다.
                MessageBoxService?.ShowMessage(
                    "드라이버를 설치했습니다.\n\nWindows 를 다시 시작해야 Interception 경로를 쓸 수 있습니다.\n"
                    + "재시작 전까지는 SendInput 경로로 돌아갑니다.",
                    "재시작이 필요합니다",
                    MessageButton.OK,
                    MessageIcon.Information);
            }
            else
            {
                MessengerUtility.SendMainMessage(result.Message);
            }
        });
    });

    private void OnSelectedInputBackendChanged()
    {
        if (_adapter is null) return;   // 아직 화면이 뜨기 전이면 OnLoaded 가 끼운다

        ApplyBackend();
        UpdateSequenceText();
        UpdateDriverNotice();

        RaisePropertyChanged(nameof(NeedsWindowTarget));

        // 대상 창이 필요해졌는데 목록이 비어 있으면 한 번 채워 준다.
        // 새로고침을 눌러야만 보이면 왜 비어 있는지 알기 어렵다.
        if (NeedsWindowTarget && WindowTargets.Count == 0) DoRefreshWindows();
    }

    // ── 대상 창 ──────────────────────────────────────────────────────────

    /// <summary>어댑터에 넘길 창 핸들. 고른 것이 없거나 이미 닫혔으면 0.</summary>
    private IntPtr ResolveTargetWindow()
    {
        if (SelectedWindowTarget is not { } target) return IntPtr.Zero;

        if (_windows?.IsAlive(target.Handle) == true) return target.Handle;

        return IntPtr.Zero;
    }

    /// <summary>
    /// 지금 떠 있는 창들을 다시 훑는다.
    /// </summary>
    /// <remarks>
    /// 고르고 있던 창이 그대로 있으면 그 선택을 지킨다. 목록을 새로 만들 때마다 선택이
    /// 풀리면 창 하나 늘었다고 다시 골라야 한다.
    /// </remarks>
    private void DoRefreshWindows() => Guard(() =>
    {
        _windows ??= WindowTargetAdapterFactory.Create();

        var chosen = SelectedWindowTarget?.Handle;

        WindowTargets.Clear();

        // 이 앱 자신도 목록에 둔다. 화면 아래 시험 입력란으로 받아 보는 것이 흔한 쓰임이다.
        foreach (var window in _windows.List()) WindowTargets.Add(window);

        SelectedWindowTarget = WindowTargets.FirstOrDefault(w => w.Handle == chosen) is { Handle: not 0 } kept
            ? kept
            : null;

        CurrentStep = $"창 {WindowTargets.Count}개를 찾았다";
    });

    /// <summary>
    /// 커서 아래 창을 대상으로 집는다.
    /// </summary>
    /// <remarks>
    /// 목록에서 고르는 것만으로는 부족하다. 제목이 같은 창이 여럿이면 어느 것인지 알 수 없고,
    /// 대상 창을 눈으로 보면서 집는 편이 확실하다. 좌표 담기(F4)와 같은 이유로 단축키다.
    /// </remarks>
    private void PickWindowUnderCursor() => Guard(() =>
    {
        if (IsRunning || _adapter is null) return;

        _windows ??= WindowTargetAdapterFactory.Create();

        if (ScreenCursor.TryGetPosition() is not { } p)
        {
            CurrentStep = "커서 자리를 알 수 없어 창을 집지 못했다";
            return;
        }

        if (_windows.FromPoint(p.X, p.Y) is not { } window)
        {
            CurrentStep = $"({p.X}, {p.Y}) 아래에 창이 없다";
            return;
        }

        if (WindowTargets.All(w => w.Handle != window.Handle)) WindowTargets.Add(window);

        SelectedWindowTarget = window;
        CurrentStep = $"대상 창을 집었다 - {window.Display}";
    });

    private void OnIsRunningChanged()
    {
        RaisePropertyChanged(nameof(IsIdle));

        DoRunOnceCommand.RaiseCanExecuteChanged();
        DoStartLoopCommand.RaiseCanExecuteChanged();
        DoStopCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 스크립트에서 읽어 둔 계획으로 시퀀스를 만든다.
    /// 시작할 때 한 번 굳혀 두므로, 도는 도중에 글을 고쳐도 그 바퀴에는 영향이 없다.
    /// </summary>
    private InputSequence BuildSequence() => _plan.Build(_service!, HoldTimeMs);

    // ── 스크립트 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 글이 바뀌면 잠시 묶어 두었다가 한 번만 돌린다.
    /// </summary>
    /// <remarks>
    /// 글자 하나 칠 때마다 컴파일하면 안 된다. 느린 것도 문제지만, Roslyn 은 컴파일할 때마다
    /// 메모리에 어셈블리를 새로 만들고 <b>그것은 언로드되지 않는다</b> - 치는 대로 쌓인다.
    /// 타이핑이 멎은 뒤에 한 번만 돌린다.
    /// </remarks>
    private void OnScriptTextChanged()
    {
        // 파일과 지금 글이 다르다는 표시. 되읽기 전에 세워 둔다 - 되읽기는 뒤늦게 끝난다.
        IsScriptDirty = true;

        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(
            _ => DispatcherService?.BeginInvoke(() => _ = RecompileAsync()),
            null, DebounceMs, System.Threading.Timeout.Infinite);
    }

    /// <summary>타이핑이 멎기를 기다리는 시간.</summary>
    private const int DebounceMs = 500;

    /// <summary>
    /// 언어를 갈아 끼운다. 손대지 않은 본보기면 새 언어 본보기로 바꾸고, 쓰던 글은 그대로 둔다.
    /// </summary>
    /// <remarks>
    /// 쓰던 글을 지우면 안 된다 - 잘못 골랐을 때 되돌릴 방법이 없어진다. 새 언어로는
    /// 컴파일되지 않을 테니 "고칠 줄" 에 그대로 뜬다.
    ///
    /// 다만 <b>아직 손대지 않은 본보기</b>는 지울 것이 없다. 그대로 두면 파이썬을 골라 놓고
    /// C# 본보기를 보게 되고, 그 상태로 저장하면 C# 이 든 .py 파일이 나온다.
    /// 그래서 열어 둔 파일이 없고 "지금 글 == 예전 언어의 본보기" 일 때만 갈아 끼운다.
    /// </remarks>
    private void OnScriptLanguageChanged() => Guard(() =>
    {
        // 갈아 끼우기 전에 예전 본보기를 챙긴다. Dispose 한 뒤에는 물어볼 데가 없다.
        var previousSample = _engine?.SampleSource;

        _engine?.Dispose();
        _engine = ScriptEngineFactory.Create(SelectedScriptLanguage);

        // 파일을 열어 둔 채면 그 글은 본보기가 아니라 그 파일의 것이다. 건드리지 않는다.
        if (string.IsNullOrWhiteSpace(ScriptText)
            || (string.IsNullOrEmpty(ScriptFilePath) && IsSame(ScriptText, previousSample)))
            ScriptText = _engine.SampleSource;

        _ = PrepareEngineAsync();
    });

    /// <summary>
    /// 두 글이 같은 글인지. 줄 끝과 앞뒤 여백은 세지 않는다.
    /// </summary>
    /// <remarks>
    /// 편집기를 거치면 줄 끝이 바뀔 수 있어 <c>==</c> 로는 손대지 않은 글도 달라 보인다.
    /// </remarks>
    private static bool IsSame(string? left, string? right)
    {
        if (left is null || right is null) return false;

        return Normalize(left) == Normalize(right);

        static string Normalize(string text)
            => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    /// <summary>
    /// 엔진을 준비시키고, 끝나면 한 번 돌려 순서를 채운다.
    /// </summary>
    /// <remarks>
    /// 파이썬은 처음 고를 때 런타임을 받아 온다(11MB). 그동안 화면이 멈춘 것처럼 보이지
    /// 않도록 무슨 일을 하는 중인지 적는다.
    /// </remarks>
    private async Task PrepareEngineAsync()
    {
        if (_engine is null) return;

        var engine = _engine;
        var progress = new Progress<string>(message => EngineStatus = message);

        await GuardAsync(async () =>
        {
            await engine.PrepareAsync(progress);

            EngineStatus = engine.IsReady ? null : engine.UnavailableReason;

            if (ReferenceEquals(engine, _engine)) await RecompileAsync();
        });
    }

    /// <summary>
    /// 글을 돌려 계획을 받아 온다. 실제 입력은 나가지 않는다 - 단계로 적힐 뿐이다.
    /// </summary>
    /// <remarks>
    /// 틀린 줄이 있어도 나머지는 그대로 계획에 담는다. 오타 한 줄 때문에 순서 미리보기가
    /// 통째로 비면 무엇을 고쳐야 하는지 오히려 알기 어렵다.
    /// 대신 실행은 막는다 - 반쪽짜리 시퀀스가 나가는 것이 더 나쁘다.
    /// </remarks>
    private async Task RecompileAsync()
    {
        if (_engine is null) return;

        // 앞선 것이 아직 돌고 있으면 접는다. 마지막 글만 의미가 있다.
        _compileCts?.Cancel();
        _compileCts?.Dispose();
        _compileCts = new CancellationTokenSource();

        var token = _compileCts.Token;
        var source = ScriptText;

        await GuardAsync(async () =>
        {
            try
            {
                var (plan, errors) = await _engine.RunAsync(source, token);

                if (token.IsCancellationRequested) return;

                _plan = plan;

                ScriptError = errors.Count == 0
                    ? null
                    : string.Join(Environment.NewLine, errors.Select(e => e.ToString()));

                UpdateSequenceText();
            }
            catch (OperationCanceledException)
            {
                // 더 새 글이 들어왔다는 뜻이다. 그쪽이 결과를 낸다.
            }
        });
    }

    /// <summary>
    /// 고른 종류의 본보기 줄을 캐럿이 있는 줄 아래에 끼운다.
    /// </summary>
    /// <remarks>
    /// 형식을 외우게 하지 않으려는 것이다. 편집기를 아직 못 잡았으면 끝에 붙인다 -
    /// 자리가 덜 좋을 뿐 하려던 일은 된다.
    /// </remarks>
    private void DoAddStep(SequenceStepKind kind) => Guard(() =>
    {
        var line = ScriptLine(Sample(kind));

        if (_editor is null)
        {
            ScriptText = string.IsNullOrEmpty(ScriptText) ? line : ScriptText + Environment.NewLine + line;
            return;
        }

        InsertLine(line);
    });

    /// <summary>
    /// 단계 하나를 지금 언어의 한 줄로 적는다.
    /// </summary>
    /// <remarks>
    /// 두 언어의 차이는 세미콜론뿐이다. 부르는 이름과 인자는 일부러 똑같이 맞춰 두었다.
    /// </remarks>
    private string ScriptLine(SequenceStepDefinition step)
    {
        var line = SequenceScript.ToCSharp(new SequencePlan { Steps = [step] });

        return SelectedScriptLanguage == ScriptLanguage.Python ? line.TrimEnd(';') : line;
    }

    /// <summary>본보기 줄에 쓸 값. 이동만 지금 커서 자리를 쓴다 - (0,0) 은 쓸 일이 거의 없다.</summary>
    private SequenceStepDefinition Sample(SequenceStepKind kind)
    {
        var step = new SequenceStepDefinition { Kind = kind };

        if (kind == SequenceStepKind.Type) step.Text = "안녕하세요";

        if (kind == SequenceStepKind.MoveTo && ScreenCursor.TryGetPosition() is { } p)
        {
            step.X = p.X;
            step.Y = p.Y;
        }

        return step;
    }

    private void InsertLine(string line)
    {
        var document = _editor!.Document;

        if (document.TextLength == 0)
        {
            document.Insert(0, line);
            _editor.CaretOffset = line.Length;
        }
        else
        {
            var at = document.GetLineByOffset(_editor.CaretOffset).EndOffset;
            var inserted = Environment.NewLine + line;

            document.Insert(at, inserted);
            _editor.CaretOffset = at + inserted.Length;
        }

        // 넣자마자 이어서 고칠 수 있게 편집기로 초점을 돌린다.
        _editor.Focus();
    }

    private void DoResetSteps() => Guard(() => ScriptText = _engine?.SampleSource ?? string.Empty);

    // ── 스크립트 파일 ────────────────────────────────────────────────────

    /// <summary>본보기 글로 새로 시작한다. 파일과의 연결도 끊는다.</summary>
    private void DoNewScript() => Guard(() =>
    {
        ScriptText = _engine?.SampleSource ?? string.Empty;
        ScriptFilePath = null;
        IsScriptDirty = false;
    });

    /// <summary>
    /// 파일을 열어 글을 갈아 끼운다. 확장자로 언어까지 맞춘다.
    /// </summary>
    /// <remarks>
    /// 언어를 먼저 바꾸고 글을 넣는다. 순서가 반대면 새 글을 예전 언어로 한 번 돌려
    /// 헛된 오류가 화면에 스쳤다 사라진다.
    /// </remarks>
    private void DoOpenScript() => Guard(() =>
    {
        if (OpenFileDialogService is not { } dialog)
        {
            MessengerUtility.SendMainMessage("파일 열기 서비스를 찾지 못했습니다.");
            return;
        }

        dialog.Filter = ScriptFiles.OpenFilter(SelectedScriptLanguage);
        dialog.InitialDirectory = ScriptFiles.DefaultDirectory;

        if (!dialog.ShowDialog()) return;

        var path = dialog.File.GetFullName();
        var text = System.IO.File.ReadAllText(path);

        if (ScriptFiles.FromPath(path) is { } language && language != SelectedScriptLanguage)
            SelectedScriptLanguage = language;

        ScriptText = text;
        ScriptFilePath = path;
        IsScriptDirty = false;

        Logger.Info($"스크립트를 열었다: {path}");
        MessengerUtility.SendMainMessage($"{System.IO.Path.GetFileName(path)} 을(를) 열었습니다.");
    });

    /// <summary>저장한다. 아직 자리를 안 정했으면 물어본다.</summary>
    private void DoSaveScript() => Guard(() =>
    {
        if (string.IsNullOrEmpty(ScriptFilePath))
        {
            DoSaveScriptAs();
            return;
        }

        WriteScript(ScriptFilePath);
    });

    private void DoSaveScriptAs() => Guard(() =>
    {
        if (SaveFileDialogService is not { } dialog)
        {
            MessengerUtility.SendMainMessage("파일 저장 서비스를 찾지 못했습니다.");
            return;
        }

        dialog.Filter = ScriptFiles.SaveFilter(SelectedScriptLanguage);
        dialog.DefaultExt = ScriptFiles.Extension(SelectedScriptLanguage).TrimStart('.');
        dialog.DefaultFileName = string.IsNullOrEmpty(ScriptFilePath)
            ? "스크립트" + ScriptFiles.Extension(SelectedScriptLanguage)
            : System.IO.Path.GetFileName(ScriptFilePath);
        dialog.InitialDirectory = ScriptFiles.DefaultDirectory;

        if (!dialog.ShowDialog()) return;

        WriteScript(dialog.File.GetFullName());
    });

    private void WriteScript(string path)
    {
        // UTF-8 로 쓴다. 한글 이름을 쓸 수 있게 해 놓고 ANSI 로 쓰면 다른 PC 에서 깨진다.
        System.IO.File.WriteAllText(path, ScriptText ?? string.Empty, new System.Text.UTF8Encoding(false));

        ScriptFilePath = path;
        IsScriptDirty = false;

        Logger.Info($"스크립트를 저장했다: {path}");
        MessengerUtility.SendMainMessage($"{System.IO.Path.GetFileName(path)} 에 저장했습니다.");
    }

    private void UpdateSequenceText() => Guard(() =>
    {
        if (_service is null) return;

        SequenceText = BuildSequence().Describe();
        PathWarning = DescribeDroppedSteps();
    });

    /// <summary>
    /// 고른 경로가 못 보내는 단계가 몇 개인지 세어 문장으로 만든다. 없으면 null.
    /// </summary>
    private string? DescribeDroppedSteps()
    {
        if (_service is null || _adapter is null) return null;

        var lines = new List<string>();

        // 창 메시지 경로는 대상 창의 "포커스를 가진 컨트롤" 로 들어간다. 버튼을 누르면
        // 그 순간 포커스가 버튼으로 옮겨 가서 글자가 갈 곳을 잃는다. 실측으로 겪었다.
        // 커널 입력 큐를 쓰는 경로(SendInput·Interception)는 이 문제가 없다.
        // ── 창 메시지 경로에만 해당하는 것 ──
        //    한 번에 판단한다. 능력별로 조기 반환을 두었더니 뒤에 있던 안내가 통째로 빠졌다.
        if (!_adapter.RequiresForegroundTarget)
        {
            // 대상 창의 "포커스를 가진 컨트롤" 로 들어간다. 버튼을 누르면 그 순간 포커스가
            // 버튼으로 옮겨 가서 글자가 갈 곳을 잃는다. 실측으로 겪었다.
            lines.Add("버튼으로 시작하면 포커스가 버튼으로 옮겨 가 입력이 갈 곳을 잃습니다 - "
                      + "대상에 포커스를 둔 채 F5(1회) · F6(반복) 으로 시작하세요.");

            // 대상 창을 안 고르면 마지막 좌표 아래 창으로 간다. 나가긴 나가는데 어디로
            // 갔는지 알 수 없어서, "끝남" 을 보고 됐다고 믿게 된다. 그 전에 말해 준다.
            if (SelectedWindowTarget is null && _plan.Steps.Count > 0)
                lines.Add("대상 창을 고르지 않아 마지막 좌표 아래의 창으로 나갑니다 - "
                          + "어디로 갈지 정하려면 창을 고르세요.");
        }

        // ── 이 경로가 못 보내는 단계 ──
        if (!_service.SupportsHangulToggle)
        {
            var dropped = _plan.Steps.Count(SequenceStepKinds.NeedsScanCode);

            if (dropped > 0)
                lines.Add($"{_adapter.Name} 경로는 한/영 전환을 하지 못해 한/영 단계 {dropped}개가 빠집니다. "
                          + "글자는 한글까지 그대로 나갑니다.");
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 다른 창이 앞에 있을 때도 시작·중지할 수 있게 단축키를 건다.
    /// </summary>
    /// <remarks>
    /// 이 화면의 버튼은 이 앱이 앞에 있어야 누를 수 있는데, 입력은 대상 창이 앞에 있어야
    /// 들어간다. 둘을 동시에 만족할 수 없어서 앱 밖에서 누를 수단이 필요하다.
    ///
    /// 등록은 흔하게 실패한다(다른 프로그램이 같은 조합을 먼저 쥐고 있으면).
    /// 조용히 넘기면 사용자는 눌러도 아무 일이 없는 이유를 알 수 없으므로 화면에 적는다.
    /// </remarks>
    private void RegisterHotkeys() => Guard(() =>
    {
        _hotkeys = GlobalHotkeyAdapterFactory.Create();

        // RegisterHotKey 는 시스템 전역이다. 맨 키로 잡으면 이 화면이 열려 있는 동안
        // 모든 앱에서 그 키를 빼앗는다 - F5 는 브라우저 새로고침과 Visual Studio 의 디버그 시작,
        // F3 은 흔한 "다음 찾기" 다. 화면을 닫으면 돌려준다(ReleaseResources 가 푼다).
        //
        // F12 는 쓸 수 없다. 다른 프로그램이 쥐고 있어서가 아니라 Windows 가 디버거용으로
        // 예약해 둔 것이다 - 맨 F12 와 Shift+F12 는 늘 1409(이미 등록됨)로 실패한다.
        // 조합키를 얹으면(Ctrl+F12 등) 된다. 실측으로 확인했다.
        (string Label, Key Key, ModifierKeys Modifiers, Action Action)[] bindings =
        [
            ("F5 1회", Key.F5, ModifierKeys.None, () => { if (IsIdle) DoRunOnce(); }),
            ("F6 반복/중지", Key.F6, ModifierKeys.None, ToggleLoop),
            ("F4 좌표 담기", Key.F4, ModifierKeys.None, PickCursorPosition),
            ("F3 대상 창 집기", Key.F3, ModifierKeys.None, PickWindowUnderCursor)
        ];

        var live = new List<string>();
        var failed = new List<string>();

        foreach (var (label, key, modifiers, action) in bindings)
        {
            if (_hotkeys.TryRegister(key, modifiers, action)) live.Add(label);
            else failed.Add(label);
        }

        // "다른 프로그램이 쥐고 있음" 이라고만 적었더니, Windows 가 예약한 키(F12)를
        // 못 쓰는 것도 남의 탓으로 읽혔다. 원인을 단정하지 않는다.
        HotkeyStatus = failed.Count == 0
            ? string.Join(" · ", live)
            : string.Join(" · ", live)
              + $"  (등록 실패: {string.Join(", ", failed)} - 다른 프로그램이 쥐고 있거나 Windows 가 예약한 키입니다)";

        Logger.Debug($"단축키 등록: 성공 {live.Count}, 실패 {failed.Count}");
    });

    /// <summary>F6 은 하나로 시작과 중지를 겸한다. 도는 중에 다시 누르면 멈춘다.</summary>
    private void ToggleLoop()
    {
        if (IsRunning) DoStop();
        else DoStartLoop();
    }

    /// <summary>
    /// 지금 커서 자리를 이동 줄로 만들어 끼운다.
    /// </summary>
    /// <remarks>
    /// 버튼으로 두면 쓸모가 없다. 버튼을 누르는 순간 커서가 그 버튼 위에 있기 때문이다.
    /// 대상 창 위에 커서를 둔 채 눌러야 하므로 단축키로만 제공한다.
    /// </remarks>
    private void PickCursorPosition() => Guard(() =>
    {
        if (IsRunning || _adapter is null) return;

        // 어댑터가 아니라 OS 에게 묻는다. 창 메시지 경로는 커서를 안 움직이므로
        // GetCursorPosition 이 null 인데, 좌표를 집는 것은 그것과 상관없는 일이다.
        if (ScreenCursor.TryGetPosition() is not { } p)
        {
            CurrentStep = "커서 자리를 읽지 못했다";
            return;
        }

        DoAddStep(SequenceStepKind.MoveTo);
        CurrentStep = $"좌표 담음 ({p.X}, {p.Y})";
    });

    private void DoRunOnce() => Start(loop: false);

    private void DoStartLoop() => Start(loop: true);

    private void DoStop() => Guard(() =>
    {
        _cts?.Cancel();
        CurrentStep = "중지 요청";
    });

    /// <summary>
    /// 시퀀스를 굳히고 돌리기 시작한다. 실제 전송은 <see cref="RunAsync"/> 가 백그라운드로 넘긴다.
    /// </summary>
    private void Start(bool loop) => Guard(() =>
    {
        if (_service is null || IsRunning) return;

        if (HasScriptError)
        {
            // 반쪽짜리 시퀀스가 나가는 것보다 안 나가는 것이 낫다.
            MessengerUtility.SendMainMessage("스크립트에 고칠 줄이 있습니다.");
            return;
        }

        var steps = BuildSequence().Steps;

        if (steps.Count == 0)
        {
            // 적은 것이 없는 것과, 적었는데 이 경로가 못 보내는 것은 다르다.
            // 뭉뚱그리면 사용자는 자기가 적은 것이 왜 안 나가는지 알 수 없다.
            MessengerUtility.SendMainMessage(PathWarning ?? "보낼 것이 하나도 적혀 있지 않습니다.");
            return;
        }

        _service.JitterMs = JitterMs;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        IsRunning = true;
        LoopCount = 0;

        // Progress<T> 는 만든 스레드(여기서는 UI)로 보고를 넘겨 준다.
        var progress = new Progress<string>(symbol => CurrentStep = symbol);

        _ = RunAsync(steps, loop, progress, _cts.Token);
    });

    /// <summary>
    /// 대기를 마치고 시퀀스를 돌린다. 돌리는 일은 스레드풀로 넘긴다.
    /// </summary>
    /// <remarks>
    /// <b>왜 Task.Run 인가</b>
    ///
    /// 이 메서드는 UI 스레드에서 시작되고, 안쪽 await 들이 UI 의 SynchronizationContext 를
    /// 잡는다. 그냥 두면 전송 전체가 UI 스레드에서 돈다. 검증 하네스는 일부러 Task.Run 으로
    /// 감싸 돌리는데, "실제 앱과 같은 조건" 이라고 적어 두고 정작 앱이 그렇지 않았다.
    ///
    /// <b>다만 이것은 눈에 보이는 버그를 고친 것이 아니다.</b> 단계 간격 1ms 로 26자를 보내는
    /// 조건에서 대상이 메모장이든 이 앱 자신의 입력란이든(= 보내는 스레드와 받는 창의 UI
    /// 스레드가 같은 경우) 유실은 없었다. 단계마다 await 이 있어 그 사이에 메시지 펌프가
    /// 도는 덕이다. UI 스레드가 무언가에 막혔을 때 전송 간격이 끌려가지 않도록 떼어 놓는,
    /// 예방에 가까운 변경이다.
    ///
    /// 대기(<see cref="CountDownAsync"/>)와 횟수 세기는 UI 스레드에 남겨 둔다 - 화면에 바로
    /// 비치는 것들이고 넘겨 봐야 득이 없다. 진행 보고는 <see cref="Progress{T}"/> 가 UI
    /// 컨텍스트를 잡아 두므로 그대로 UI 로 온다.
    ///
    /// 취소 토큰은 <see cref="Task.Run(Func{Task}, CancellationToken)"/> 에 넘기지 않는다.
    /// 시작 전에 이미 취소돼 있으면 그 오버로드는 예외를 던지는데, 여기서는 취소가 정상
    /// 경로다. <see cref="SequenceRunner"/> 가 토큰을 직접 보고 조용히 멈춘다.
    /// </remarks>
    private async Task RunAsync(
        System.Collections.Generic.IReadOnlyList<InputStep> steps,
        bool loop,
        IProgress<string> progress,
        CancellationToken token)
    {
        await GuardAsync(async () =>
        {
            try
            {
                if (!await CountDownAsync(token)) return;

                do
                {
                    var finished = await Task.Run(
                        () => SequenceRunner.RunOnceAsync(steps, IntervalMs, progress, _service!.Jitter, token));

                    if (!finished) break;

                    LoopCount++;
                }
                while (loop && !token.IsCancellationRequested && (MaxLoops <= 0 || LoopCount < MaxLoops));
            }
            finally
            {
                IsRunning = false;
                CurrentStep = token.IsCancellationRequested ? "중지함" : "끝남";
                MessengerUtility.SendMainMessage($"입력 자동화를 마쳤습니다. ({LoopCount}회)");
            }
        });
    }

    /// <summary>
    /// 시작 전 대기. 이 사이에 대상 창을 앞으로 가져와야 한다.
    /// 남은 시간을 표시해 주지 않으면 사용자가 언제 옮겨야 할지 알 수 없다.
    /// </summary>
    /// <returns>기다림을 마쳤으면 true, 중간에 취소되었으면 false.</returns>
    private async Task<bool> CountDownAsync(CancellationToken token)
    {
        for (var remain = StartDelaySeconds; remain > 0; remain--)
        {
            CurrentStep = $"{remain}초 뒤 시작 - 대상 창을 앞으로";

            try
            {
                await Task.Delay(1000, token);
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        return true;
    }
}
