using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Minguk.Tools.Input.Scripting.Live;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 로드해 둔 빌드 결과물 - IL 바이트와, 리소스를 찾을 폴더(파일 옆).
/// </summary>
/// <param name="Assembly">빌드된 IL(PE) 바이트.</param>
/// <param name="ResourceRoot">리소스(<c>리소스경로("적.png")</c>)를 찾는 폴더. 대개 <c>.mtsx</c> 가 든 폴더.</param>
/// <param name="Name">사람이 보는 이름(확장자 뺀 파일 이름).</param>
public sealed record CompiledPlayable(byte[] Assembly, string? ResourceRoot, string Name);

/// <summary>
/// 빌드된 IL(<see cref="CompiledScriptBuilder"/> 이 만든 <c>__Compiled</c>)을 로드해 돌린다.
/// </summary>
/// <remarks>
/// 엔진·언어와 무관하다 - 컴파일은 이미 끝났고, 여기서는 <c>Assembly.Load</c> + 진입점 호출뿐이다. 그래서 플레이어는
/// 언어를 고를 것도, Roslyn 을 부를 것도 없이 이것만 부른다. 진입점 이름이 우리 것이라 Roslyn 을 올려도 안 깨진다.
/// </remarks>
public static class CompiledScriptRunner
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// IL 을 로드해 실시간으로 돌린다. 만들어진 인스턴스(<see cref="LiveScriptApi"/> 파생)를 <paramref name="onCreated"/> 로
    /// 넘겨, 화면이 비상 정지에서 <c>ReleaseAll</c> 을 부를 수 있게 한다.
    /// </summary>
    public static async Task<IReadOnlyList<ScriptError>> RunAsync(
        byte[] assembly, LiveScriptHost host, Action<LiveScriptApi> onCreated, CancellationToken token = default)
    {
        // 스크립트가 참조한 바깥 DLL 은 빌드가 .mtsx 옆에 복사해 둔다. IL 은 이름으로만 가리키므로 런타임이 못 찾을 때 그 폴더를 보게 한다.
        // 돌리는 동안만 건다 - 형식은 메서드를 처음 부를 때(JIT) 올라오므로 로드 직후에 풀면 늦게 찾는 DLL 을 놓친다.
        var probe = host.ResourceRoot;
        ResolveEventHandler? resolver = string.IsNullOrEmpty(probe) ? null : (_, args) => ResolveNextTo(probe, args);

        if (resolver is not null) AppDomain.CurrentDomain.AssemblyResolve += resolver;

        try
        {
            return await RunLoadedAsync(assembly, host, onCreated, token);
        }
        finally
        {
            if (resolver is not null) AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    private static async Task<IReadOnlyList<ScriptError>> RunLoadedAsync(
        byte[] assembly, LiveScriptHost host, Action<LiveScriptApi> onCreated, CancellationToken token)
    {
        LiveScriptApi api;
        MethodInfo run;

        try
        {
            var loaded = Assembly.Load(assembly);
            var type = loaded.GetType(CompiledScriptBuilder.EntryTypeName)
                ?? throw new InvalidOperationException($"{CompiledScriptBuilder.EntryTypeName} 타입이 없습니다.");

            api = (LiveScriptApi)Activator.CreateInstance(type, host, token)!;
            run = type.GetMethod(CompiledScriptBuilder.EntryMethodName)
                ?? throw new InvalidOperationException($"{CompiledScriptBuilder.EntryMethodName} 메서드가 없습니다.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "빌드된 스크립트를 로드하지 못했다");
            return [new ScriptError(0, $"빌드된 스크립트를 로드하지 못했습니다: {ex.Message}")];
        }

        onCreated(api);

        try
        {
            await (Task)run.Invoke(api, null)!;
            return [];
        }
        catch (Exception ex)
        {
            // 리플렉션은 예외를 TargetInvocationException 으로 감싼다 - 안엣것을 본다.
            var inner = (ex as TargetInvocationException)?.InnerException ?? ex;

            if (api.Outcome == LiveScriptOutcome.Stopped || token.IsCancellationRequested) return [];
            if (api.Outcome == LiveScriptOutcome.Guarded) return [new ScriptError(0, api.GuardMessage ?? "안전장치가 막았습니다.")];

            if (MissingReference(inner) is { } missing)
                return [new ScriptError(0, $"참조한 DLL '{missing}' 을(를) 찾지 못했습니다 - .mtsx 옆({host.ResourceRoot})에 두세요. 스크립트 화면에서 다시 빌드하면 같이 복사됩니다.")];

            return [new ScriptError(0, $"스크립트가 도는 중에 멈췄습니다: {inner.Message}")];
        }
    }

    /// <summary>그 폴더에 <c>&lt;어셈블리 이름&gt;.dll</c> 이 있으면 올린다. 없으면 null - 다음 찾기(다른 화면의 것 등)에 넘긴다.</summary>
    private static Assembly? ResolveNextTo(string directory, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrEmpty(name)) return null;

        // 이미 올라온 같은 이름이 있으면 그것 - 한 프로세스에 사본 둘을 올리면 형식이 서로 안 맞는다.
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return loaded;

        var candidate = System.IO.Path.Combine(directory, name + ".dll");
        if (!System.IO.File.Exists(candidate)) return null;

        Logger.Info($"빌드된 스크립트의 참조 DLL 을 옆 폴더에서 올린다: {candidate}");

        return Assembly.LoadFrom(candidate);
    }

    /// <summary>못 찾은 것이 어셈블리면 그 이름. JIT 이 예외를 TypeInitialization·TargetInvocation 으로 감싸기도 해 안까지 본다.</summary>
    private static string? MissingReference(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            var file = ex switch
            {
                System.IO.FileNotFoundException notFound => notFound.FileName,
                System.IO.FileLoadException loadFailed => loadFailed.FileName,
                _ => null
            };

            // 스크립트가 없는 파일을 읽어도 FileNotFoundException 이다 - 그때 FileName 은 경로다. 어셈블리 표시 이름("x, Version=…")만 본다.
            if (file is not null && file.Contains("Version=", StringComparison.Ordinal))
                return new AssemblyName(file).Name;
        }

        return null;
    }
}
