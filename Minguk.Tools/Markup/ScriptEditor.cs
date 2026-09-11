using System;
using System.Collections.Generic;
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
/// 스크립트 편집기. <see cref="TextEditor"/> 에 세 가지를 얹었다 - 자동화 피어, 틀린 줄의 빨간 밑줄, 코드 완성.
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
/// </remarks>
public sealed class ScriptEditor : TextEditor
{
    /// <summary>밑줄 색. 화면의 "고칠 줄" 글자색과 같다.</summary>
    public static readonly Color UnderlineColor = Color.FromRgb(0xD1, 0x24, 0x2F);

    public static readonly DependencyProperty ErrorsProperty = DependencyProperty.Register(
        nameof(Errors), typeof(IReadOnlyList<ScriptError>), typeof(ScriptEditor),
        new PropertyMetadata(null, (d, _) => ((ScriptEditor)d).OnErrorsChanged()));

    private readonly ErrorUnderlineRenderer _underline;
    private readonly ToolTip _errorTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private CompletionWindow? _completion;

    public ScriptEditor()
    {
        _underline = new ErrorUnderlineRenderer(this);
        TextArea.TextView.BackgroundRenderers.Add(_underline);

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

    /// <summary>지금 완성 목록이 떠 있는지. 검증에서 본다.</summary>
    public bool IsCompletionOpen => _completion is not null;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private void OnErrorsChanged()
    {
        _errorTip.IsOpen = false;
        TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
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
        public object Content { get; } = $"{name}({entry.Parameters})   {entry.Summary}";

        /// <summary>고른 항목 옆에 뜨는 풍선. 영문·한글 이름이 같은 것임을 알려 준다.</summary>
        public object Description { get; } = $"{entry.Signature} = {entry.KoreanSignature}{Environment.NewLine}{entry.Summary}";

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
}
