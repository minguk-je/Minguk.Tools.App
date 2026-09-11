using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>읽은 단어 하나. 자리는 넣어 준 그림 안의 0~1 비율이다.</summary>
public readonly record struct OcrWord(string Text, LabelBox Box);

/// <summary>읽은 한 줄. 단어들의 자리를 합친 것이 줄의 자리다.</summary>
public readonly record struct OcrLine(string Text, IReadOnlyList<OcrWord> Words);

/// <summary>한 번 읽은 결과. <see cref="Text"/> 는 줄을 줄바꿈으로 이은 것이다.</summary>
public sealed record OcrOutcome(string Text, IReadOnlyList<OcrLine> Lines, TimeSpan Elapsed)
{
    public static readonly OcrOutcome Empty = new(string.Empty, [], TimeSpan.Zero);
}

/// <summary>
/// 그림에서 글자를 읽는다.
///
/// 왜 인터페이스인가
///   지금은 Windows 내장 OCR 하나다. 그래도 인터페이스로 두는 것은, 인식률이 모자라
///   다른 엔진(Tesseract, 클라우드)으로 갈아끼울 때 부르는 쪽(캡처 모니터·스크립트)을
///   안 고치려는 것이다. 팩터리만 고친다.
///
/// 되지 않는 경우
///   Windows OCR 은 언어 팩이 있어야 한다. 없으면 <see cref="OcrEngineFactory.TryCreate"/> 가
///   null 을 주고, 부르는 쪽은 "설정 > 시간 및 언어에서 언어를 추가하라" 고 말해야 한다.
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>사람이 읽을 이름. 어느 엔진으로 도는지 화면에 보여 준다.</summary>
    string Name { get; }

    /// <summary>읽는 언어 태그. "ko", "en-US" 같은 것.</summary>
    string Language { get; }

    /// <summary>
    /// 그림을 읽는다. 어느 스레드에서 불러도 된다.
    /// </summary>
    /// <param name="image">읽을 그림. Bgra32 가 아니어도 안에서 맞춘다. Freeze 해서 넘기면 스레드를 안 탄다.</param>
    Task<OcrOutcome> RecognizeAsync(BitmapSource image, CancellationToken token = default);
}
