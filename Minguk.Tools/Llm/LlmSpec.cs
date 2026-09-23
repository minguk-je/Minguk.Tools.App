using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Llm;

/// <summary>
/// 프로젝트마다 쓸 모델 - 프로젝트 폴더의 <c>llm.json</c>. 없으면 기본값으로 처음 부를 때 만든다.
/// </summary>
/// <remarks>
/// <b>기본값은 이 PC 실측으로 골랐다</b>(2026-09-24): 글 판단 <c>qwen3:8b</c> - 아이온2 사냥 장면 8개 중 7개를 맞혔다(qwen3:4b 는 3~4개).
/// 그림 <c>qwen2.5vl:3b</c> - 3GB 카드에 올라가는 것 중 하나일 뿐, 판단은 믿기 어렵다(같은 그림에 묻는 방식만 바꿔도 적 있음·없음이 갈렸다).
/// 12GB 이상 카드를 달면 <c>visionModel</c> 을 <c>qwen2.5vl:7b</c> 로 바꾼다 - 코드는 그대로다.
///
/// <see cref="Vision.Minimap.MinimapSpec"/> 과 같은 꼴 - 값은 이 파일에, 부르는 규칙은 코드에.
/// </remarks>
public sealed class LlmSpec
{
    public const string FileName = "llm.json";

    /// <summary>게임 규칙서 - 프로젝트 폴더에 있으면 판단할 때마다 넣는다. 사람이 적는 글(마크다운).</summary>
    public const string RulesFileName = "llm-rules.md";

    /// <summary>고쳐 준 판단 - <c>판단고치기</c> 가 한 줄씩 붙인다(JSON Lines). 최근 것 몇 개를 예시로 넣는다.</summary>
    public const string ExamplesFileName = "llm-examples.jsonl";

    /// <summary>어느 길인가. 지금은 <c>ollama</c> 하나.</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "ollama";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "http://localhost:11434";

    /// <summary>글로 판단할 모델(<c>판단</c>·<c>물어보기</c>).</summary>
    [JsonPropertyName("textModel")]
    public string TextModel { get; set; } = "qwen3:8b";

    /// <summary>화면을 보고 판단할 모델(<c>화면판단</c>·<c>화면물어보기</c>).</summary>
    [JsonPropertyName("visionModel")]
    public string VisionModel { get; set; } = "qwen2.5vl:3b";

    /// <summary>다 쓴 뒤 모델을 올려 둘 시간. 내렸다 다시 올리면 1~2분이다.</summary>
    [JsonPropertyName("keepAlive")]
    public string KeepAlive { get; set; } = "30m";

    /// <summary>한 번 부르기 상한(초). 모델 올리기까지 들어가 넉넉히.</summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// 그림을 보낼 때 긴 변을 이만큼으로 줄인다(px). 그림 토큰은 크기에 비례해 늘고 그만큼 느리다 - 720p 한 장이 약 1,300토큰, 18초였다(3B·CPU 대부분).
    /// </summary>
    [JsonPropertyName("maxImageSide")]
    public int MaxImageSide { get; set; } = 1024;

    /// <summary>
    /// 그림 모델이 내는 상자 좌표의 단위 - <c>pixel</c>(보낸 그림의 픽셀, Qwen2.5-VL) 또는 <c>1000</c>(0~1000 비율, Qwen3-VL 등). 스크립트 도우미가 본다.
    /// </summary>
    [JsonPropertyName("boxUnits")]
    public string BoxUnits { get; set; } = "pixel";

    /// <summary>판단에 넣을 고쳐 준 예시 수(최근 것부터).</summary>
    [JsonPropertyName("examples")]
    public int Examples { get; set; } = 8;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>파일을 읽는다. 없으면 null. 형식이 틀리면 <see cref="JsonException"/>.</summary>
    public static LlmSpec? Load(string path)
    {
        if (!File.Exists(path)) return null;

        var spec = JsonSerializer.Deserialize<LlmSpec>(File.ReadAllText(path), Options) ?? new LlmSpec();

        if (!Uri.TryCreate(spec.Url, UriKind.Absolute, out _)) throw new JsonException($"url 이 주소가 아닙니다: {spec.Url}");
        if (string.IsNullOrWhiteSpace(spec.TextModel)) throw new JsonException("textModel 이 비었습니다 - 판단할 모델 이름(qwen3:8b)을 적습니다.");

        spec.TimeoutSeconds = Math.Max(10, spec.TimeoutSeconds);
        spec.MaxImageSide = Math.Clamp(spec.MaxImageSide, 256, 4096);
        spec.Examples = Math.Clamp(spec.Examples, 0, 50);

        return spec;
    }

    /// <summary>프로젝트 폴더의 llm.json - 없으면 기본값으로 만든다(<paramref name="created"/>). 형식이 틀리면 <see cref="JsonException"/>.</summary>
    public static LlmSpec LoadOrCreate(string root, out bool created)
    {
        var path = Path.Combine(root, FileName);

        created = false;

        if (Load(path) is { } spec) return spec;

        spec = new LlmSpec();

        try
        {
            spec.Save(path);
            created = true;
        }
        catch (IOException)
        {
            // 못 적어도 기본값으로 돈다.
        }

        return spec;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options), new System.Text.UTF8Encoding(false));

    /// <summary>주소 끝에 / 를 붙인 것 - 상대 경로(<c>api/chat</c>)가 이어 붙게.</summary>
    [JsonIgnore]
    public Uri BaseUri => new(Url.EndsWith('/') ? Url : Url + "/");
}
