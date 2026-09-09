using System.Collections.Generic;
using System.Linq;

namespace Minguk.Tools.Input.Korean;

/// <summary>
/// 한글 문자를 두벌식 자판에서 눌러야 할 영문 키 순서로 바꿉니다.
/// 예) '안' -> "dks" (ㅇ=d, ㅏ=k, ㄴ=s)
/// </summary>
/// <remarks>
/// 반환 문자열에서 <b>대문자는 Shift를 함께 눌러야 하는 키</b>를 뜻합니다.
/// 예) 'ㄲ' -> "R" 은 Shift+R 이 아니라 Shift+r(즉 자판의 R 위치 + Shift)입니다.
/// </remarks>
public static class HangulKeyMap
{
    // 유니코드 한글 음절 영역: 가(U+AC00) ~ 힣(U+D7A3)
    private const int SyllableBase = 0xAC00;
    private const int SyllableLast = 0xD7A3;
    private const int JungCount = 21;
    private const int JongCount = 28;

    /// <summary>초성 19자를 두벌식 키로. 인덱스는 유니코드 초성 순서.</summary>
    private static readonly string[] Choseong =
    [
        "r",  // ㄱ
        "R",  // ㄲ
        "s",  // ㄴ
        "e",  // ㄷ
        "E",  // ㄸ
        "f",  // ㄹ
        "a",  // ㅁ
        "q",  // ㅂ
        "Q",  // ㅃ
        "t",  // ㅅ
        "T",  // ㅆ
        "d",  // ㅇ
        "w",  // ㅈ
        "W",  // ㅉ
        "c",  // ㅊ
        "z",  // ㅋ
        "x",  // ㅌ
        "v",  // ㅍ
        "g"   // ㅎ
    ];

    /// <summary>중성 21자. 복합 모음은 두 키를 이어 누릅니다(ㅘ = ㅗ+ㅏ).</summary>
    private static readonly string[] Jungseong =
    [
        "k",   // ㅏ
        "o",   // ㅐ
        "i",   // ㅑ
        "O",   // ㅒ
        "j",   // ㅓ
        "p",   // ㅔ
        "u",   // ㅕ
        "P",   // ㅖ
        "h",   // ㅗ
        "hk",  // ㅘ
        "ho",  // ㅙ
        "hl",  // ㅚ
        "y",   // ㅛ
        "n",   // ㅜ
        "nj",  // ㅝ
        "np",  // ㅞ
        "nl",  // ㅟ
        "b",   // ㅠ
        "m",   // ㅡ
        "ml",  // ㅢ
        "l"    // ㅣ
    ];

    /// <summary>종성 28자. 0번은 받침 없음, 겹받침은 두 키를 이어 누릅니다(ㄳ = ㄱ+ㅅ).</summary>
    private static readonly string[] Jongseong =
    [
        "",    // 없음
        "r",   // ㄱ
        "R",   // ㄲ
        "rt",  // ㄳ
        "s",   // ㄴ
        "sw",  // ㄵ
        "sg",  // ㄶ
        "e",   // ㄷ
        "f",   // ㄹ
        "fr",  // ㄺ
        "fa",  // ㄻ
        "fq",  // ㄼ
        "ft",  // ㄽ
        "fx",  // ㄾ
        "fv",  // ㄿ
        "fg",  // ㅀ
        "a",   // ㅁ
        "q",   // ㅂ
        "qt",  // ㅄ
        "t",   // ㅅ
        "T",   // ㅆ
        "d",   // ㅇ
        "w",   // ㅈ
        "c",   // ㅊ
        "z",   // ㅋ
        "x",   // ㅌ
        "v",   // ㅍ
        "g"    // ㅎ
    ];

    /// <summary>호환 자모(ㄱ~ㅣ, U+3131~U+3163)를 단독으로 입력할 때의 키.</summary>
    private static readonly Dictionary<char, string> CompatibilityJamo = new()
    {
        ['ㄱ'] = "r",  ['ㄲ'] = "R",  ['ㄳ'] = "rt", ['ㄴ'] = "s",  ['ㄵ'] = "sw",
        ['ㄶ'] = "sg", ['ㄷ'] = "e",  ['ㄸ'] = "E",  ['ㄹ'] = "f",  ['ㄺ'] = "fr",
        ['ㄻ'] = "fa", ['ㄼ'] = "fq", ['ㄽ'] = "ft", ['ㄾ'] = "fx", ['ㄿ'] = "fv",
        ['ㅀ'] = "fg", ['ㅁ'] = "a",  ['ㅂ'] = "q",  ['ㅃ'] = "Q",  ['ㅄ'] = "qt",
        ['ㅅ'] = "t",  ['ㅆ'] = "T",  ['ㅇ'] = "d",  ['ㅈ'] = "w",  ['ㅉ'] = "W",
        ['ㅊ'] = "c",  ['ㅋ'] = "z",  ['ㅌ'] = "x",  ['ㅍ'] = "v",  ['ㅎ'] = "g",
        ['ㅏ'] = "k",  ['ㅐ'] = "o",  ['ㅑ'] = "i",  ['ㅒ'] = "O",  ['ㅓ'] = "j",
        ['ㅔ'] = "p",  ['ㅕ'] = "u",  ['ㅖ'] = "P",  ['ㅗ'] = "h",  ['ㅘ'] = "hk",
        ['ㅙ'] = "ho", ['ㅚ'] = "hl", ['ㅛ'] = "y",  ['ㅜ'] = "n",  ['ㅝ'] = "nj",
        ['ㅞ'] = "np", ['ㅟ'] = "nl", ['ㅠ'] = "b",  ['ㅡ'] = "m",  ['ㅢ'] = "ml",
        ['ㅣ'] = "l"
    };

    /// <summary>한글 음절(가~힣) 또는 호환 자모인지.</summary>
    public static bool IsHangul(char c)
        => (c >= SyllableBase && c <= SyllableLast) || CompatibilityJamo.ContainsKey(c);

    /// <summary>문자열에 한글이 하나라도 있는지.</summary>
    public static bool ContainsHangul(string text) => text.Any(IsHangul);

    /// <summary>
    /// 한글 한 글자를 두벌식 키 순서로 바꿉니다.
    /// </summary>
    /// <param name="keys">눌러야 할 키들. 대문자는 Shift가 필요한 키입니다.</param>
    public static bool TryGetKeySequence(char c, out string keys)
    {
        if (CompatibilityJamo.TryGetValue(c, out string? jamo))
        {
            keys = jamo;
            return true;
        }

        if (c < SyllableBase || c > SyllableLast)
        {
            keys = string.Empty;
            return false;
        }

        // 음절 = ((초성 * 21) + 중성) * 28 + 종성  구조를 되돌립니다.
        int index = c - SyllableBase;
        int cho = index / (JungCount * JongCount);
        int jung = index % (JungCount * JongCount) / JongCount;
        int jong = index % JongCount;

        keys = Choseong[cho] + Jungseong[jung] + Jongseong[jong];
        return true;
    }
}
