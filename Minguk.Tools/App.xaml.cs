using DevExpress.Mvvm.UI;
using DevExpress.Utils;
using DevExpress.Xpf.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Minguk.Base.Extension;
using Minguk.Base.Listener;
using Minguk.Base.Utilities;
using Minguk.Base.Views;

using NLog.Extensions.Logging;

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using Minguk.Tools.Helper;
using Minguk.Tools.Locator;
using Minguk.Tools.SplashScreen;
using Minguk.Tools.Views;

namespace Minguk.Tools;

public partial class App : Application
{
    App()
    {
        // ── 테마 ────────────────────────────────────────────────────────────
        // 경량 테마(LightweightThemes)는 시작이 훨씬 빠르다. 대신 표준 WPF 컨트롤에는
        // 테마가 적용되지 않으므로(AllowStandardControlsTheming=false) 화면은 DevExpress 컨트롤로 짠다.
        CompatibilitySettings.UseLightweightThemes = true;
        LightweightThemeManager.AllowStandardControlsTheming = false;
        ApplicationThemeHelper.PreloadAsync(PreloadCategories.Core);

        // .NET 9 이상에서 BinaryFormatter 가 빠지면서 앱 간 드래그 앤 드롭이 깨진다. 되돌린다.
        DeserializationSettings.EnableDataObjectBinarySerialization = true;
        DevExpress.Xpf.Grid.DataControlBase.AllowInfiniteGridSize = true;

        // ── 스플래시 ────────────────────────────────────────────────────────
        var splashScreenManager = SplashScreenManager.Create(
            () => new StartupSplashScreen(),
            new DevExpress.Mvvm.DXSplashScreenViewModel
            {
                IsIndeterminate = true,
                Title = AppVersionHelper.ProductName,
                Subtitle = "실행 중 입니다.",
                Status = "잠시만 기다려 주세요.",
                Copyright = $"Copyright ⓒ {DateTime.Now:yyyy} 모비어스(주) All rights reserved.",
                Logo = new Uri(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "Resource", "CI_Burgundy.png"), UriKind.Absolute)
            });

        // 스플래시가 닫힌 뒤에 사용자 설정을 적용한다.
        // 테마·폰트 변경은 리소스를 갈아 끼우는 작업이라 스플래시가 떠 있는 동안 하면 깜빡인다.
        splashScreenManager.StateChanged += (sender, args) =>
        {
            if (args is { NewValue: DevExpress.Mvvm.SplashScreenState.Closed })
            {
                UserPreferencesHelper.Initialize();
                UserPreferencesHelper.LoadUserPreferences();
            }
        };

        splashScreenManager.ShowOnStartup();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            // ── 로캘 ────────────────────────────────────────────────────────
            CultureInfo culture = CultureInfo.CreateSpecificCulture("ko-KR");
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // 첫 화면이 뜬 뒤로 미룬다. 시작 지연을 줄이는 것이 목적이다.
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                SetupExceptionHandling();
                BindingErrorTraceListener.SetTrace();   // 바인딩 오류를 출력 창에 남긴다
            }, DispatcherPriority.Render);

            _ = Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                ApplicationThemeHelper.PreloadAsync(PreloadCategories.Grid, PreloadCategories.LayoutControl);
            }, DispatcherPriority.Render);

            // ── DI ──────────────────────────────────────────────────────────
            var builder = Host.CreateApplicationBuilder();

            string? environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
            InitializeConfig(builder, environment);

            builder.Services.AddLogging(loggingBuilder => loggingBuilder.AddNLog());

            // 화면(View)을 DI 에 등록한다.
            // MainViewLocator 가 메뉴의 CLASS_NM 으로 타입을 찾아 여기서 인스턴스를 꺼내므로,
            // 새 화면을 추가하면 ① 여기에 AddTransient ② MainMenu 에 항목 추가, 두 곳을 손대면 된다.
            builder.Services.AddTransient<DashboardView>();
            builder.Services.AddTransient<CaptureMonitorView>();

            var host = builder.Build();
            host.Start();

            // ViewLocator: 문자열 뷰 이름 → 실제 View 인스턴스
            ViewLocator.Default = new MainViewLocator(builder.Services.BuildServiceProvider());

            // ServiceContainer: ViewModel 에서 DI 컨테이너 없이 꺼내 쓰는 전역 서비스 자리
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            DevExpress.Mvvm.ServiceContainer.Default.RegisterService("Configuration", configuration);

            // ── 자동 업데이트 ────────────────────────────────────────────────
            // 조용히 받아 두고 종료할 때 적용한다(OnExit). 여기서는 걸어 두기만 하고 기다리지 않는다.
            AppUpdater.StartBackgroundCheck();
        }
        catch (Exception ex)
        {
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod());
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            UserPreferencesHelper.SaveUserPreferences();

            // 받아 둔 새 버전이 있으면 업데이터에 넘긴다. 창은 띄우지 않고 재시작도 하지 않는다.
            // 반드시 종료 시점이어야 한다 - 업데이터는 우리 프로세스가 끝나기를 60초만 기다린다.
            AppUpdater.ApplyPendingOnExit();

            base.OnExit(e);
        }
        catch (Exception ex)
        {
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod());
        }
    }

    /// <summary>
    /// AppSettings 를 Development / Production 으로 나눠 읽는다.
    ///
    /// 환경 변수(DOTNET_ENVIRONMENT)가 없으면 사용자 설정(Config.AppSettings)에 저장된 값을 보고
    /// 해당 파일을 AppSettings.json 으로 복사한 뒤 그것을 읽는다.
    /// 이렇게 하는 이유는 실행 중에 설정 화면에서 환경을 바꾸고 재시작만으로 반영되게 하기 위함이다.
    /// </summary>
    private static void InitializeConfig(HostApplicationBuilder builder, string? environment)
    {
        try
        {
            if (string.IsNullOrEmpty(environment))
            {
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

                var appSettingsFile = Path.Combine(baseDirectory, "AppSettings.json");
                var developmentFile = Path.Combine(baseDirectory, "AppSettings.Development.json");
                var productionFile = Path.Combine(baseDirectory, "AppSettings.Production.json");

                if (File.Exists(appSettingsFile))
                    File.Delete(appSettingsFile);

                var config = AppSettingUtility.Get("Config.AppSettings", "Production");
                if ("Development".Equals(config) && File.Exists(developmentFile))
                    File.Copy(developmentFile, appSettingsFile);
                else if (File.Exists(productionFile))
                    File.Copy(productionFile, appSettingsFile);
            }

            if (environment != null && environment.In("Development", "Production"))
                builder.Configuration.AddJsonFile($"AppSettings.{environment}.json", optional: true, reloadOnChange: true);
            else
                builder.Configuration.AddJsonFile("AppSettings.json", optional: true, reloadOnChange: true);
        }
        catch (Exception ex)
        {
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod());
        }
    }

    /// <summary>
    /// 미처리 예외를 네 경로에서 모두 잡는다.
    /// UI 스레드(Dispatcher)만 잡으면 Task 안에서 터진 예외가 조용히 사라진다.
    /// </summary>
    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            LogUnhandledException(e.Exception, "DispatcherUnhandledException");
            NLog.LogManager.Flush();

            Minguk.Base.Utility.MessageBox(e.Exception.ToString(), "미처리 예외(UI)", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;   // UI 스레드 예외로 앱이 죽지 않게 한다
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var exception = e.ExceptionObject as Exception
                            ?? new Exception($"Non-Exception 형태의 예외 : {e.ExceptionObject?.GetType().FullName ?? "null"} / {e.ExceptionObject}");

            LogUnhandledException(exception, $"AppDomain.UnhandledException (IsTerminating={e.IsTerminating})");
            NLog.LogManager.Flush();
        };

        Dispatcher.UnhandledException += (s, e) =>
        {
            LogUnhandledException(e.Exception, "Dispatcher.UnhandledException");
            NLog.LogManager.Flush();
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
            NLog.LogManager.Flush();
            e.SetObserved();
        };
    }

    private static void LogUnhandledException(Exception exception, string source)
    {
        var logger = NLog.LogManager.GetCurrentClassLogger();
        try
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName();

            // AggregateException 을 그대로 두면 진짜 원인이 묻힌다.
            if (exception is AggregateException aggregate)
                exception = aggregate.Flatten();

            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"Unhandled exception in {assemblyName.Name} v{assemblyName.Version}");
            builder.AppendLine($"  발생 위치 : {source}");
            builder.AppendLine($"  스레드    : {Environment.CurrentManagedThreadId} / UI 스레드 여부 {Current?.Dispatcher?.CheckAccess()}");
            builder.AppendLine($"  종류      : {exception.GetType().FullName}");
            builder.AppendLine($"  메시지    : {exception.Message}");

            if (exception.TargetSite != null)
                builder.AppendLine($"  메서드    : {exception.TargetSite.DeclaringType?.FullName}.{exception.TargetSite.Name}");

            // 내부 예외 사슬을 전부 남긴다. 실제 원인은 대개 가장 안쪽에 있다.
            var inner = exception.InnerException;
            for (int depth = 1; inner != null && depth <= 10; depth++, inner = inner.InnerException)
                builder.AppendLine($"  내부 예외 {depth} : {inner.GetType().FullName} : {inner.Message}");

            logger.Error(exception, builder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Exception in LogUnhandledException");
        }
    }
}
