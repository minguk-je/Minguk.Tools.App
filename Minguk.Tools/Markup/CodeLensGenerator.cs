using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

using Minguk.Tools.Input.Scripting;

namespace Minguk.Tools.Markup;

/// <summary>
/// 선언 줄 위에 "참조 N개" 를 끼운다 - VS 의 CodeLens.
/// </summary>
/// <remarks>
/// <b>줄 사이에 자리를 만드는 법</b> - AvalonEdit 에는 줄 위 여백이 없다. 그래서 선언 줄의 첫 글자(들여쓰기 뒤) 자리에 <b>폭 0, 키가
/// 참조 한 줄 + 글자 높이</b>인 요소를 끼운다. 그 줄은 요소만큼 높아지고 코드 글자는 요소의 밑선에 맞춰 아래에 앉으며, 요소의 윗부분에
/// "참조 N개" 가 그려진다. 폭이 0 이라 코드가 옆으로 밀리지 않고, 들여쓰기 뒤에 끼우니 글자가 코드와 같은 열에서 시작한다.
/// 문서 길이 0 짜리 요소라 캐럿·선택·되돌리기에 끼어들지 않는다.
///
/// 자리는 <see cref="TextAnchor"/> 로 문서에 매어 둔다 - 분류가 다시 돌기 전에 위에 줄을 치면 표시가 옛 줄에 남는다.
/// </remarks>
public sealed class CodeLensGenerator : VisualLineElementGenerator
{
    private sealed record Lens(TextAnchor Anchor, int Count, int Offset, string Name);

    private readonly List<Lens> _lenses = [];
    private TextDocument? _document;

    /// <summary>"참조 N개" 를 눌렀다. 그 선언(현재 오프셋)과 누른 요소(팝업 자리).</summary>
    public event Action<int, FrameworkElement>? Clicked;

    public int Count => _lenses.Count;

    /// <summary>글자 크기. 편집기 글꼴에 맞춰 줄 높이를 잰다.</summary>
    public double FontSize { get; set; } = 13;

    public Brush Foreground { get; set; } = Brushes.Gray;

    /// <summary>
    /// 새 목록을 건다. 판이 달라졌으면 버린다(분류와 같은 규칙). 바뀐 것이 없으면 false - 다시 그리지 않아도 된다.
    /// </summary>
    public bool Apply(TextDocument document, ITextSourceVersion? version, IReadOnlyList<ScriptLens> lenses)
    {
        if (version is not null && (!version.BelongsToSameDocumentAs(document.Version) || version.CompareAge(document.Version) != 0))
            return false;

        var same = ReferenceEquals(_document, document) && _lenses.Count == lenses.Count &&
                   _lenses.Zip(lenses).All(p => p.First.Offset == p.Second.Offset && p.First.Count == p.Second.Count && p.First.Name == p.Second.Name);
        if (same) return false;

        _document = document;
        _lenses.Clear();

        foreach (var lens in lenses)
        {
            if (lens.Offset < 0 || lens.Offset > document.TextLength) continue;

            var anchor = document.CreateAnchor(lens.Offset);
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            _lenses.Add(new Lens(anchor, lens.Count, lens.Offset, lens.Name));
        }

        return true;
    }

    public void Clear()
    {
        _lenses.Clear();
        _document = null;
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (_lenses.Count == 0 || !ReferenceEquals(CurrentContext.Document, _document)) return -1;

        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (LensOf(line) is null) return -1;

        var insert = InsertOffset(line);

        return startOffset <= insert ? insert : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var line = CurrentContext.Document.GetLineByOffset(offset);
        if (LensOf(line) is not { } lens || InsertOffset(line) != offset) return null;

        return new InlineObjectElement(0, MakeElement(lens));
    }

    private Lens? LensOf(DocumentLine line)
        => _lenses.FirstOrDefault(l => !l.Anchor.IsDeleted && l.Anchor.Line == line.LineNumber);

    /// <summary>들여쓰기 뒤 첫 글자 자리 - 거기 끼워야 "참조" 가 코드와 같은 열에서 시작한다.</summary>
    private int InsertOffset(DocumentLine line)
        => TextUtilities.GetLeadingWhitespace(CurrentContext.Document, line).EndOffset;

    private FrameworkElement MakeElement(Lens lens)
    {
        var lensHeight = Math.Round(FontSize * 1.25);

        // 윗줄과 띄우는 틈. 글자를 요소 맨 위에 두면 윗줄 코드 밑에 달라붙어 보였다(사용자, 2026-09-15). VS 도 CodeLens 위가 비어 있다.
        var topGap = Math.Round(FontSize * 0.4);

        var label = new TextBlock
        {
            Text = lens.Count == 1 ? "참조 1개" : $"참조 {lens.Count}개",
            FontSize = Math.Max(9, FontSize - 2),
            Foreground = Foreground,
            Cursor = Cursors.Hand,
            ToolTip = $"'{lens.Name}' 을(를) 부르는 곳 - 눌러서 보기"
        };

        // VS 처럼 올리면 밑줄.
        label.MouseEnter += (_, _) => label.TextDecorations = TextDecorations.Underline;
        label.MouseLeave += (_, _) => label.TextDecorations = null;

        var host = new Canvas
        {
            Width = 0,
            // 틈 + 참조 한 줄 + 코드 글자 높이. 코드 글자는 이 요소의 밑선에 앉는다.
            Height = topGap + lensHeight + FontSize,
            ClipToBounds = false,
            Background = null
        };

        Canvas.SetTop(label, topGap);
        host.Children.Add(label);

        label.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Clicked?.Invoke(lens.Anchor.IsDeleted ? lens.Offset : lens.Anchor.Offset, label);
        };

        return host;
    }
}
