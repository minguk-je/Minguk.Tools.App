using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace Minguk.Tools.Vision.Ocr;

/// <summary>
/// 읽기 전에 그림을 손보는 법. 자리마다 고른다.
/// </summary>
/// <remarks>
/// <b>왜 고르게 하나</b> - 게임마다 글자가 다르다. 오버워치 탄약은 밝은 배경에 얹힌 흰 이탤릭 숫자고, 어떤 게임은
/// 어두운 글자에 밝은 창이다. 한 가지 손질로 다 덮을 수 없어서(실측: 흰 글자만 남기기가 탄약에서는 0/12,
/// 그냥 키우기가 10/12) 자리마다 고르고 저장한다.
///
/// 어느 것을 골라도 <b>키우기는 공통</b>이다 - 작은 글자는 어느 엔진에서나 불리하다(같은 자리에서 3배 0/12 → 5배 10/12).
/// </remarks>
public interface IOcrPreprocessor
{
    /// <summary>저장에 쓰는 이름(regions.json). 바꾸면 옛 파일이 못 찾는다.</summary>
    string Id { get; }

    /// <summary>화면에 보이는 이름.</summary>
    string Name { get; }

    /// <summary>무엇에 쓰는지 한 줄. 화면 도움말에 그대로 뜬다.</summary>
    string Summary { get; }

    /// <summary>읽을 그림으로 만든다. 돌려주는 그림은 고정(Frozen)되어 있어야 한다.</summary>
    /// <param name="crop">자리에서 오려낸 원본 조각.</param>
    /// <param name="options">목표 높이·기울기 같은 공통 옵션.</param>
    BitmapSource Prepare(BitmapSource crop, OcrPreprocessOptions options);
}

/// <summary>전처리 공통 옵션. 자리마다 저장한다.</summary>
/// <param name="TargetHeight">이 높이가 되도록 키운다(px). 원본이 더 크면 그대로 둔다.</param>
/// <param name="ShearDegrees">글자가 오른쪽으로 기운 각도. 0 이면 손대지 않는다(이탤릭 HUD 는 10~12도).</param>
/// <param name="KeepFrom">자리 안에서 읽을 가로 범위의 시작(0~1). 밖은 배경색으로 지운다.</param>
/// <param name="KeepTo">읽을 가로 범위의 끝(0~1). 0.5 면 왼쪽 절반만 읽는다.</param>
public readonly record struct OcrPreprocessOptions(
    int TargetHeight = OcrPreprocessOptions.DefaultTargetHeight,
    double ShearDegrees = 0,
    double KeepFrom = 0,
    double KeepTo = 1)
{
    /// <summary>
    /// 키울 목표 높이(px).
    /// </summary>
    /// <remarks>
    /// 오버워치 탄약 자리(56px)에서 3배(168px)는 12장 중 0장, 5배(280px)는 10장을 읽었다(실측 2026-09-16).
    /// 그래서 300 을 기본으로 둔다 - 더 키워도 나아지지 않고 읽는 시간만 는다(70ms → 90ms).
    /// </remarks>
    public const int DefaultTargetHeight = 300;

    public static readonly OcrPreprocessOptions Default = new();

    /// <summary>읽을 범위를 좁혀 놓았는가(「17 | 24」 에서 앞 숫자만 읽을 때).</summary>
    public bool HasKeepRange => KeepFrom > 0.0001 || KeepTo < 0.9999;

    /// <summary>원본 높이를 목표 높이로 만드는 배율. 1 아래로는 안 줄인다.</summary>
    public double ScaleFor(int pixelHeight) => pixelHeight <= 0 ? 1 : Math.Max(1, (double)TargetHeight / pixelHeight);
}

/// <summary>전처리 목록. 이름으로 고르고, 모르는 이름이면 기본(그대로 키우기).</summary>
public static class OcrPreprocessors
{
    public static IReadOnlyList<IOcrPreprocessor> All { get; } =
    [
        new PlainOcrPreprocessor(),
        new AutoInkOcrPreprocessor(),
        new BrightInkOcrPreprocessor(),
        new DarkInkOcrPreprocessor()
    ];

    public static IOcrPreprocessor Default => All[0];

    /// <summary>이름으로 찾는다. 없으면 기본.</summary>
    public static IOcrPreprocessor Find(string? id)
        => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}

/// <summary>자리 그리드의 언어 콤보에 넣는 한 줄.</summary>
/// <param name="Tag">언어 태그. 빈 글이면 자동(숫자는 영문, 안 되면 화면 언어).</param>
/// <param name="Name">화면에 보이는 이름.</param>
public readonly record struct OcrLanguageChoice(string Tag, string Name);

/// <summary>자리마다 고를 수 있는 언어. 화면 콤보가 이것을 문다.</summary>
public static class OcrLanguages
{
    /// <summary>자동 · 한국어 · 영문. 자동이 기본이다 - 숫자는 영문이 낫고 글자는 한국어가 낫다.</summary>
    public static IReadOnlyList<OcrLanguageChoice> All { get; } =
    [
        new(string.Empty, "자동"),
        new("ko", "한국어"),
        new("en-US", "영문")
    ];
}
