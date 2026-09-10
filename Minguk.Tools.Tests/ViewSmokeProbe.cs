using System;
using System.Windows;
using System.Windows.Threading;
using Minguk.Tools.Input;
using Minguk.Tools.Source;
using Minguk.Tools.ViewModels;
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

            failures += CheckPathWarning();

            app.Shutdown();
        });

        app.Run();

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "== 화면 생성 통과 ==" : $"== 화면 생성 실패 {failures}건 ==");

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 고른 경로가 못 보내는 단계가 있을 때 화면이 그 사실을 말하는지.
    /// </summary>
    /// <remarks>
    /// PostMessage 는 스캔코드를 못 넣어 글자·Enter·한/영 단계가 통째로 빠진다.
    /// 그것을 알려 주지 않으면 사용자는 "순서" 가 비고 실행해도 아무 일이 없는 이유를
    /// 알 수 없다 - 실제로 그렇게 헤맨 적이 있어서 검사로 남긴다.
    ///
    /// 화면이 아니라 ViewModel 을 직접 만져서 본다. 콤보를 UI 로 돌리는 것은 DevExpress
    /// 드롭다운이 별도 팝업으로 떠서 자동화가 불안정하다.
    /// </remarks>
    private static int CheckPathWarning()
    {
        try
        {
            var vm = InputAutomationViewModel.Create();

            vm.ScriptText = """
                            Type("가");
                            ToggleHangul();
                            Enter();
                            MoveTo(100, 200);
                            """;

            Apply(vm, InputBackend.SendInput);
            var quietOnSendInput = vm.PathWarning is null;

            Apply(vm, InputBackend.PostMessage);

            // 이제 이 경로도 한/영 을 뒤집는다. 빠지는 단계는 없어야 하고,
            // 대신 이 경로에서만 필요한 안내(단축키로 시작)가 떠야 한다.
            //
            // "대상 창" 줄은 여기서 못 본다. 스크립트를 돌려 단계가 생겨야 뜨는데,
            // 스크립트 실행은 비동기라 이 동기 검사에서는 아직 안 끝났다.
            // 스크립트가 단계가 되는 것은 하네스의 "스크립트 엔진" 항목이 본다.
            var warned = vm.PathWarning is not null
                         && !vm.PathWarning.Contains("빠집니다")
                         && vm.PathWarning.Contains("F5");

            if (quietOnSendInput && warned)
            {
                Console.WriteLine($"[PASS] 못 보내는 단계 알림 — {vm.PathWarning}");
                return 0;
            }

            Console.WriteLine("[FAIL] 못 보내는 단계 알림 — "
                              + (quietOnSendInput ? "" : "SendInput 인데도 경고가 떴다. ")
                              + $"PostMessage 경고: {vm.PathWarning ?? "(없음)"}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 못 보내는 단계 알림 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 경로를 갈아 끼우고 순서 문구를 다시 만들게 한다.
    /// </summary>
    /// <remarks>
    /// 화면에서는 OnLoaded 가 ApplyBackend 를 부르는데, 그 단계는 문서 탭 안에서만 돈다.
    /// 검사용 공개 메서드를 제품에 새로 내는 대신 리플렉션으로 부른다 -
    /// 이 하나를 위해 API 를 늘리면 그 API 가 제품 코드인 척 남는다.
    /// </remarks>
    private static void Apply(InputAutomationViewModel vm, InputBackend backend)
    {
        vm.SelectedInputBackend = backend;

        var type = vm.GetType();

        // ViewModelSource 가 만든 것은 파생 프록시라, 원본 타입까지 올라가며 찾는다.
        while (type is not null)
        {
            var method = type.GetMethod("ApplyBackend",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly);

            if (method is not null)
            {
                method.Invoke(vm, null);

                var update = type.GetMethod("UpdateSequenceText",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly);

                update?.Invoke(vm, null);
                return;
            }

            type = type.BaseType;
        }

        throw new MissingMethodException("ApplyBackend 를 찾지 못했다");
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
