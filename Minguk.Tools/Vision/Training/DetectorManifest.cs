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

    /// <summary>
    /// 이 모델을 무엇으로 돌리는지. 없으면 <see cref="DetectorEngine.Torch"/> - 여기 생기기 전에 학습한 것들이다.
    /// </summary>
    [JsonPropertyName("engine")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DetectorEngine Engine { get; set; } = DetectorEngine.Torch;

    /// <summary>
    /// 사람이 읽을 모델 이름("YOLO11n", "D-FINE-N"). 들일 때 적는다. 없으면 엔진 이름으로 부른다.
    /// </summary>
    /// <remarks>
    /// 2026-09-14 에 생겼다. 시험은 YOLO11n, 배포는 D-FINE-N 으로 같은 자리(detector.onnx)를 번갈아 쓰게 되어,
    /// 화면의 "쓰는 모델" 줄이 둘을 가르지 못하면 배포 전에 되돌렸는지 눈으로 확인할 길이 없다.
    /// </remarks>
    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }

    [JsonPropertyName("inputWidth")]
    public int InputWidth { get; set; } = LegacyWidth;

    [JsonPropertyName("inputHeight")]
    public int InputHeight { get; set; } = LegacyHeight;

    /// <summary>
    /// 그림을 입력 칸에 <b>어떻게 넣어</b> 학습했는지. 참이면 비율을 지키고 남는 자리를 회색으로 채웠다(레터박스),
    /// 거짓이면 그냥 늘려 맞췄다.
    /// </summary>
    /// <remarks>
    /// <b>크기만큼 중요하다.</b> 넣는 방식이 학습과 다르면 모델은 한 마리도 못 찾는다 - 사각형이 어긋나는 것이
    /// 아니라 아예 못 본다(실측: 늘려 배운 D-FINE 에 레터박스로 넣었더니 192개 중 0개).
    /// 게다가 헛것도 0개라 "모델이 덜 배웠나" 로 보여 엉뚱한 데를 파게 된다.
    ///
    /// D-FINE · RT-DETR 의 공식 설정은 <c>Resize [640,640]</c> 하나뿐이라 <b>늘리기</b>다.
    /// YOLO 계열은 레터박스가 관습이다.
    /// </remarks>
    [JsonPropertyName("letterbox")]
    public bool Letterbox { get; set; } = true;

    /// <summary>언제 학습했는지. 데이터셋을 고친 뒤 다시 학습했는지 가늠하는 데 쓴다.</summary>
    [JsonPropertyName("trainedAt")]
    public DateTime TrainedAt { get; set; }

    [JsonPropertyName("images")]
    public int Images { get; set; }

    [JsonPropertyName("boxes")]
    public int Boxes { get; set; }

    [JsonPropertyName("epochs")]
    public int Epochs { get; set; }

    /// <summary>학습할 때의 검출 이름들. 그 뒤 목록이 바뀌었는지 볼 수 있다.</summary>
    [JsonPropertyName("classes")]
    public string[] Classes { get; set; } = [];

    /// <summary>이 데이터셋에서 몇 번째 학습인지. 지난 쪽지의 값에 1을 더해 이어 간다.</summary>
    [JsonPropertyName("trainCount")]
    public int TrainCount { get; set; }

    [JsonPropertyName("elapsedSeconds")]
    public double ElapsedSeconds { get; set; }

    [JsonPropertyName("learningRate")]
    public double LearningRate { get; set; }

    /// <summary>마지막 바퀴의 평균 loss. 스텝 하나는 그림 하나의 값이라 튄다. 다음 학습과 견주는 눈금이다.</summary>
    [JsonPropertyName("finalLoss")]
    public double? FinalLoss { get; set; }

    [JsonPropertyName("usedGpu")]
    public bool UsedGpu { get; set; }

    /// <summary>학습 뒤 학습 그림을 다시 찾은 결과. 재현율을 안 돌렸으면 비어 있다.</summary>
    [JsonPropertyName("recallFound")]
    public int? RecallFound { get; set; }

    [JsonPropertyName("recallLabels")]
    public int? RecallLabels { get; set; }

    [JsonPropertyName("recallExtra")]
    public int? RecallExtra { get; set; }

    [JsonPropertyName("recallThreshold")]
    public double? RecallThreshold { get; set; }

    /// <summary>
    /// 그 모델의 쪽지 자리. <b>모델마다 따로</b> 둔다.
    /// </summary>
    /// <remarks>
    /// 한 폴더에 우리 학습(detector.zip)과 밖에서 가져온 detector.onnx 가 같이 있을 수 있다. 쪽지를 하나만 두면
    /// 가져오기가 입력 크기를 640x640 으로 덮어써, 640x360 으로 학습한 옛 모델이 재현율 70%→0% 가 됐다(실측).
    /// 옛 이름(detector.json)은 zip 쪽이 그대로 쓴다 - 이미 있는 데이터셋을 건드리지 않으려고.
    /// </remarks>
    public static string PathFor(string modelPath)
    {
        var folder = Path.GetDirectoryName(modelPath) ?? string.Empty;

        return string.Equals(Path.GetExtension(modelPath), ".zip", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(folder, FileName)
            : modelPath + ".json";
    }

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

            // 한글 검출 이름을 \uXXXX 로 흘려 적지 않는다. 사람이 열어 볼 파일이다.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        File.WriteAllText(PathFor(modelPath), json, new UTF8Encoding(false));
    }

    /// <summary>사람에게 보여 줄 한 마디. 파일에는 안 적는다 - 계산해서 나오는 값이다.</summary>
    [JsonIgnore]
    public string Describe => $"{InputWidth}x{InputHeight}";

    /// <summary>
    /// 화면 한 줄에 다 적는다. 사람이 "지금 모델이 어떤 것인지" 물을 때 답이 되는 것들.
    /// </summary>
    /// <remarks>
    /// 옛 쪽지(학습 횟수·loss 가 없던 것)도 깨지지 않고 있는 만큼만 적는다.
    /// </remarks>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();

            // 무엇인지가 맨 앞이다 - YOLO11n(시험)과 D-FINE-N(배포)이 같은 자리를 번갈아 쓴다.
            parts.Add(Engine == DetectorEngine.Onnx
                ? $"{ModelName ?? "ONNX 모델"} (ONNX · {(Letterbox ? "비율" : "늘리기")})"
                : $"{ModelName ?? "AutoFormerV2"} (TorchSharp)");

            if (TrainCount > 0) parts.Add($"{TrainCount}번째 학습");
            if (TrainedAt != default) parts.Add(TrainedAt.ToString("MM-dd HH:mm"));
            parts.Add($"{InputWidth}x{InputHeight}");

            // 밖에서 들인 ONNX 는 우리 학습 기록이 없다(0 으로 둔다) - "그림 0장 · 0바퀴" 를 적으면 안 배운 모델처럼 보인다.
            if (Images > 0) parts.Add($"그림 {Images}장 · 사각형 {Boxes}개 · 검출 {Classes.Length}종");
            if (Epochs > 0) parts.Add($"{Epochs}바퀴");
            if (ElapsedSeconds > 0) parts.Add($"{ElapsedSeconds / 60:0.0}분 ({(UsedGpu ? "GPU" : "CPU")})");
            if (LearningRate > 0) parts.Add($"학습률 {LearningRate:0.###}");
            if (FinalLoss is { } loss) parts.Add($"마지막 바퀴 loss {loss:0.00}");

            if (RecallFound is { } found && RecallLabels is { } labels && labels > 0)
            {
                var recall = $"재현율 {found}/{labels} ({100.0 * found / labels:0}%)";
                if (RecallExtra is > 0) recall += $" · 헛것 {RecallExtra}";
                if (RecallThreshold is { } threshold) recall += $" @{threshold:P0}";
                parts.Add(recall);
            }

            return string.Join("  ·  ", parts);
        }
    }
}
