using System;
using System.Windows;

using Minguk.Tools.Helper;

using Velopack;

namespace Minguk.Tools;

public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack 은 Application 이 만들어지기 전에 가장 먼저 실행되어야 한다.
        // 설치/제거/업데이트 훅이 여기서 처리되고, 훅 실행이면 이 줄에서 프로세스가 끝난다.
        StartupTrace.Mark("Main 진입 (런타임 기동 + 어셈블리 로딩)");

        VelopackApp.Build().Run();
        StartupTrace.Mark("Velopack");

        // 사용자 설정을 실행 폴더 밖(%AppData%)에 두고, 옛 위치의 파일이 있으면 옮겨 온다.
        // 업데이트 때 설치 폴더가 통째로 교체되어도 설정이 남도록, 설정을 읽기 전에 가장 먼저 호출한다.
        UserDataPaths.Initialize();
        StartupTrace.Mark("UserDataPaths");

        // 업데이트 확인은 여기서 하지 않는다.
        // 화면이 뜨기 전에 확인·다운로드를 하면 그 시간만큼 앱이 멈춘 것처럼 보인다.
        // App.OnStartup 에서 백그라운드로 걸고, App.OnExit 에서 적용한다. (AppUpdater 참조)
        App app = new();
        StartupTrace.Mark("App 생성자");

        app.InitializeComponent();
        StartupTrace.Mark("App.InitializeComponent");

        app.Run();
    }
}
