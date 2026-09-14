using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 스크립트 프로젝트(<see cref="ScriptUnit"/>)를 <b>업그레이드에 안 흔들리는 .NET DLL</b> 로 빌드한다.
/// </summary>
/// <remarks>
/// <b>왜 스크립팅 emit 이 아닌가</b> - <c>CSharpScript</c> 를 그대로 emit 하면 실행에 Roslyn 내부의 제출 factory
/// (<c>&lt;Factory&gt;</c>)를 리플렉션으로 불러야 하는데, 그건 공개 API 가 아니라 버전이 바뀌면 예전에 빌드해 둔
/// 파일이 안 돌 수 있다. 그래서 글을 우리가 정한 진입점으로 감싸 <b>일반 C# 컴파일</b>로 만든다:
/// <code>
/// public class __Compiled : LiveScriptApi {
///     public __Compiled(LiveScriptHost h, CancellationToken t) : base(h, t) { }
///     public async Task __Run() { /* 프로젝트 글 전부 */ }
/// }
/// </code>
/// 타입·메서드·생성자 이름이 전부 우리 것이라, 플레이어는 <c>Activator.CreateInstance</c> + <c>__Run()</c> 로
/// 부른다(<see cref="RoslynScriptEngine.RunCompiledAsync"/>). 스크립트가 부르는 <c>목표()</c>·<c>출력()</c> 은
/// 전부 <see cref="Live.LiveScriptApi"/> 의 public 멤버라 상속으로 이름만으로 잡힌다 - 스크립팅 전역과 같다.
///
/// <b>글을 어떻게 옮기나</b> - 각 소스를 스크립트로 파싱해 <c>using</c> 은 파일 맨 위로 모으고(중복 제거),
/// 최상위 문장·지역 함수는 <c>__Run</c> 몸으로, 타입·대리자 선언은 <c>__Compiled</c> 의 중첩 멤버로 옮긴다.
/// <c>#load</c>·<c>#r</c>·주석은 노드의 <c>ToString()</c> 이 앞뒤 트리비아를 떼어 자연히 빠진다 -
/// 프로젝트 소스는 이미 <see cref="ScriptUnit.Sources"/> 로 다 들어오므로 손으로 적은 <c>#load</c> 는 안 쓴다.
/// 사람이 적은 <c>#r "x.dll"</c> 은 정규식으로 긁어 참조에 더한다.
///
/// <b>바깥 DLL 은 합치지 않는다</b> - IL 이 이름으로만 가리키므로 빌드가 결과물 옆에 복사하고(<see cref="CopyReferences"/>)
/// 플레이어가 그 폴더에서 찾는다(<see cref="CompiledScriptRunner"/>).
/// </remarks>
public static class CompiledScriptBuilder
{
    /// <summary>빌드가 만드는 타입·진입점 이름. 플레이어가 이 이름으로 찾는다 - 절대 바꾸지 않는다(옛 파일이 안 돈다).</summary>
    public const string EntryTypeName = "__Compiled";
    public const string EntryMethodName = "__Run";

    /// <summary>늘 여는 이름들 - 실시간 스크립팅(<c>LiveScriptOptions</c>)과 같은 범위.</summary>
    private static readonly string[] DefaultUsings =
    [
        "System", "System.Collections.Generic", "System.Linq", "System.Threading", "System.Threading.Tasks",
        "Minguk.Tools.Input", "Minguk.Tools.Input.Scripting", "Minguk.Tools.Input.Scripting.Live"
    ];

    private static readonly Regex ReferenceDirective = new("""^\s*#r\s+"([^"]+)"\s*$""", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// 프로젝트 글을 <c>__Compiled</c> 로 감싼 소스를 만든다. 컴파일은 <see cref="Build"/> 가 한다 - 이 문자열만 따로 볼 수 있게 나눴다.
    /// </summary>
    public static string Assemble(ScriptUnit unit)
    {
        var usings = new List<string>();
        var seenUsings = new HashSet<string>(StringComparer.Ordinal);

        void AddUsing(string text)
        {
            if (seenUsings.Add(text)) usings.Add(text);
        }

        foreach (var name in DefaultUsings) AddUsing($"using {name};");

        var body = new StringBuilder();
        var members = new StringBuilder();

        foreach (var (name, text) in OrderedSources(unit))
        {
            var root = (CompilationUnitSyntax)CSharpSyntaxTree
                .ParseText(text, new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetRoot();

            foreach (var directive in root.Usings)
                AddUsing(directive.ToString());

            var hasCode = root.Members.Any(m => m is not GlobalStatementSyntax { Statement: EmptyStatementSyntax });

            if (hasCode) body.Append("        // ── ").Append(name).Append(" ──\n");

            foreach (var member in root.Members)
            {
                if (member is GlobalStatementSyntax statement)
                {
                    // ToString() 은 앞뒤 트리비아(#load·#r·주석)를 떼어 낸다.
                    body.Append("        ").Append(statement.Statement.ToString().Replace("\n", "\n        ")).Append('\n');
                }
                else
                {
                    // 타입·대리자 선언은 __Compiled 안의 중첩 멤버로.
                    members.Append("    ").Append(member.ToString().Replace("\n", "\n    ")).Append("\n\n");
                }
            }
        }

        var builder = new StringBuilder();

        foreach (var directive in usings) builder.Append(directive).Append('\n');

        builder.Append('\n');
        builder.Append("#pragma warning disable\n");
        builder.Append("public class ").Append(EntryTypeName)
               .Append(" : global::Minguk.Tools.Input.Scripting.Live.LiveScriptApi\n{\n");
        builder.Append("    public ").Append(EntryTypeName)
               .Append("(global::Minguk.Tools.Input.Scripting.Live.LiveScriptHost __host, global::System.Threading.CancellationToken __token) : base(__host, __token) { }\n\n");
        builder.Append("    public async global::System.Threading.Tasks.Task ").Append(EntryMethodName).Append("()\n    {\n");
        builder.Append(body);
        builder.Append("    }\n");

        if (members.Length > 0) builder.Append('\n').Append(members);

        builder.Append("}\n");

        return builder.ToString();
    }

    /// <summary>프로젝트 글을 컴파일해 IL 바이트를 낸다. 틀린 곳이 있으면 바이트는 null 이고 오류가 온다.</summary>
    public static (byte[]? Assembly, IReadOnlyList<ScriptError> Errors) Build(ScriptUnit unit, string assemblyName)
    {
        var source = Assemble(unit);

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));

        var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable,
                deterministic: true)
            .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>
            {
                // async 인데 await 이 없는 스크립트는 흔하다(입력만 보내고 끝). 경고일 뿐 막지 않는다.
                ["CS1998"] = ReportDiagnostic.Suppress
            });

        var compilation = CSharpCompilation.Create(Sanitize(assemblyName), [tree], References(unit), options);

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new ScriptError(d.Location.GetLineSpan().StartLinePosition.Line + 1, d.GetMessage()))
                .ToList();

            // 감싼 소스에서만 나는 오류(우리 조립기 탓)일 수 있어, 하나도 못 맞히면 그 사실을 말한다.
            if (errors.Count == 0) errors.Add(new ScriptError(0, "빌드에 실패했지만 오류를 읽지 못했습니다."));

            return (null, errors);
        }

        return (stream.ToArray(), []);
    }

    /// <summary>소스 순서 - 조각들(적힌 순서) 다음에 시작 파일. 시작 파일의 최상위 문장이 마지막에 돈다.</summary>
    private static IEnumerable<(string Name, string Text)> OrderedSources(ScriptUnit unit)
    {
        foreach (var path in unit.Sources)
            yield return (Path.GetFileName(path), ReadSource(unit, path));

        yield return (string.IsNullOrEmpty(unit.EntryPath) ? "시작" : Path.GetFileName(unit.EntryPath), unit.EntryText);
    }

    private static string ReadSource(ScriptUnit unit, string path)
        => unit.OpenTexts.TryGetValue(path, out var open) ? open
            : File.Exists(path) ? File.ReadAllText(path)
            : string.Empty;

    /// <summary>
    /// 컴파일 참조. 스크립팅과 달리 일반 컴파일은 참조를 손으로 다 줘야 한다 - 지금 프로세스에 올라온 것을
    /// 전부 준다(넘치게 주는 것은 괜찮다). 거기에 프로젝트가 적은 참조와 소스에 손으로 적은 <c>#r</c> 을 더한다.
    /// </summary>
    private static IReadOnlyList<MetadataReference> References(ScriptUnit unit)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(string? location)
        {
            if (string.IsNullOrEmpty(location) || !File.Exists(location) || !seen.Add(location)) return;

            try { references.Add(MetadataReference.CreateFromFile(location)); }
            catch (Exception) { /* 못 읽는 참조는 건너뛴다 - 대개 없어도 컴파일된다 */ }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.IsDynamic) Add(assembly.Location);

        foreach (var reference in unit.References) Add(reference);

        foreach (var (_, text) in OrderedSources(unit))
            foreach (Match match in ReferenceDirective.Matches(text))
                Add(ResolveReference(unit, match.Groups[1].Value));

        return references;
    }

    /// <summary>
    /// 스크립트가 참조한 DLL 중 <b>이 앱에 딸려 오지 않는 것</b> - 프로젝트의 참조 항목과 손으로 적은 <c>#r</c>. 있는 파일만.
    /// </summary>
    /// <remarks>
    /// 빌드한 IL 은 이 DLL 을 이름으로만 가리킨다. 옆에 없으면 플레이에서 로드하다 실패한다 - 그래서 빌드가 <see cref="CopyReferences"/> 로 옆에 둔다.
    /// 앱 폴더·.NET 런타임 폴더에 있는 것(Minguk.Tools·System.*)은 뺀다 - 이미 올라와 있고, 복사하면 판이 다른 사본이 끼어든다.
    /// </remarks>
    public static IReadOnlyList<string> ExternalReferences(ScriptUnit unit)
    {
        var appFolder = Path.GetFullPath(AppContext.BaseDirectory);
        var runtimeFolder = Path.GetDirectoryName(typeof(object).Assembly.Location);

        bool Shipped(string path)
        {
            var folder = Path.GetDirectoryName(path) ?? string.Empty;

            return folder.StartsWith(appFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrEmpty(runtimeFolder) && string.Equals(folder, runtimeFolder, StringComparison.OrdinalIgnoreCase));
        }

        var found = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var full = Path.GetFullPath(path);

            if (Shipped(full) || found.Contains(full, StringComparer.OrdinalIgnoreCase)) return;

            found.Add(full);
        }

        foreach (var reference in unit.References) Add(reference);

        foreach (var (_, text) in OrderedSources(unit))
            foreach (Match match in ReferenceDirective.Matches(text))
                Add(ResolveReference(unit, match.Groups[1].Value));

        return found;
    }

    /// <summary>
    /// 바깥 DLL(<see cref="ExternalReferences"/>)을 빌드 결과물 폴더로 복사한다. 복사한 파일 이름들을 돌려준다.
    /// </summary>
    /// <remarks>
    /// 파일 이름만으로 둔다 - 플레이어(<see cref="CompiledScriptRunner"/>)가 어셈블리 이름 + <c>.dll</c> 로 그 폴더에서 찾는다.
    /// 이름이 같은 DLL 이 두 곳에서 오면 먼저 온 것만 둔다 - 한 폴더에 둘 다 둘 수 없고, 어느 쪽이 이겼는지 모르게 덮으면 안 된다.
    /// </remarks>
    public static IReadOnlyList<string> CopyReferences(ScriptUnit unit, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var copied = new List<string>();

        foreach (var path in ExternalReferences(unit))
        {
            var name = Path.GetFileName(path);

            if (copied.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

            var destination = Path.Combine(outputDirectory, name);

            if (!string.Equals(Path.GetFullPath(destination), path, StringComparison.OrdinalIgnoreCase))
                File.Copy(path, destination, overwrite: true);

            copied.Add(name);
        }

        return copied;
    }

    private static string ResolveReference(ScriptUnit unit, string reference)
    {
        if (Path.IsPathRooted(reference)) return reference;

        var root = unit.ResourceRoot ?? Path.GetDirectoryName(unit.EntryPath);
        return string.IsNullOrEmpty(root) ? reference : Path.GetFullPath(Path.Combine(root, reference));
    }

    /// <summary>어셈블리 이름에 못 쓰는 글자를 뺀다. 비면 기본 이름.</summary>
    private static string Sanitize(string name)
    {
        var cleaned = new string([.. (name ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-')]);
        return cleaned.Length == 0 ? "CompiledScript" : cleaned;
    }
}
