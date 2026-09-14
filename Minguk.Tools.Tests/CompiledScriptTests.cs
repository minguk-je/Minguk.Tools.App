using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Minguk.Tools.Capture;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Live;

namespace Minguk.Tools.Tests;

/// <summary>
/// 프로젝트를 .NET DLL(IL)로 빌드하고, 그 IL 을 로드해 돌린다. 가짜 어댑터라 입력은 실제로 안 나간다.
/// </summary>
/// <remarks>
/// 여기서 보는 것 - 두 파일짜리(조각의 함수를 시작 파일이 부름)가 IL 로 빌드되고, 그 IL 을 로드해 돌리면 조각의
/// 함수가 불리고 입력이 나가는지. 진입점(<c>__Compiled.__Run</c>)이 우리 것이라 Roslyn 내부에 안 기댄다.
/// </remarks>
internal static partial class Program
{
    private static void TestCompiledScript()
    {
        var monitor = CaptureTarget.EnumerateMonitors().FirstOrDefault();

        if (monitor is null)
        {
            Check("빌드→실행 (모니터 없음, 건너뜀)", true, "");
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "minguk-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            // 조각(공용.csx)의 함수를 시작 파일(main.csx)이 부른다. using·타입 선언도 섞어 조립기를 시험한다.
            var helperPath = Path.Combine(folder, "공용.csx");
            var entryPath = Path.Combine(folder, "main.csx");

            var helper = "using System.Text;\n\nvoid 인사() { 출력(\"안녕\"); }\n\nclass 도우미 { public static int 값 => 7; }\n";
            var entry = "인사();\n출력(\"값=\" + 도우미.값);\nType(\"a\");\nEnter();\n키(\"F\");\n";

            File.WriteAllText(helperPath, helper);
            File.WriteAllText(entryPath, entry);

            var unit = new ScriptUnit(
                entryPath, entry,
                [helperPath], [],
                new Dictionary<string, string> { [helperPath] = helper, [entryPath] = entry },
                folder);

            // 조립한 소스에 우리 진입점이 있는가(감싸기가 도는지).
            var assembled = CompiledScriptBuilder.Assemble(unit);
            Check("조립한 소스에 __Compiled·__Run 이 있다",
                  assembled.Contains("class __Compiled") && assembled.Contains("__Run()") && assembled.Contains("인사()"),
                  assembled.Length + "자");

            var engine = new RoslynScriptEngine();

            var (bytes, errors) = ((ICompiledScriptEngine)engine).BuildAsync(unit, "사격장").GetAwaiter().GetResult();

            Check("빌드가 IL 바이트를 낸다", bytes is { Length: > 0 } && errors.Count == 0,
                  bytes is null ? (errors.Count > 0 ? errors[0].ToString() : "(바이트 없음)") : $"{bytes.Length:N0}바이트");

            if (bytes is null) { engine.Dispose(); return; }

            // 진짜 PE 인가 - MZ 로 시작한다.
            Check("빌드 결과가 PE 바이너리다(텍스트 편집기로는 못 봄)", bytes[0] == (byte)'M' && bytes[1] == (byte)'Z', $"머리 {bytes[0]:X2} {bytes[1]:X2}");

            // 로드해서 돌린다 - 조각의 함수가 불리고, 입력이 나가는지.
            var adapter = new RecordingAdapter();
            var printed = new List<string>();
            var host = new LiveScriptHost
            {
                Service = new InputService(adapter),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor),
                Print = printed.Add,
                Watch = (_, _) => { },
                HoldTimeMs = 1,
                ResourceRoot = folder
            };

            LiveScriptApi? captured = null;
            var runErrors = ((ICompiledScriptEngine)engine)
                .RunCompiledAsync(bytes, host, api => captured = api, CancellationToken.None)
                .GetAwaiter().GetResult();

            Check("빌드된 IL 을 로드해 돌린다 - 조각 함수·타입·입력이 다 산다",
                  runErrors.Count == 0 && printed.Contains("안녕") && printed.Contains("값=7") && adapter.Calls.Contains("Press 70") && adapter.Calls.Contains("Release 70"),
                  runErrors.Count > 0 ? runErrors[0].ToString() : $"출력 [{string.Join(", ", printed)}] · 키 {adapter.Calls.Count(c => c.Contains("70"))}건");

            Check("돌린 인스턴스를 잡아 비상 정지에 넘길 수 있다(LiveScriptApi 파생)",
                  captured is not null, captured?.GetType().Name ?? "(null)");

            // .mtsx 로 써서 파일로 알아보고 다시 로드해 돌린다(플레이어가 파일에서 하는 것).
            var mtsx = Path.Combine(folder, "사격장" + ScriptFiles.CompiledExtension);
            File.WriteAllBytes(mtsx, bytes);

            var item = Minguk.Tools.ViewModels.ScriptFileItem.From(mtsx);
            Check(".mtsx 는 빌드된 것으로 알아본다", ScriptFiles.IsCompiledPath(mtsx) && item.IsCompiled && !item.IsProject, item.Name);

            var reprinted = new List<string>();
            var reHost = new LiveScriptHost
            {
                Service = new InputService(new RecordingAdapter()),
                RequiresForeground = false,
                Target = () => monitor,
                Hub = new FakeHub(monitor),
                Print = reprinted.Add,
                Watch = (_, _) => { },
                HoldTimeMs = 1,
                ResourceRoot = folder
            };

            var reErrors = CompiledScriptRunner.RunAsync(File.ReadAllBytes(mtsx), reHost, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
            Check("파일에서 다시 로드해도 그대로 돈다", reErrors.Count == 0 && reprinted.Contains("안녕"), string.Join(", ", reprinted));

            TestCompiledExternalReference(folder, monitor);

            engine.Dispose();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// 스크립트가 <c>#r</c> 로 문 바깥 DLL - 빌드가 옆에 복사하고, 플레이어가 그 폴더에서 찾는가. 없으면 무엇을 어디 두라고 말하는가.
    /// </summary>
    /// <remarks>
    /// 이것이 없을 때는 빌드는 되는데 플레이에서 "파일을 찾을 수 없다" 로만 멈췄다. 못 찾는 경우를 먼저 본다 - 한 번 올라온 어셈블리는
    /// 프로세스에 남아 뒤의 검사가 늘 찾게 된다.
    /// </remarks>
    private static void TestCompiledExternalReference(string folder, CaptureTarget monitor)
    {
        const string assemblyName = "MingukExternalProbe";

        var libs = Path.Combine(folder, "Libs");
        Directory.CreateDirectory(libs);

        var dllPath = Path.Combine(libs, assemblyName + ".dll");
        var library = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            assemblyName,
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public static class ExternalProbe { public static int Value() => 42; }")],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        Microsoft.CodeAnalysis.Emit.EmitResult emitted;
        using (var stream = File.Create(dllPath)) emitted = library.Emit(stream);

        if (!emitted.Success)
        {
            Check("바깥 DLL 참조 (시험용 DLL 을 못 만듦)", false, string.Join(" / ", emitted.Diagnostics.Take(3)));
            return;
        }

        var entryPath = Path.Combine(folder, "외부.csx");
        var entry = $"#r \"Libs/{assemblyName}.dll\"\n출력(\"외부=\" + ExternalProbe.Value());\n";
        File.WriteAllText(entryPath, entry);

        var unit = new ScriptUnit(entryPath, entry, [], [], new Dictionary<string, string>(), folder);

        var (bytes, errors) = CompiledScriptBuilder.Build(unit, "외부");
        Check("#r 로 문 바깥 DLL 을 참조해 빌드된다", bytes is not null && errors.Count == 0, errors.Count > 0 ? errors[0].ToString() : $"{bytes?.Length:N0}바이트");
        if (bytes is null) return;

        var external = CompiledScriptBuilder.ExternalReferences(unit);
        Check("바깥 DLL 로 잡히는 것은 그것 하나(앱·런타임 DLL 은 뺀다)",
              external.Count == 1 && string.Equals(external[0], dllPath, StringComparison.OrdinalIgnoreCase),
              string.Join(", ", external.Select(Path.GetFileName)));

        LiveScriptHost HostAt(string root, List<string> printed) => new()
        {
            Service = new InputService(new RecordingAdapter()),
            RequiresForeground = false,
            Target = () => monitor,
            Hub = new FakeHub(monitor),
            Print = printed.Add,
            Watch = (_, _) => { },
            HoldTimeMs = 1,
            ResourceRoot = root
        };

        // 옆에 DLL 이 없는 bin - 무엇을 못 찾았는지 이름을 대고 멈춰야 한다.
        var emptyBin = Path.Combine(folder, "bin-빈");
        Directory.CreateDirectory(emptyBin);

        var missingErrors = CompiledScriptRunner.RunAsync(bytes, HostAt(emptyBin, []), _ => { }, CancellationToken.None).GetAwaiter().GetResult();
        Check("옆에 DLL 이 없으면 그 DLL 이름과 둘 자리를 말한다",
              missingErrors.Count == 1 && missingErrors[0].Message.Contains(assemblyName) && missingErrors[0].Message.Contains(emptyBin),
              missingErrors.Count > 0 ? missingErrors[0].Message : "(오류 없음)");

        // 빌드가 하는 복사 - 그 폴더에서 돌리면 찾는다.
        var bin = Path.Combine(folder, "bin");
        var copied = CompiledScriptBuilder.CopyReferences(unit, bin);

        var printed = new List<string>();
        var runErrors = CompiledScriptRunner.RunAsync(bytes, HostAt(bin, printed), _ => { }, CancellationToken.None).GetAwaiter().GetResult();

        Check("빌드가 바깥 DLL 을 옆에 복사하고, 플레이어가 그 폴더에서 찾아 돈다",
              copied.Count == 1 && File.Exists(Path.Combine(bin, assemblyName + ".dll")) && runErrors.Count == 0 && printed.Contains("외부=42"),
              runErrors.Count > 0 ? runErrors[0].ToString() : $"복사 [{string.Join(", ", copied)}] · 출력 [{string.Join(", ", printed)}]");
    }
}
