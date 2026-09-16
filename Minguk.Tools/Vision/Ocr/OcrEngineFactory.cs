using System;
using System.IO;

using Minguk.Tools.Vision.Ocr.Paddle;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 쓸 OCR 엔진을 고른다. PP-OCRv5(<see cref="PaddleOcrEngine"/>) 하나다.
/// </summary>
/// <remarks>
/// <b>GPU 먼저, 안 되면 CPU</b> - DirectML 을 못 쓰는 PC 에서도 읽기는 돼야 한다. 내려앉으면 이유를 돌려준다(조용히 다른 길로 보내지 않는다).
///
/// <b>학습 중이면 처음부터 CPU</b> - 3GB 카드에서 CUDA 학습과 DirectML 추론이 겹쳐 GPU 가 리셋됐다(<see cref="TrainingActivity"/>).
/// 글자 조각은 작아 CPU 로도 읽힌다. 화면은 학습이 시작·끝날 때 엔진을 버려 다음 읽기에서 맞는 쪽으로 다시 만든다.
///
/// 다른 엔진을 넣을 때 여기만 고친다. 부르는 쪽은 <see cref="IOcrEngine"/> 만 안다.
/// </remarks>
public static class OcrEngineFactory
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>엔진을 만든다. 모델 파일이 없으면 사용자가 무엇을 해야 하는지 담은 예외.</summary>
    /// <param name="fallbackReason">GPU 를 원했는데 CPU 로 내려앉았으면 그 이유(한국어). 아니면 null.</param>
    public static IOcrEngine Create(out string? fallbackReason)
    {
        fallbackReason = null;

        if (TrainingActivity.IsBusy) return PaddleOcrEngine.Create(useGpu: false);

        try
        {
            return PaddleOcrEngine.Create(useGpu: true);
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "글자 읽기를 GPU(DirectML)로 못 열어 CPU 로 연다");
            fallbackReason = "글자 읽기를 GPU 로 열지 못해 CPU 로 읽습니다(조금 느립니다).";

            return PaddleOcrEngine.Create(useGpu: false);
        }
    }
}
