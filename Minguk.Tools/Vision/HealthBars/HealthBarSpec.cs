using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Vision.HealthBars;

/// <summary>
/// 게임마다 다른 체력바 모양 - 프로젝트 폴더의 <c>healthbar.json</c>. 읽는 방법(<see cref="HealthBarReader"/>)은 모든 게임이 같다.
/// </summary>
/// <remarks>
/// 사용자(2026-09-19) "이 게임에서만 사용할게 아니야. 프로젝트 폴더내에 두고 참조하는 식으로, 공통 로직" - 처음에는 오버워치 색을 엔진 코드에 박았다.
/// 색·범위는 이 파일에, 찾고 재는 규칙은 엔진에 둔다. 파일은 도는 중에 고쳐도 다음 호출부터 먹는다(<see cref="Load"/> 가 시각을 본다).
///
/// 자리는 검출 사각형을 기준으로 한 배수다 - 봇이 멀고 가까워 사각형이 커지고 작아져도 같은 값으로 맞는다.
/// </remarks>
public sealed class HealthBarSpec
{
    public const string FileName = "healthbar.json";

    /// <summary>찬 칸 색(R, G, B).</summary>
    [JsonPropertyName("filled")]
    public int[] Filled { get; set; } = [255, 66, 107];

    /// <summary>빈 칸 색(R, G, B). 없으면(빈 배열) 빈 칸을 안 본다 - 그때는 찬 칸 길이만 견준다.</summary>
    [JsonPropertyName("empty")]
    public int[] Empty { get; set; } = [110, 75, 112];

    /// <summary>기준색에서 이 거리(RGB 공간) 안이면 그 색으로 본다. 너무 크면 배경이 걸리고, 너무 작으면 가장자리 번짐을 놓친다.</summary>
    [JsonPropertyName("tolerance")]
    public double Tolerance { get; set; } = 45;

    /// <summary>찾을 띠 - 검출 사각형 위 모서리에서 위로 몸 높이의 이만큼까지.</summary>
    [JsonPropertyName("above")]
    public double Above { get; set; } = 1.0;

    /// <summary>찾을 띠 - 위 모서리에서 아래로 몸 높이의 이만큼까지(바가 머리에 겹치는 게임).</summary>
    [JsonPropertyName("below")]
    public double Below { get; set; } = 0.1;

    /// <summary>찾을 띠 - 가운데에서 양옆으로 몸 너비의 이만큼씩(바가 몸보다 넓다).</summary>
    [JsonPropertyName("side")]
    public double Side { get; set; } = 1.3;

    /// <summary>바로 볼 가장 짧은 길이 - 몸 너비의 이만큼. 테두리·글자의 짧은 조각을 거른다.</summary>
    [JsonPropertyName("minWidth")]
    public double MinWidth { get; set; } = 0.45;

    /// <summary>칸 사이 틈으로 봐 줄 폭(px). 이보다 벌어지면 다른 줄이다.</summary>
    [JsonPropertyName("gap")]
    public int Gap { get; set; } = 4;

    /// <summary>쏜 뒤 이만큼(0~1) 넘게 줄면 맞은 것. 칸 하나보다 작게, 읽기 흔들림보다 크게.</summary>
    [JsonPropertyName("drop")]
    public double Drop { get; set; } = 0.03;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>파일을 읽는다. 없으면 null. 형식이 틀리면 <see cref="JsonException"/>.</summary>
    public static HealthBarSpec? Load(string path)
    {
        if (!File.Exists(path)) return null;

        var spec = JsonSerializer.Deserialize<HealthBarSpec>(File.ReadAllText(path), Options) ?? new HealthBarSpec();

        if (spec.Filled is not { Length: 3 }) throw new JsonException("filled 는 [R, G, B] 세 숫자여야 합니다.");
        if (spec.Empty is not ({ Length: 0 } or { Length: 3 })) throw new JsonException("empty 는 [R, G, B] 세 숫자이거나 빈 배열이어야 합니다.");

        return spec;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options), new System.Text.UTF8Encoding(false));
}
