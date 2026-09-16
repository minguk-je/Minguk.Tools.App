using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Minguk.Tools.Vision.Ocr.Paddle;

/// <summary>한 줄을 읽은 글과 믿음(고른 글자들의 평균 확률, 0~1).</summary>
public readonly record struct CtcResult(string Text, float Confidence);

/// <summary>
/// 인식 모델 출력(시간 칸 × 글자 수)을 글로 바꾼다. 칸마다 가장 높은 글자를 고르고, 이어진 같은 글자와 빈칸(0번)은 버린다.
/// </summary>
/// <remarks>
/// <b>글자 번호</b> - 0 은 빈칸, 1 부터 사전 순서, 맨 끝은 띄어쓰기. PaddleOCR <c>CTCLabelDecode(use_space_char=True)</c> 와 같다.
/// 그래서 모델의 글자 수는 사전 + 2 다(11,945 + 2 = 11,947, 실측 2026-09-16). 다르면 모델과 사전이 짝이 아니다 - 헛글을 내느니 멈춘다.
///
/// <b>조합용 자모</b> - 사전에 완성형 11,172자와 함께 조합용 자모 238자가 있다. 자모로 나오면 NFC 로 합쳐 음절로 만든다.
/// </remarks>
public sealed class CtcDecoder
{
    private readonly string[] _labels;

    public CtcDecoder(IReadOnlyList<string> dictionary)
    {
        _labels = new string[dictionary.Count + 2];
        _labels[0] = string.Empty;

        for (var i = 0; i < dictionary.Count; i++) _labels[i + 1] = dictionary[i];

        _labels[^1] = " ";
    }

    /// <summary>한 줄에 한 글자씩 적힌 사전(UTF-8)을 읽는다.</summary>
    public static CtcDecoder Load(string path) => new(File.ReadAllLines(path, Encoding.UTF8));

    /// <summary>모델 출력이 가져야 할 글자 수(사전 + 빈칸 + 띄어쓰기).</summary>
    public int ClassCount => _labels.Length;

    /// <param name="scores">[칸 × 글자] 를 한 줄로 편 것. 모델이 softmax 를 거쳐 내놓은 확률.</param>
    public CtcResult Decode(ReadOnlySpan<float> scores, int steps, int classes)
    {
        if (classes != _labels.Length)
            throw new InvalidOperationException(
                $"인식 모델의 글자 수({classes})와 사전({_labels.Length - 2}자 + 빈칸·띄어쓰기)이 맞지 않습니다 - 모델과 사전을 같이 내보내야 합니다.");

        if (scores.Length < steps * classes)
            throw new ArgumentException($"출력 길이({scores.Length})가 모양({steps}x{classes})보다 짧다", nameof(scores));

        var text = new StringBuilder();
        var previous = -1;
        double sum = 0;
        var count = 0;

        for (var t = 0; t < steps; t++)
        {
            var row = scores.Slice(t * classes, classes);
            var best = 0;
            var bestScore = row[0];

            for (var c = 1; c < classes; c++)
            {
                if (row[c] <= bestScore) continue;

                best = c;
                bestScore = row[c];
            }

            if (best != 0 && best != previous)
            {
                text.Append(_labels[best]);
                sum += bestScore;
                count++;
            }

            previous = best;
        }

        return new CtcResult(text.ToString().Normalize(NormalizationForm.FormC).Trim(), count == 0 ? 0f : (float)(sum / count));
    }
}
