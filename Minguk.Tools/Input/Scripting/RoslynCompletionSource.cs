using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// C# 완성. Roslyn 의 <see cref="CompletionService"/> 에 스크립트 문서를 들이밀어 그 자리에서 될 수 있는 것을 받는다.
/// </summary>
/// <remarks>
/// <b>왜 따로 무거운 패키지를</b> - API 표만으로는 <c>검출.중심x</c> 나 <c>string.Length</c> 같은 멤버, <c>var</c>·<c>while</c>
/// 같은 문법이 안 나온다. Roslyn 은 실제 컴파일러라 전역(<see cref="Live.LiveScriptApi"/> 의 메서드)부터 지역 변수의
/// 멤버까지 다 안다. 대신 Features 패키지가 수십 MB 고 첫 호출이 1~2초다 - 그래서 설계에서 4단계로 미뤄 두었다.
///
/// 문서는 <b>스크립트 종류</b>(SourceCodeKind.Script)에 <b>전역 타입</b>(hostObjectType)을 실은 제출(submission) 프로젝트다.
/// 그래야 <c>Type("...")</c> 처럼 전역처럼 부르는 것이 완성에 나온다. 참조는 스크립트 엔진과 같은 것을 준다 -
/// 다르면 완성에는 뜨는데 돌리면 없는 이름이 된다.
///
/// 요청마다 문서의 글만 갈아 끼운다(<c>WithText</c>). 작업 공간을 다시 만들면 MEF 구성부터 다시 해 몇 초가 든다.
/// </remarks>
public sealed partial class RoslynCompletionSource : IScriptCompletionSource, IScriptClassifier, IScriptReferenceFinder
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly object _gate = new();
    private readonly Type _globalsType;
    private AdhocWorkspace? _workspace;
    private DocumentId? _documentId;

    /// <param name="globalsType">스크립트가 전역처럼 부르는 것들의 타입. 계획 모드는 SequenceScriptApi, 실시간은 LiveScriptApi.</param>
    public RoslynCompletionSource(Type globalsType) => _globalsType = globalsType;

    /// <summary>
    /// 파일 경로로 그 파일이 든 프로젝트의 컴파일 한 벌을 준다. 프로젝트가 아니면 null. 화면(워크벤치)이 꽂는다.
    /// </summary>
    /// <remarks>저장 안 한 탭의 글도 담아야 한다 - 방금 다른 탭에 만든 함수가 완성에 나와야 한다.</remarks>
    public Func<string, ScriptUnit?>? UnitFor { get; set; }

    /// <summary>
    /// 작업 공간의 문서에 이 글을 넣은 판과, 앞에 붙인 머리말 길이. 작업 공간 자체는 안 바뀐다 - 완성과 분류가 서로의 글을 밟지 않는다.
    /// </summary>
    /// <remarks>
    /// 프로젝트 파일이면 같은 프로젝트의 다른 소스를 <c>#load</c> 로 앞에 붙인다(실행 때와 같은 방식). 그러면 다른 파일의 함수가
    /// 완성과 색에 나온다. 자기 자신은 뺀다. 시작 파일이 아닌 파일에서는 시작 파일도 뺀다 - 실행 때 시작 파일은 맨 뒤라
    /// 다른 파일에서 그 변수를 볼 수 없다.
    /// </remarks>
    /// <param name="includeEntry">
    /// 시작 파일도 머리말에 넣을지. 참조를 셀 때만 켠다 - 도우미 파일의 함수를 시작 파일이 부르는 것도 세야 한다.
    /// 완성·색에서는 끈다(실행 때 시작 파일은 맨 뒤라 그 변수가 다른 파일에서 안 보인다).
    /// </param>
    private (Document Document, int Offset) DocumentFor(string? text, string? filePath, bool includeEntry = false)
    {
        var unit = filePath is null ? null : UnitFor?.Invoke(filePath);

        lock (_gate)
        {
            var workspace = EnsureWorkspace();
            var solution = workspace.CurrentSolution;
            var current = solution.GetDocument(_documentId!)!;

            if (unit is null) return (current.WithText(SourceText.From(text ?? string.Empty)), 0);

            var self = System.IO.Path.GetFullPath(filePath!);
            var sources = includeEntry && !string.IsNullOrEmpty(unit.EntryPath) ? unit.Sources.Append(unit.EntryPath) : unit.Sources;
            var loads = sources.Where(s => !string.Equals(System.IO.Path.GetFullPath(s), self, StringComparison.OrdinalIgnoreCase)).ToList();

            var prelude = new System.Text.StringBuilder();
            foreach (var load in loads) prelude.Append("#load \"").Append(load.Replace('\\', '/')).Append("\"\n");

            // 다른 탭에서 고친 글을 읽게 한다. 이 파일 자신은 문서 글로 들어가므로 빼도 된다.
            var baseDirectory = unit.ResourceRoot ?? System.IO.Path.GetDirectoryName(self);
            var project = solution.GetProject(_documentId!.ProjectId)!;
            var options = ((CSharpCompilationOptions)project.CompilationOptions!)
                .WithSourceReferenceResolver(new ScriptSourceResolver(baseDirectory, unit.OpenTexts))
                .WithMetadataReferenceResolver(Microsoft.CodeAnalysis.Scripting.ScriptMetadataResolver.Default.WithBaseDirectory(baseDirectory));

            solution = solution
                .WithProjectCompilationOptions(project.Id, options)
                .WithProjectMetadataReferences(project.Id, [.. BaseReferences, .. unit.References.Where(System.IO.File.Exists).Select(r => MetadataReference.CreateFromFile(r))])
                .WithDocumentFilePath(_documentId, self)
                .WithDocumentText(_documentId, SourceText.From(prelude + (text ?? string.Empty)));

            return (solution.GetDocument(_documentId)!, prelude.Length);
        }
    }

    private IReadOnlyList<MetadataReference>? _baseReferences;

    private IReadOnlyList<MetadataReference> BaseReferences => _baseReferences ??= References();

    /// <summary>
    /// 낱말마다 VS 와 같은 분류를 받아 편집기가 칠할 종류로 옮긴다.
    /// </summary>
    /// <remarks>
    /// 분류 이름은 VS 의 "글꼴 및 색" 항목 이름과 같다(<see cref="Microsoft.CodeAnalysis.Classification.ClassificationTypeNames"/>).
    /// 한 자리에 둘이 오기도 한다 - "static symbol" 같은 덧붙임 분류는 색이 아니라 표시라 건넌다.
    /// 구두점·연산자·공백은 안 준다. 본문색 그대로라 칠할 것이 없고, 토막 수만 몇 배로 는다.
    /// </remarks>
    public async Task<IReadOnlyList<ScriptToken>> ClassifyAsync(string text, CancellationToken token = default, string? filePath = null)
    {
        var (document, offset) = DocumentFor(text, filePath);
        var length = text?.Length ?? 0;

        var spans = await Microsoft.CodeAnalysis.Classification.Classifier
            .GetClassifiedSpansAsync(document, new TextSpan(offset, length), token)
            .ConfigureAwait(false);

        if (token.IsCancellationRequested) return [];

        var tokens = new List<ScriptToken>();

        foreach (var span in spans)
        {
            if (Microsoft.CodeAnalysis.Classification.ClassificationTypeNames.AdditiveTypeNames.Contains(span.ClassificationType)) continue;
            if (span.TextSpan.Start < offset) continue;

            if (KindOf(span.ClassificationType) is { } kind)
                tokens.Add(new ScriptToken(span.TextSpan.Start - offset, span.TextSpan.Length, kind));
        }

        return tokens;
    }

    private static ScriptTokenKind? KindOf(string classification)
    {
        switch (classification)
        {
            case "keyword - control": return ScriptTokenKind.ControlKeyword;
            case "keyword": case "preprocessor keyword": return ScriptTokenKind.Keyword;

            case "method name": case "extension method name": return ScriptTokenKind.Method;

            case "field name": case "property name": case "event name": case "constant name": case "enum member name":
                return ScriptTokenKind.Member;

            case "local name": case "parameter name": case "range variable name":
                return ScriptTokenKind.Local;

            case "class name": case "record class name": case "struct name": case "record struct name":
            case "interface name": case "enum name": case "delegate name": case "type parameter name": case "module name":
                return ScriptTokenKind.Type;

            case "namespace name": case "label name": return ScriptTokenKind.Plain;

            case "string": case "string - verbatim": case "string - escape character": return ScriptTokenKind.String;
            case "number": return ScriptTokenKind.Number;
        }

        return classification.StartsWith("comment", StringComparison.Ordinal) || classification.StartsWith("xml doc comment", StringComparison.Ordinal)
            ? ScriptTokenKind.Comment
            : null;
    }

    public async Task<IReadOnlyList<CompletionSuggestion>> GetAsync(string text, int position, CancellationToken token = default, string? filePath = null)
    {
        var (document, offset) = DocumentFor(text, filePath);

        var service = CompletionService.GetService(document);
        if (service is null) return [];

        var clamped = Math.Clamp(position, 0, text?.Length ?? 0) + offset;
        var list = await service.GetCompletionsAsync(document, clamped, cancellationToken: token).ConfigureAwait(false);

        if (token.IsCancellationRequested) return [];

        var suggestions = new List<CompletionSuggestion>(list.ItemsList.Count);

        foreach (var item in list.ItemsList)
        {
            // 컴파일러가 만든 이름(<>, __)은 사람이 칠 것이 아니다.
            if (item.DisplayText.StartsWith('<') || item.DisplayText.StartsWith("__", StringComparison.Ordinal)) continue;

            suggestions.Add(new CompletionSuggestion(item.DisplayText, item.DisplayText, Describe(item.Tags)));
        }

        return suggestions;
    }

    /// <summary>첫 호출이 유독 느리다(MEF 구성 + 참조 읽기). 화면이 뜰 때 미리 한 번 부른다.</summary>
    public Task WarmUpAsync() => Task.Run(async () =>
    {
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await GetAsync("T", 1).ConfigureAwait(false);
            await ClassifyAsync("var a = 1;").ConfigureAwait(false);
            Logger.Debug($"C# 완성·분류 예열 {watch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "C# 완성 예열에 실패했다");
        }
    });

    private AdhocWorkspace EnsureWorkspace()
    {
        if (_workspace is not null) return _workspace;

        var workspace = new AdhocWorkspace(MefHostServices.DefaultHost);

        var project = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            name: "스크립트",
            assemblyName: "스크립트",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: Usings),
            parseOptions: new CSharpParseOptions(kind: SourceCodeKind.Script),
            metadataReferences: References(),
            isSubmission: true,
            hostObjectType: _globalsType);

        workspace.AddProject(project);

        // 문서도 스크립트 종류여야 한다. 프로젝트의 파싱 옵션과 별개로 DocumentInfo 의 SourceCodeKind 는 기본이 Regular 라,
        // 그냥 AddDocument 하면 "스크립트 코드만 제출할 수 있다" 며 완성이 터진다(실측).
        var info = DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id),
            "스크립트.csx",
            sourceCodeKind: SourceCodeKind.Script,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(string.Empty), VersionStamp.Create())));

        var document = workspace.AddDocument(info);

        _documentId = document.Id;
        _workspace = workspace;

        return workspace;
    }

    /// <summary>스크립트 엔진(RoslynScriptEngine)의 using 과 같아야 한다.</summary>
    private static readonly string[] Usings = ["System", "Minguk.Tools.Input", "Minguk.Tools.Input.Scripting", "Minguk.Tools.Input.Scripting.Live"];

    /// <summary>스크립트 엔진이 보는 것과 같은 어셈블리들. 이미 올라온 것만 - 파일에서 새로 읽지 않는다.</summary>
    private static IReadOnlyList<MetadataReference> References()
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Linq", "System.Console",
            "System.Text.RegularExpressions", "netstandard", "Minguk.Tools"
        };

        var references = new List<MetadataReference>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) continue;
            if (!wanted.Contains(assembly.GetName().Name ?? string.Empty)) continue;

            try
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"참조를 못 읽었다: {assembly.GetName().Name}");
            }
        }

        return references;
    }

    /// <summary>Roslyn 의 태그("Method", "Keyword", "Class"...)를 한 낱말 우리말로.</summary>
    private static string Describe(System.Collections.Immutable.ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
        {
            switch (tag)
            {
                case "Method": case "ExtensionMethod": return "메서드";
                case "Property": return "속성";
                case "Field": return "필드";
                case "Local": return "지역 변수";
                case "Parameter": return "매개변수";
                case "Keyword": return "키워드";
                case "Class": return "클래스";
                case "Structure": return "구조체";
                case "Enum": return "열거형";
                case "EnumMember": return "열거값";
                case "Interface": return "인터페이스";
                case "Namespace": return "네임스페이스";
                case "Event": return "이벤트";
                case "Delegate": return "대리자";
                case "Snippet": return "조각";
            }
        }

        return string.Empty;
    }
}
