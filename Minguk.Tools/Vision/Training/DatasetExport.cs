using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Vision.Training;

/// <summary>
/// 찍어 둔 데이터셋을 밖에서 학습할 수 있는 모양으로 내보낸다.
/// </summary>
/// <remarks>
/// <b>왜 두 가지인가</b> - 학습기마다 먹는 모양이 다르다. YOLO 계열(Ultralytics·YOLOX)은 우리가 이미 쓰는
/// 한 줄 형식(<c>분류 중심x 중심y 너비 높이</c>)에 <c>data.yaml</c> 한 장만 더 있으면 되고,
/// DETR 계열(D-FINE·RT-DETR)은 COCO json 을 받는다. 어느 쪽으로 학습할지는 사람이 정하므로 둘 다 내보낸다.
///
/// <b>사진은 복사하지 않는다</b> - 97장이 237MB 다. yaml·json 이 원래 폴더를 가리키게 적는다.
/// </remarks>
public static class DatasetExport
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // BOM 없는 UTF-8. 파이썬의 json 모듈은 BOM 이 있으면 "Unexpected UTF-8 BOM" 으로 거절한다(실측) -
    // 내보낸 파일은 남의 학습기가 읽는 것이라 여기서 맞춘다.
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// YOLO 학습기용 <c>data.yaml</c>. 우리 라벨 파일이 이미 YOLO 형식이라 이 한 장이면 된다.
    /// </summary>
    /// <remarks>
    /// 학습·검증을 나누지 않고 같은 폴더를 준다. 97장에서 10장을 떼면 검증 숫자가 너무 흔들려 쓸모가 없다 -
    /// 대신 학습 뒤 <c>--detect-check</c> 로 <b>학습에 쓴 그림을 되찾는지</b> 본다(옛 모델과 같은 잣대).
    /// </remarks>
    public static string WriteYolo(LabelDataset dataset, string? path = null)
    {
        var classes = dataset.LoadClasses();
        var target = path ?? Path.Combine(dataset.Root, "data.yaml");
        var text = new StringBuilder();

        text.AppendLine("# Minguk.Tools 가 내보낸 것. 사진과 라벨은 원래 자리를 그대로 가리킨다.");
        text.AppendLine($"path: {dataset.Root.Replace('\\', '/')}");
        text.AppendLine("train: images");
        text.AppendLine("val: images");
        text.AppendLine();
        text.AppendLine("names:");

        for (var i = 0; i < classes.Count; i++)
            text.AppendLine($"  {i}: {classes.Names[i]}");

        File.WriteAllText(target, text.ToString(), Utf8);
        Logger.Info($"YOLO 용 data.yaml 을 썼다: {target} (몹 {classes.Count}종)");

        return target;
    }

    /// <summary>
    /// DETR 계열 학습기용 COCO json. 라벨의 0~1 값을 그림 픽셀 좌표로 되돌려 적는다.
    /// </summary>
    public static string WriteCoco(LabelDataset dataset, string? path = null)
    {
        var classes = dataset.LoadClasses();
        var target = path ?? Path.Combine(dataset.Root, "coco.json");

        var images = new List<object>();
        var annotations = new List<object>();
        var imageId = 1;
        var annotationId = 1;

        foreach (var item in dataset.EnumerateItems())
        {
            if (!File.Exists(item.LabelPath)) continue;

            var (width, height) = ImageSize(item.ImagePath);
            if (width <= 0 || height <= 0) continue;

            images.Add(new
            {
                id = imageId,
                file_name = Path.GetFileName(item.ImagePath),
                width,
                height
            });

            foreach (var line in File.ReadAllLines(item.LabelPath))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 5) continue;

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var classId)) continue;

                var values = parts.Skip(1).Take(4)
                    .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
                    .ToArray();

                if (values.Any(double.IsNaN)) continue;

                // 우리 형식은 중심 기준 0~1, COCO 는 왼쪽 위 기준 픽셀이다.
                var boxWidth = values[2] * width;
                var boxHeight = values[3] * height;
                var left = (values[0] * width) - (boxWidth / 2);
                var top = (values[1] * height) - (boxHeight / 2);

                annotations.Add(new
                {
                    id = annotationId++,
                    image_id = imageId,
                    // 분류 번호는 우리 것(0부터) 그대로 둔다. COCO 관습은 1부터지만, 그렇게 적으면 학습한 모델의
                    // 0번 자리가 비고 우리 라벨(0번)과 한 칸씩 어긋나 되찾기가 0% 가 된다. 학습기는 번호를 그대로 쓴다.
                    category_id = classId,
                    bbox = new[] { Math.Round(left, 2), Math.Round(top, 2), Math.Round(boxWidth, 2), Math.Round(boxHeight, 2) },
                    area = Math.Round(boxWidth * boxHeight, 2),
                    iscrowd = 0
                });
            }

            imageId++;
        }

        var categories = Enumerable.Range(0, classes.Count)
            .Select(i => new { id = i, name = classes.Names[i], supercategory = "mob" })
            .ToArray();

        var json = JsonSerializer.Serialize(new { images, annotations, categories },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        File.WriteAllText(target, json, Utf8);
        Logger.Info($"COCO json 을 썼다: {target} (그림 {images.Count}장 · 사각형 {annotations.Count}개)");

        return target;
    }

    /// <summary>그림 파일의 픽셀 크기만 읽는다(전체를 디코딩하지 않는다).</summary>
    private static (int Width, int Height) ImageSize(string imagePath)
    {
        try
        {
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(imagePath),
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.None);

            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"그림 크기를 못 읽었다: {imagePath}");

            return (0, 0);
        }
    }
}
