using System;
using System.Diagnostics;
using System.IO;

using Minguk.Base.Utilities;

using NLog;

namespace Minguk.Tools.Helper;

/// <summary>
/// 사용자 데이터(사용자 설정 등)의 저장 위치를 한곳에서 관리한다.
///
/// 실행 폴더(AppContext.BaseDirectory)에 두면 안 된다. Velopack 은 업데이트 때 설치 폴더(current)를
/// 새 패키지로 통째로 교체하므로, 그 안에 있던 사용자 파일이 업데이트마다 사라진다.
/// 그래서 %AppData%\Minguk.Tools 로 옮기고, 첫 실행 때 실행 폴더에 남은 옛 파일을 한 번 옮겨 온다.
/// (%LocalAppData% 는 Velopack 이 소유하는 설치 루트라 제거 시 같이 지워지므로 쓰지 않는다.)
/// </summary>
public static class UserDataPaths
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>사용자 데이터 루트. 예: C:\Users\{user}\AppData\Roaming\Minguk.Tools</summary>
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minguk.Tools");

    /// <summary>AppSettingUtility 가 읽고 쓰는 사용자 설정 파일.</summary>
    public static string UserSettingsFile => Path.Combine(Root, AppSettingUtility.FileName);

    /// <summary>
    /// 앱 시작 시 가장 먼저 한 번 호출한다. AppSettingUtility 를 읽기 전이어야 한다.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Root);
            CopyFileIfMissing(Path.Combine(AppContext.BaseDirectory, AppSettingUtility.FileName), UserSettingsFile);
        }
        catch (Exception ex)
        {
            // 시작 시점이라 NLog 설정이 아직 없을 수 있어 Debug 출력도 같이 남긴다.
            Debug.WriteLine($"UserDataPaths.Initialize failed: {ex}");
            Logger.Warn(ex, "사용자 데이터 폴더 준비 실패. 실행 폴더를 그대로 사용한다.");
            return;
        }

        AppSettingUtility.DirectoryPath = Root;
    }

    /// <summary>원본은 지우지 않는다. 설치본은 다음 업데이트 때 어차피 사라진다.</summary>
    private static void CopyFileIfMissing(string source, string destination)
    {
        if (File.Exists(destination) || !File.Exists(source))
            return;

        try
        {
            File.Copy(source, destination);
            Logger.Info($"사용자 파일 이전: {source} → {destination}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UserDataPaths copy failed ({source}): {ex}");
            Logger.Warn(ex, $"사용자 파일 이전 실패: {source}");
        }
    }
}
