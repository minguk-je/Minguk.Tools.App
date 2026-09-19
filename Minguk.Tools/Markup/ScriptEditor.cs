using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

using Minguk.Tools.Input.Scripting;

namespace Minguk.Tools.Markup;

/// <summary>
/// 스크립트 편집기. <see cref="TextEditor"/> 에 얹은 것 - 자동화 피어, 틀린 줄의 빨간 밑줄, 코드 완성, 중단점 여백, 멈춘 줄 칠하기.
/// </summary>
/// <remarks>
/// <b>자동화 피어</b> - AvalonEdit 을 그냥 얹었더니 <b>창 전체의 UI 자동화 트리가 비었다.</b> 실측으로
/// 갈렸다 - 같은 화면에서 버튼이 그리드 시절에는 37개로 보였는데 편집기를 넣은 뒤로는 0개가 됐다.
/// 원인은 AvalonEdit 이 제 몫으로 만드는 피어다. 그 피어가 트리를 훑는 도중에 막히면 UIA 는 그 위쪽
/// 가지를 통째로 버린다. 그래서 평범한 <see cref="FrameworkElementAutomationPeer"/> 로 갈아 끼운다.
/// 스크린 리더에도 같은 문제라 검증 도구만의 일이 아니다.
///
/// <b>빨간 밑줄</b> - 엔진이 준 <see cref="ScriptError"/>(줄 번호)를 <see cref="Errors"/> 로 받아 그 줄 아래에
/// 물결선을 그린다. 화면 아래 "고칠 줄" 칸에 문장이 뜨긴 하지만 눈은 편집기에 가 있다 - 몇 번째 줄인지 세어
/// 찾게 하는 것보다 그 줄에 표시가 있는 편이 빠르다. 마우스를 올리면 문장이 풍선으로 뜬다.
/// 배경 렌더러(<see cref="IBackgroundRenderer"/>)로 그린다 - 글자 뒤에 그려지므로 글을 가리지 않는다.
///
/// <b>코드 완성</b> - 낱말을 치기 시작하면 <see cref="ScriptApiCatalog"/> 의 이름(영문·한글)이 목록으로 뜬다.
/// 세 언어가 같은 이름을 쓰므로 목록도 하나다. 언어 고유 문법(for, def, let)까지는 안 한다 - C# 전체 완성은
/// Roslyn Features 패키지가 수십 MB 라 따로 결정할 일이다(docs/스크립트-설계.md 4단계).
/// Ctrl+Space 로도 연다. 고르면 이름만 넣는다 - 괄호까지 넣으면 이미 친 괄호와 겹친다.
///
/// <b>중단점과 멈춘 줄</b> - 왼쪽 여백(<see cref="BreakpointMargin"/>)을 누르면 그 줄이 <see cref="Breakpoints"/> 에
/// 들고 나며 빨간 점이 찍힌다. 컬렉션은 디버그 세션 것이라 편집기가 직접 고친다 - 커맨드로 올렸다 내리면 줄 번호를
/// 두 번 옮겨 적게 된다. <see cref="CurrentLine"/> 이 0 이 아니면 그 줄을 노랗게 칠하고 보이는 자리로 굴린다.
/// </remarks>
public sealed class ScriptEditor : TextEditor
{
    /// <summary>밑줄 색. 화면의 "고칠 줄" 글자색과 같다.</summary>
    public static readonly Color UnderlineColor = Color.FromRgb(0xD1, 0x24, 0x2F);

    /// <summary>중단점 점 색.</summary>
    public static readonly Color BreakpointColor = Color.FromRgb(0xE5, 0x14, 0x00);

    /// <summary>멈춘 줄 바탕색. 반투명이라 글이 그대로 보인다.</summary>
    public static readonly Color CurrentLineColor = Color.FromArgb(0x66, 0xFF, 0xE0, 0x66);

    public static readonly DependencyProperty ErrorsProperty = DependencyProperty.Register(
        nameof(Errors), typeof(IReadOnlyList<ScriptError>), typeof(ScriptEditor),
        new PropertyMetadata(null, (d, _) => ((ScriptEditor)d).OnErrorsChanged()));

    public static readonly DependencyProperty BreakpointsProperty = DependencyProperty.Register(
        nameof(Breakpoints), typeof(ObservableCollection<int>), typeof(ScriptEditor),
        new PropertyMetadata(null, (d, e) => ((ScriptEditor)d).OnBreakpointsChanged(e)));

    public static readonly DependencyProperty CompletionSourceProperty = DependencyProperty.Register(
        nameof(CompletionSource), typeof(IScriptCompletionSource), typeof(ScriptEditor),
        new PropertyMetadata(null, (d, _) => ((ScriptEditor)d).ScheduleClassify(immediately: true)));

    /// <summary>분류를 다시 돌리기까지 기다리는 시간. 치는 동안에는 xshd 색으로 버틴다.</summary>
    private const int ClassifyDelayMs = 250;

    private readonly SemanticColorizer _semantic = new();
    private readonly DispatcherTimer _classifyTimer;
    private CancellationTokenSource? _classifyCts;

    public static readonly DependencyProperty CurrentLineProperty = DependencyProperty.Register(
        nameof(CurrentLine), typeof(int), typeof(ScriptEditor),
        new PropertyMetadata(0, (d, _) => ((ScriptEditor)d).OnCurrentLineChanged()));

    private readonly ErrorUnderlineRenderer _underline;
    private readonly CurrentLineRenderer _currentLine;
    private readonly BreakpointMargin _margin;
    private readonly ToolTip _errorTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private CompletionWindow? _completion;
    private CancellationTokenSource? _completionCts;

    public ScriptEditor()
    {
        _underline = new ErrorUnderlineRenderer(this);
        _currentLine = new CurrentLineRenderer(this);
        TextArea.TextView.BackgroundRenderers.Add(_currentLine);
        TextArea.TextView.BackgroundRenderers.Add(_underline);

        // 선택은 우리가 그린다 - 참조 표시가 있는 줄은 키가 두 줄이라, AvalonEdit 기본 선택은 참조 글자까지 덮었다(사용자, 2026-09-19).
        TextArea.TextView.BackgroundRenderers.Add(new SelectionRenderer(TextArea, TextArea.SelectionBrush));
        TextArea.SelectionBrush = Brushes.Transparent;
        TextArea.SelectionBorder = null;

        _margin = new BreakpointMargin(this);
        TextArea.LeftMargins.Insert(0, _margin);

        // 컴파일러 분류로 덧칠한다. xshd 색칠기(맨 앞)보다 뒤에 있어야 이긴다.
        TextArea.TextView.LineTransformers.Add(_semantic);

        // 선언 줄 위의 "참조 N개".
        TextArea.TextView.ElementGenerators.Add(_codeLens);
        _codeLens.Clicked += OnCodeLensClicked;
        _classifyTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(ClassifyDelayMs), DispatcherPriority.Background, (_, _) => RunClassify(), Dispatcher);
        _classifyTimer.Stop();
        TextChanged += (_, _) => ScheduleClassify(immediately: false);
        DocumentChanged += (_, _) => { _semantic.Clear(); _codeLens.Clear(); ScheduleClassify(immediately: true); };
        Unloaded += (_, _) => { _classifyTimer.Stop(); _classifyCts?.Cancel(); };

        TextArea.Caret.PositionChanged += OnCaretMoved;
        TextArea.TextEntered += OnTextEntered;
        TextArea.TextEntering += OnTextEntering;
        MouseHover += OnMouseHover;
        MouseHoverStopped += (_, _) => _errorTip.IsOpen = false;
        PreviewKeyDown += OnPreviewKeyDown;

        // 붙여 넣은 것도 줄을 맞춘다. AvalonEdit 은 엔터만 맞추고 붙여 넣기는 그대로 둔다.
        DataObject.AddPastingHandler(this, OnPasting);

        // 언어가 한 번도 안 바뀌면 바뀜 알림이 안 오므로 여기서 한 번 건다.
        ApplyIndentation();
    }

    /// <summary>
    /// 줄 맞추기(들여쓰기) 규칙. 언어마다 다르다.
    /// </summary>
    /// <remarks>
    /// <b>파이썬은 손대면 안 된다.</b> 다른 언어에서 들여쓰기는 보기 좋으라고 있는 것이지만 파이썬에서는
    /// <b>문법</b>이다 - 중괄호 규칙으로 다시 맞추면 남의 코드를 붙여 넣는 순간 뜻이 바뀐다.
    /// 그래서 파이썬은 앞 줄을 따라가는 기본 규칙만 쓰고 붙여 넣기도 손대지 않는다.
    ///
    /// C#·자바스크립트는 둘 다 중괄호라 같은 규칙을 쓴다.
    /// </remarks>
    public ScriptLanguage Language
    {
        get => (ScriptLanguage)GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    public static readonly DependencyProperty LanguageProperty = DependencyProperty.Register(
        nameof(Language), typeof(ScriptLanguage), typeof(ScriptEditor),
        new PropertyMetadata(ScriptLanguage.CSharp, OnLanguageChanged));

    private static void OnLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScriptEditor editor) return;

        editor.ApplyIndentation();
        editor.ScheduleClassify(immediately: true);
    }

    // ── 컴파일러 분류로 칠하기 ───────────────────────────────────────────

    // ── 캐럿 줄·열, 줄로 가기 ────────────────────────────────────────────

    public static readonly DependencyProperty CaretLineProperty = DependencyProperty.Register(
        nameof(CaretLine), typeof(int), typeof(ScriptEditor), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CaretColumnProperty = DependencyProperty.Register(
        nameof(CaretColumn), typeof(int), typeof(ScriptEditor), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>캐럿 줄(1부터). 캐럿이 움직이면 편집기가 넣는다 - 상태 표시줄이 본다.</summary>
    public int CaretLine
    {
        get => (int)GetValue(CaretLineProperty);
        set => SetValue(CaretLineProperty, value);
    }

    public int CaretColumn
    {
        get => (int)GetValue(CaretColumnProperty);
        set => SetValue(CaretColumnProperty, value);
    }

    public static readonly DependencyProperty LineRequestProperty = DependencyProperty.Register(
        nameof(LineRequest), typeof(EditorLineRequest), typeof(ScriptEditor),
        new PropertyMetadata(null, (d, e) => ((ScriptEditor)d).OnLineRequested(e.NewValue as EditorLineRequest)));

    /// <summary>이 줄로 캐럿을 옮기고 보이게 굴린다(오류 목록·참조 창에서). 매번 새 객체가 온다.</summary>
    public EditorLineRequest? LineRequest
    {
        get => (EditorLineRequest?)GetValue(LineRequestProperty);
        set => SetValue(LineRequestProperty, value);
    }

    private void OnLineRequested(EditorLineRequest? request)
    {
        if (request is null || Document.LineCount == 0) return;

        var line = Math.Clamp(request.Line, 1, Document.LineCount);

        // 창에 붙기 전에 오면(탭을 막 연 참) 붙은 뒤에 한다 - 그 전에는 굴릴 자리가 없다.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            TextArea.Caret.Position = new TextViewPosition(line, 1);
            ScrollToLine(line);
            TextArea.Focus();
        }));
    }

    private void OnCaretMoved(object? sender, EventArgs e)
    {
        CaretLine = TextArea.Caret.Line;
        CaretColumn = TextArea.Caret.Column;
    }

    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(ScriptEditor),
        new PropertyMetadata(null, (d, _) => ((ScriptEditor)d).ScheduleClassify(immediately: true)));

    /// <summary>
    /// 이 편집기에 든 파일의 전체 경로. 프로젝트면 완성·색이 같은 프로젝트의 다른 파일도 보고, 오류도 이 파일 것만 긋는다.
    /// </summary>
    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>분류가 한 번이라도 칠해졌는지(토막 수). 검증에서 본다.</summary>
    public int SemanticTokenCount => _semantic.Count;

    /// <summary>분류를 글에 입힐 때마다. 검증이 기다리는 데 쓴다.</summary>
    public event EventHandler? Classified;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // 테마가 바뀌면 강조 정의가 갈린다. 색은 정의에서 꺼내므로 새 정의로 다시 그리기만 하면 된다.
        if (e.Property == SyntaxHighlightingProperty)
        {
            _semantic.Definition = SyntaxHighlighting;
            TextArea.TextView.Redraw();
        }
    }

    private void ScheduleClassify(bool immediately)
    {
        _classifyTimer.Stop();

        if (Language != ScriptLanguage.CSharp || CompletionSource is not IScriptClassifier)
        {
            // 남은 C# 색이 다른 언어의 글을 칠하면 안 된다.
            if (_semantic.Count > 0 || _codeLens.Count > 0)
            {
                _semantic.Clear();
                _codeLens.Clear();
                TextArea.TextView.Redraw();
            }

            return;
        }

        if (immediately) RunClassify();
        else _classifyTimer.Start();
    }

    /// <summary>
    /// 지금 글을 분류하러 보낸다. 끝나면 UI 스레드에서 입힌다.
    /// </summary>
    /// <remarks>
    /// 앞선 요청은 접는다 - 마지막 글만 뜻이 있다. 돌아왔을 때 글의 판이 다르면 버린다(<see cref="SemanticColorizer.Apply"/>).
    /// 분류는 백그라운드에서 돈다(Roslyn 이 await 로 돌려준다). 실패해도 xshd 색은 남으니 조용히 로그만 남긴다.
    /// </remarks>
    private async void RunClassify()
    {
        _classifyTimer.Stop();

        if (Language != ScriptLanguage.CSharp || CompletionSource is not IScriptClassifier classifier) return;

        _classifyCts?.Cancel();
        _classifyCts = new CancellationTokenSource();

        var token = _classifyCts.Token;
        var document = Document;
        var version = document.Version;
        var text = document.Text;

        try
        {
            var filePath = FilePath;
            var tokens = await System.Threading.Tasks.Task.Run(() => classifier.ClassifyAsync(text, token, filePath), token);

            if (token.IsCancellationRequested || !ReferenceEquals(document, Document)) return;

            _semantic.Definition = SyntaxHighlighting;

            // 판이 달라 버린 결과는 알리지 않는다 - 뒤따르는 분류가 입힌다.
            if (!_semantic.Apply(document, version, tokens)) return;

            TextArea.TextView.Redraw();
            Classified?.Invoke(this, EventArgs.Empty);

            // 참조 표시는 분류 뒤에 센다 - 분류가 먼저 보여야 치는 동안 색이 늦지 않는다. 컴파일은 작업 공간이 들고 있어 두 번째가 빠르다.
            if (CompletionSource is IScriptReferenceFinder finder)
            {
                var lenses = await System.Threading.Tasks.Task.Run(() => finder.GetLensesAsync(text, token, filePath), token);

                if (token.IsCancellationRequested || !ReferenceEquals(document, Document)) return;

                _codeLens.FontSize = FontSize;
                _codeLens.Foreground = LineNumbersForeground ?? Brushes.Gray;

                if (_codeLens.Apply(document, version, lenses)) TextArea.TextView.Redraw();

                LensesApplied?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // 더 새 글이 들어왔다.
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "C# 분류·참조 표시에 실패했다 - 강조 정의 색만 쓴다");
        }
    }

    // ── 참조 표시(CodeLens) ──────────────────────────────────────────────

    private readonly CodeLensGenerator _codeLens = new();
    private System.Windows.Controls.Primitives.Popup? _referencesPopup;

    /// <summary>지금 그려진 "참조 N개" 수. 검증에서 본다.</summary>
    public int CodeLensCount => _codeLens.Count;

    /// <summary>참조 표시를 새로 셀 때마다. 검증이 기다리는 데 쓴다.</summary>
    public event EventHandler? LensesApplied;

    public static readonly DependencyProperty NavigateCommandProperty = DependencyProperty.Register(
        nameof(NavigateCommand), typeof(ICommand), typeof(ScriptEditor), new PropertyMetadata(null));

    /// <summary>다른 파일의 줄로 가야 할 때 올린다(<see cref="EditorNavigation"/>). 같은 파일이면 편집기가 스스로 간다.</summary>
    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    /// <summary>
    /// "참조 N개" 를 눌렀다 - 부르는 곳을 찾아 VS 처럼 편집기 위에 창을 띄운다.
    /// </summary>
    private async void OnCodeLensClicked(int offset, FrameworkElement anchor)
    {
        if (CompletionSource is not IScriptReferenceFinder finder) return;

        try
        {
            var text = Document.Text;
            var filePath = FilePath;
            var references = await System.Threading.Tasks.Task.Run(() => finder.FindReferencesAsync(text, offset, default, filePath));

            ShowReferences(references, anchor);
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "참조를 찾지 못했다");
        }
    }

    /// <summary>참조 창을 띄운다. 검증도 이것을 부른다.</summary>
    public void ShowReferences(IReadOnlyList<ScriptReference> references, FrameworkElement? anchor)
    {
        _referencesPopup?.SetCurrentValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty, false);

        // VS 처럼 "경로 (개수)" 로 묶는다. 경로는 이 파일 폴더에서 본 상대 경로 - 전체 경로는 너무 길다.
        var baseFolder = string.IsNullOrEmpty(FilePath) ? null : System.IO.Path.GetDirectoryName(FilePath);

        string Display(string path) => string.IsNullOrEmpty(path) ? "(이 스크립트)"
            : baseFolder is null ? System.IO.Path.GetFileName(path)
            : System.IO.Path.GetRelativePath(baseFolder, path);

        var rows = references
            .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.Select(r => new CodeLensReferenceRow(r, $"{Display(g.Key)} ({g.Count()})")))
            .ToList();

        var panel = new Views.Parts.CodeLensReferencesPanel { Rows = rows };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Child = panel,
            PlacementTarget = anchor ?? this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };

        panel.Navigate += (_, row) =>
        {
            popup.IsOpen = false;

            if (string.IsNullOrEmpty(row.FilePath) || string.Equals(row.FilePath, FilePath, StringComparison.OrdinalIgnoreCase))
                OnLineRequested(new EditorLineRequest(row.Line));
            else if (NavigateCommand?.CanExecute(null) != false)
                NavigateCommand?.Execute(new EditorNavigation(row.FilePath, row.Line));
        };

        _referencesPopup = popup;
        popup.IsOpen = true;
    }

    /// <summary>지금 떠 있는 참조 창. 검증에서 본다.</summary>
    public Views.Parts.CodeLensReferencesPanel? ReferencesPanel
        => _referencesPopup is { IsOpen: true, Child: Views.Parts.CodeLensReferencesPanel panel } ? panel : null;

    private void ApplyIndentation()
        => TextArea.IndentationStrategy = Language == ScriptLanguage.Python
            ? new ICSharpCode.AvalonEdit.Indentation.DefaultIndentationStrategy()
            : new ICSharpCode.AvalonEdit.Indentation.CSharp.CSharpIndentationStrategy(Options);

    /// <summary>
    /// 붙여 넣은 줄들을 그 자리에 맞게 다시 들여쓴다.
    /// </summary>
    /// <remarks>
    /// 남의 코드를 붙여 넣으면 원래 있던 들여쓰기가 그대로 따라와 지금 자리와 안 맞는다. 엔터는 AvalonEdit 이
    /// 알아서 맞춰 주는데 붙여 넣기는 안 해 준다 - 여기서 한다.
    ///
    /// <b>붙여 넣은 자리만</b> 다시 맞춘다. 글 전체를 맞추면 사람이 일부러 비뚜로 둔 곳까지 바뀌고,
    /// 되돌리기(Ctrl+Z) 한 번에 안 돌아간다.
    /// </remarks>
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (Language == ScriptLanguage.Python || IsReadOnly) return;
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true)) return;
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText, true) is not string pasted) return;

        // 한 줄짜리는 맞출 것이 없다. 여러 줄일 때만 손댄다.
        if (!pasted.Contains('\n')) return;

        var start = TextArea.Caret.Line;

        // 붙여 넣기가 끝난 뒤에 맞춘다 - 지금은 아직 글이 안 들어가 있다.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var end = Math.Min(Document.LineCount, TextArea.Caret.Line);

            if (end < start) return;

            using (Document.RunUpdate())
                TextArea.IndentationStrategy?.IndentLines(Document, start, end);
        }));
    }

    /// <summary>틀린 줄들. 줄 번호는 1부터. 바뀌면 다시 그린다.</summary>
    public IReadOnlyList<ScriptError>? Errors
    {
        get => (IReadOnlyList<ScriptError>?)GetValue(ErrorsProperty);
        set => SetValue(ErrorsProperty, value);
    }

    /// <summary>중단점 줄 번호들(1부터). 여백을 누르면 여기에 넣고 뺀다.</summary>
    public ObservableCollection<int>? Breakpoints
    {
        get => (ObservableCollection<int>?)GetValue(BreakpointsProperty);
        set => SetValue(BreakpointsProperty, value);
    }

    /// <summary>
    /// 언어를 아는 완성(C# 은 Roslyn). 없거나 빈 목록이면 API 표(<see cref="ScriptApiCatalog"/>)로 돌아간다.
    /// </summary>
    public IScriptCompletionSource? CompletionSource
    {
        get => (IScriptCompletionSource?)GetValue(CompletionSourceProperty);
        set => SetValue(CompletionSourceProperty, value);
    }

    /// <summary>멈춘 줄(1부터). 0 이면 없음.</summary>
    public int CurrentLine
    {
        get => (int)GetValue(CurrentLineProperty);
        set => SetValue(CurrentLineProperty, value);
    }

    /// <summary>지금 완성 목록이 떠 있는지. 검증에서 본다.</summary>
    public bool IsCompletionOpen => _completion is not null;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    /// <summary>
    /// 이 파일의 오류만. 프로젝트 오류 목록에는 다른 파일의 것도 섞여 있다 - 그 줄 번호로 이 파일에 밑줄을 그으면 엉뚱한 줄이다.
    /// 파일이 안 적힌 오류(한 파일짜리·실행 중 멈춤)는 이 편집기 것으로 본다.
    /// </summary>
    internal IReadOnlyList<ScriptError> OwnErrors
    {
        get
        {
            if (Errors is not { Count: > 0 } errors) return [];
            if (string.IsNullOrEmpty(FilePath)) return errors;

            var self = System.IO.Path.GetFullPath(FilePath);

            return [.. errors.Where(e => e.File is null || string.Equals(System.IO.Path.GetFullPath(e.File), self, StringComparison.OrdinalIgnoreCase))];
        }
    }

    private void OnErrorsChanged()
    {
        _errorTip.IsOpen = false;
        TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    // ── 중단점 ───────────────────────────────────────────────────────────

    /// <summary>캐럿이 있는 줄의 중단점을 켜고 끈다 (Ctrl+B · 버튼).</summary>
    public void ToggleBreakpointAtCaret() => ToggleBreakpoint(TextArea.Caret.Line);

    public void ToggleBreakpoint(int line)
    {
        var points = Breakpoints;
        if (points is null || line < 1) return;

        if (points.Contains(line)) points.Remove(line);
        else points.Add(line);
    }

    private void OnBreakpointsChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ObservableCollection<int> old) old.CollectionChanged -= OnBreakpointCollectionChanged;
        if (e.NewValue is ObservableCollection<int> now) now.CollectionChanged += OnBreakpointCollectionChanged;

        _margin.InvalidateVisual();
    }

    private void OnBreakpointCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => _margin.InvalidateVisual();

    private void OnCurrentLineChanged()
    {
        TextArea.TextView.InvalidateLayer(KnownLayer.Background);

        var line = CurrentLine;
        if (line > 0 && line <= Document.LineCount) ScrollToLine(line);
    }

    // ── 밑줄 풍선 ────────────────────────────────────────────────────────

    private void OnMouseHover(object sender, MouseEventArgs e)
    {
        var errors = OwnErrors;
        if (errors.Count == 0) return;

        var position = GetPositionFromPoint(e.GetPosition(this));
        if (position is null) return;

        var line = position.Value.Line;
        var messages = errors.Where(x => x.Line == line).Select(x => x.Message).ToList();

        if (messages.Count == 0) return;

        _errorTip.Content = string.Join(Environment.NewLine, messages);
        _errorTip.PlacementTarget = this;
        _errorTip.IsOpen = true;
        e.Handled = true;
    }

    // ── 코드 완성 ────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenCompletion();
            e.Handled = true;
        }
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleBreakpointAtCaret();
            e.Handled = true;
        }
    }

    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (_completion is not null || IsReadOnly || e.Text.Length == 0) return;

        // 낱말의 첫 글자에서 연다. 그 뒤는 창이 스스로 거른다.
        if (IsIdentifierChar(e.Text[0]) && WordStart(CaretOffset) == CaretOffset - 1)
            OpenCompletion();
    }

    /// <summary>낱말이 아닌 글자(괄호·공백·세미콜론)를 치면 고른 것을 넣고 닫는다. 표준 AvalonEdit 관례다.</summary>
    private void OnTextEntering(object sender, TextCompositionEventArgs e)
    {
        if (_completion is null || e.Text.Length == 0) return;

        if (!IsIdentifierChar(e.Text[0]))
            _completion.CompletionList.RequestInsertion(e);
    }

    /// <summary>
    /// 캐럿 앞 낱말을 앞글자로 삼아 목록을 연다. 언어 완성이 있으면 그것을(비동기), 없으면 API 표를. 맞는 것이 없으면 안 연다.
    /// </summary>
    /// <remarks>
    /// Roslyn 은 첫 호출이 1~2초라 기다리는 동안 타이핑을 막지 않는다. 결과가 왔을 때 캐럿이 낱말 시작보다 앞으로
    /// 갔으면(지웠으면) 열지 않는다. 그 사이에 다시 부르면 앞선 요청은 접는다.
    /// </remarks>
    public async void OpenCompletion()
    {
        if (IsReadOnly || _completion is not null) return;

        var start = WordStart(CaretOffset);
        var prefix = Document.GetText(start, CaretOffset - start);

        _completionCts?.Cancel();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;

        List<ICompletionData> items = [];

        if (CompletionSource is { } source)
        {
            try
            {
                var suggestions = await source.GetAsync(Document.Text, CaretOffset, token, FilePath);

                foreach (var suggestion in suggestions)
                    items.Add(new LanguageCompletionData(suggestion));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // 완성이 터져도 편집은 되어야 한다. 표로 돌아간다.
                items.Clear();
            }

            if (token.IsCancellationRequested || _completion is not null) return;
            if (CaretOffset < start || WordStart(CaretOffset) != start) return;
        }

        if (items.Count == 0)
        {
            foreach (var (name, entry) in ScriptApiCatalog.Match(string.Empty))
                items.Add(new ApiCompletionData(name, entry));
        }

        var current = Document.GetText(start, CaretOffset - start);

        if (current.Length > 0 && !items.Any(i => i.Text.StartsWith(current, StringComparison.OrdinalIgnoreCase)))
            return;

        var window = new CompletionWindow(TextArea)
        {
            StartOffset = start,
            CloseWhenCaretAtBeginning = true,
            CloseAutomatically = true,
            Width = 420
        };

        // 전부 넣는다. 창이 StartOffset~캐럿 사이 글로 거르고, 첫 항목을 고른다.
        foreach (var item in items) window.CompletionList.CompletionData.Add(item);

        window.Closed += (_, _) => _completion = null;
        _completion = window;

        window.Show();

        if (current.Length > 0) window.CompletionList.SelectItem(current);
    }

    /// <summary>캐럿 앞으로 낱말 글자가 이어지는 시작 자리.</summary>
    private int WordStart(int offset)
    {
        var start = offset;

        while (start > 0 && IsIdentifierChar(Document.GetCharAt(start - 1))) start--;

        return start;
    }

    /// <summary>낱말을 이루는 글자. 영문·숫자·밑줄과 한글(완성형·자모).</summary>
    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c is (>= '가' and <= '힣') or (>= 'ㄱ' and <= 'ㆎ');

    /// <summary>언어 완성(Roslyn)의 한 줄. 이름과 종류.</summary>
    private sealed class LanguageCompletionData(CompletionSuggestion suggestion) : ICompletionData
    {
        public ImageSource? Image => null;

        public string Text { get; } = suggestion.Text;

        public object Content { get; } = suggestion.Kind.Length > 0 ? $"{suggestion.Display}   {suggestion.Kind}" : suggestion.Display;

        public object Description { get; } = suggestion.Kind.Length > 0 ? $"{suggestion.Display} - {suggestion.Kind}" : suggestion.Display;

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
            => textArea.Document.Replace(completionSegment, Text);
    }

    /// <summary>완성 목록의 한 줄. 이름은 그대로 넣고, 옆에 부르는 모양과 설명을 보여 준다.</summary>
    private sealed class ApiCompletionData(string name, ScriptApiEntry entry) : ICompletionData
    {
        public ImageSource? Image => null;

        public string Text { get; } = name;

        /// <summary>목록에 보이는 것. "글자(text)  글자를 하나씩 누른다" 처럼 이름·인자·설명을 한 줄에.</summary>
        public object Content { get; } = $"{name}({entry.Parameters})   {entry.DisplaySummary}";

        /// <summary>고른 항목 옆에 뜨는 풍선. 영문·한글 이름이 같은 것임을 알려 준다.</summary>
        public object Description { get; } = $"{entry.Signature} = {entry.KoreanSignature}{Environment.NewLine}{entry.DisplaySummary}";

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
            => textArea.Document.Replace(completionSegment, Text);
    }

    /// <summary>틀린 줄 아래에 물결선을 그린다. 글자 뒤 층(Selection)에 그려 글을 가리지 않는다.</summary>
    private sealed class ErrorUnderlineRenderer(ScriptEditor owner) : IBackgroundRenderer
    {
        private static readonly Pen WavePen = MakePen();

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var errors = owner.OwnErrors;
            if (errors.Count == 0 || textView.Document is null) return;

            textView.EnsureVisualLines();

            foreach (var lineNumber in errors.Select(e => e.Line).Where(l => l > 0).Distinct())
            {
                if (lineNumber > textView.Document.LineCount) continue;

                var line = textView.Document.GetLineByNumber(lineNumber);
                var text = textView.Document.GetText(line);
                var lead = text.Length - text.TrimStart().Length;

                // 빈 줄이면 짧은 자리라도 표시한다 - 아무것도 안 그리면 "줄 3" 이 어디인지 모른다.
                ISegment segment = line.Length - lead > 0
                    ? new TextSegment { StartOffset = line.Offset + lead, Length = line.Length - lead }
                    : new TextSegment { StartOffset = line.Offset, Length = 0 };

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    var width = Math.Max(rect.Width, 24);
                    drawingContext.DrawGeometry(null, WavePen, Wave(rect.Left, rect.Bottom - 1, width));
                }
            }
        }

        /// <summary>왼쪽에서 오른쪽으로 3px 간격 지그재그.</summary>
        private static StreamGeometry Wave(double left, double baseline, double width)
        {
            var geometry = new StreamGeometry();

            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(left, baseline), false, false);

                var up = true;

                for (var x = left + 3; x <= left + width; x += 3)
                {
                    context.LineTo(new Point(x, up ? baseline - 2 : baseline), true, false);
                    up = !up;
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private static Pen MakePen()
        {
            var pen = new Pen(new SolidColorBrush(UnderlineColor), 1.2);
            pen.Freeze();
            return pen;
        }
    }

    /// <summary>
    /// 줄 칠하기를 코드 글자 높이로 줄인다. 참조 표시(CodeLens)가 붙은 줄은 위에 참조 한 줄만큼 키가 커서, 줄 전체를 칠하면
    /// "참조 N개" 까지 덮는다 - 코드 글자는 그 줄의 아래쪽에 앉으므로 밑에서 한 줄 높이만 남긴다.
    /// </summary>
    private static Rect CodeOnly(TextView textView, Rect rect)
    {
        var lineHeight = textView.DefaultLineHeight;

        return rect.Height > lineHeight * 1.5 ? new Rect(rect.X, rect.Bottom - lineHeight, rect.Width, lineHeight) : rect;
    }

    /// <summary>선택을 칠한다. 기본 선택 층 대신 - 참조 표시 줄에서 글자 높이만 칠하게(<see cref="CodeOnly"/>).</summary>
    private sealed class SelectionRenderer(TextArea area, Brush fill) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (area.Selection.IsEmpty) return;

            foreach (var segment in area.Selection.Segments)
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                drawingContext.DrawRectangle(fill, null, CodeOnly(textView, rect));
        }
    }

    /// <summary>멈춘 줄을 노랗게 칠한다. 맨 아래 층이라 글과 선택이 그 위에 그대로 보인다.</summary>
    private sealed class CurrentLineRenderer(ScriptEditor owner) : IBackgroundRenderer
    {
        private static readonly Brush Fill = MakeBrush();

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var lineNumber = owner.CurrentLine;
            if (lineNumber <= 0 || textView.Document is null || lineNumber > textView.Document.LineCount) return;

            textView.EnsureVisualLines();

            var line = textView.Document.GetLineByNumber(lineNumber);

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line).Select(r => CodeOnly(textView, r)))
                drawingContext.DrawRectangle(Fill, null, new Rect(0, rect.Top, Math.Max(textView.ActualWidth, rect.Right), rect.Height));
        }

        private static Brush MakeBrush()
        {
            var brush = new SolidColorBrush(CurrentLineColor);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>줄 번호 왼쪽의 좁은 띠. 중단점을 빨간 점으로 그리고, 누르면 그 줄의 중단점을 켜고 끈다.</summary>
    private sealed class BreakpointMargin : AbstractMargin
    {
        private const double Width = 16;
        private static readonly Brush Dot = MakeBrush();
        private readonly ScriptEditor _owner;

        public BreakpointMargin(ScriptEditor owner)
        {
            _owner = owner;
            Cursor = Cursors.Arrow;
        }

        protected override Size MeasureOverride(Size availableSize) => new(Width, 0);

        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView is not null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
            if (newTextView is not null) newTextView.VisualLinesChanged += OnVisualLinesChanged;

            base.OnTextViewChanged(oldTextView, newTextView);
            InvalidateVisual();
        }

        private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            // 아무것도 안 그린 자리는 히트 테스트에 안 걸려 클릭이 안 온다. 투명 판을 깔아 띠 전체가 눌리게 한다.
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

            var points = _owner.Breakpoints;
            var textView = TextView;

            if (points is null || points.Count == 0 || textView is null || !textView.VisualLinesValid) return;

            foreach (var visualLine in textView.VisualLines)
            {
                var lineNumber = visualLine.FirstDocumentLine.LineNumber;
                if (!points.Contains(lineNumber)) continue;

                var top = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.LineTop) - textView.VerticalOffset;
                var height = visualLine.Height;
                var radius = Math.Min(6, height / 2 - 1);

                drawingContext.DrawEllipse(Dot, null, new Point(Width / 2, top + (height / 2)), radius, radius);
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            var textView = TextView;
            if (textView is null) return;

            var y = e.GetPosition(this).Y + textView.VerticalOffset;
            var visualLine = textView.GetVisualLineFromVisualTop(y);

            if (visualLine is null) return;

            _owner.ToggleBreakpoint(visualLine.FirstDocumentLine.LineNumber);
            e.Handled = true;
        }

        private static Brush MakeBrush()
        {
            var brush = new SolidColorBrush(BreakpointColor);
            brush.Freeze();
            return brush;
        }
    }
}
