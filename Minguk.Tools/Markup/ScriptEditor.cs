using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    public static readonly DependencyProperty CurrentLineProperty = DependencyProperty.Register(
        nameof(CurrentLine), typeof(int), typeof(ScriptEditor),
        new PropertyMetadata(0, (d, _) => ((ScriptEditor)d).OnCurrentLineChanged()));

    private readonly ErrorUnderlineRenderer _underline;
    private readonly CurrentLineRenderer _currentLine;
    private readonly BreakpointMargin _margin;
    private readonly ToolTip _errorTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private CompletionWindow? _completion;

    public ScriptEditor()
    {
        _underline = new ErrorUnderlineRenderer(this);
        _currentLine = new CurrentLineRenderer(this);
        TextArea.TextView.BackgroundRenderers.Add(_currentLine);
        TextArea.TextView.BackgroundRenderers.Add(_underline);

        _margin = new BreakpointMargin(this);
        TextArea.LeftMargins.Insert(0, _margin);

        TextArea.TextEntered += OnTextEntered;
        TextArea.TextEntering += OnTextEntering;
        MouseHover += OnMouseHover;
        MouseHoverStopped += (_, _) => _errorTip.IsOpen = false;
        PreviewKeyDown += OnPreviewKeyDown;
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

    /// <summary>멈춘 줄(1부터). 0 이면 없음.</summary>
    public int CurrentLine
    {
        get => (int)GetValue(CurrentLineProperty);
        set => SetValue(CurrentLineProperty, value);
    }

    /// <summary>지금 완성 목록이 떠 있는지. 검증에서 본다.</summary>
    public bool IsCompletionOpen => _completion is not null;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

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
        var errors = Errors;
        if (errors is null || errors.Count == 0) return;

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

    /// <summary>캐럿 앞 낱말을 앞글자로 삼아 목록을 연다. 맞는 것이 없으면 안 연다.</summary>
    public void OpenCompletion()
    {
        if (IsReadOnly) return;

        var start = WordStart(CaretOffset);
        var prefix = Document.GetText(start, CaretOffset - start);
        var matches = ScriptApiCatalog.Match(prefix).ToList();

        if (matches.Count == 0)
        {
            _completion?.Close();
            return;
        }

        if (_completion is not null) return;

        var window = new CompletionWindow(TextArea)
        {
            StartOffset = start,
            CloseWhenCaretAtBeginning = true,
            CloseAutomatically = true,
            Width = 360
        };

        // 전부 넣는다. 창이 StartOffset~캐럿 사이 글로 거르고, 첫 항목을 고른다.
        foreach (var (name, entry) in ScriptApiCatalog.Match(string.Empty))
            window.CompletionList.CompletionData.Add(new ApiCompletionData(name, entry));

        window.Closed += (_, _) => _completion = null;
        _completion = window;

        window.Show();

        if (prefix.Length > 0) window.CompletionList.SelectItem(prefix);
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
            var errors = owner.Errors;
            if (errors is null || errors.Count == 0 || textView.Document is null) return;

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

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
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
