using System;
using System.Windows;
using System.Windows.Threading;
using Minguk.Tools.Source;
using Minguk.Tools.Views;

namespace Minguk.Tools.Tests;

/// <summary>
/// 화면이 실제로 만들어지는지 본다.
/// </summary>
/// <remarks>
/// XAML 은 빌드를 통과하고 런타임에만 터진다 - 리소스 경로가 틀렸거나, 바인딩 대상이 없거나,
/// 아이콘 파일이 없거나. 빌드 오류 0 을 확인했다고 화면이 뜬다는 뜻은 아니다.
///
/// 여기서 잡는 것
///   XAML 파싱, 리소스 사전(Style.xaml) 해석, ViewModelSource 로 ViewModel 생성,
///   아이콘 리소스 이름이 실제로 있는지.
///
/// 여기서 못 잡는 것
///   부모(MainViewModel)가 주입되어야 도는 초기화 단계. 이 화면은 문서 탭 안에서 살기 때문에
///   단독으로 띄우면 OnLoaded 까지는 가지 않는다. 그 뒤는 앱에서 눈으로 봐야 한다.
/// </remarks>
internal static class ViewSmokeProbe
{
    public static int Run()
    {
        var failures = 0;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            failures += Check("InputAutomationView 생성", () => new InputAutomationView());
            failures += Check("DashboardView 생성", () => new DashboardView());
            failures += Check("CaptureMonitorView 생성", () => new CaptureMonitorView());
            failures += Check("ConfigView 생성", () => new ConfigView());

            // FindMenuItem 은 Instance 가 한 번 만들어진 뒤에만 찾는다. 앱에서는 메뉴가 먼저
            // 만들어지므로 문제가 없지만, 여기서는 직접 건드려 줘야 한다.
            _ = MainMenu.Instance;

            failures += CheckMenu("Minguk.Tools.Views.InputAutomationView");
            failures += CheckMenu("Minguk.Tools.Views.CaptureMonitorView");

            app.Shutdown();
        });

        app.Run();

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "== 화면 생성 통과 ==" : $"== 화면 생성 실패 {failures}건 ==");

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 메뉴 항목이 실제 View 타입을 가리키는지, 아이콘이 잡히는지.
    /// </summary>
    /// <remarks>
    /// CLASS_NM 은 문자열이라 오타가 나도 빌드는 통과한다. MainViewLocator 가 그 이름으로
    /// DI 에서 뷰를 꺼내므로, 틀리면 메뉴는 보이는데 탭이 비어서 열린다.
    /// 아이콘 경로도 마찬가지로 문자열이다 - 없으면 조용히 빈 아이콘이 된다.
    /// </remarks>
    private static int CheckMenu(string className)
    {
        var item = MainMenu.FindMenuItem(className);

        if (item is null)
        {
            Console.WriteLine($"[FAIL] 메뉴 {className} — MainMenu 에 항목이 없다");
            return 1;
        }

        var type = Type.GetType($"{className}, Minguk.Tools");
        var hasIcon = item.Icon is byte[] { Length: > 0 };

        if (type is null)
        {
            Console.WriteLine($"[FAIL] 메뉴 {className} — 그런 타입이 없다 (CLASS_NM 오타)");
            return 1;
        }

        Console.WriteLine($"[{(hasIcon ? "PASS" : "FAIL")}] 메뉴 {item.MENU_NM} — 타입 확인, 아이콘 {(hasIcon ? "있음" : "없음")}");
        return hasIcon ? 0 : 1;
    }

    /// <summary>
    /// 실패는 "만들다 터졌는가" 로만 센다.
    /// </summary>
    /// <remarks>
    /// DataContext 가 비어 있는 것을 실패로 보면 안 된다. 다이얼로그로 띄우는 화면(ConfigView)은
    /// 일부러 XAML 에서 물리지 않고 DialogService 가 넣어 주기 때문이다.
    /// 대신 무엇이 붙었는지는 적어 둔다 - ViewModelSource 를 쓰는 화면에서 비어 있으면 그게 신호다.
    /// </remarks>
    private static int Check(string name, Func<FrameworkElement> create)
    {
        try
        {
            var view = create();
            var context = view.DataContext;

            Console.WriteLine($"[PASS] {name} — DataContext {context?.GetType().Name ?? "(없음 - 바깥에서 주입)"}");
            return 0;
        }
        catch (Exception ex)
        {
            // XAML 파싱 오류는 안쪽 예외에 진짜 이유가 들어 있다.
            var reason = ex.InnerException?.Message ?? ex.Message;
            Console.WriteLine($"[FAIL] {name} — {ex.GetType().Name}: {reason}");
            return 1;
        }
    }
}
