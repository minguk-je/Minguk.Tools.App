using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace Minguk.Tools.Input.Adapters;

/// <summary>드라이버가 지금 어느 상태인지.</summary>
public enum InterceptionDriverState
{
    /// <summary>설치되어 돌고 있다. 바로 쓸 수 있다.</summary>
    Ready,

    /// <summary>설치는 됐는데 아직 안 돌고 있다. 재부팅해야 올라온다.</summary>
    NeedsReboot,

    /// <summary>설치되어 있지 않다.</summary>
    NotInstalled,

    /// <summary>상태를 알아내지 못했다.</summary>
    Unknown
}

/// <summary>
/// Interception 커널 드라이버를 살피고, 없으면 설치한다.
/// </summary>
/// <remarks>
/// <b>왜 별도 프로세스를 관리자로 띄우는가</b>
///
/// 드라이버를 넣는 것은 관리자 권한이 필요하다. 그렇다고 앱 전체를 관리자로 올리면 안 된다 -
/// 관리자로 도는 창에는 탐색기에서 파일을 끌어다 놓을 수 없고(UIPI), 사용자가 늘 UAC 를
/// 거쳐 앱을 켜야 한다. 설치할 때만 <c>runas</c> 로 설치 프로그램을 띄우는 편이 맞다.
///
/// <b>왜 재부팅이 필요한가</b>
///
/// 이 드라이버는 키보드·마우스 장치 스택 사이에 끼어든다(필터 드라이버). 그 자리는 장치가
/// 올라올 때 정해지므로, 이미 올라와 있는 장치에는 다음 부팅에야 붙는다. 설치가 성공해도
/// 재부팅 전에는 <see cref="InterceptionInputAdapter.IsAvailable"/> 가 false 다.
///
/// <b>설치 프로그램은 서명이 없다</b>
///
/// 드라이버(.sys)는 서명되어 있지만 설치 프로그램 껍데기는 아니다. SmartScreen 경고가 뜰 수
/// 있다. 저장소에 넣어 둔 것은 공식 릴리스(v1.0.1)에서 꺼낸 것이고, 같이 든
/// <c>interception.dll</c> 이 우리가 쓰는 것과 바이트 단위로 같은지 확인하고 넣었다.
/// </remarks>
public static class InterceptionDriver
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 드라이버가 만드는 서비스 이름들.
    /// </summary>
    /// <remarks>
    /// 키보드와 마우스가 따로다. 둘 중 하나만 올라와 있는 어중간한 상태도 있을 수 있어
    /// 둘 다 본다.
    /// </remarks>
    private static readonly string[] ServiceNames = ["keyboard", "mouse"];

    /// <summary>설치 프로그램. 실행 파일 옆에 둔다.</summary>
    public static string InstallerPath => Path.Combine(
        Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "install-interception.exe");

    public static bool HasInstaller => File.Exists(InstallerPath);

    /// <summary>
    /// 지금 상태를 알아본다.
    /// </summary>
    /// <remarks>
    /// 어댑터를 만들어 보는 것으로도 "쓸 수 있는지" 는 알 수 있지만, <b>왜</b> 못 쓰는지는
    /// 모른다. 설치가 안 된 것과 설치는 됐는데 재부팅을 안 한 것은 사용자가 할 일이 다르다.
    /// 서비스를 보면 그것이 갈린다.
    /// </remarks>
    public static InterceptionDriverState GetState()
    {
        var anyInstalled = false;
        var allRunning = true;

        foreach (var name in ServiceNames)
        {
            try
            {
                using var service = new ServiceController(name);
                var status = service.Status;   // 없는 서비스면 여기서 던진다

                anyInstalled = true;

                if (status != ServiceControllerStatus.Running) allRunning = false;
            }
            catch (InvalidOperationException)
            {
                // 그 이름의 서비스가 없다. 설치가 안 된 것이다.
                allRunning = false;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"서비스 상태를 못 읽었다: {name}");
                return InterceptionDriverState.Unknown;
            }
        }

        if (!anyInstalled) return InterceptionDriverState.NotInstalled;

        return allRunning ? InterceptionDriverState.Ready : InterceptionDriverState.NeedsReboot;
    }

    /// <param name="State">설치를 마친 뒤의 상태.</param>
    /// <param name="Message">사용자에게 보여 줄 한 줄.</param>
    /// <param name="NeedsReboot">재부팅해야 쓸 수 있는지.</param>
    public readonly record struct InstallResult(InterceptionDriverState State, string Message, bool NeedsReboot);

    /// <summary>
    /// 설치 프로그램을 관리자로 띄워 드라이버를 넣는다.
    /// </summary>
    /// <remarks>
    /// UAC 를 사용자가 거절하면 예외(1223)로 온다. 그것은 오류가 아니라 선택이므로 그렇게 적는다.
    /// </remarks>
    public static Task<InstallResult> InstallAsync() => Task.Run(() =>
    {
        if (!HasInstaller)
        {
            return new InstallResult(GetState(),
                $"설치 프로그램을 찾지 못했습니다: {InstallerPath}", NeedsReboot: false);
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = InstallerPath,
                Arguments = "/install",
                UseShellExecute = true,   // runas 를 쓰려면 셸을 거쳐야 한다
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(start);

            if (process is null)
                return new InstallResult(GetState(), "설치 프로그램을 띄우지 못했습니다.", NeedsReboot: false);

            process.WaitForExit();

            var state = GetState();

            Logger.Info($"Interception 설치 프로그램이 끝났다. 종료 코드 {process.ExitCode}, 상태 {state}");

            if (process.ExitCode != 0)
            {
                return new InstallResult(state,
                    $"설치 프로그램이 오류로 끝났습니다(종료 코드 {process.ExitCode}).", NeedsReboot: false);
            }

            return state == InterceptionDriverState.Ready
                ? new InstallResult(state, "드라이버가 설치되어 바로 쓸 수 있습니다.", NeedsReboot: false)
                : new InstallResult(state,
                    "드라이버를 설치했습니다. Windows 를 다시 시작해야 쓸 수 있습니다.", NeedsReboot: true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED - 사용자가 UAC 를 거절했다.
            return new InstallResult(GetState(), "관리자 권한을 거절해 설치하지 않았습니다.", NeedsReboot: false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Interception 설치에 실패했다");
            return new InstallResult(GetState(), $"설치하지 못했습니다: {ex.Message}", NeedsReboot: false);
        }
    });

    /// <summary>상태를 사람이 읽는 한 줄로.</summary>
    public static string Describe(InterceptionDriverState state) => state switch
    {
        InterceptionDriverState.Ready => "드라이버가 설치되어 있습니다.",
        InterceptionDriverState.NeedsReboot => "드라이버는 설치됐지만 아직 안 올라왔습니다 - Windows 를 다시 시작하세요.",
        InterceptionDriverState.NotInstalled => "드라이버가 설치되어 있지 않습니다.",
        _ => "드라이버 상태를 알아내지 못했습니다."
    };
}
