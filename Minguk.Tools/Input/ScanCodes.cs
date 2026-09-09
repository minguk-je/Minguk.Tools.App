namespace Minguk.Tools.Input;

/// <summary>
/// Scan Code Set 1 값. 키보드의 <b>물리적 위치</b>를 가리킨다.
///
/// 가상 키와 무엇이 다른가
///   가상 키는 "무슨 글자인가", 스캔코드는 "어느 자리인가" 다. 자리로 찍으므로
///   키보드 레이아웃이나 지금 IME 상태와 무관하게 늘 같은 키가 눌린다.
///   두벌식 한글 입력이 이것을 필요로 한다 - 자모는 자판 "자리" 로 정해져 있기 때문이다.
/// </summary>
public static class ScanCodes
{
    // 방향키. E0 확장 플래그가 함께 가야 넘패드와 구분된다.
    public const ushort Up = 0x48;
    public const ushort Right = 0x4D;
    public const ushort Down = 0x50;
    public const ushort Left = 0x4B;

    /// <summary>한/영. 키 업(브레이크) 코드가 없는 "메이크 온리" 키라 키 다운만 보낸다.</summary>
    public const ushort Hangul = 0xF2;

    /// <summary>한자. 한/영 과 같이 메이크 온리 키다.</summary>
    public const ushort Hanja = 0xF1;

    /// <summary>101키 종류 1·3 에서 한/영 으로 동작하는 오른쪽 Alt (E0 확장 키).</summary>
    public const ushort RightAlt = 0x38;

    public const ushort LeftShift = 0x2A;
    public const ushort Space = 0x39;

    /// <summary>메인 Enter. 넘패드 Enter 는 같은 값에 E0 확장 플래그가 붙는 별개 키다.</summary>
    public const ushort Enter = 0x1C;

    /// <summary>
    /// 문자를 "스캔코드 + Shift 필요 여부" 로 바꾼다.
    /// 영문 대문자와 <c>!</c> <c>@</c> 같은 문장부호는 Shift 를 함께 눌러야 한다.
    /// </summary>
    /// <returns>다룰 수 없는 문자면 false.</returns>
    public static bool TryGetKeyStroke(char c, out ushort scanCode, out bool needsShift)
    {
        // A~Z / 0~9 는 자리가 그대로고, 대문자일 때만 Shift 가 붙는다.
        if (TryGetScanCode(c, out scanCode))
        {
            needsShift = char.IsAsciiLetterUpper(c);
            return true;
        }

        (scanCode, needsShift) = c switch
        {
            ' ' => (Space, false),

            '-' => ((ushort)0x0C, false),
            '_' => ((ushort)0x0C, true),
            '=' => ((ushort)0x0D, false),
            '+' => ((ushort)0x0D, true),
            '[' => ((ushort)0x1A, false),
            '{' => ((ushort)0x1A, true),
            ']' => ((ushort)0x1B, false),
            '}' => ((ushort)0x1B, true),
            '\\' => ((ushort)0x2B, false),
            '|' => ((ushort)0x2B, true),
            ';' => ((ushort)0x27, false),
            ':' => ((ushort)0x27, true),
            '\'' => ((ushort)0x28, false),
            '"' => ((ushort)0x28, true),
            '`' => ((ushort)0x29, false),
            '~' => ((ushort)0x29, true),
            ',' => ((ushort)0x33, false),
            '<' => ((ushort)0x33, true),
            '.' => ((ushort)0x34, false),
            '>' => ((ushort)0x34, true),
            '/' => ((ushort)0x35, false),
            '?' => ((ushort)0x35, true),
            '!' => ((ushort)0x02, true),
            '@' => ((ushort)0x03, true),
            '#' => ((ushort)0x04, true),
            '$' => ((ushort)0x05, true),
            '%' => ((ushort)0x06, true),
            '^' => ((ushort)0x07, true),
            '&' => ((ushort)0x08, true),
            '*' => ((ushort)0x09, true),
            '(' => ((ushort)0x0A, true),
            ')' => ((ushort)0x0B, true),
            _ => ((ushort)0, false)
        };

        return scanCode != 0;
    }

    /// <summary>영문자(대소문자 구분 없음) 또는 숫자를 스캔코드로 바꾼다.</summary>
    /// <returns>다룰 수 없는 문자면 false.</returns>
    public static bool TryGetScanCode(char c, out ushort scanCode)
    {
        scanCode = char.ToUpperInvariant(c) switch
        {
            'A' => 0x1E, 'B' => 0x30, 'C' => 0x2E, 'D' => 0x20,
            'E' => 0x12, 'F' => 0x21, 'G' => 0x22, 'H' => 0x23,
            'I' => 0x17, 'J' => 0x24, 'K' => 0x25, 'L' => 0x26,
            'M' => 0x32, 'N' => 0x31, 'O' => 0x18, 'P' => 0x19,
            'Q' => 0x10, 'R' => 0x13, 'S' => 0x1F, 'T' => 0x14,
            'U' => 0x16, 'V' => 0x2F, 'W' => 0x11, 'X' => 0x2D,
            'Y' => 0x15, 'Z' => 0x2C,
            '1' => 0x02, '2' => 0x03, '3' => 0x04, '4' => 0x05, '5' => 0x06,
            '6' => 0x07, '7' => 0x08, '8' => 0x09, '9' => 0x0A, '0' => 0x0B,
            _ => 0
        };

        return scanCode != 0;
    }
}
