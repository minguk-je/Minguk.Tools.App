using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Minguk.Tools.Input;
using Minguk.Tools.Input.Korean;

namespace Minguk.Tools.Tests;

internal static partial class Program
{
    /// <summary>
    /// 한글 음절이 두벌식 자판 자리로 옳게 풀리는지.
    ///
    /// IME 도 입력도 필요 없는 순수 계산이라 어느 경로에서든 돈다.
    /// 아래 실제 타이핑 검증은 한국어 IME 가 있어야 돌기 때문에, 표가 맞는지는 여기서 따로 잡는다.
    /// </summary>
    private static void TestHangulMapping()
    {
        // (음절, 기대 키, 무엇을 보는가)
        (string Syllable, string Keys, string Why)[] cases =
        [
            ("가", "rk",   "초성 + 중성"),
            ("안", "dks",  "초성 + 중성 + 종성"),
            ("값", "rkqt", "겹받침 ㅄ 이 q + t 로 풀리는지"),
            ("와", "dhk",  "복합모음 ㅘ 가 h + k 로 풀리는지 (초성 ㅇ = d 포함)"),
            ("꽃", "Rhc",  "된소리 ㄲ 이 Shift(대문자 R) 로 나가는지"),
            ("쌀", "Tkf",  "된소리 ㅆ"),
            ("예", "dP",   "복합모음 ㅖ 는 Shift 한 자리 (초성 ㅇ = d 포함)"),
            ("닭", "ekfr", "겹받침 ㄺ 이 f + r 로 풀리는지"),
            ("ㄱ", "r",    "완성형이 아닌 호환 자모 낱글자도 다루는지"),
        ];

        var failures = 0;

        foreach (var (syllable, expected, why) in cases)
        {
            var got = string.Empty;
            foreach (var c in syllable)
            {
                if (HangulKeyMap.TryGetKeySequence(c, out var keys)) got += keys;
            }

            if (got == expected) continue;

            failures++;
            Console.WriteLine($"  '{syllable}' 기대 \"{expected}\" 실제 \"{got}\"  ({why})");
        }

        Check("한글 자판 매핑", failures == 0, $"{cases.Length}개 음절 중 {failures}개 어긋남");

        // 한글이 아닌 것을 한글로 보면 엉뚱한 키가 나간다.
        // 호환 자모(ㄱ)는 의도적으로 true 다 - 낱글자로 들어와도 자리를 알기 때문이다.
        Check("한글 판별",
              HangulKeyMap.IsHangul('가') && HangulKeyMap.IsHangul('힣') && HangulKeyMap.IsHangul('ㄱ')
              && !HangulKeyMap.IsHangul('a') && !HangulKeyMap.IsHangul('1') && !HangulKeyMap.IsHangul('漢'),
              "완성형 음절과 호환 자모는 true, 영문·숫자·한자는 false");
    }

    /// <summary>키보드 종류를 읽어 한/영 키가 무엇인지 판별하는지.</summary>
    private static void TestKeyboardDetection()
    {
        var detection = KoreanKeyboardInfo.Detect();

        Console.WriteLine($"  키보드 Type={detection.Type} Subtype={detection.Subtype} "
                          + $"→ {detection.Description} / 한영 전송 «{KoreanKeyboardInfo.ModeLabel(detection.Mode)}»");

        Check("키보드 판별", Enum.IsDefined(detection.Mode),
              $"{KoreanKeyboardInfo.ModeLabel(detection.Mode)}, 한국어 키보드 {detection.IsKorean}");
    }

    /// <summary>
    /// 실제로 한글이 찍히는지. 한국어 IME 가 있어야 돌아간다.
    /// </summary>
    /// <remarks>
    /// 조합 중인 마지막 음절은 확정되기 전까지 IME 가 들고 있다.
    /// 그래서 다 친 뒤 Enter 로 확정하고 읽는다.
    /// </remarks>
    private static async Task TestHangulTypingAsync(TestWindow ui)
    {
        if (!_service.SupportsTyping)
        {
            Skip("한글 입력", $"{_adapter.Name} 은 스캔코드를 넣지 못한다");
            return;
        }

        if (!IsKoreanInputLanguage())
        {
            Skip("한글 입력", "지금 입력 언어가 한국어가 아니다 - 한국어 IME 를 켜고 다시 돌릴 것");
            return;
        }

        if (!KoreanKeyboardInfo.TryGetForegroundHangulMode(out var startedInHangul))
        {
            Skip("한글 입력", "포커스를 가진 창의 IME 상태를 읽지 못했다");
            return;
        }

        Console.WriteLine($"  시작 IME 상태: {(startedInHangul ? "한글" : "영문")}");

        const string expected = "한글 ab";
        var mode = KoreanKeyboardInfo.Detect().Mode;

        Post(ui.Input.Clear);

        foreach (var c in expected)
        {
            await _service.TypeCharAsync(c, holdTimeMs: 20, mode);
            await Task.Delay(40);
        }

        // 마지막 음절이 조합 중일 수 있다. Enter 로 확정한다.
        await _service.TapKeyAsync(ScanCodes.Enter, holdTimeMs: 20);
        await Task.Delay(500);

        var actual = Read(() => ui.Input.Text).TrimEnd('\r', '\n');

        Check("한글 입력", actual == expected,
              $"기대 \"{expected}\" / 실제 \"{actual}\" — 한글과 영문이 섞여도 IME 를 알아서 맞추는지");
    }

    /// <summary>
    /// 지금 스레드의 입력 언어가 한국어인지. 아니면 어떤 키를 보내도 한글이 나오지 않는다.
    /// </summary>
    private static bool IsKoreanInputLanguage()
    {
        const int KoreanLanguageId = 0x0412;   // ko-KR

        var layout = GetKeyboardLayout(GetWindowThreadProcessId(OurHandle, IntPtr.Zero));

        return ((long)layout & 0xFFFF) == KoreanLanguageId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
}
