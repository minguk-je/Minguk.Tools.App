using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Korean;

namespace Minguk.Tools.Tests;

/// <summary>
/// 입력 어댑터가 실제로 OS 를 거쳐 살아 있는 창에 입력을 전달하는지 확인한다.
/// 자기 소유의 WPF 창을 띄우고 거기에 입력을 쏜 뒤 결과를 되읽는다.
/// </summary>
internal static partial class Program
{
    private static readonly List<string> Results = [];
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Contains("--calibrate")) return Calibrate.Run();
        if (args.Contains("--fallback")) return FallbackProbe.Run();

        // 같은 검증을 경로만 바꿔 돌린다. 경로마다 실제로 입력이 나가는지 따로 봐야 한다.
        var backend = ParseBackend(args);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var ui = new TestWindow();

        Console.WriteLine($"검증할 경로: {backend}");

        ui.ContentRendered += async (_, _) =>
        {
            try
            {
                // 실제 앱과 같은 조건: 전송은 백그라운드 스레드에서 이뤄진다.
                // 보내는 스레드와 받는 창의 UI 스레드가 같으면 연속 전송한 키가 유실된다.
                await Task.Run(() => RunAllAsync(ui, backend));
            }
            catch (Exception ex)
            {
                Fail("예외", ex.ToString());
            }
            finally
            {
                ui.Close();
                app.Shutdown();
            }
        };

        ui.Show();
        app.Run();

        Console.WriteLine();
        foreach (var line in Results) Console.WriteLine(line);
        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "== 전체 통과 ==" : $"== 실패 {_failures}건 ==");

        return _failures == 0 ? 0 : 1;
    }

    private static void Pass(string name, string detail) => Results.Add($"[PASS] {name} — {detail}");

    private static void Fail(string name, string detail)
    {
        _failures++;
        Results.Add($"[FAIL] {name} — {detail}");
    }

    /// <summary>
    /// 이 경로에는 해당하지 않는 항목. 실패가 아니다.
    /// PostMessage 는 진짜 커서를 움직이지 않고 스캔코드도 넣지 못한다 - 못 하는 것이 아니라
    /// 그 경로의 존재 이유(포커스·커서를 안 뺏는 것)에서 따라오는 성질이다.
    /// </summary>
    private static void Skip(string name, string reason) => Results.Add($"[N/A ] {name} — {reason}");

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) Pass(name, detail);
        else Fail(name, detail);
    }

    private static IInputAdapter _adapter = null!;
    private static InputService _service = null!;

    /// <summary>--backend=Interception 처럼 지정한다. 없으면 SendInput.</summary>
    private static InputBackend ParseBackend(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith("--backend=", StringComparison.OrdinalIgnoreCase));
        if (arg is null) return InputBackend.SendInput;

        var name = arg["--backend=".Length..];

        if (!Enum.TryParse<InputBackend>(name, ignoreCase: true, out var backend))
            throw new ArgumentException($"모르는 경로다: {name}. 쓸 수 있는 것: {string.Join(", ", Enum.GetNames<InputBackend>())}");

        return backend;
    }

    private static async Task RunAllAsync(TestWindow ui, InputBackend backend)
    {
        // PostMessage 경로는 어느 창에 넣을지 알아야 한다. 검증 대상이 이 창이다.
        OurHandle = Read(() => new WindowInteropHelper(ui).Handle);

        _adapter = InputAdapterFactory.Create(backend, () => OurHandle);
        _service = new InputService(_adapter);

        if (!_adapter.IsAvailable)
        {
            Fail("어댑터 준비", $"{_adapter.Name} 을 쓸 수 없다 - {_adapter.UnavailableReason}");
            return;
        }

        Pass("어댑터 준비", $"{_adapter.Name}, 스캔코드 {(_service.SupportsTyping ? "가능" : "불가")}, "
                          + $"진짜 커서 {(_adapter.GetCursorPosition() is null ? "안 움직임" : "움직임")}");

        TestBackendSwitching();

        SetForegroundWindow(OurHandle);
        Post(() =>
        {
            ui.Activate();
            ui.Input.Focus();
        });
        await Task.Delay(300);

        if (!Read(() => ui.Input.IsKeyboardFocused))
        {
            Fail("포커스 확보", "테스트 창의 입력란이 키보드 포커스를 못 잡아 이후 검증을 건너뛴다.");
            return;
        }
        Pass("포커스 확보", "입력란이 키보드 포커스를 가짐");

        if (_service.SupportsTyping)
        {
            await TestTypingAsync(ui);
            await TestEnterAsync(ui);
        }
        else
        {
            Skip("키보드 문자 입력", $"{_adapter.Name} 은 스캔코드를 넣지 못한다");
            Skip("Enter 키", $"{_adapter.Name} 은 스캔코드를 넣지 못한다");
        }

        await TestVirtualKeyAsync(ui);
        await TestMouseMoveAsync();
        await TestClickAsync(ui);
        await TestWheelAsync(ui);

        _adapter.Dispose();
    }

    /// <summary>
    /// 세 경로가 각각 만들어지고 정리되는지. Interception 은 드라이버가 없으면 사용 불가로 나온다.
    /// </summary>
    private static void TestBackendSwitching()
    {
        foreach (var backend in Enum.GetValues<InputBackend>())
        {
            using var adapter = InputAdapterFactory.Create(backend, () => IntPtr.Zero);

            var scanCodes = adapter is IScanCodeInput ? "스캔코드 가능" : "스캔코드 불가";
            var state = adapter.IsAvailable ? "사용 가능" : $"사용 불가 ({adapter.UnavailableReason})";

            Console.WriteLine($"  {backend,-12} {adapter.Name,-14} {state} / {scanCodes}");
        }

        Pass("경로 세 개 생성·정리", $"{string.Join(", ", Enum.GetNames<InputBackend>())}");
    }

    /// <summary>영문 대소문자·숫자·문장부호·공백이 그대로 찍히는지.</summary>
    private static async Task TestTypingAsync(TestWindow ui)
    {
        const string expected = "Hi Claude, 42%!";

        Post(ui.Input.Clear);

        // 글자 사이에 일부러 아무 대기도 두지 않는다. 연속 전송에서 유실이 없는지가 관건이다.
        foreach (var c in expected)
        {
            await _service.TypeCharAsync(c, holdTimeMs: 12, HangulKeyMode.HangulScanCode);
        }

        await Task.Delay(400);

        var actual = Read(() => ui.Input.Text);
        Check("키보드 문자 입력", actual == expected, $"기대 \"{expected}\" / 실제 \"{actual}\"");
    }

    /// <summary>Enter 스캔코드가 줄바꿈으로 도착하는지.</summary>
    private static async Task TestEnterAsync(TestWindow ui)
    {
        Post(ui.Input.Clear);
        await _service.TapKeyAsync(ScanCodes.Enter, holdTimeMs: 12);
        await Task.Delay(250);

        var text = Read(() => ui.Input.Text);
        Check("Enter 키", text.Contains((char)10), $"입력란 내용 = {Describe(text)}");
    }

    /// <summary>
    /// 가상 키로 넣는 경로. <c>PreviewInputRouter</c> 가 실제로 쓰는 길이라 세 경로 모두 확인한다.
    /// WPF 가 주는 것이 가상 키라 미리보기 입력은 이쪽으로만 나간다.
    /// </summary>
    private static async Task TestVirtualKeyAsync(TestWindow ui)
    {
        const ushort vkA = 0x41;
        const ushort vkB = 0x42;

        Post(ui.Input.Clear);

        foreach (var vk in (ushort[])[vkA, vkB])
        {
            _adapter.PressKey(vk);
            await Task.Delay(20);
            _adapter.ReleaseKey(vk);
            await Task.Delay(40);
        }

        await Task.Delay(300);

        var actual = Read(() => ui.Input.Text);
        Check("가상 키 입력", actual == "ab", $"기대 \"ab\" / 실제 \"{actual}\"");
    }

    private static string Describe(string s)
        => "\"" + s.Replace(((char)13).ToString(), "<CR>").Replace(((char)10).ToString(), "<LF>") + "\"";

    /// <summary>UI 스레드의 값을 읽어 온다 (검증은 백그라운드 스레드에서 돈다).</summary>
    private static T Read<T>(Func<T> read) => Application.Current.Dispatcher.Invoke(read);

    /// <summary>UI 스레드에서 동작을 실행한다.</summary>
    private static void Post(Action action) => Application.Current.Dispatcher.Invoke(action);

    private static IntPtr OurHandle;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
