using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>
/// 글자 읽기 모델 파일 자리. 앱과 같이 배포한다(저장소 <c>Libs\PaddleOcr</c> → 출력 <c>Models\Ocr</c>).
/// </summary>
/// <remarks>
/// 모델은 <c>도구\ppocr-onnx-내보내기.py</c> 로 한 번 만들어 커밋한 것이다(PP-OCRv5 mobile det · korean rec, Apache-2.0).
/// 사전은 rec 모델의 <c>inference.yml</c> 에서 뽑은 11,945자 - 모델과 짝이라 따로 바꾸면 안 된다.
/// </remarks>
public static class PaddleOcrModels
{
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "Models", "Ocr");

    public static string Detector => Path.Combine(Folder, "det.onnx");

    public static string Recognizer => Path.Combine(Folder, "rec.onnx");

    public static string Dictionary => Path.Combine(Folder, "rec-dict.txt");

    /// <summary>PP-OCRv5 mobile(det) + korean rec - 기본.</summary>
    public static PaddleOcrModelSet V5 { get; } = new("PP-OCRv5", Detector, Recognizer, Dictionary, [0.485f, 0.456f, 0.406f], [0.229f, 0.224f, 0.225f]);

    /// <summary>
    /// PP-OCRv6 tiny(약 6MB) - 다국어 한 모델(라틴 + CJK, 사전 6,904자). RapidOCR 가 ONNX 로 배포한 것(<c>Libs\PaddleOcr\v6</c>, Apache-2.0, 2026-09-18 사용자 "v6도 해보자").
    /// </summary>
    /// <remarks>
    /// 입력 모양은 v5 와 같다(det 3×H×W 동적, rec 3×48×W, 출력 사전+2). 다른 것은 <b>det 의 정규화</b>뿐 - v5 는 ImageNet 평균·편차, v6 는 (x−127.5)/127.5
    /// (RapidOcrNet 의 <c>RapidOcrModelSet</c> 에서 확인). rec 은 둘 다 (x/255−0.5)/0.5. v5 의 방향 분류기(cls)는 우리 길에 없고 v6 는 제 것이 없다 - 안 쓴다.
    /// </remarks>
    public static PaddleOcrModelSet V6Tiny { get; } = V6("tiny", "ppocrv6_tiny_dict.txt");

    /// <summary>PP-OCRv6 small(약 31MB, 사전 18,708자) - 파이썬 rapidocr 의 v6 기본값.</summary>
    public static PaddleOcrModelSet V6Small { get; } = V6("small", "ppocrv6_dict.txt");

    private static PaddleOcrModelSet V6(string size, string dictionary)
    {
        var folder = Path.Combine(Folder, "v6");

        return new($"PP-OCRv6 {size}", Path.Combine(folder, $"PP-OCRv6_det_{size}.onnx"), Path.Combine(folder, $"PP-OCRv6_rec_{size}.onnx"), Path.Combine(folder, dictionary), [0.5f, 0.5f, 0.5f], [0.5f, 0.5f, 0.5f]);
    }

    /// <summary>없는 파일 이름들. 비어 있으면 다 있다.</summary>
    public static IReadOnlyList<string> Missing() => V5.Missing();
}

/// <summary>모델 한 벌 - 줄 찾기(det)·읽기(rec)·사전과 det 의 정규화(0~1 단위의 평균·편차, BGR 순).</summary>
public sealed record PaddleOcrModelSet(string Name, string Detector, string Recognizer, string Dictionary, float[] DetectorMean, float[] DetectorStd)
{
    /// <summary>없는 파일 이름들. 비어 있으면 다 있다.</summary>
    public IReadOnlyList<string> Missing()
        => [.. new[] { Detector, Recognizer, Dictionary }.Where(path => !File.Exists(path)).Select(path => Path.GetFileName(path))];
}
