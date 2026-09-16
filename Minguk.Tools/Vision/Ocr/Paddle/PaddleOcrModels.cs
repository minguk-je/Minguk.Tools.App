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

    /// <summary>없는 파일 이름들. 비어 있으면 다 있다.</summary>
    public static IReadOnlyList<string> Missing()
        => [.. new[] { Detector, Recognizer, Dictionary }.Where(path => !File.Exists(path)).Select(path => Path.GetFileName(path))];
}
