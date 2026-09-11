using System;
using System.Linq;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 쓸 OCR 엔진을 고른다. 지금은 Windows 내장 OCR 하나다.
/// </summary>
/// <remarks>
/// 다른 엔진을 넣을 때 여기만 고친다. 부르는 쪽은 <see cref="IOcrEngine"/> 만 안다.
/// </remarks>
public static class OcrEngineFactory
{
    /// <summary>기본으로 먼저 찾는 언어. 게임 UI 가 한글이다. 없으면 있는 것으로 간다.</summary>
    public const string PreferredLanguage = "ko";

    /// <summary>엔진을 만든다. 언어 팩이 하나도 없으면 null.</summary>
    public static IOcrEngine? TryCreate(string? languageTag = PreferredLanguage)
        => WindowsOcrEngine.TryCreate(languageTag);

    /// <summary>엔진을 만든다. 못 만들면 사용자가 무엇을 해야 하는지 담은 예외.</summary>
    public static IOcrEngine Create(string? languageTag = PreferredLanguage)
        => TryCreate(languageTag)
           ?? throw new InvalidOperationException(
               "Windows OCR 언어 팩이 없습니다. 설정 > 시간 및 언어 > 언어 에서 한국어나 영어를 추가하면 됩니다. "
               + $"지금 깔린 것: {(WindowsOcrEngine.AvailableLanguages.Count == 0 ? "없음" : string.Join(", ", WindowsOcrEngine.AvailableLanguages))}");
}
