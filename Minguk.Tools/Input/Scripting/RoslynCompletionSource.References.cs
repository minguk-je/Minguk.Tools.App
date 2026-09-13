using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 참조 표시(CodeLens) - 선언마다 참조 수, 그리고 부르는 곳 목록.
/// </summary>
/// <remarks>
/// <b>SymbolFinder 를 안 쓴다.</b> 그것은 작업 공간의 <i>문서</i>만 뒤지는데, 프로젝트의 다른 파일은 문서가 아니라 <c>#load</c> 로
/// 컴파일에 들어온 구문 트리다 - 그래서 다른 파일에서 부르는 것을 못 센다. 대신 컴파일의 <b>모든 구문 트리</b>를 돌며 이름마다
/// <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, CancellationToken)"/> 로 같은 심볼인지 본다. 스크립트 몇 개 크기라 한 번에 수십 ms 다.
///
/// 무엇에 붙이나 - VS 처럼 형식·메서드(스크립트 안 함수 포함)·속성·필드·이벤트. 스크립트 최상위 변수는 제출 클래스의 필드라 여기 든다.
/// 함수 안의 지역 변수는 VS 도 안 붙인다.
/// </remarks>
public sealed partial class RoslynCompletionSource
{
    public async Task<IReadOnlyList<ScriptLens>> GetLensesAsync(string text, CancellationToken token = default, string? filePath = null)
    {
        var (document, offset) = DocumentFor(text, filePath, includeEntry: true);
        var compilation = await document.Project.GetCompilationAsync(token).ConfigureAwait(false);
        var tree = await document.GetSyntaxTreeAsync(token).ConfigureAwait(false);

        if (compilation is null || tree is null || token.IsCancellationRequested) return [];

        var model = compilation.GetSemanticModel(tree);
        var declarations = Declarations(tree, model, offset, token).ToList();

        if (declarations.Count == 0) return [];

        var counts = declarations.ToDictionary(d => d.Symbol, _ => 0, SymbolEqualityComparer.Default);

        foreach (var (_, symbol, _) in References(compilation, token))
        {
            if (counts.TryGetValue(symbol, out var count)) counts[symbol] = count + 1;
        }

        var source = tree.GetText(token);

        return [.. declarations.Select(d =>
        {
            var line = source.Lines.GetLineFromPosition(d.Position).LineNumber - LineOf(source, offset);
            return new ScriptLens(d.Position - offset, line + 1, d.Symbol.Name, counts[d.Symbol]);
        })];
    }

    public async Task<IReadOnlyList<ScriptReference>> FindReferencesAsync(string text, int position, CancellationToken token = default, string? filePath = null)
    {
        var (document, offset) = DocumentFor(text, filePath, includeEntry: true);
        var compilation = await document.Project.GetCompilationAsync(token).ConfigureAwait(false);
        var tree = await document.GetSyntaxTreeAsync(token).ConfigureAwait(false);

        if (compilation is null || tree is null || token.IsCancellationRequested) return [];

        var model = compilation.GetSemanticModel(tree);
        var target = Declarations(tree, model, offset, token).FirstOrDefault(d => d.Position - offset == position).Symbol;

        if (target is null) return [];

        var self = filePath is null ? null : System.IO.Path.GetFullPath(filePath);
        var found = new List<ScriptReference>();

        foreach (var (node, symbol, where) in References(compilation, token))
        {
            if (!SymbolEqualityComparer.Default.Equals(symbol, target)) continue;

            var span = node.Span;
            var treeText = where.GetText(token);
            var isSelf = ReferenceEquals(where, tree);

            // 이 문서 트리는 머리말(#load 줄)이 앞에 붙어 있다 - 줄을 그만큼 되돌린다.
            var lineShift = isSelf ? LineOf(treeText, offset) : 0;
            var line = treeText.Lines.GetLineFromPosition(span.Start);

            if (isSelf && span.Start < offset) continue;

            var path = isSelf ? self ?? string.Empty : string.IsNullOrEmpty(where.FilePath) ? string.Empty : System.IO.Path.GetFullPath(where.FilePath);

            found.Add(new ScriptReference(path, line.LineNumber - lineShift + 1, span.Start - line.Start, span.Length, treeText.ToString(line.Span)));
        }

        return [.. found.OrderBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Line).ThenBy(r => r.Column)];
    }

    private static int LineOf(Microsoft.CodeAnalysis.Text.SourceText text, int position)
        => position <= 0 ? 0 : text.Lines.GetLineFromPosition(Math.Min(position, text.Length)).LineNumber;

    /// <summary>이 트리의 선언들 - 머리말 뒤(사람이 쓴 글)만. 위치는 이름 토큰이 시작하는 자리.</summary>
    private static IEnumerable<(int Position, ISymbol Symbol)> Declarations(SyntaxTree tree, SemanticModel model, int offset, CancellationToken token)
    {
        var root = tree.GetRoot(token);

        foreach (var node in root.DescendantNodes())
        {
            if (node.SpanStart < offset) continue;

            (SyntaxToken Name, SyntaxNode Declared)? declared = node switch
            {
                MethodDeclarationSyntax m => (m.Identifier, m),
                LocalFunctionStatementSyntax f when f.Parent is GlobalStatementSyntax => (f.Identifier, f),
                PropertyDeclarationSyntax p => (p.Identifier, p),
                EventDeclarationSyntax e => (e.Identifier, e),
                BaseTypeDeclarationSyntax t => (t.Identifier, t),
                DelegateDeclarationSyntax delegateDeclaration => (delegateDeclaration.Identifier, delegateDeclaration),
                // 필드와 스크립트 최상위 변수(제출 클래스의 필드). 함수 안 지역 변수는 VS 도 안 붙인다.
                VariableDeclaratorSyntax v when v.Parent?.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax
                                              || v.Parent?.Parent is LocalDeclarationStatementSyntax { Parent: GlobalStatementSyntax } => (v.Identifier, v),
                _ => null
            };

            if (declared is not { } found) continue;

            var symbol = model.GetDeclaredSymbol(found.Declared, token);
            if (symbol is null) continue;

            yield return (found.Name.SpanStart, symbol.OriginalDefinition);
        }
    }

    /// <summary>컴파일의 모든 트리에서 이름 하나하나가 가리키는 심볼. 선언 자신은 이름 노드가 아니라 안 나온다.</summary>
    private static IEnumerable<(SimpleNameSyntax Node, ISymbol Symbol, SyntaxTree Tree)> References(Compilation compilation, CancellationToken token)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (token.IsCancellationRequested) yield break;

            var model = compilation.GetSemanticModel(tree);

            foreach (var name in tree.GetRoot(token).DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var info = model.GetSymbolInfo(name, token);
                var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();

                if (symbol is null) continue;

                // 확장·제네릭 인스턴스는 원래 정의로 모은다.
                if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;

                yield return (name, symbol.OriginalDefinition, tree);
            }
        }
    }
}
