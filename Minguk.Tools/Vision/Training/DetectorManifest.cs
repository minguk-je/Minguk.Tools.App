using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 모델 옆에 두는 쪽지. <b>그 모델이 어떤 크기로 학습됐는지</b>를 든다.
/// </summary>
/// <remarks>
/// <b>왜 설정이 아니라 파일인가</b>
///
/// 모델이 보는 크기를 바꾸면 반드시 다시 학습해야 한다. 그런데 그 값을 설정에서 읽으면,
/// 320 으로 학습해 둔 모델을 640 설정으로 읽는 순간 <b>좌표가 조용히 어긋난다</b> -
/// 추론이 돌려주는 픽셀을 640 으로 나누게 되어 사각형이 절반 자리에 그려진다.
/// 화면에는 그럴싸한 사각형이 떠서 눈으로는 못 잡고, 사람은 모델이 못 배웠다고 여긴다.
///
/// 그래서 크기는 <b>모델과 함께</b> 남긴다. 읽을 때 그 값을 쓰면 설정을 아무리 바꿔도
/// 이미 만들어진 모델은 제 크기로 정확히 돈다.
///
/// 쪽지가 없으면 이 기능이 생기기 전에 학습한 것이다. 그때 쓰던 값으로 본다.
/// </remarks>
public sealed class DetectorManifest
{
    /// <summary>쪽지 이름. 모델과 같은 폴더에 둔다.</summary>
    public const string FileName = "detector.json";

    /// <summary>이 기능이 생기기 전에 학습한 모델이 쓰던 크기.</summary>
    public const int LegacyWidth = 320;

    public const int LegacyHeight = 180;

    [JsonPropertyName("inputWidth")]
    public int InputWidth { get; set; } = LegacyWidth;

    [JsonPropertyName("inputHeight")]
    public int InputHeight { get; set; } = LegacyHeight;

    /// <summary>언제 학습했는지. 데이터셋을 고친 뒤 다시 학습했는지 가늠하는 데 쓴다.</summary>
    [JsonPropertyName("trainedAt")]
    public DateTime TrainedAt { get; set; }

    [JsonPropertyName("images")]
    public int Images { get; set; }

    [JsonPropertyName("boxes")]
    public int Boxes { get; set; }

    [JsonPropertyName("epochs")]
    public int Epochs { get; set; }

    /// <summary>학습할 때의 몹 이름들. 그 뒤 목록이 바뀌었는지 볼 수 있다.</summary>
    [JsonPropertyName("classes")]
    public string[] Classes { get; set; } = [];

    public static string PathFor(string modelPath)
        => Path.Combine(Path.GetDirectoryName(modelPath) ?? string.Empty, FileName);

    /// <summary>
    /// 모델 옆의 쪽지를 읽는다. 없거나 망가졌으면 <b>옛 값</b>으로 본다.
    /// </summary>
    /// <remarks>
    /// 여기서 터지면 안 된다. 쪽지 하나 때문에 이미 학습해 둔 모델을 못 쓰게 되는 것이
    /// 더 나쁘다. 대신 무엇을 가정했는지는 로그에 남긴다.
    /// </remarks>
    public static DetectorManifest Load(string modelPath)
    {
        var path = PathFor(modelPath);

        try
        {
            if (!File.Exists(path)) return new DetectorManifest();

            return JsonSerializer.Deserialize<DetectorManifest>(File.ReadAllText(path))
                   ?? new DetectorManifest();
        }
        catch (Exception)
        {
            return new DetectorManifest();
        }
    }

    public void Save(string modelPath)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,

            // 한글 몹 이름을 \uXXXX 로 흘려 적지 않는다. 사람이 열어 볼 파일이다.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        File.WriteAllText(PathFor(modelPath), json, new UTF8Encoding(false));
    }

    public string Describe => $"{InputWidth}x{InputHeight}";
}
