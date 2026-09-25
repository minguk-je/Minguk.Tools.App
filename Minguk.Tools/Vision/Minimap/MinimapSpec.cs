using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Vision.Minimap;

/// <summary>
/// 게임마다 다른 미니맵 모양 - 프로젝트 폴더의 <c>minimap.json</c>. 읽는 방법(<see cref="MinimapReader"/>)은 모든 게임이 같다.
/// </summary>
/// <remarks>
/// <see cref="HealthBars.HealthBarSpec"/> 과 같은 꼴이다 - 색·크기는 이 파일에, 찾고 재는 규칙은 계산기에 둔다.
/// 값은 실제 게임 화면 다섯 장(필드 1·던전 4, 1920x1080)에서 재서 넣었다. 그 화면에서 화살표는 61~71px 로 뽑히고
/// 잡음은 1px 이하다 - 노란 원·흰 길 띠가 깔린 위에서도 깨끗하다.
///
/// 기본값이 안 맞는 게임이면 <c>[미니맵 익히기]</c> 가 남긴 <c>Resources/미니맵화살표.png</c> 를 열어 보고
/// 문턱을 고친다. 익히기는 조각 원본을 저장하므로 색 규칙을 바꿔 다시 뽑을 수 있다.
/// </remarks>
public sealed class MinimapSpec
{
    public const string FileName = "minimap.json";

    /// <summary>익히기가 만드는 본보기 그림 - 프로젝트 <c>Resources</c> 안. 조각 원본(색)이라 사람이 열어 볼 수 있다.</summary>
    public const string TemplateName = "미니맵화살표.png";

    /// <summary>화살표를 찾을 이름 붙인 자리. 영역 패널에서 미니맵을 둘러 이 이름으로 만든다.</summary>
    [JsonPropertyName("region")]
    public string Region { get; set; } = "미니맵";

    /// <summary>자리 한가운데에서 이만큼(px) 되는 네모 안에서만 화살표를 찾는다. 지도 그림·길 띠가 안 걸릴 만큼 좁게.</summary>
    [JsonPropertyName("box")]
    public int Box { get; set; } = 40;

    /// <summary>화살표로 볼 밝기(R·G·B 가운데 가장 큰 값). 이보다 어두우면 지도다.</summary>
    [JsonPropertyName("minBrightness")]
    public int MinBrightness { get; set; } = 175;

    /// <summary>
    /// 길찾기(<c>길찾아걷기</c>)가 바닥으로 볼 칸 밝기 - 이보다 밝으면 바닥, 어두우면 벽·바깥(<see cref="MinimapPathFinder"/>).
    /// 아이온2 던전 실측: 바닥 55~100 · 벽 11~17(캡처 29·30·52, 2026-09-26).
    /// </summary>
    [JsonPropertyName("floorMinBrightness")]
    public int FloorMinBrightness { get; set; } = 30;

    /// <summary>화살표는 무채색~푸른 회백색이라 R − B 가 작다. 미니맵 바탕의 노란 원·길 띠는 이 값이 크다.</summary>
    [JsonPropertyName("maxRedMinusBlue")]
    public int MaxRedMinusBlue { get; set; } = 12;

    /// <summary>본보기를 익힐 때 화살표가 향하던 방위(도, 북 0·시계 방향). 익히기가 모양에서 스스로 잰다.</summary>
    /// <remarks>
    /// 머리를 모양만으로 가리는 규칙은 <see cref="MinimapReader"/> 에 - 부챗살 안 픽셀의 거리 합이 가장 무거운 쪽이다.
    /// 머리와 꼬리의 무게가 비슷한 아이콘에서는 흔들리므로(익히기가 「여유」 배수로 알린다) 그럴 때는
    /// <c>출력(방위())</c> 을 찍어 보고 이 숫자를 손으로 고친다.
    /// </remarks>
    [JsonPropertyName("headingAtTemplate")]
    public double HeadingAtTemplate { get; set; }

    /// <summary>마우스 가로 1 카운트가 몸을 몇 도 돌리나. 0 이면 아직 안 배웠다(<c>회전배율</c> 이 그 자리에서 잰다).</summary>
    [JsonPropertyName("degreesPerCount")]
    public double DegreesPerCount { get; set; }

    /// <summary>
    /// 마커를 안 볼 구역 - 자리 기준 0~1 비율로 <c>[x, y, 너비, 높이]</c>. 미니맵에 겹쳐 있는 시계·나침반·확대 아이콘을 뺀다.
    /// </summary>
    /// <remarks>
    /// 게임 시계 옆 해 아이콘(☀)이 목표 마커와 <b>색이 거의 같다</b>(실측 2026-09-23: 평균 RGB 222,181,64 대 232,182,81) -
    /// 색으로는 못 가른다. 가장자리를 통째로 빼는 것도 안 된다: 범위 밖 목표는 미니맵 <b>테두리에 붙어</b> 방향을 알려 준다.
    /// 그래서 자리만 빼 둔다. 익히기가 노란 표시를 보면 그 비율을 그대로 알려 주니 붙여 넣으면 된다.
    /// </remarks>
    [JsonPropertyName("ignore")]
    public double[][] Ignore { get; set; } = [];

    /// <summary>그 자리가 안 보는 구역 안인가(자리 기준 0~1 비율).</summary>
    public bool IsIgnored(double ratioX, double ratioY)
    {
        foreach (var box in Ignore ?? [])
        {
            if (box is not { Length: 4 }) continue;
            if (ratioX >= box[0] && ratioX <= box[0] + box[2] && ratioY >= box[1] && ratioY <= box[1] + box[3]) return true;
        }

        return false;
    }

    /// <summary>목표 마커(노란 ▼) 색 - 진한 노랑. 게임마다 다르면 여기를 고친다.</summary>
    [JsonPropertyName("marker")]
    public MinimapMarkerSpec Marker { get; set; } = new();

    /// <summary>
    /// 이름 붙인 점 종류 - 이름 → 색·모양 규칙. 스크립트가 <c>마커방위("선공몹")</c>·<c>마커로걷기("선공몹,일반몹", 밀리초)</c> 처럼 이름으로 부른다(쉼표로 여럿).
    /// </summary>
    /// <remarks>
    /// 게임마다 점 색이 다르다 - 코드에 「몹」 을 박지 않고 여기에 둔다(사용자, 2026-09-25 "이런것도 공통으로 사용할 수 있게").
    /// 다른 종류(채집·동료)는 이 파일에 이름과 색만 더하면 된다. 「목표」 는 따로 적지 않아도 <see cref="Marker"/>(노란 ▼)다.
    ///
    /// 기본은 둘 - 아이온2 미니맵(사용자 2026-09-25 "빨강색은 선공몹, 흰색은 일반몹"):
    /// 「선공몹」 빨간 ●(<see cref="MinimapMarkerSpec.RedDot"/>) 1080p 실측 23~25px, 한가운데 RGB 239,34,33. 지도의 어두운 붉은 무늬(115,31,28)는 밝기에서 갈린다.
    /// 「일반몹」 흰 ●(<see cref="MinimapMarkerSpec.WhiteDot"/>) - 흰 깃털·화살표·빨간 점 테두리·시계 숫자도 흰색이라 <b>모양</b>으로 가른다(꽉 찬 동그라미만).
    /// </remarks>
    [JsonPropertyName("markers")]
    public Dictionary<string, MinimapMarkerSpec> Markers { get; set; } = DefaultMarkers();

    /// <summary>「목표」 는 늘 <see cref="Marker"/>.</summary>
    public const string TargetMarkerName = "목표";

    private static Dictionary<string, MinimapMarkerSpec> DefaultMarkers() => new()
    {
        ["선공몹"] = MinimapMarkerSpec.RedDot(),
        ["일반몹"] = MinimapMarkerSpec.WhiteDot()
    };

    /// <summary>그 이름의 색 규칙(이름 하나). 없으면 null. 여럿은 <see cref="MarkersNamed"/>.</summary>
    public MinimapMarkerSpec? MarkerNamed(string name)
        => name == TargetMarkerName ? Marker : Markers.GetValueOrDefault(name);

    /// <summary>
    /// 쉼표로 이은 이름들의 규칙 - <c>"선공몹,일반몹"</c>. 모르는 이름이 하나라도 있으면 그 이름들을 <paramref name="unknown"/> 에 담고 빈 목록.
    /// </summary>
    public IReadOnlyList<MinimapMarkerSpec> MarkersNamed(string names, out IReadOnlyList<string> unknown)
    {
        var parts = (names ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var missing = parts.Where(part => MarkerNamed(part) is null).ToList();

        unknown = parts.Length == 0 ? [names ?? string.Empty] : missing;

        return parts.Length == 0 || missing.Count > 0 ? [] : [.. parts.Select(part => MarkerNamed(part)!)];
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>파일을 읽는다. 없으면 null. 형식이 틀리면 <see cref="JsonException"/>.</summary>
    public static MinimapSpec? Load(string path)
    {
        if (!File.Exists(path)) return null;

        var spec = JsonSerializer.Deserialize<MinimapSpec>(File.ReadAllText(path), Options) ?? new MinimapSpec();

        if (spec.Box < 8) throw new JsonException("box 는 8 이상이어야 합니다 - 화살표가 들어갈 네모 크기(px)입니다.");
        if (string.IsNullOrWhiteSpace(spec.Region)) throw new JsonException("region 은 화살표를 찾을 자리 이름입니다.");

        spec.Marker ??= new MinimapMarkerSpec();
        // 옛 파일(markers 없음)은 기본 종류를 받는다. 사람이 적은 종류는 그대로 두고 기본 이름만 없으면 채운다.
        spec.Markers ??= DefaultMarkers();

        foreach (var (name, rule) in DefaultMarkers()) spec.Markers.TryAdd(name, rule);

        return spec;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options), new System.Text.UTF8Encoding(false));
}

/// <summary>
/// 미니맵 위 점 하나의 규칙 - 색, 그리고 필요하면 크기·모양. 기본값은 실제 게임의 노란 ▼(실측 24px·22px 뭉치).
/// </summary>
/// <remarks>모양 조건(최대 크기·채움·가로세로)은 기본이 「안 본다」 - 색만으로 갈리는 종류(노란 ▼·빨간 ●)는 그대로 둔다.</remarks>
public sealed class MinimapMarkerSpec
{
    /// <summary>몬스터 빨간 점 - 초록·파랑이 둘 다 빠진 밝은 빨강.</summary>
    public static MinimapMarkerSpec RedDot() => new()
    {
        MinRed = 150,
        MinGreen = 0,
        MaxBlue = 255,
        MinRedMinusBlue = 80,
        MinRedMinusGreen = 80,
        MinPixels = 8
    };

    /// <summary>
    /// 흰 점 - 셋 다 밝고 고른 색에, <b>꽉 찬 동그라미</b>만.
    /// </summary>
    /// <remarks>
    /// 아이온2 미니맵의 다른 흰 것은 모두 길쭉하거나 속이 비었다(실측 2026-09-25 사냥터 한 장): 깃털 68~94px·채움 0.27~0.32, 캐릭터 화살표 46px·0.29,
    /// 빨간 점 흰 테두리 22px·0.17, 시계 숫자 10~16px·0.38~0.56(12px 넘는 것은 0.44 이하). 빨간 점은 같은 크기에 채움이 0.7 을 넘는다.
    /// 흰 점을 찍은 화면은 아직 없다 - 크기·채움은 빨간 점을 본떠 너그럽게 잡았다. 안 잡히면 이 값들을 고친다.
    /// </remarks>
    public static MinimapMarkerSpec WhiteDot() => new()
    {
        MinRed = 190,
        MinGreen = 190,
        MinBlue = 190,
        MaxBlue = 255,
        MaxSpread = 45,
        MinRedMinusBlue = -255,
        MinRedMinusGreen = -255,
        MinPixels = 12,
        MaxPixels = 60,
        MinFill = 0.55,
        MaxAspect = 1.6
    };

    [JsonPropertyName("minRed")]
    public int MinRed { get; set; } = 160;

    [JsonPropertyName("minGreen")]
    public int MinGreen { get; set; } = 115;

    [JsonPropertyName("maxBlue")]
    public int MaxBlue { get; set; } = 100;

    /// <summary>파랑도 이만큼은 있어야 한다 - 흰색처럼 셋 다 밝은 색. 기본 0(안 본다).</summary>
    [JsonPropertyName("minBlue")]
    public int MinBlue { get; set; }

    /// <summary>R·G·B 가장 큰 것과 가장 작은 것의 차가 이 안 - 흰·회색처럼 고른 색. 기본 255(안 본다).</summary>
    [JsonPropertyName("maxSpread")]
    public int MaxSpread { get; set; } = 255;

    /// <summary>노랑은 파랑이 빠진 색이다 - 이만큼은 차이 나야 지도의 누런 벽선과 갈린다.</summary>
    [JsonPropertyName("minRedMinusBlue")]
    public int MinRedMinusBlue { get; set; } = 90;

    /// <summary>빨강은 초록도 빠진다 - 노랑(R≈G)과 가르는 값. 노란 마커는 안 쓴다(−255).</summary>
    [JsonPropertyName("minRedMinusGreen")]
    public int MinRedMinusGreen { get; set; } = -255;

    /// <summary>이 색이 들어 있는가.</summary>
    public bool Matches(int r, int g, int b)
        => r >= MinRed && g >= MinGreen && b >= MinBlue && b <= MaxBlue
           && r - b >= MinRedMinusBlue && r - g >= MinRedMinusGreen
           && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= MaxSpread;

    /// <summary>이보다 작은 뭉치는 안 본다 - 벽선 조각·글자를 거른다.</summary>
    [JsonPropertyName("minPixels")]
    public int MinPixels { get; set; } = 12;

    /// <summary>이보다 큰 뭉치는 안 본다(px). 0 이면 안 본다.</summary>
    [JsonPropertyName("maxPixels")]
    public int MaxPixels { get; set; }

    /// <summary>뭉치가 둘레 네모를 이만큼은 채워야 한다(0~1) - 동그라미 ≈ 0.7, 깃털·테두리 ≈ 0.3. 0 이면 안 본다.</summary>
    [JsonPropertyName("minFill")]
    public double MinFill { get; set; }

    /// <summary>둘레 네모의 긴 변 ÷ 짧은 변이 이 안 - 동그라미 ≈ 1. 0 이면 안 본다.</summary>
    [JsonPropertyName("maxAspect")]
    public double MaxAspect { get; set; }

    /// <summary>뭉치 크기·모양이 맞는가.</summary>
    public bool FitsShape(int pixels, int boxWidth, int boxHeight)
    {
        if (pixels < MinPixels) return false;
        if (MaxPixels > 0 && pixels > MaxPixels) return false;
        if (MinFill > 0 && pixels < MinFill * boxWidth * boxHeight) return false;

        var longSide = Math.Max(boxWidth, boxHeight);
        var shortSide = Math.Max(1, Math.Min(boxWidth, boxHeight));

        return MaxAspect <= 0 || longSide <= MaxAspect * shortSide;
    }
}
