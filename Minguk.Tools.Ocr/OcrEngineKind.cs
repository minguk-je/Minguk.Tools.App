using System.Collections.Generic;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>고를 수 있는 글자 읽기 엔진.</summary>
public enum OcrEngineKind
{
    /// <summary>PP-OCRv5 를 GPU(DirectML)로. 못 열면 CPU 로 내려앉는다.</summary>
    PaddleGpu,

    /// <summary>PP-OCRv5 를 CPU 로. 20Hz 검출과 GPU 를 나눠 쓰는 것을 피한다.</summary>
    PaddleCpu,

    /// <summary>Windows 내장 OCR(<c>Windows.Media.Ocr</c>). 빠르지만 게임 글자는 잘 못 읽었다(2026-09-16 까지 쓰던 것).</summary>
    Windows,

    /// <summary>PP-OCRv6 tiny 를 GPU 로 - 가장 작고 빠르다(약 6MB, 실측 44ms). <b>한글은 없다</b>(사전 6,904자 - 라틴·한자·가나) - 숫자·영문 HUD 용.</summary>
    PaddleV6TinyGpu,

    /// <summary>PP-OCRv6 small 을 GPU 로(약 31MB, 실측 57ms). 역시 한글은 없다(사전 18,708자).</summary>
    PaddleV6SmallGpu
}

/// <summary>
/// 콤보에 보이는 엔진 하나 - 이름과 종류.
/// </summary>
/// <remarks>
/// 사용자(2026-09-18) "OCR 종류 선택해서 돌려 볼 수 있게". 후보로 봤던 RapidOcrNet 은 넣지 않았다 - <c>Microsoft.ML.OnnxRuntime</c> 1.29(CPU)와 관리 어셈블리 1.29 를 끌고 와
/// 우리 DirectML 1.24.4 와 짝이 어긋나고(관리 1.29 + 네이티브 1.24.4 면 세션을 못 만든다), 모델도 같은 PP-OCR 계열이라 얻는 것이 없다.
/// </remarks>
public sealed record OcrEngineChoice(OcrEngineKind Kind, string Name)
{
    public static readonly IReadOnlyList<OcrEngineChoice> All =
    [
        // 이름은 도구 줄 콤보와 영역 그리드 「OCR」 열이 같이 쓴다 - 한 이름으로(사용자, 2026-09-26 "상단의 OCR 이름이랑 영역 그리드 OCR 컬럼의 이름이 틀리네"). 설명은 콤보 힌트에.
        new(OcrEngineKind.PaddleGpu, "PP-OCRv5 GPU"),
        new(OcrEngineKind.PaddleCpu, "PP-OCRv5 CPU"),
        new(OcrEngineKind.Windows, "Windows OCR"),
        new(OcrEngineKind.PaddleV6TinyGpu, "PP-OCRv6 tiny"),
        new(OcrEngineKind.PaddleV6SmallGpu, "PP-OCRv6 small")
    ];

    public static OcrEngineChoice Default => All[0];

    /// <summary>저장값(종류 이름)으로 찾는다. 모르면 기본.</summary>
    public static OcrEngineChoice Parse(string? saved)
    {
        foreach (var choice in All)
            if (string.Equals(choice.Kind.ToString(), saved, System.StringComparison.OrdinalIgnoreCase)) return choice;

        return Default;
    }

    public override string ToString() => Name;
}
