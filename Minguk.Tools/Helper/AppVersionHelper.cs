using System;
using System.Reflection;

namespace Minguk.Tools.Helper;

/// <summary>
/// 화면에 표시할 앱 버전.
///
/// 버전은 어셈블리 버전에서 가져온다. 배포 스크립트가 dotnet publish 에 -p:Version={배포 버전} 을
/// 넘기면 설치본의 어셈블리 버전이 Velopack 패키지 버전과 같아진다.
/// 개발 환경(Visual Studio 실행)에서는 csproj 기본값(1.0.0)이 나온다.
/// </summary>
public static class AppVersionHelper
{
    public const string ProductName = "Minguk Tools";

    /// <summary>"1.0.4" 처럼 세 자리로 다듬은 버전 문자열.</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>창 제목 등에 쓰는 표시 문자열. 예: "Minguk Tools v1.0.0"</summary>
    public static string DisplayTitle { get; } = $"{ProductName} v{Version}";

    private static string ResolveVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
                      ?? typeof(AppVersionHelper).Assembly.GetName().Version;

        if (version is null)
            return "0.0.0";

        return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
