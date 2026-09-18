using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>
/// PP-OCRv5 로 읽는다 - 글자 줄을 찾고(det) 줄마다 읽는다(rec). 한글·영문·숫자를 한 모델이 읽어 언어도 손질도 고르지 않는다.
/// </summary>
/// <remarks>
/// <b>못 찾으면 통째로</b> - 찾은 줄이 없으면 조각 전체를 한 줄로 보고 한 번 읽는다. 글자가 조각을 꽉 채우면 검출이 놓친다.
///
/// <b>둘레에 바탕색 여백</b>(<see cref="EdgePadding"/>) - 글자가 조각 가장자리에 붙으면 줄 찾기가 조각 전체를 한 줄로 잡고, 읽기는 글자를 맞게 읽어도
/// 믿음이 0.42 로 떨어져 아래 규칙에 버려졌다(14px 「LV 57」 90x24, 실측 2026-09-17). 가장자리 색으로 12px 덧대면 줄을 글자에 딱 맞게 찾고 0.92~0.99 로 읽는다.
/// 이름표·HUD 자리는 사람이 글자에 바짝 맞춰 잡아 늘 이 경우다.
///
/// <b>믿음이 낮은 줄은 버린다</b>(<see cref="MinimumConfidence"/>, PaddleOCR drop_score 와 같은 0.5) - 빈 배경을 통째로 읽으면 헛글이 나온다.
///
/// <b>한 번에 하나</b> - 계속 읽기·이름표·스크립트가 같은 엔진을 부른다. 세션을 번갈아 쓰지 않게 줄 세운다.
///
/// <b>놓기</b> - 읽는 중에 놓으면(학습이 시작돼 화면이 엔진을 CPU 로 바꿀 때) 그 읽기가 끝나기를 기다렸다 놓는다.
/// 자물쇠는 놓지 않는다 - 늦게 온 읽기가 기다리다 "놓였다" 를 받아야지 자물쇠에서 터지면 안 된다.
/// </remarks>
public sealed class PaddleOcrEngine : IOcrEngine
{
    public const float MinimumConfidence = 0.5f;

    /// <summary>줄 찾기 전에 조각 둘레에 덧대는 여백(px). 8 부터 효과가 났다(12·14·18px 글자).</summary>
    public const int EdgePadding = 12;

    private readonly PaddleDetector _detector;
    private readonly PaddleRecognizer _recognizer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    private PaddleOcrEngine(PaddleOcrModelSet models, bool useGpu)
    {
        _detector = new PaddleDetector(models.Detector, useGpu, models.DetectorMean, models.DetectorStd);

        try
        {
            _recognizer = new PaddleRecognizer(models.Recognizer, models.Dictionary, useGpu);
        }
        catch
        {
            _detector.Dispose();
            throw;
        }

        UseGpu = useGpu;
        Models = models;
    }

    /// <summary>만든다(PP-OCRv5). 모델 파일이 없거나 세션을 못 만들면 예외.</summary>
    public static PaddleOcrEngine Create(bool useGpu) => Create(PaddleOcrModels.V5, useGpu);

    /// <summary>고른 모델 벌로 만든다. 모델 파일이 없거나 세션을 못 만들면 예외.</summary>
    public static PaddleOcrEngine Create(PaddleOcrModelSet models, bool useGpu)
    {
        var missing = models.Missing();

        if (missing.Count > 0)
            throw new FileNotFoundException(
                $"글자 읽기 모델이 없습니다({string.Join(", ", missing)}) - 설치 폴더의 Models\\Ocr 를 확인하세요. 앱을 다시 설치하면 들어옵니다.");

        return new PaddleOcrEngine(models, useGpu);
    }

    /// <summary>쓰는 모델 벌.</summary>
    public PaddleOcrModelSet Models { get; }

    public string Name => $"{Models.Name} ({(UseGpu ? "GPU" : "CPU")})";

    /// <summary>한국어 사전이지만 영문·숫자·기호도 들어 있다.</summary>
    public string Language => "ko";

    public bool UseGpu { get; }

    public Task<OcrOutcome> RecognizeAsync(BitmapSource image, CancellationToken token = default)
    {
        // 픽셀은 부른 스레드에서 옮긴다 - 그림은 이 스레드 것이거나 Freeze 된 것이다.
        var pixels = BgraImage.From(image);

        return Task.Run(async () =>
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);

            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

                return Recognize(pixels);
            }
            finally
            {
                _gate.Release();
            }
        }, token);
    }

    private OcrOutcome Recognize(BgraImage image)
    {
        var watch = Stopwatch.StartNew();
        var words = new List<(DbBox Box, string Text)>();
        var padded = image.PadWithEdgeColor(EdgePadding);
        var boxes = _detector.Detect(padded);

        foreach (var box in boxes)
        {
            var left = (int)Math.Floor(box.Left);
            var top = (int)Math.Floor(box.Top);
            var line = padded.Crop(left, top, (int)Math.Ceiling(box.Right) - left, (int)Math.Ceiling(box.Bottom) - top);

            // 낱말 자리는 덧대기 전 조각 좌표로 되돌린다.
            if (line is not null) ReadLine(line, left - EdgePadding, top - EdgePadding, box.Score, words);
        }

        if (boxes.Count == 0) ReadLine(image, 0, 0, 1f, words);

        var lines = OcrLineGrouping.Group(words, image.Width, image.Height);

        watch.Stop();

        return new OcrOutcome(string.Join(Environment.NewLine, lines.Select(l => l.Text)), lines, watch.Elapsed);
    }

    /// <summary>
    /// 한 줄을 읽어 낱말로 보탠다. 구분선(<see cref="SeparatorSplit"/>)이 있으면 양옆을 따로 읽는다 - 「17 | 24」 가 「1724」 로 붙지 않게.
    /// </summary>
    /// <param name="left">줄이 조각 안에서 놓인 왼쪽(px). 낱말 자리를 조각 좌표로 되돌리는 데 쓴다.</param>
    private void ReadLine(BgraImage line, int left, int top, float score, List<(DbBox Box, string Text)> words)
    {
        var parts = SeparatorSplit.Split(line);

        foreach (var (start, end) in parts)
        {
            var piece = parts.Count == 1 ? line : line.Crop(start, 0, end - start, line.Height);

            if (piece is null) continue;

            var read = _recognizer.Read(piece);

            if (read.Text.Length > 0 && read.Confidence >= MinimumConfidence)
                words.Add((new DbBox(left + start, top, left + end, top + line.Height, score), read.Text));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // 읽는 중이면 끝나기를 기다린다(3초까지) - 세션을 쓰는 중에 놓으면 네이티브에서 터진다.
        var entered = _gate.Wait(3000);

        try
        {
            _recognizer.Dispose();
            _detector.Dispose();
        }
        finally
        {
            if (entered) _gate.Release();
        }
    }
}
