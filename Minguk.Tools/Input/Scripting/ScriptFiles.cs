using System;
using System.IO;
using System.Linq;

namespace Minguk.Tools.Input.Scripting;

/// <summary>
/// 스크립트를 파일로 다룰 때 쓰는 것들 - 확장자, 대화 상자 필터, 기본 폴더.
/// </summary>
/// <remarks>
/// <b>왜 언어마다 확장자를 나누는가</b>
///
/// 파일만 보고 무슨 언어인지 알 수 있어야 한다. 하나로 쓰면(<c>.script</c> 같은) 열 때마다
/// 사람이 언어를 다시 골라야 하고, 잘못 고르면 문법 오류만 잔뜩 뜬다.
/// 확장자를 쓰면 여는 쪽이 알아서 맞출 수 있고, 편집기·형상관리 같은 다른 도구도 알아본다.
///
/// C# 스크립트는 <c>.csx</c> 다. <c>.cs</c> 가 아니다 - 여기 든 것은 클래스가 아니라
/// 곧바로 도는 문장들이라 컴파일러가 보는 것이 다르다. Roslyn 스크립팅의 관례를 따른다.
/// </remarks>
public static class ScriptFiles
{
    /// <summary>
    /// 스크립트를 두는 기본 자리. Automation 에서 프로젝트를 골랐으면 <b>그 프로젝트 폴더</b>, 아니면 옛 자리(없으면 만든다).
    /// </summary>
    /// <remarks>
    /// 플레이 목록·열기/저장 대화 상자가 다 이것을 본다. 옛 자리(<c>%AppData%\Scripts</c>)에 두면 솔루션으로 옮긴 뒤 플레이 목록이
    /// 비었다(실측 2026-09-14 - 스크립트가 사격장 폴더로 옮겨 갔다).
    /// </remarks>
    public static string DefaultDirectory
    {
        get
        {
            if (global::Minguk.Tools.Projects.SolutionWorkspace.StartupDirectory is { } project && Directory.Exists(project))
                return project;

            var path = Path.Combine(Helper.UserDataPaths.Root, "Scripts");

            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception)
            {
                // 못 만들면 대화 상자가 알아서 다른 자리를 보여 준다. 여기서 막을 일은 아니다.
            }

            return path;
        }
    }

    public static string Extension(ScriptLanguage language) => language switch
    {
        ScriptLanguage.Python => ".py",
        ScriptLanguage.JavaScript => ".js",
        _ => ".csx"
    };

    /// <summary>빌드된 스크립트(.NET DLL/IL). 소스가 아니라 실행만 하는 것이라 언어가 없다.</summary>
    public const string CompiledExtension = ".mtsx";

    /// <summary>빌드 결과물(<c>.mtsx</c>)인가. 이건 편집이 아니라 플레이어에서 실행만 한다.</summary>
    public static bool IsCompiledPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), CompiledExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 파일 이름으로 언어를 알아낸다. 모르는 확장자면 null.
    /// </summary>
    /// <remarks>
    /// 모를 때 아무거나 고르지 않는다. 잘못 고르면 문법 오류만 잔뜩 뜨는데, 사용자는 글이
    /// 틀린 줄 알지 언어가 어긋난 줄은 모른다. 모르면 지금 언어를 그대로 둔다.
    /// </remarks>
    public static ScriptLanguage? FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csx" or ".cs" => ScriptLanguage.CSharp,
            ".py" or ".pyw" => ScriptLanguage.Python,
            ".js" or ".mjs" => ScriptLanguage.JavaScript,
            _ => null
        };
    }

    /// <summary>여는 대화 상자용 필터. 지금 언어를 맨 앞에 둔다.</summary>
    public static string OpenFilter(ScriptLanguage current) => string.Join("|",
    [
        $"{Describe(current)}|*{Extension(current)}",
        .. Enum.GetValues<ScriptLanguage>()
            .Where(l => l != current)
            .Select(l => $"{Describe(l)}|*{Extension(l)}"),
        "모든 스크립트|*.csx;*.py;*.js",
        "모든 파일|*.*"
    ]);

    /// <summary>저장 대화 상자용 필터. 지금 언어 하나만 보여 준다 - 다른 확장자로 저장할 일이 없다.</summary>
    public static string SaveFilter(ScriptLanguage current)
        => $"{Describe(current)}|*{Extension(current)}|모든 파일|*.*";

    private static string Describe(ScriptLanguage language) => language switch
    {
        ScriptLanguage.Python => "파이썬 스크립트",
        ScriptLanguage.JavaScript => "자바스크립트",
        _ => "C# 스크립트"
    };
}
