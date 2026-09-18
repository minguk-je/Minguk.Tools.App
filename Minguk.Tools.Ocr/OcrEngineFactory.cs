using System;
using System.IO;

using Minguk.Tools.Vision.Ocr.Paddle;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 쓸 OCR 엔진을 만든다 - 기본은 PP-OCRv5(<see cref="PaddleOcrEngine"/>) GPU, 화면의 콤보로 CPU·Windows 내장 OCR 을 고를 수 있다(<see cref="OcrEngineKind"/>).
/// </summary>
/// <remarks>
/// <b>GPU 먼저, 안 되면 CPU</b> - DirectML 을 못 쓰는 PC 에서도 읽기는 돼야 한다. 내려앉으면 이유를 돌려준다(조용히 다른 길로 보내지 않는다).
///
/// <b>학습 중이면 CPU</b> - 3GB 카드에서 CUDA 학습과 DirectML 추론이 겹쳐 GPU 가 리셋됐다. 그 판단은 부르는 쪽(화면)이 한다 - 이 프로젝트는 학습을 모른다.
/// 화면은 학습이 시작·끝날 때 엔진을 버려 다음 읽기에서 맞는 쪽(<see cref="OcrEngineKind.PaddleCpu"/>)으로 다시 만든다.
///
/// 다른 엔진을 넣을 때 여기만 고친다. 부르는 쪽은 <see cref="IOcrEngine"/> 만 안다.
/// </remarks>
public static class OcrEngineFactory
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>엔진을 만든다. 모델 파일이 없으면 사용자가 무엇을 해야 하는지 담은 예외.</summary>
    /// <param name="fallbackReason">GPU 를 원했는데 CPU 로 내려앉았으면 그 이유(한국어). 아니면 null.</param>
    public static IOcrEngine Create(out string? fallbackReason) => Create(OcrEngineKind.PaddleGpu, out fallbackReason);

    /// <summary>고른 종류로 만든다. GPU 를 못 열면 CPU 로 내려앉고, Windows OCR 에 언어 팩이 없으면 무엇을 해야 하는지 담은 예외.</summary>
    public static IOcrEngine Create(OcrEngineKind kind, out string? fallbackReason)
    {
        fallbackReason = null;

        if (kind == OcrEngineKind.Windows)
        {
            return WindowsOcrEngine.TryCreate("ko")
                   ?? throw new InvalidOperationException(
                       "Windows 내장 OCR 을 열지 못했습니다 - Windows 설정 > 시간 및 언어 > 언어에서 한국어(또는 영어)를 추가하고 「광학 문자 인식」 기능을 설치하세요. " +
                       $"지금 깔린 것: {(WindowsOcrEngine.AvailableLanguages.Count == 0 ? "없음" : string.Join(", ", WindowsOcrEngine.AvailableLanguages))}");
        }

        if (kind == OcrEngineKind.PaddleCpu) return PaddleOcrEngine.Create(useGpu: false);

        var models = kind switch
        {
            OcrEngineKind.PaddleV6TinyGpu => PaddleOcrModels.V6Tiny,
            OcrEngineKind.PaddleV6SmallGpu => PaddleOcrModels.V6Small,
            _ => PaddleOcrModels.V5
        };

        try
        {
            return PaddleOcrEngine.Create(models, useGpu: true);
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "글자 읽기를 GPU(DirectML)로 못 열어 CPU 로 연다");
            fallbackReason = "글자 읽기를 GPU 로 열지 못해 CPU 로 읽습니다(조금 느립니다).";

            return PaddleOcrEngine.Create(models, useGpu: false);
        }
    }
}
