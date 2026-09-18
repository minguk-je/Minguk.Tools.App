using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using DevExpress.Xpf.Core;
using Minguk.Tools.Input;
using System.Windows.Media;
using Minguk.Tools.Helper;
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
            failures += Check("LabelingView 생성", () => new LabelingView());
            failures += Check("DashboardView 생성", () => new DashboardView());
            failures += Check("CaptureMonitorView 생성", () => new CaptureMonitorView());
            failures += Check("ScriptStudioView 생성", () => new ScriptStudioView());
            failures += Check("PlayView 생성", () => new PlayView());
            failures += Check("ConfigView 생성", () => new ConfigView());

            // 시작 창. Automation 을 처음 누를 때 뜬다 - 여기서 터지면 Automation 을 아예 못 연다.
            failures += Check("StartWindow 생성", () => new Minguk.Tools.Views.StartWindow());

            // 새 프로젝트 이름을 받는 창. Automation 화면의 "새 프로젝트" 가 띄운다.
            failures += Check("NameInputWindow 생성", () => new Minguk.Tools.Views.NameInputWindow());

            // Automation 화면. 위에서 솔루션·프로젝트를 고르고 아래 탭들이 따라간다.
            failures += Check("AutomationMainView 생성", () => new Minguk.Tools.Views.AutomationMainView());

            // FindMenuItem 은 Instance 가 한 번 만들어진 뒤에만 찾는다. 앱에서는 메뉴가 먼저
            // 만들어지므로 문제가 없지만, 여기서는 직접 건드려 줘야 한다.
            _ = MainMenu.Instance;

            // 왼쪽 메뉴: Automation Builder(만드는 쪽) · 플레이(돌리는 쪽) · 입력 테스트(점검 도구).
            failures += CheckMenu(MainMenu.AutomationClassName);
            failures += CheckMenu("Minguk.Tools.Views.PlayView");
            failures += CheckMenu("Minguk.Tools.Views.InputAutomationView");

            // Automation 화면 아래 탭들. 이름을 여기 적지 않는다 - AutomationScreens 가 셸 화면과 모듈 화면을 다 준다.
            failures += CheckAutomationScreens();

            // 모듈이 들고 온 최상위 메뉴(환경 등). 이름을 여기 적지 않는다 - 모듈이 늘어도 이 파일은 안 고친다.
            failures += CheckModuleMenus();

            failures += CheckPathWarning();
            failures += CheckEditorPalette();
            failures += CheckLabelCanvasDrawing();
            failures += CheckLabelingAssist();
            failures += CheckScriptPerLanguage();
            failures += CheckLossSparkline();
            failures += CheckScriptApiCatalog();
            failures += CheckErrorUnderline();
            failures += CheckCompletion();
            failures += CheckSemanticColoring();
            failures += CheckCodeLens();
            failures += CheckPlayProject();

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

            vm.Script.Text = """
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
    /// 캔버스가 사각형을 제 자리에 그리는지.
    /// </summary>
    /// <remarks>
    /// 진짜 추론은 2.2GB 를 받고 학습까지 해야 볼 수 있어 여기서 못 돌린다. 대신 <b>그리는
    /// 쪽</b>만 떼어 본다 - 0~1 좌표가 화면 어디로 가는지가 여기서 정해지고, 그것이 틀리면
    /// 사각형이 엉뚱한 자리에 그려지는데 화면에는 그럴싸하게 떠서 눈으로는 못 잡는다.
    ///
    /// 오프스크린으로 그린 뒤 픽셀을 직접 본다.
    /// </remarks>
    private static int CheckLabelCanvasDrawing()
    {
        try
        {
            const int size = 200;

            // 200x200 캔버스에 200x200 그림을 채운다. 그러면 0~1 이 곧 픽셀 자리가 된다.
            var image = new System.Windows.Media.Imaging.WriteableBitmap(
                size, size, 96, 96, PixelFormats.Bgra32, null);

            image.WritePixels(new Int32Rect(0, 0, size, size), new byte[size * size * 4], size * 4, 0);
            image.Freeze();

            var canvas = new Minguk.Tools.Markup.LabelCanvas
            {
                Width = size,
                Height = size,
                ImageSource = image,
                Boxes = [],
                Predictions =
                [
                    new Minguk.Tools.Markup.PredictedBox(
                        Minguk.Tools.Vision.Labeling.LabelBox.FromCorners(0, 0.25, 0.25, 0.75, 0.75), "슬라임 90%")
                ]
            };

            canvas.Measure(new Size(size, size));
            canvas.Arrange(new Rect(0, 0, size, size));
            canvas.UpdateLayout();

            var target = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);

            target.Render(canvas);

            var pixels = new byte[size * size * 4];

            target.CopyPixels(pixels, size * 4, 0);

            // 0번 검출의 색. 점선 테두리가 이 색으로 그려져 있어야 한다.
            var expected = Minguk.Tools.Markup.LabelCanvas.ColorOf(0);

            // 사각형 위쪽 변(y=50) 을 훑어 그 색이 있는지 본다. 점선이라 군데군데 비어 있다.
            var onEdge = CountColour(pixels, size, y: 50, from: 50, to: 150, expected);

            // 사각형 <b>안쪽</b>(y=100 의 가운데)은 비어 있어야 한다. 채워 그리면 그림을 가린다.
            var inside = CountColour(pixels, size, y: 100, from: 90, to: 110, expected);

            var ok = onEdge > 10 && inside == 0;

            Console.WriteLine(ok
                ? $"[PASS] 캔버스가 예측을 제 자리에 그린다 — 위쪽 변에서 {onEdge}px, 안쪽은 비어 있음"
                : $"[FAIL] 캔버스가 예측을 제 자리에 그린다 — 위쪽 변 {onEdge}px / 안쪽 {inside}px");

            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 캔버스가 예측을 제 자리에 그린다 — {ex.GetType().Name}: {ex.Message}");

            return 1;
        }
    }

    /// <summary>한 줄에서 그 색인 픽셀이 몇 개인지. 그리기는 안티앨리어싱을 타므로 넉넉히 본다.</summary>
    private static int CountColour(byte[] pixels, int stride, int y, int from, int to, Color colour)
    {
        var count = 0;

        for (var x = from; x < to; x++)
        {
            var i = ((y * stride) + x) * 4;

            if (Math.Abs(pixels[i + 2] - colour.R) < 40
                && Math.Abs(pixels[i + 1] - colour.G) < 40
                && Math.Abs(pixels[i + 0] - colour.B) < 40)
                count++;
        }

        return count;
    }

    /// <summary>
    /// 편집기 색이 테마 팔레트에서 나오는지.
    /// </summary>
    /// <remarks>
    /// AvalonEdit 은 경량 테마를 안 타서 색을 직접 줘야 한다. 예전에는 테마 <b>이름</b>으로
    /// 밝은 벌·어두운 벌을 갈랐는데, 팔레트로 만든 테마(VS2019Blue 같은)는 이름에 Dark 도
    /// Black 도 없어 밝은 쪽으로 잘못 봤다. 지금은 팔레트에서 실제 색을 읽는다.
    ///
    /// <b>여기서 못 보는 것</b>: 테마를 바꿨을 때 따라오는지. LightweightThemeManager 는
    /// 화면 없이 도는 여기서 테마 변경을 따라오지 않고(실측: 이름을 바꿔도 CurrentTheme 이
    /// Office2019Colorful 그대로였다) CurrentTheme 세터도 공개가 아니다.
    /// 그 갈래는 앱에서 눈으로 봐야 한다.
    /// </remarks>
    private static int CheckEditorPalette()
    {
        try
        {
            var palette = LightweightThemeManager.CurrentTheme?.Palette;

            if (palette is null || !palette.Contains("Brush.Editor.Background"))
            {
                Console.WriteLine("[N/A ] 편집기 색 — 지금 테마에 팔레트가 없다");
                return 0;
            }

            var expected = palette["Brush.Editor.Background"] as SolidColorBrush;
            var actual = SequenceScriptHighlighting.Background as SolidColorBrush;

            var same = expected is not null && actual is not null && expected.Color == actual.Color;

            // 줄 번호는 본문과 바탕 사이여야 한다. 둘 중 하나와 같으면 섞은 것이 아니다.
            var line = SequenceScriptHighlighting.LineNumberForeground as SolidColorBrush;
            var fore = SequenceScriptHighlighting.Foreground as SolidColorBrush;
            var between = line is not null && fore is not null && actual is not null
                          && line.Color != fore.Color && line.Color != actual.Color;

            if (same && between)
            {
                Console.WriteLine($"[PASS] 편집기 색 — 팔레트({LightweightThemeManager.CurrentTheme!.Name})에서 읽음 "
                                  + $"바탕 {actual!.Color} · 글자 {fore!.Color} · 줄번호 {line!.Color}");
                return 0;
            }

            Console.WriteLine($"[FAIL] 편집기 색 — 바탕 일치 {same} / 줄번호가 사이값 {between}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 편집기 색 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>ViewModel 의 비공개 메서드를 부른다. 검사용 공개 API 를 제품에 내지 않으려는 것이다.</summary>
    private static void Invoke(InputAutomationViewModel vm, string name)
    {
        var type = vm.GetType();

        while (type is not null)
        {
            var method = type.GetMethod(name,
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly);

            if (method is not null) { method.Invoke(vm, null); return; }

            type = type.BaseType;
        }

        throw new MissingMethodException(name);
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
    /// <summary>
    /// Automation 화면 아래 탭들이 제대로 붙는지 - 타입·아이콘, 그리고 실제로 만들어 보기.
    /// </summary>
    /// <remarks>
    /// 셸 화면과 모듈이 들고 온 화면(학습환경 등)이 한 목록(<c>AutomationScreens</c>)에서 나온다. 여기서 이름을 적지 않으므로
    /// 화면이나 모듈이 늘어도 이 파일은 안 고친다. 타입 이름은 문자열이라 오타가 나도 빌드는 통과하고,
    /// 앱에서는 "탭을 눌렀는데 비어 있다" 로만 보여 원인을 찾기 어렵다.
    /// </remarks>
    private static int CheckAutomationScreens()
    {
        var failures = 0;

        foreach (var screen in Minguk.Tools.Source.AutomationScreens.All())
        {
            var type = ResolveViewType(screen.ViewName);
            var hasIcon = screen.Icon is byte[] { Length: > 0 };

            if (type is null)
            {
                Console.WriteLine($"[FAIL] 화면 탭 {screen.Title} — 그런 타입이 없다: {screen.ViewName}");
                failures++;
                continue;
            }

            Console.WriteLine($"[{(hasIcon ? "PASS" : "FAIL")}] 화면 탭 {screen.Title} — 타입 확인, 아이콘 {(hasIcon ? "있음" : "없음")}");
            if (!hasIcon) failures++;

        }

        return failures;
    }

    /// <summary>CLASS_NM 으로 타입을 찾는다. 셸만 뒤지면 모듈 화면을 못 찾는다 - 앱의 MainViewLocator 와 같은 자리를 본다.</summary>
    private static Type? ResolveViewType(string className)
        => Type.GetType($"{className}, Minguk.Tools")
           ?? Minguk.Tools.Modules.ToolModules.All
               .Select(module => module.GetType().Assembly.GetType(className))
               .FirstOrDefault(found => found is not null);

    /// <summary>모듈이 들고 온 메뉴 항목이 메뉴에 붙고, 화면이 실제로 만들어지는지.</summary>
    private static int CheckModuleMenus()
    {
        var failures = 0;

        foreach (var module in Minguk.Tools.Modules.ToolModules.All)
        {
            foreach (var item in module.CreateMenuItems())
            {
                failures += CheckMenu(item.CLASS_NM);

                if (ResolveViewType(item.CLASS_NM) is { } viewType)
                    failures += Check($"{viewType.Name} 생성", () => (FrameworkElement)Activator.CreateInstance(viewType)!);
            }
        }

        return failures;
    }

    private static int CheckMenu(string className)
    {
        var item = MainMenu.FindMenuItem(className);

        if (item is null)
        {
            Console.WriteLine($"[FAIL] 메뉴 {className} — MainMenu 에 항목이 없다");
            return 1;
        }

        var type = ResolveViewType(className);
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

    /// <summary>
    /// 앞 장 가져오기 · 점선 받기. ViewModel 을 임시 데이터셋에 붙여 커서 없이 본다.
    /// </summary>
    /// <remarks>
    /// 둘 다 사각형 컬렉션을 옮기는 일이라 화면이 없어도 결과가 컬렉션에 남는다.
    /// 사용자 데이터셋이 아니라 임시 폴더다 - 가져오기가 라벨을 더하고 넘기면 저장까지 한다.
    /// </remarks>
    private static int CheckLabelingAssist()
    {
        var failures = 0;
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "minguk-assist-" + Guid.NewGuid().ToString("N"));

        try
        {
            var dataset = new Minguk.Tools.Vision.Labeling.LabelDataset(root);
            dataset.EnsureCreated();
            System.IO.File.WriteAllText(dataset.ClassesPath, "슬라임" + Environment.NewLine);

            foreach (var stem in new[] { "a", "b" })
                WriteTinyPng(System.IO.Path.Combine(dataset.ImageDirectory, stem + ".png"));

            var first = Minguk.Tools.Vision.Labeling.LabelBox.FromCorners(0, 0.4, 0.4, 0.6, 0.6);
            Minguk.Tools.Vision.Labeling.LabelFile.Save(dataset.LabelPathFor(System.IO.Path.Combine(dataset.ImageDirectory, "a.png")), [first]);

            var vm = LabelingViewModel.Create();
            vm.DatasetRoot = root;
            vm.DoReloadCommand.Execute(null);

            if (vm.Items.Count != 2)
            {
                Console.WriteLine($"[FAIL] 라벨링 보조 — 임시 데이터셋을 못 읽었다 ({vm.Items.Count}장)");
                return 1;
            }

            // b 로 넘어가면 비어 있다. 앞 장(a)의 사각형을 가져온다.
            vm.SelectedItem = vm.Items[1];
            vm.DoCopyPreviousCommand.Execute(null);

            var copied = vm.Boxes.Count == 1 && vm.Boxes[0] == first;
            Console.WriteLine($"[{(copied ? "PASS" : "FAIL")}] 앞 장 가져오기 — 사각형 {vm.Boxes.Count}개, 고른 것 {vm.SelectedBoxIndex}");
            if (!copied) failures++;

            // 점선 둘을 받으면 라벨이 되고 점선은 없어진다.
            vm.Predictions.Add(new Minguk.Tools.Markup.PredictedBox(Minguk.Tools.Vision.Labeling.LabelBox.FromCorners(0, 0.1, 0.1, 0.2, 0.2), "슬라임 90%"));
            vm.Predictions.Add(new Minguk.Tools.Markup.PredictedBox(Minguk.Tools.Vision.Labeling.LabelBox.FromCorners(0, 0.7, 0.7, 0.9, 0.9), "슬라임 80%"));
            vm.DoAdoptPredictionsCommand.Execute(null);

            var adopted = vm.Boxes.Count == 3 && vm.Predictions.Count == 0;
            Console.WriteLine($"[{(adopted ? "PASS" : "FAIL")}] 점선 받기 — 사각형 {vm.Boxes.Count}개, 점선 {vm.Predictions.Count}개");
            if (!adopted) failures++;

            // 앞에 라벨이 없으면 아무것도 안 가져온다(a 는 첫 장).
            vm.SelectedItem = vm.Items[0];
            var before = vm.Boxes.Count;
            vm.DoCopyPreviousCommand.Execute(null);

            var untouched = vm.Boxes.Count == before;
            Console.WriteLine($"[{(untouched ? "PASS" : "FAIL")}] 첫 장에서는 가져올 것이 없다 — {vm.StatusText}");
            if (!untouched) failures++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 라벨링 보조 — {ex.GetType().Name}: {ex.Message}");
            failures++;
        }
        finally
        {
            try { System.IO.Directory.Delete(root, recursive: true); } catch (Exception) { }
        }

        return failures;
    }

    private static void WriteTinyPng(string path)
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, 8, 8), new byte[8 * 8 * 4], 8 * 4, 0);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// 언어를 바꾸면 쓰던 글이 언어별로 남는지. C# ↔ JavaScript 로 본다(파이썬은 런타임을 받아 와야 해서 뺀다).
    /// </summary>
    /// <remarks>
    /// "파이썬 골랐더니 스크립트는 C#" 이 여기서 잡혀야 한다. 바꾸면 그 언어의 글(없으면 본보기)이
    /// 올라와야 하고, 돌아오면 쓰던 글이 그대로여야 한다.
    /// </remarks>
    private static int CheckScriptPerLanguage()
    {
        var failures = 0;

        try
        {
            var vm = InputAutomationViewModel.Create();

            vm.Script.Text = "var mine = 1; // C# 에서 쓰던 글";
            vm.Script.SelectedLanguage = Minguk.Tools.Input.Scripting.ScriptLanguage.JavaScript;

            var swapped = vm.Script.Text is { } js && !js.Contains("mine = 1") && js.Length > 0;
            Console.WriteLine($"[{(swapped ? "PASS" : "FAIL")}] 언어를 바꾸면 그 언어의 글이 올라온다 — {(vm.Script.Text ?? string.Empty).Split('\n')[0].Trim()}");
            if (!swapped) failures++;

            vm.Script.Text = "let theirs = 2; // JavaScript 에서 쓰던 글";
            vm.Script.SelectedLanguage = Minguk.Tools.Input.Scripting.ScriptLanguage.CSharp;

            var restored = vm.Script.Text?.Contains("mine = 1") == true;
            Console.WriteLine($"[{(restored ? "PASS" : "FAIL")}] 돌아오면 쓰던 글이 그대로다 — {(vm.Script.Text ?? string.Empty).Split('\n')[0].Trim()}");
            if (!restored) failures++;

            vm.Script.SelectedLanguage = Minguk.Tools.Input.Scripting.ScriptLanguage.JavaScript;

            var kept = vm.Script.Text?.Contains("theirs = 2") == true;
            Console.WriteLine($"[{(kept ? "PASS" : "FAIL")}] 다른 언어의 글도 각자 남는다 — {(vm.Script.Text ?? string.Empty).Split('\n')[0].Trim()}");
            if (!kept) failures++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 언어별 스크립트 — {ex.GetType().Name}: {ex.Message}");
            failures++;
        }

        return failures;
    }

    /// <summary>
    /// API 목록표. 이름이 겹치지 않고, 앞글자 고르기가 영문(대소문자 무시)·한글 둘 다 되고,
    /// 자바스크립트 엔진이 심는 이름과 어긋나지 않는지.
    /// </summary>
    /// <remarks>
    /// 자바스크립트 엔진은 인자 형이 붙은 대리자를 심어야 해서 표에서 자동으로 못 만든다. 대신 여기서
    /// 실제로 엔진을 돌려 표의 이름을 하나씩 불러 본다 - 없는 이름이면 ReferenceError 로 나온다.
    /// </remarks>
    private static int CheckScriptApiCatalog()
    {
        var failures = 0;

        try
        {
            var names = Minguk.Tools.Input.Scripting.ScriptApiCatalog.LiveNames;
            var unique = names.Distinct().Count() == names.Count;
            Console.WriteLine($"[{(unique ? "PASS" : "FAIL")}] API 표 이름이 겹치지 않는다 — {names.Count}개");
            if (!unique) failures++;

            // ── 줄 맞추기: 언어마다 규칙이 다르다 ──
            // 파이썬에서 들여쓰기는 문법이라 손대면 뜻이 바뀜다. 중괄호 규칙을 걸면 안 된다.
            {
                var editor = new Minguk.Tools.Markup.ScriptEditor();
                var forCSharp = editor.TextArea.IndentationStrategy?.GetType().Name;

                editor.Language = Minguk.Tools.Input.Scripting.ScriptLanguage.Python;
                var forPython = editor.TextArea.IndentationStrategy?.GetType().Name;

                editor.Language = Minguk.Tools.Input.Scripting.ScriptLanguage.JavaScript;
                var forJs = editor.TextArea.IndentationStrategy?.GetType().Name;

                var rules = forCSharp == "CSharpIndentationStrategy"
                            && forJs == "CSharpIndentationStrategy"
                            && forPython == "DefaultIndentationStrategy";

                Console.WriteLine($"[{(rules ? "PASS" : "FAIL")}] 줄 맞추기는 언어마다 - 파이썬은 중괄호 규칙을 안 쓴다 — C#={forCSharp} JS={forJs} Python={forPython}");
                if (!rules) failures++;

                // 실제로 맞춰지는지. 비뚚로 붙여 넣은 꼴을 만들어 놓고 그 줄들을 맞춘다.
                editor.Language = Minguk.Tools.Input.Scripting.ScriptLanguage.CSharp;
                editor.Text = string.Join(Environment.NewLine, ["while (true)", "{", "var a = 1;", "if (a > 0)", "{", "출력(a);", "}", "}"]);
                editor.TextArea.IndentationStrategy?.IndentLines(editor.Document, 1, editor.Document.LineCount);

                var lines = editor.Text.Split(Environment.NewLine);
                var indented = lines.Length == 8 && lines[2].StartsWith("\t") && lines[5].StartsWith("\t\t");

                Console.WriteLine($"[{(indented ? "PASS" : "FAIL")}] 중괄호만큼 들여쓴다 — {string.Join(" / ", lines.Select(l => l.Replace("\t", "→")))}");
                if (!indented) failures++;
            }

            var korean = Minguk.Tools.Input.Scripting.ScriptApiCatalog.Match("글").Select(m => m.Name).ToList();
            var english = Minguk.Tools.Input.Scripting.ScriptApiCatalog.Match("cl").Select(m => m.Name).ToList();
            // 앞글자가 같은 것은 다 나와야 한다 - 목록에서 고르는 것이니 하나로 좁혀질 이유가 없다.
            var ok = korean.SequenceEqual(["글자", "글자있나", "글자찾기", "글자누르기"]) && english.SequenceEqual(["Click", "ClickAt"]);
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] 앞글자로 고른다 — 글→{string.Join(",", korean)} cl→{string.Join(",", english)}");
            if (!ok) failures++;

            // 자바스크립트로 표의 이름을 전부 불러 본다. 인자가 필요한 것은 안전한 값을 넣는다.
            // 계획 모드 엔진이므로 계획 모드에 있는 것만 - 실시간 전용은 --vision 의 실시간 검사가 본다.
            using var js = new Minguk.Tools.Input.Scripting.JavaScriptEngine();
            var calls = string.Join("\n", Minguk.Tools.Input.Scripting.ScriptApiCatalog.Entries.Where(e => e.Mode == Minguk.Tools.Input.Scripting.ScriptApiMode.Both).SelectMany(e => new[] { e.Name, e.Korean }.Select(n =>
                e.Parameters switch
                {
                    "" => $"{n}();",
                    "text" => $"{n}(\"a\");",
                    "button" => $"{n}();",
                    "x, y" => $"{n}(1, 2);",
                    "x, y, button" => $"{n}(1, 2);",
                    _ => $"{n}(1);"
                })));

            var (plan, errors) = js.RunAsync(calls).GetAwaiter().GetResult();
            var bound = errors.Count == 0 && plan.Steps.Count > 0;
            Console.WriteLine($"[{(bound ? "PASS" : "FAIL")}] 자바스크립트 엔진이 표의 이름을 다 안다 — 단계 {plan.Steps.Count}개"
                              + (errors.Count == 0 ? "" : $" / {errors[0]}"));
            if (!bound) failures++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] API 표 — {ex.GetType().Name}: {ex.Message}");
            failures++;
        }

        return failures;
    }

    /// <summary>
    /// 틀린 줄 아래에 빨간 물결선이 실제로 그려지는지. 오프스크린으로 그려 빨간 픽셀의 세로 자리를 본다.
    /// </summary>
    /// <remarks>
    /// 1번 줄에 오류를 두었을 때보다 2번 줄에 두었을 때 빨간 픽셀이 더 아래에 있어야 한다.
    /// 줄 높이를 숫자로 박아 두면 글꼴이 바뀔 때 검사가 먼저 깨진다.
    /// </remarks>
    private static int CheckErrorUnderline()
    {
        try
        {
            var first = RedTop(1);
            var second = RedTop(2);
            var none = RedTop(0);

            var ok = first > 0 && second > first && none < 0;

            Console.WriteLine(ok
                ? $"[PASS] 틀린 줄에 빨간 밑줄 — 1번 줄 y={first}, 2번 줄 y={second}, 오류 없으면 없음"
                : $"[FAIL] 틀린 줄에 빨간 밑줄 — 1번 줄 y={first}, 2번 줄 y={second}, 오류 없을 때 y={none}");

            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 틀린 줄에 빨간 밑줄 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        // 그 줄에 오류를 두고 그렸을 때 빨간 픽셀이 처음 나오는 y. 없으면 -1.
        static int RedTop(int errorLine)
        {
            const int width = 240, height = 80;

            var editor = new Minguk.Tools.Markup.ScriptEditor
            {
                Width = width,
                Height = height,
                FontSize = 13,
                FontFamily = new FontFamily("Consolas"),
                Text = "Type(\"안녕\");\nEnter();\nWait(100);",
                Errors = errorLine > 0 ? [new Minguk.Tools.Input.Scripting.ScriptError(errorLine, "시험")] : []
            };

            // 줄 자리(VisualLines)는 편집기가 진짜 창에 붙어 스크롤 뷰어까지 배치돼야 나온다.
            // Measure/Arrange 만으로는 비어 있었다. 완성 검사와 같이 보이지 않는 창에 담는다.
            var host = new Window { Width = width + 20, Height = height + 40, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -5000, Top = -5000, Content = editor };
            host.Show();
            editor.UpdateLayout();

            try
            {
            // 편집기를 통째로 오프스크린에 그리면 TextView 의 층(Layer)이 그려지지 않는다 - 그 층은 디스패처의
            // 렌더 패스에서만 그려진다. 렌더러의 Draw 를 직접 불러 그 그림만 본다. 줄 자리는 TextView 가 준다.
            var textView = editor.TextArea.TextView;
            var visual = new DrawingVisual();

            using (var dc = visual.RenderOpen())
                foreach (var renderer in textView.BackgroundRenderers)
                    renderer.Draw(textView, dc);

            var target = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);

            var pixels = new byte[width * height * 4];
            target.CopyPixels(pixels, width * 4, 0);

            // 투명 바탕의 Pbgra32 는 색이 알파로 곱해져 있다(1.2px 선은 가장자리가 반투명). 되돌려서 "붉은가" 만 본다.
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                int a = pixels[i + 3];
                if (a < 60) continue;

                int r = pixels[i + 2] * 255 / a, g = pixels[i + 1] * 255 / a, b = pixels[i] * 255 / a;
                if (r > 150 && g < 110 && b < 110) return y;
            }

            return -1;
            }
            finally
            {
                host.Close();
            }
        }
    }

    /// <summary>
    /// 편집기에 C# 분류가 실제로 덧칠되는지. 글을 넣고 분류가 돌아오기를 기다린 뒤, 줄을 그려 낱말 자리의 글자색을 본다.
    /// </summary>
    private static int CheckSemanticColoring()
    {
        try
        {
            var source = Minguk.Tools.Input.Scripting.ScriptCompletionSourceFactory.Create(Minguk.Tools.Input.Scripting.ScriptLanguage.CSharp, isLive: true);
            var editor = new Minguk.Tools.Markup.ScriptEditor
            {
                Width = 400, Height = 100,
                SyntaxHighlighting = Minguk.Tools.Helper.SequenceScriptHighlighting.Dark,
                CompletionSource = source
            };

            var host = new Window { Width = 420, Height = 140, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -5000, Top = -5000, Content = editor };
            host.Show();

            try
            {
                // 필드 `다음` 을 점 없이 쓴다 - 정규식은 지역 변수 색, 분류는 멤버 색이어야 한다.
                const string text = "long 다음 = 0;\n다음 = 1;";
                var done = false;
                editor.Classified += (_, _) => done = true;
                editor.Text = text;

                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!done && DateTime.UtcNow < deadline)
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                editor.TextArea.TextView.EnsureVisualLines();

                var line = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(2));
                var offset = text.IndexOf("다음", text.IndexOf('\n'), StringComparison.Ordinal) - editor.Document.GetLineByNumber(2).Offset;
                var element = line.Elements.FirstOrDefault(e => e.RelativeTextOffset <= offset && offset < e.RelativeTextOffset + e.DocumentLength);
                var brush = element?.TextRunProperties.ForegroundBrush as System.Windows.Media.SolidColorBrush;

                var member = Minguk.Tools.Helper.SequenceScriptHighlighting.Dark.GetNamedColor("Member").Foreground.GetColor(null);
                var ok = done && editor.SemanticTokenCount > 0 && brush is not null && brush.Color == member;

                Console.WriteLine(ok
                    ? $"[PASS] C# 분류 덧칠 — 점 없는 필드 \"다음\" 이 멤버 색 {brush!.Color}, 토막 {editor.SemanticTokenCount}개"
                    : $"[FAIL] C# 분류 덧칠 — 분류 끝남 {done}, 토막 {editor.SemanticTokenCount}, 글자색 {brush?.Color.ToString() ?? "없음"} (기대 {member})");

                return ok ? 0 : 1;
            }
            finally
            {
                host.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] C# 분류 덧칠 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 플레이 화면에서 프로젝트를 고르면 워크벤치가 프로젝트로 열리고 틀린 곳이 파일·줄로 잡히는지, 파일을 고르면 프로젝트가 닫히는지.
    /// </summary>
    private static int CheckPlayProject()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "minguk-play-" + Guid.NewGuid().ToString("N"));

        try
        {
            var project = Minguk.Tools.Input.Scripting.Projects.ScriptProject.Create(System.IO.Path.Combine(folder, "사냥"), "사냥", "출력(도우미());\n");
            Minguk.Tools.Input.Scripting.Projects.ScriptProject.WriteText(project.FullPath("도우미.csx"), "int 도우미() => 없는것;\n");
            project.Add("도우미.csx");
            project.Save();

            var single = System.IO.Path.Combine(folder, "혼자.csx");
            System.IO.File.WriteAllText(single, "출력(1);");

            var vm = PlayViewModel.Create();
            vm.SelectedScript = ScriptFileItem.From(project.FilePath);

            // 검사는 입력이 멈춘 뒤(500ms) 돈다 - 기다린다.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (vm.Script.Errors.Count == 0 && DateTime.UtcNow < deadline)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            var error = vm.Script.Errors.FirstOrDefault();
            var projectOk = vm.Script.IsProject && error.FileName == "도우미.csx" && error.Line == 1;

            vm.SelectedScript = ScriptFileItem.From(single);
            var singleOk = !vm.Script.IsProject && vm.Script.Text == "출력(1);";

            var ok = projectOk && singleOk;
            Console.WriteLine(ok
                ? $"[PASS] 플레이 화면 프로젝트 — 고르면 프로젝트로 열려 '{error}' 를 잡고, 파일을 고르면 한 파일짜리로 돌아간다"
                : $"[FAIL] 플레이 화면 프로젝트 — 프로젝트 {vm.Script.IsProject}/{projectOk} (오류 {vm.Script.Errors.Count}: {error}), 한 파일짜리 {singleOk}");

            vm.Script.Dispose();
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 플레이 화면 프로젝트 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            try { System.IO.Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// 참조 표시 - 선언 줄 위에 "참조 N개" 가 끼워져 그 줄만 높아지는지, 참조 창에 부르는 곳이 들어가는지.
    /// </summary>
    private static int CheckCodeLens()
    {
        try
        {
            var source = Minguk.Tools.Input.Scripting.ScriptCompletionSourceFactory.Create(Minguk.Tools.Input.Scripting.ScriptLanguage.CSharp, isLive: true);
            var editor = new Minguk.Tools.Markup.ScriptEditor
            {
                Width = 500, Height = 200, FontSize = 13,
                SyntaxHighlighting = Minguk.Tools.Helper.SequenceScriptHighlighting.Dark,
                CompletionSource = source
            };

            var host = new Window { Width = 520, Height = 240, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -5000, Top = -5000, Content = editor };
            host.Show();

            try
            {
                const string text = "int 둘() => 2;\n출력(둘());\n출력(둘());\n";
                var done = false;
                editor.LensesApplied += (_, _) => done = true;
                editor.Text = text;

                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!done && DateTime.UtcNow < deadline)
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                editor.TextArea.TextView.EnsureVisualLines();

                var view = editor.TextArea.TextView;
                var declared = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1)).Height;
                var plain = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(2)).Height;

                // 참조 창: 선언(둘)의 자리로 찾아 창에 넣는다.
                var finder = (Minguk.Tools.Input.Scripting.IScriptReferenceFinder)source!;
                var references = finder.FindReferencesAsync(text, text.IndexOf("둘", StringComparison.Ordinal)).GetAwaiter().GetResult();
                editor.ShowReferences(references, editor);

                var panel = editor.ReferencesPanel;
                var rows = (panel?.FindName("Grid") as DevExpress.Xpf.Grid.GridControl)?.ItemsSource as System.Collections.ICollection;

                var ok = done && editor.CodeLensCount == 1 && declared > plain * 1.5 && panel is not null && rows?.Count == 2;

                // 눈으로 볼 수 있게 남긴다(화면 밖이라 코드 글자 층은 안 그려질 수 있다 - "참조" 글자와 줄 높이만 본다).
                try
                {
                    var png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "minguk-codelens.png");
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)editor.ActualWidth, (int)editor.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    var visual = new System.Windows.Media.DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, editor.ActualWidth, editor.ActualHeight));
                        dc.DrawRectangle(new System.Windows.Media.VisualBrush(editor), null, new Rect(0, 0, editor.ActualWidth, editor.ActualHeight));
                    }
                    bitmap.Render(visual);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = System.IO.File.Create(png);
                    encoder.Save(stream);
                    Console.WriteLine($"[INFO] 참조 표시 그림: {png}");
                }
                catch (Exception) { }

                Console.WriteLine(ok
                    ? $"[PASS] 참조 표시 — \"참조 2개\" 1곳, 선언 줄 {declared:0}px / 보통 줄 {plain:0}px, 참조 창 {rows!.Count}줄"
                    : $"[FAIL] 참조 표시 — 셈 끝 {done}, 표시 {editor.CodeLensCount}곳, 선언 줄 {declared:0}px / 보통 줄 {plain:0}px, 참조 창 {(panel is null ? "없음" : $"{rows?.Count}줄")}");

                return ok ? 0 : 1;
            }
            finally
            {
                host.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 참조 표시 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>낱말을 치면 완성 목록이 뜨고, 고르면 이름이 들어가는지.</summary>
    private static int CheckCompletion()
    {
        try
        {
            var editor = new Minguk.Tools.Markup.ScriptEditor { Width = 300, Height = 100 };

            // 창은 편집기가 화면에 붙어 있어야 뜬다. 보이지 않는 창에 담아 띄운다.
            var host = new Window { Width = 320, Height = 140, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -5000, Top = -5000, Content = editor };
            host.Show();

            try
            {
                editor.Text = "";
                editor.CaretOffset = 0;
                editor.Document.Insert(0, "글");
                editor.CaretOffset = 1;
                editor.OpenCompletion();

                var opened = editor.IsCompletionOpen;

                // 목록에서 첫 항목을 넣는다 - 캐럿 앞 "글" 이 "글자" 로 바뀌어야 한다.
                var inserted = false;
                if (opened)
                {
                    editor.TextArea.PerformTextInput("(");
                    inserted = editor.Text.StartsWith("글자(");
                }

                var ok = opened && inserted;
                Console.WriteLine(ok
                    ? $"[PASS] 코드 완성 — \"글\" 을 치면 목록이 뜨고 고르면 \"{editor.Text}\""
                    : $"[FAIL] 코드 완성 — 열림 {opened}, 넣음 {inserted}, 글=\"{editor.Text}\"");

                return ok ? 0 : 1;
            }
            finally
            {
                host.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] 코드 완성 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>loss 꺾은선이 실제로 선을 그리는지. 오프스크린으로 그려 선 색 픽셀을 센다.</summary>
    private static int CheckLossSparkline()
    {
        try
        {
            const int width = 200, height = 44;

            var values = new System.Collections.ObjectModel.ObservableCollection<double> { 278, 1.5, 1.4, 1.2, 1.1, 0.95 };
            var chart = new Minguk.Tools.Markup.LossSparkline { Width = width, Height = height, Values = values };

            chart.Measure(new Size(width, height));
            chart.Arrange(new Rect(0, 0, width, height));
            chart.UpdateLayout();

            var target = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(chart);

            var pixels = new byte[width * height * 4];
            target.CopyPixels(pixels, width * 4, 0);

            var expected = Minguk.Tools.Markup.LabelCanvas.ColorOf(0);
            var painted = 0;

            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (Math.Abs(pixels[i + 2] - expected.R) < 40 && Math.Abs(pixels[i + 1] - expected.G) < 40 && Math.Abs(pixels[i] - expected.B) < 40)
                    painted++;
            }

            // 값 하나를 더하면 다시 그려야 한다 - 컬렉션 구독이 빠지면 학습 내내 빈 칸이다.
            values.Add(0.9);
            chart.UpdateLayout();

            var ok = painted > 40;
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] loss 꺾은선을 그린다 — 선 색 픽셀 {painted}개");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] loss 꺾은선 — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
