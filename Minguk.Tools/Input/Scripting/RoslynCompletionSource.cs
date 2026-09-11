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
/// <b>왜 따로 무거운 패키지를</b> - API 표만으로는 <c>몹.중심x</c> 나 <c>string.Length</c> 같은 멤버, <c>var</c>·<c>while</c>
/// 같은 문법이 안 나온다. Roslyn 은 실제 컴파일러라 전역(<see cref="Live.LiveScriptApi"/> 의 메서드)부터 지역 변수의
/// 멤버까지 다 안다. 대신 Features 패키지가 수십 MB 고 첫 호출이 1~2초다 - 그래서 설계에서 4단계로 미뤄 두었다.
///
/// 문서는 <b>스크립트 종류</b>(SourceCodeKind.Script)에 <b>전역 타입</b>(hostObjectType)을 실은 제출(submission) 프로젝트다.
/// 그래야 <c>Type("...")</c> 처럼 전역처럼 부르는 것이 완성에 나온다. 참조는 스크립트 엔진과 같은 것을 준다 -
/// 다르면 완성에는 뜨는데 돌리면 없는 이름이 된다.
///
/// 요청마다 문서의 글만 갈아 끼운다(<c>WithText</c>). 작업 공간을 다시 만들면 MEF 구성부터 다시 해 몇 초가 든다.
/// </remarks>
public sealed class RoslynCompletionSource : IScriptCompletionSource
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly object _gate = new();
    private readonly Type _globalsType;
    private AdhocWorkspace? _workspace;
    private DocumentId? _documentId;

    /// <param name="globalsType">스크립트가 전역처럼 부르는 것들의 타입. 계획 모드는 SequenceScriptApi, 실시간은 LiveScriptApi.</param>
    public RoslynCompletionSource(Type globalsType) => _globalsType = globalsType;

    public async Task<IReadOnlyList<CompletionSuggestion>> GetAsync(string text, int position, CancellationToken token = default)
    {
        Document document;

        lock (_gate)
        {
            var workspace = EnsureWorkspace();
            var current = workspace.CurrentSolution.GetDocument(_documentId!)!;

            document = current.WithText(SourceText.From(text ?? string.Empty));
        }

        var service = CompletionService.GetService(document);
        if (service is null) return [];

        var clamped = Math.Clamp(position, 0, text?.Length ?? 0);
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
            Logger.Debug($"C# 완성 예열 {watch.ElapsedMilliseconds}ms");
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
