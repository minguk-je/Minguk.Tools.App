using System;
using System.Collections.Generic;
using System.Windows.Media;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

using Minguk.Tools.Input.Scripting;

namespace Minguk.Tools.Markup;

/// <summary>
/// 컴파일러가 준 낱말 종류(<see cref="ScriptToken"/>)로 글을 덧칠한다.
/// </summary>
/// <remarks>
/// <b>xshd 위에 덧칠한다.</b> 강조 정의의 색칠기는 LineTransformers 맨 앞에 들어가고 이것은 뒤에 붙으므로 같은 자리면 이것이 이긴다.
/// 분류가 도는 사이(입력이 멈추고 잠시 뒤) 새로 친 글은 xshd 의 정규식 색으로 먼저 보이고, 분류가 오면 바로잡힌다.
///
/// <b>토막은 문서에 매어 둔다</b>(<see cref="TextSegmentCollection{T}"/>). 분류가 끝난 뒤 사람이 앞에 글을 더 치면 오프셋이
/// 문서를 따라 밀린다 - 안 그러면 한 글자 칠 때마다 색이 한 칸씩 어긋나 다음 분류까지 엉뚱한 낱말이 칠해진다.
///
/// 색은 <b>강조 정의의 이름 붙은 색</b>에서 꺼낸다(<see cref="ScriptTokenKind"/> 이름 = xshd <c>Color name</c>).
/// 그래서 테마가 바뀌어 정의가 갈리면 색도 같이 갈린다.
/// </remarks>
public sealed class SemanticColorizer : DocumentColorizingTransformer
{
    private sealed class TokenSegment : TextSegment
    {
        public ScriptTokenKind Kind { get; init; }
    }

    private TextSegmentCollection<TokenSegment>? _segments;
    private TextDocument? _document;

    /// <summary>색을 꺼낼 강조 정의. 없으면 칠하지 않는다.</summary>
    public IHighlightingDefinition? Definition { get; set; }

    /// <summary>토막이 있는지. 검증에서 본다.</summary>
    public int Count => _segments?.Count ?? 0;

    /// <summary>
    /// 새 분류를 건다. <paramref name="version"/> 은 분류에 넣은 글의 판 - 그 사이 글이 바뀌었으면 버린다.
    /// </summary>
    /// <remarks>
    /// 판이 다르면 오프셋이 이미 어긋나 있다. 곧 새 분류가 오므로 옛 토막을 그대로 둔다(문서를 따라 밀려 있어 대체로 맞다).
    /// </remarks>
    public bool Apply(TextDocument document, ITextSourceVersion? version, IReadOnlyList<ScriptToken> tokens)
    {
        if (version is not null && (!version.BelongsToSameDocumentAs(document.Version) || version.CompareAge(document.Version) != 0))
            return false;

        Clear();

        _document = document;
        _segments = new TextSegmentCollection<TokenSegment>(document);

        foreach (var token in tokens)
        {
            if (token.Length <= 0 || token.Start < 0 || token.Start + token.Length > document.TextLength) continue;

            _segments.Add(new TokenSegment { StartOffset = token.Start, Length = token.Length, Kind = token.Kind });
        }

        return true;
    }

    /// <summary>토막을 비운다. 언어가 C# 이 아니게 되면 부른다 - 남은 C# 색이 파이썬 글을 칠하면 안 된다.</summary>
    public void Clear()
    {
        // 문서에 매인 컬렉션은 문서의 변경 알림을 약한 이벤트로 받는다 - 놓기만 하면 GC 가 거둔다.
        _segments = null;
        _document = null;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_segments is null || Definition is null || !ReferenceEquals(CurrentContext.Document, _document)) return;

        foreach (var segment in _segments.FindOverlappingSegments(line.Offset, line.Length))
        {
            if (Definition.GetNamedColor(segment.Kind.ToString()) is not { Foreground: { } foreground } color) continue;

            var start = Math.Max(segment.StartOffset, line.Offset);
            var end = Math.Min(segment.EndOffset, line.EndOffset);

            if (end <= start) continue;

            var brush = foreground.GetBrush(CurrentContext);
            var bold = color.FontWeight;

            ChangeLinePart(start, end, element =>
            {
                if (brush is not null) element.TextRunProperties.SetForegroundBrush(brush);

                // 굵기는 색이 정한 대로 되돌린다. xshd 가 이미 굵게 칠한 것을 분류가 보통으로 바꿀 수 있어야 한다.
                var typeface = element.TextRunProperties.Typeface;
                var weight = bold ?? System.Windows.FontWeights.Normal;

                if (typeface.Weight != weight)
                    element.TextRunProperties.SetTypeface(new Typeface(typeface.FontFamily, typeface.Style, weight, typeface.Stretch));
            });
        }
    }
}
