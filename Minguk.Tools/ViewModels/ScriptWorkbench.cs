using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

using DevExpress.Mvvm;

using ICSharpCode.AvalonEdit.Highlighting;

using Minguk.Base.Utilities;
using Minguk.Tools.Helper;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Sequencing;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 스크립트 문서가 바깥에서 빌려 쓰는 것들 - 설정 저장소, 파일 대화 상자, UI 스레드.
/// </summary>
/// <remarks>
/// 문서는 화면(DocumentViewModelBase)이 아니라서 GetSetting 도 대화 상자 서비스도 없다.
/// 화면이 제 것을 이렇게 빌려 준다. 설정 키는 화면 이름 아래로 들어가므로 화면마다 따로 기억된다.
/// </remarks>
public sealed class ScriptWorkbenchHost
{
    public required Func<string, string, string> GetSetting { get; init; }

    public required Action<string, string> SetSetting { get; init; }

    /// <summary>UI 스레드에서 돌려 달라. 타이머 스레드에서 컴파일 결과를 화면에 놓을 때 쓴다.</summary>
    public required Action<Action> OnUi { get; init; }

    public Func<IOpenFileDialogService?>? OpenDialog { get; init; }

    public Func<ISaveFileDialogService?>? SaveDialog { get; init; }

    /// <summary>실시간 모드인가. 그러면 검사만 하고(돌리면 입력이 나간다) 계획은 만들지 않는다.</summary>
    public bool IsLive { get; init; }

    /// <summary>예·아니요·취소를 묻는다(저장 안 한 탭 닫기 등). 없으면 예로 본다.</summary>
    public Func<string, MessageButton, MessageResult>? Ask { get; init; }
}

/// <summary>
/// 스크립트 문서 하나 - 언어, 글, 파일, 컴파일 결과(계획·틀린 줄), 편집기 색.
/// </summary>
/// <remarks>
/// <b>왜 따로 두나</b> - 입력 자동화·스크립트·플레이 세 화면이 같은 일을 한다: 언어 고르기, 글 되살리기,
/// 타이핑이 멎으면 컴파일, 파일 열고 저장, 테마 따라 편집기 색 맞추기. 화면마다 베끼면 밑줄·완성 같은
/// 것이 한쪽에만 붙는다. 화면은 이것을 프로퍼티 하나로 들고 XAML 은 <c>Script.Text</c> 처럼 한 단계 들어가 묶는다.
///
/// 규칙은 입력 자동화 화면에서 그대로 가져왔다(그 화면은 아직 제 것을 들고 있다 - 옮기는 것이 다음 일).
///   - 언어마다 글을 따로 기억한다. 하나만 들면 파이썬을 골라 놓고 C# 글을 보게 된다(실제로 그랬다).
///   - 손대지 않은 본보기는 저장하지 않는다. 저장해 두면 본보기가 바뀌어도 옛것이 되살아난다.
///   - 글자 하나 칠 때마다 컴파일하지 않는다. Roslyn 은 컴파일마다 어셈블리를 만들고 그것은 안 풀린다.
///     타이핑이 멎은 뒤 500ms 에 한 번만.
///   - 틀린 줄이 있어도 나머지는 계획에 담는다. 오타 한 줄에 미리보기가 통째로 비면 무엇을 고칠지 오히려 모른다.
///     대신 실행은 부르는 쪽이 <see cref="HasError"/> 로 막는다.
/// </remarks>
public sealed class ScriptWorkbench : ViewModelBase, IDisposable
{
    /// <summary>타이핑이 멎기를 기다리는 시간.</summary>
    private const int DebounceMs = 500;

    private const string LanguageKey = "SelectedScriptLanguage";
    private static string TextKeyFor(ScriptLanguage language) => $"Script.{language}";
    private static string PathKeyFor(ScriptLanguage language) => $"ScriptPath.{language}";
    private static string DirtyKeyFor(ScriptLanguage language) => $"ScriptDirty.{language}";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ScriptWorkbenchHost _host;

    /// <summary>언어마다 따로 둔 글. 언어를 바꾸면 쓰던 글을 여기 넣어 두고 그 언어의 글을 꺼낸다.</summary>
    private readonly Dictionary<ScriptLanguage, (string Text, string? Path, bool Dirty)> _byLanguage = new();

    private IScriptEngine? _engine;

    /// <summary>지금 화면에 올라 있는 글이 어느 언어의 것인지. SetProperty 콜백은 예전 값을 안 알려 준다.</summary>
    private ScriptLanguage _shownLanguage;

    private Timer? _debounce;

    /// <summary>열어 둔 파일을 밖에서 고치면 다시 읽으려고 본다. 파일이 바뀌면(열기·다른 이름으로 저장·언어 전환) 갈아 끼운다.</summary>
    private System.IO.FileSystemWatcher? _watcher;

    /// <summary>
    /// 파일 알림을 묶는다. 편집기는 한 번 저장에 Changed 를 두세 번 내거나, 임시 파일에 쓰고 이름을 바꾼다 -
    /// 첫 알림에 읽으면 반쯤 쓴 파일이거나 아직 잠겨 있다.
    /// </summary>
    private Timer? _reloadDebounce;

    /// <summary>도는 동안 바뀌었다. 끝나면 읽는다 - 도중에 글을 갈면 무엇이 나갔는지 알 수 없다.</summary>
    private bool _reloadPending;

    private int _reloadRetries;

    private const int ReloadDebounceMs = 300;
    private const int ReloadMaxRetries = 5;

    private CancellationTokenSource? _compileCts;
    private bool _restoring;
    private bool _disposed;

    public ScriptWorkbench(ScriptWorkbenchHost host)
    {
        _host = host;

        Project = new ScriptProjectWorkspace(new ScriptProjectWorkspaceHost
        {
            OnUi = host.OnUi,
            Ask = host.Ask,
            OpenDialog = host.OpenDialog,
            SaveDialog = host.SaveDialog,
            Notify = message => MessengerUtility.SendMainMessage(message)
        });

        Project.EditorSettings = this;

        // 탭의 글·목록이 바뀌면 프로젝트 전체를 다시 검사한다. 완성·분류가 다른 스레드에서 읽을 글도 떠 둔다.
        Project.Changed += (_, _) =>
        {
            Project.SnapshotOpenTexts();
            ScheduleRecompile();
        };

        Project.ProjectChanged += (_, _) =>
        {
            // 프로젝트를 열면 빌드된 것은 잊는다 - 편집·검사는 소스로 한다.
            if (Project.IsOpen) Compiled = null;

            RaisePropertyChanged(nameof(IsProject));

            // 프로젝트는 C# 부터다. 언어가 다르면 맞춘다 - 파이썬 엔진으로 .csx 를 검사하면 오류만 가득 뜬다.
            if (Project.IsOpen && SelectedLanguage != ScriptLanguage.CSharp) SelectedLanguage = ScriptLanguage.CSharp;

            RefreshCompletionSource();
            ScheduleRecompile();
        };

        NewCommand = new DelegateCommand(DoNew, () => !IsLocked, false);
        OpenCommand = new DelegateCommand(DoOpen, () => !IsLocked, false);
        SaveCommand = new DelegateCommand(DoSave, () => true, false);
        SaveAsCommand = new DelegateCommand(DoSaveAs, () => true, false);

        // 테마가 바뀌면 편집기 색을 다시 잰다. 정적 이벤트라 Dispose 에서 반드시 푼다.
        DevExpress.Xpf.Core.LightweightThemeManager.CurrentThemeChanged += OnThemeChanged;
    }

    // ── 커맨드 ───────────────────────────────────────────────────────────

    public DelegateCommand NewCommand { get; }

    public DelegateCommand OpenCommand { get; }

    public DelegateCommand SaveCommand { get; }

    public DelegateCommand SaveAsCommand { get; }

    // ── 언어 ─────────────────────────────────────────────────────────────

    public ObservableCollection<ScriptLanguage> Languages { get; }
        = new((ScriptLanguage[])Enum.GetValues(typeof(ScriptLanguage)));

    /// <summary>무슨 언어로 쓸지. 언어가 달라도 부르는 것은 같다(<see cref="SequenceScriptApi"/>).</summary>
    public ScriptLanguage SelectedLanguage
    {
        get => GetProperty(() => SelectedLanguage);
        set => SetProperty(() => SelectedLanguage, value, OnLanguageChanged);
    }

    /// <summary>엔진이 준비되는 동안 무슨 일을 하는지. 파이썬은 처음에 11MB 를 받아 온다. 준비됐으면 null.</summary>
    public string? EngineStatus
    {
        get => GetProperty(() => EngineStatus);
        set => SetProperty(() => EngineStatus, value, () => RaisePropertyChanged(nameof(HasEngineStatus)));
    }

    /// <summary>엔진 줄을 보일지. 준비됐으면 자리를 차지하지 않는다.</summary>
    public bool HasEngineStatus => !string.IsNullOrEmpty(EngineStatus);

    /// <summary>이 언어의 본보기 글.</summary>
    public string SampleSource => EnsureEngine().SampleSource;

    /// <summary>지금 언어의 엔진. 실시간 실행은 이것으로 돌린다.</summary>
    public IScriptEngine Engine => EnsureEngine();

    /// <summary>실시간 모드인가.</summary>
    public bool IsLive => _host.IsLive;

    /// <summary>지금 언어의 완성. C# 은 Roslyn, 나머지는 null(편집기가 API 표로 돌아간다). 언어를 바꾸면 갈아 끼운다.</summary>
    public IScriptCompletionSource? CompletionSource
    {
        get => GetProperty(() => CompletionSource);
        private set => SetProperty(() => CompletionSource, value);
    }

    /// <summary>언어에 맞는 완성을 끼운다. C# 은 첫 호출이 느려 미리 한 번 부른다.</summary>
    private void RefreshCompletionSource()
    {
        CompletionSource = ScriptCompletionSourceFactory.Create(SelectedLanguage, _host.IsLive);

        if (CompletionSource is RoslynCompletionSource roslyn)
        {
            // 프로젝트 파일이면 같은 프로젝트의 다른 파일도 보게 한다.
            roslyn.UnitFor = Project is { } project ? project.UnitFor : null;
            _ = roslyn.WarmUpAsync();
        }
    }

    // ── 프로젝트 ─────────────────────────────────────────────────────────

    /// <summary>열린 프로젝트(탐색기·탭). 열려 있지 않으면 한 파일짜리 편집기로 돈다.</summary>
    public ScriptProjectWorkspace Project { get; }

    public bool IsProject => Project?.IsOpen == true;

    // ── 빌드된 것(플레이 전용) ────────────────────────────────────────────

    /// <summary>
    /// 로드해 둔 빌드 결과물(<c>.mtsx</c>, IL). 소스가 없어 편집·검사를 안 하고 실행만 한다. 플레이 화면에서만 채운다.
    /// </summary>
    public CompiledPlayable? Compiled
    {
        get => GetProperty(() => Compiled);
        private set => SetProperty(() => Compiled, value, () => RaisePropertyChanged(nameof(IsCompiled)));
    }

    /// <summary>빌드된 것을 로드해 둔 상태인가. 그러면 실행은 IL 을 돌린다(<see cref="LiveScriptSession"/>).</summary>
    public bool IsCompiled => Compiled is not null;

    /// <summary>빌드 결과물을 읽어 든다. 소스로 열지 않는다 - 편집기에 든 것은 IL 이라 글이 아니다.</summary>
    public void LoadCompiled(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));

        Compiled = new CompiledPlayable(bytes, root, System.IO.Path.GetFileNameWithoutExtension(path));

        Logger.Info($"빌드된 스크립트를 로드했다: {path} ({bytes.Length:N0}바이트)");
    }

    /// <summary>한 파일짜리 편집기에 "이 줄로" 요청(오류 목록·참조 창).</summary>
    public Markup.EditorLineRequest? LineRequest
    {
        get => GetProperty(() => LineRequest);
        set => SetProperty(() => LineRequest, value);
    }

    /// <summary>
    /// 편집기가 올린 "이 파일의 이 줄로"(참조 창 더블 클릭). 프로젝트 파일이면 그 탭을 열어 가고, 아니면 한 파일짜리 편집기 안에서 간다.
    /// </summary>
    /// <remarks>탭 편집기의 데이터 문맥은 문서고 공용 설정이 이 워크벤치라, 명령도 여기 둔다 - 화면 VM 까지 거슬러 오르면 떠 있는 창에서 끊긴다.</remarks>
    public DelegateCommand<Markup.EditorNavigation> NavigateCommand => _navigateCommand ??= new DelegateCommand<Markup.EditorNavigation>(target =>
    {
        if (target is null || target.Line <= 0) return;

        if (IsProject && !string.IsNullOrEmpty(target.FilePath) && System.IO.File.Exists(target.FilePath))
        {
            Project.OpenFile(target.FilePath).GoToLine(target.Line);
            return;
        }

        LineRequest = new Markup.EditorLineRequest(target.Line);
    });

    private DelegateCommand<Markup.EditorNavigation>? _navigateCommand;

    private const string ProjectPathKey = "ScriptProjectPath";
    private const string ProjectDocumentsKey = "ScriptProjectDocuments";
    private const string ProjectActiveKey = "ScriptProjectActive";

    private void ScheduleRecompile()
    {
        _debounce?.Dispose();
        _debounce = new Timer(_ => _host.OnUi(() => _ = RecompileAsync()), null, DebounceMs, Timeout.Infinite);
    }

    /// <summary>지난번 프로젝트와 열어 둔 탭을 되살린다. 파일이 없어졌으면 조용히 건너뛴다.</summary>
    private void RestoreProject()
    {
        var path = _host.GetSetting(ProjectPathKey, string.Empty);
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

        try
        {
            Project.OpenProject(path);

            foreach (var document in _host.GetSetting(ProjectDocumentsKey, string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries))
                if (System.IO.File.Exists(document)) Project.OpenFile(document);

            var active = _host.GetSetting(ProjectActiveKey, string.Empty);
            if (Project.Documents.FirstOrDefault(d => string.Equals(d.FilePath, active, StringComparison.OrdinalIgnoreCase)) is { } doc)
                Project.ActiveDocument = doc;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"지난번 프로젝트를 못 열었다: {path}");
        }
    }

    private void SaveProjectSettings()
    {
        _host.SetSetting(ProjectPathKey, Project.Project?.FilePath ?? string.Empty);
        _host.SetSetting(ProjectDocumentsKey, string.Join('|', Project.Documents.Select(d => d.FilePath)));
        _host.SetSetting(ProjectActiveKey, Project.ActiveDocument?.FilePath ?? string.Empty);
    }

    // ── 글과 파일 ────────────────────────────────────────────────────────

    /// <summary>편집기에 든 글. <b>이것이 원본이다</b> - 계획은 여기서 읽어 낸다.</summary>
    public string? Text
    {
        get => GetProperty(() => Text);
        set => SetProperty(() => Text, value, OnTextChanged);
    }

    /// <summary>지금 열어 둔 파일. 아직 저장한 적 없으면 null.</summary>
    public string? FilePath
    {
        get => GetProperty(() => FilePath);
        set => SetProperty(() => FilePath, value, () =>
        {
            RaisePropertyChanged(nameof(FileLabel));
            WatchFile();
        });
    }

    /// <summary>글이 마지막으로 저장된 뒤 바뀌었는지. "파일과 지금 글이 다르다" 는 뜻이지 잃는다는 경고가 아니다.</summary>
    public bool IsDirty
    {
        get => GetProperty(() => IsDirty);
        set => SetProperty(() => IsDirty, value, () => RaisePropertyChanged(nameof(FileLabel)));
    }

    /// <summary>화면에 보여 줄 파일 이름. 안 바뀐 것과 바뀐 것을 * 로 가른다.</summary>
    public string FileLabel
    {
        get
        {
            // 열어 둔 파일이 없으면 * 를 안 붙인다. 견줄 파일이 없는데 "다르다" 고 할 수 없다.
            if (string.IsNullOrEmpty(FilePath)) return "(저장 안 함)";

            var name = System.IO.Path.GetFileName(FilePath);

            return IsDirty ? name + " *" : name;
        }
    }

    /// <summary>도는 동안 잠근다. 도중에 글이 바뀌면 무엇이 나갔는지 알 수 없다.</summary>
    public bool IsLocked
    {
        get => GetProperty(() => IsLocked);
        set => SetProperty(() => IsLocked, value, () =>
        {
            RaisePropertyChanged(nameof(IsEditable));
            NewCommand.RaiseCanExecuteChanged();
            OpenCommand.RaiseCanExecuteChanged();

            if (!value && _reloadPending) ReloadFromDisk();
        });
    }

    public bool IsEditable => !IsLocked;

    // ── 컴파일 결과 ──────────────────────────────────────────────────────

    /// <summary>글에서 읽어 낸 계획. 실행할 때 이것으로 시퀀스를 만든다.</summary>
    public SequencePlan Plan { get; private set; } = new();

    /// <summary>틀린 줄들. 없으면 빈 목록. 편집기가 이것으로 빨간 밑줄을 긋는다.</summary>
    public IReadOnlyList<ScriptError> Errors
    {
        get => GetProperty(() => Errors) ?? [];
        private set => SetProperty(() => Errors, value);
    }

    /// <summary>틀린 줄들을 한 번에 모아 둔 글. 없으면 null.</summary>
    public string? ErrorText
    {
        get => GetProperty(() => ErrorText);
        set => SetProperty(() => ErrorText, value, () => RaisePropertyChanged(nameof(HasError)));
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>계획이 새로 나왔다. 순서 미리보기 같은 것을 다시 그릴 때.</summary>
    public event EventHandler? PlanChanged;

    // ── 편집기 색 ────────────────────────────────────────────────────────
    //    AvalonEdit 은 순수 WPF 컨트롤이라 DevExpress 경량 테마가 손대지 않는다.
    //    테마 팔레트에서 실제 색을 재 와 여기 넣는다(ApplyEditorTheme).

    public IHighlightingDefinition? Highlighting { get => GetProperty(() => Highlighting); set => SetProperty(() => Highlighting, value); }

    public Brush? EditorBackground { get => GetProperty(() => EditorBackground); set => SetProperty(() => EditorBackground, value); }

    public Brush? EditorForeground { get => GetProperty(() => EditorForeground); set => SetProperty(() => EditorForeground, value); }

    public Brush? EditorLineNumberForeground { get => GetProperty(() => EditorLineNumberForeground); set => SetProperty(() => EditorLineNumberForeground, value); }

    public Brush? EditorBorder { get => GetProperty(() => EditorBorder); set => SetProperty(() => EditorBorder, value); }

    /// <summary>편집기 색을 지금 테마에 맞춘다. 화면이 뜰 때와 테마가 바뀔 때.</summary>
    public void ApplyEditorTheme()
    {
        try
        {
            EditorBackground = SequenceScriptHighlighting.Background;
            EditorForeground = SequenceScriptHighlighting.Foreground;
            EditorLineNumberForeground = SequenceScriptHighlighting.LineNumberForeground;
            EditorBorder = SequenceScriptHighlighting.Border;
            Highlighting = SequenceScriptHighlighting.Current;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "편집기 색을 못 맞췄다");
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _host.OnUi(ApplyEditorTheme);

    // ── 설정 저장·복원 ───────────────────────────────────────────────────

    /// <summary>지난번 언어와 언어별 글을 되살린다. 화면의 RestoreSettings 에서 부른다.</summary>
    public void Restore()
    {
        _restoring = true;

        try
        {
            SelectedLanguage = Enum.TryParse<ScriptLanguage>(_host.GetSetting(LanguageKey, nameof(ScriptLanguage.CSharp)), out var language)
                ? language
                : ScriptLanguage.CSharp;

            _engine?.Dispose();
            _engine = ScriptEngineFactory.Create(SelectedLanguage);
            _shownLanguage = SelectedLanguage;
            RefreshCompletionSource();

            foreach (var each in Languages)
            {
                var text = _host.GetSetting(TextKeyFor(each), string.Empty);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var path = _host.GetSetting(PathKeyFor(each), string.Empty);

                // 파일과 같던 글이면 파일을 믿는다 - 꺼져 있는 동안 밖에서 고쳤을 수 있다.
                // 표시가 없는 옛 설정은 고친 채 닫았는지 모르므로 설정의 글을 그대로 둔다.
                if (!string.IsNullOrEmpty(path)
                    && bool.TryParse(_host.GetSetting(DirtyKeyFor(each), string.Empty), out var dirty) && !dirty
                    && TryReadFile(path) is { } fileText)
                {
                    text = fileText;
                }

                _byLanguage[each] = (text, string.IsNullOrEmpty(path) ? null : path, false);
            }

            if (_byLanguage.TryGetValue(SelectedLanguage, out var shown))
            {
                Text = shown.Text;
                FilePath = shown.Path;
            }
            else
            {
                Text = _engine.SampleSource;
                FilePath = null;
            }

            // 되살린 글은 아직 아무것도 안 고친 상태다.
            IsDirty = false;
        }
        finally
        {
            _restoring = false;
        }

        if (_host.IsLive) RestoreProject();
    }

    /// <summary>언어와 언어별 글을 저장한다. 화면의 SaveSettings 에서 부른다.</summary>
    public void Save()
    {
        _host.SetSetting(LanguageKey, SelectedLanguage.ToString());

        StashShown();

        foreach (var (language, script) in _byLanguage)
        {
            _host.SetSetting(TextKeyFor(language), script.Text);
            _host.SetSetting(PathKeyFor(language), script.Path ?? string.Empty);
            _host.SetSetting(DirtyKeyFor(language), script.Dirty.ToString());
        }

        if (_host.IsLive) SaveProjectSettings();
    }

    // ── 언어 바꾸기 ──────────────────────────────────────────────────────

    /// <summary>
    /// 언어를 갈아 끼운다. 쓰던 글은 제 언어 칸에 넣어 두고, 그 언어로 쓰던 글이 있으면 그것을, 없으면 본보기를 올린다.
    /// </summary>
    private void OnLanguageChanged()
    {
        if (_restoring || _shownLanguage == SelectedLanguage) return;

        StashShown();

        _engine?.Dispose();
        _engine = ScriptEngineFactory.Create(SelectedLanguage);
        _shownLanguage = SelectedLanguage;
        RefreshCompletionSource();

        if (_byLanguage.TryGetValue(SelectedLanguage, out var kept))
        {
            Text = kept.Text;
            FilePath = kept.Path;
            IsDirty = kept.Dirty;
        }
        else
        {
            Text = _engine.SampleSource;
            FilePath = null;
            IsDirty = false;
        }

        _ = PrepareAsync();
    }

    /// <summary>화면의 글을 지금 언어 칸에 넣는다. 손대지 않은 본보기는 넣지 않는다.</summary>
    private void StashShown()
    {
        var text = Text ?? string.Empty;
        var untouchedSample = string.IsNullOrEmpty(FilePath) && IsSame(text, _engine?.SampleSource);

        if (string.IsNullOrWhiteSpace(text) || untouchedSample)
        {
            _byLanguage.Remove(_shownLanguage);
            return;
        }

        _byLanguage[_shownLanguage] = (text, FilePath, IsDirty);
    }

    /// <summary>두 글이 같은 글인지. 편집기를 거치면 줄 끝이 바뀔 수 있어 == 로는 손대지 않은 글도 달라 보인다.</summary>
    private static bool IsSame(string? left, string? right)
    {
        if (left is null || right is null) return false;

        return Normalize(left) == Normalize(right);

        static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private IScriptEngine EnsureEngine()
    {
        if (_engine is null)
        {
            _engine = ScriptEngineFactory.Create(SelectedLanguage);
            _shownLanguage = SelectedLanguage;
            RefreshCompletionSource();
        }

        return _engine;
    }

    // ── 컴파일 ───────────────────────────────────────────────────────────

    private void OnTextChanged()
    {
        if (_restoring) return;

        // 파일과 지금 글이 다르다는 표시. 되읽기 전에 세워 둔다 - 되읽기는 뒤늦게 끝난다.
        IsDirty = true;

        _debounce?.Dispose();
        _debounce = new Timer(_ => _host.OnUi(() => _ = RecompileAsync()), null, DebounceMs, Timeout.Infinite);
    }

    /// <summary>엔진을 준비시키고, 끝나면 한 번 돌려 계획을 채운다. 첫 준비가 유독 느리다 - 화면이 뜰 때 미리 치른다.</summary>
    public async Task PrepareAsync()
    {
        var engine = EnsureEngine();
        var progress = new Progress<string>(message => EngineStatus = message);

        try
        {
            await engine.PrepareAsync(progress);

            EngineStatus = engine.IsReady ? null : engine.UnavailableReason;

            if (ReferenceEquals(engine, _engine)) await RecompileAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "스크립트 엔진을 준비하지 못했다");
            EngineStatus = ex.Message;
        }
    }

    /// <summary>글을 돌려 계획을 받아 온다. 실제 입력은 나가지 않는다 - 단계로 적힐 뿐이다.</summary>
    public async Task RecompileAsync()
    {
        if (_disposed) return;

        var engine = EnsureEngine();

        // 앞선 것이 아직 돌고 있으면 접는다. 마지막 글만 의미가 있다.
        _compileCts?.Cancel();
        _compileCts?.Dispose();
        _compileCts = new CancellationTokenSource();

        var token = _compileCts.Token;
        var source = Text;

        try
        {
            SequencePlan plan;
            IReadOnlyList<ScriptError> errors;

            if (_host.IsLive && IsProject)
            {
                // 프로젝트는 시작 파일 하나가 아니라 전체를 검사한다. 오류에는 파일이 붙어 탭마다 제 것만 긋는다.
                errors = engine is IProjectScriptEngine projectEngine && Project.ToUnit() is { } unit
                    ? await projectEngine.CheckLiveAsync(unit, token)
                    : [new ScriptError(0, $"{engine.Name} 은(는) 여러 파일짜리 프로젝트를 아직 돌리지 못합니다. 프로젝트는 C# 으로 씁니다.")];
                plan = new SequencePlan();
            }
            else if (_host.IsLive)
            {
                // 실시간 모드는 검사만. 돌리면 입력이 나간다.
                errors = await engine.CheckLiveAsync(source, token);
                plan = new SequencePlan();
            }
            else
            {
                (plan, errors) = await engine.RunAsync(source, token);
            }

            if (token.IsCancellationRequested) return;

            Plan = plan;
            Errors = errors;
            ErrorText = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors.Select(e => e.ToString()));

            PlanChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // 더 새 글이 들어왔다는 뜻이다. 그쪽이 결과를 낸다.
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "스크립트를 읽지 못했다");
            ErrorText = ex.Message;
        }
    }

    // ── 파일 ─────────────────────────────────────────────────────────────

    /// <summary>본보기 글로 새로 시작한다. 파일과의 연결도 끊는다.</summary>
    private void DoNew()
    {
        Text = SampleSource;
        FilePath = null;
        IsDirty = false;
    }

    /// <summary>
    /// 파일을 읽어 글을 갈아 끼운다. 확장자로 언어까지 맞춘다.
    /// </summary>
    /// <remarks>
    /// 언어를 먼저 바꾸고 글을 넣는다. 순서가 반대면 새 글을 예전 언어로 한 번 돌려 헛된 오류가 스쳤다 사라진다.
    /// </remarks>
    public void LoadFile(string path)
    {
        if (ScriptFiles.IsCompiledPath(path))
        {
            MessengerUtility.SendMainMessage("빌드 결과물(.mtsx)은 편집할 수 없습니다 - 플레이 화면에서 실행만 합니다. 고치려면 원본 프로젝트를 여세요.");
            return;
        }

        // 소스를 열면 빌드된 것은 잊는다.
        Compiled = null;

        var text = System.IO.File.ReadAllText(path);

        if (ScriptFiles.FromPath(path) is { } language && language != SelectedLanguage)
            SelectedLanguage = language;

        Text = text;
        FilePath = path;
        IsDirty = false;

        Logger.Info($"스크립트를 열었다: {path}");
    }

    private void DoOpen()
    {
        if (_host.OpenDialog?.Invoke() is not { } dialog)
        {
            MessengerUtility.SendMainMessage("파일 열기 서비스를 찾지 못했습니다.");
            return;
        }

        dialog.Filter = ScriptFiles.OpenFilter(SelectedLanguage);
        dialog.InitialDirectory = ScriptFiles.DefaultDirectory;

        if (!dialog.ShowDialog()) return;

        var path = dialog.File.GetFullName();

        LoadFile(path);
        MessengerUtility.SendMainMessage($"{System.IO.Path.GetFileName(path)} 을(를) 열었습니다.");
    }

    /// <summary>저장한다. 아직 자리를 안 정했으면 물어본다.</summary>
    private void DoSave()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            DoSaveAs();
            return;
        }

        Write(FilePath);
    }

    private void DoSaveAs()
    {
        if (_host.SaveDialog?.Invoke() is not { } dialog)
        {
            MessengerUtility.SendMainMessage("파일 저장 서비스를 찾지 못했습니다.");
            return;
        }

        dialog.Filter = ScriptFiles.SaveFilter(SelectedLanguage);
        dialog.DefaultExt = ScriptFiles.Extension(SelectedLanguage).TrimStart('.');
        dialog.DefaultFileName = string.IsNullOrEmpty(FilePath)
            ? "스크립트" + ScriptFiles.Extension(SelectedLanguage)
            : System.IO.Path.GetFileName(FilePath);
        dialog.InitialDirectory = ScriptFiles.DefaultDirectory;

        if (!dialog.ShowDialog()) return;

        Write(dialog.File.GetFullName());
    }

    private void Write(string path)
    {
        // UTF-8 로 쓴다. 한글 이름을 쓸 수 있게 해 놓고 ANSI 로 쓰면 다른 PC 에서 깨진다.
        System.IO.File.WriteAllText(path, Text ?? string.Empty, new System.Text.UTF8Encoding(false));

        FilePath = path;
        IsDirty = false;

        Logger.Info($"스크립트를 저장했다: {path}");
        MessengerUtility.SendMainMessage($"{System.IO.Path.GetFileName(path)} 에 저장했습니다.");
    }

    // ── 밖에서 고친 파일 다시 읽기 ───────────────────────────────────────

    /// <summary>지금 파일을 보기 시작한다. 파일이 없으면(저장 안 한 글) 보던 것만 놓는다.</summary>
    private void WatchFile()
    {
        _watcher?.Dispose();
        _watcher = null;
        _reloadPending = false;

        if (_disposed || string.IsNullOrEmpty(FilePath)) return;

        var directory = System.IO.Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory)) return;

        try
        {
            var watcher = new System.IO.FileSystemWatcher(directory, System.IO.Path.GetFileName(FilePath))
            {
                NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.Size | System.IO.NotifyFilters.FileName,
            };

            // 임시 파일에 쓰고 이름을 바꾸는 편집기(VS Code·VS)는 Changed 가 아니라 Renamed·Created 로 온다.
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"스크립트 파일을 지켜보지 못했다: {FilePath}");
        }
    }

    /// <summary>감시 스레드. 여기서는 읽지 않고 묶기만 한다.</summary>
    private void OnFileEvent(object sender, System.IO.FileSystemEventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _watcher)) return;

        _reloadRetries = 0;
        ScheduleReload();
    }

    private void ScheduleReload()
    {
        if (_disposed) return;

        _reloadDebounce ??= new Timer(_ => _host.OnUi(ReloadFromDisk), null, Timeout.Infinite, Timeout.Infinite);
        _reloadDebounce.Change(ReloadDebounceMs, Timeout.Infinite);
    }

    /// <summary>
    /// 파일을 다시 읽어 글을 갈아 끼운다. UI 스레드.
    /// </summary>
    /// <remarks>
    /// 여기서 고친 것이 있으면(<see cref="IsDirty"/>) 덮어쓰지 않는다 - 조용히 날리면 되돌릴 길이 없다.
    /// 우리가 저장해서 온 알림은 글이 같으므로 아무 일도 안 한다.
    /// </remarks>
    private void ReloadFromDisk()
    {
        if (_disposed || string.IsNullOrEmpty(FilePath)) return;

        if (IsLocked)
        {
            _reloadPending = true;
            return;
        }

        _reloadPending = false;

        var path = FilePath;

        // 지우고 새로 쓰는 편집기는 잠깐 파일이 없다. 다시 생기면 Created 가 또 부른다.
        if (!System.IO.File.Exists(path)) return;

        string text;
        try
        {
            text = ReadShared(path);
        }
        catch (System.IO.IOException ex)
        {
            if (++_reloadRetries <= ReloadMaxRetries)
            {
                ScheduleReload();
                return;
            }

            Logger.Warn(ex, $"밖에서 바뀐 스크립트를 읽지 못했다: {path}");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, $"밖에서 바뀐 스크립트를 읽지 못했다: {path}");
            return;
        }

        var name = System.IO.Path.GetFileName(path);

        if (IsSame(text, Text))
        {
            // 파일과 글이 같아졌다 - 우리가 저장했거나, 밖에서 같은 내용으로 맞췄다.
            if (IsDirty) IsDirty = false;
            return;
        }

        if (IsDirty)
        {
            Logger.Info($"스크립트가 밖에서 바뀌었지만 여기서 고친 것이 있어 안 읽었다: {path}");
            MessengerUtility.SendMainMessage($"{name} 이(가) 밖에서 바뀌었지만 여기서 고친 것이 있어 불러오지 않았습니다. 버리려면 다시 여세요.");
            return;
        }

        Text = text;
        IsDirty = false;

        Logger.Info($"밖에서 바뀐 스크립트를 다시 읽었다: {path}");
        MessengerUtility.SendMainMessage($"밖에서 바뀐 {name} 을(를) 다시 불러왔습니다.");
    }

    /// <summary>쓰는 쪽이 아직 쥐고 있어도 읽을 수 있게 공유를 넓게 연다.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
            System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
        using var reader = new System.IO.StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }

    /// <summary>못 읽으면(없거나 잠김) null.</summary>
    private static string? TryReadFile(string path)
    {
        try
        {
            return System.IO.File.Exists(path) ? ReadShared(path) : null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Logger.Warn(ex, $"스크립트 파일을 읽지 못했다: {path}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DevExpress.Xpf.Core.LightweightThemeManager.CurrentThemeChanged -= OnThemeChanged;

        _debounce?.Dispose();
        _debounce = null;

        _watcher?.Dispose();
        _watcher = null;

        _reloadDebounce?.Dispose();
        _reloadDebounce = null;

        _compileCts?.Cancel();
        _compileCts?.Dispose();
        _compileCts = null;

        _engine?.Dispose();
        _engine = null;

        Project.Dispose();
    }
}
