using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using Minguk.Base.Utilities;

using Velopack;
using Velopack.Sources;

namespace Minguk.Tools.Helper;

/// <summary>
/// 자동 업데이트. 조용히 받아 두고, 다음 실행 때 적용한다.
///
/// 흐름:
///   앱 시작 → (조금 기다렸다가) 백그라운드로 확인·다운로드 → 상태바에 한 줄 알림
///   앱 종료 → 받아 둔 것이 있으면 업데이터에 넘기고 빠진다
///
/// 사용자를 멈춰 세우지 않는 것이 요점이다. 확인도 다운로드도 UI 스레드를 잡지 않고,
/// 도중에 앱을 꺼도 다음 실행에서 이어서 받는다. 재시작을 강요하지 않으므로
/// 새 버전은 사용자가 다음에 앱을 켤 때 적용된다.
///
/// 주의: <see cref="UpdateManager.WaitExitThenApplyUpdates"/> 는 우리 프로세스가 끝나기를
/// 최대 60초만 기다린다. 그래서 다운로드 직후가 아니라 반드시 종료 시점에 불러야 한다.
/// </summary>
public static class AppUpdater
{
    /// <summary>배포(릴리스) 저장소. 소스 저장소(Minguk.Tools)와 별개로 릴리스만 올리는 곳이다.</summary>
    private const string UpdateRepoUrl = "https://github.com/minguk-je/Minguk.Tools.App";

    /// <summary>첫 화면이 뜨고 나서 확인을 시작한다. 시작 지연과 회선 경합을 함께 피한다.</summary>
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static UpdateManager? _manager;

    /// <summary>받아 둔 새 버전. 종료할 때 이게 있으면 업데이터에 넘긴다.</summary>
    public static string? DownloadedVersion { get; private set; }

    /// <summary>
    /// 백그라운드 확인을 건다. 즉시 반환한다 — 결과를 기다리지 않는다.
    /// 개발 환경(비설치 실행)에서는 아무것도 하지 않는다.
    /// </summary>
    public static void StartBackgroundCheck()
    {
        _ = Task.Run(CheckAndDownloadAsync);
    }

    private static async Task CheckAndDownloadAsync()
    {
        try
        {
            await Task.Delay(StartDelay).ConfigureAwait(false);

            var manager = new UpdateManager(new GithubSource(UpdateRepoUrl, accessToken: null, prerelease: false));

            // 설치본으로 실행된 경우에만 의미가 있다.
            if (!manager.IsInstalled)
                return;

            _manager = manager;

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                Logger.Debug("업데이트 없음");
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            Logger.Info($"새 버전 확인: {version}. 백그라운드로 내려받는다.");

            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);

            DownloadedVersion = version;
            Logger.Info($"새 버전 {version} 다운로드 완료. 다음 실행 때 적용된다.");

            Notify($"새 버전 {version} 을 받았습니다. 다음 실행 때 적용됩니다.");
        }
        catch (Exception ex)
        {
            // 업데이트가 안 되는 것이 앱을 못 쓸 이유는 아니다. 남기기만 하고 넘어간다.
            Logger.Warn(ex, "업데이트 확인/다운로드 실패");
        }
    }

    /// <summary>
    /// 종료 직전에 부른다. 받아 둔 것이 있으면 업데이터를 띄우고 바로 반환한다.
    /// 창 없이(silent) 적용하고, 재시작은 하지 않는다 — 사용자가 다음에 켤 때 새 버전이 뜬다.
    /// </summary>
    public static void ApplyPendingOnExit()
    {
        try
        {
            var pending = _manager?.UpdatePendingRestart;
            if (_manager is null || pending is null)
                return;

            Logger.Info($"종료. 받아 둔 {pending.Version} 을 적용한다.");

            _manager.WaitExitThenApplyUpdates(pending, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "업데이트 적용 예약 실패");
        }
    }

    /// <summary>상태바에 한 줄 남긴다. 백그라운드 스레드에서 불리므로 UI 스레드로 넘긴다.</summary>
    private static void Notify(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        _ = dispatcher.BeginInvoke(() => MessengerUtility.SendMainMessage(message));
    }
}
