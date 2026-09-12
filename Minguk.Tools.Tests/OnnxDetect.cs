using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Inference.Onnx;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// ONNX 검출기가 그림 하나에서 무엇을 어디서 찾는지 눈과 숫자로 본다(설계 2단계).
/// <c>--onnx-detect --model=경로.onnx --image=경로.png [--size=640x640] [--score=0.4] [--coco]</c>
/// </summary>
/// <remarks>
/// <b>왜 눈으로도 보나</b> - 좌표를 되돌리는 계산(레터박스 여백 빼기)이 틀려도 숫자는 그럴듯하게 나온다.
/// 사각형을 그려 원본 위에 얹으면 어긋남이 한눈에 보인다. 그린 그림은 모델 옆에 <c>-검출.png</c> 로 남긴다.
/// </remarks>
internal static class OnnxDetect
{
    public static int Run(string[] args)
    {
        var modelPath = Program.ArgValue(args, "--model=");
        var imagePath = Program.ArgValue(args, "--image=");
        var score = float.TryParse(Program.ArgValue(args, "--score="), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0.4f;

        if (modelPath is null || !File.Exists(modelPath) || imagePath is null || !File.Exists(imagePath))
        {
            Console.WriteLine("--model=<경로.onnx> --image=<경로.png> 가 있어야 한다.");
            return 1;
        }

        var size = (Program.ArgValue(args, "--size=") ?? "640x640").Split('x');
        var manifest = new DetectorManifest
        {
            Engine = DetectorEngine.Onnx,
            InputWidth = int.Parse(size[0], CultureInfo.InvariantCulture),
            InputHeight = int.Parse(size.Length > 1 ? size[1] : size[0], CultureInfo.InvariantCulture)
        };

        // COCO 모델로 확인할 때만 이름표가 필요하다. 우리 모델이면 데이터셋의 classes.txt 를 쓴다.
        var classes = args.Contains("--coco")
            ? new LabelClasses(CocoNames)
            : new LabelDataset(LabelDataset.ConfiguredRoot).LoadClasses();

        using var detector = new OnnxDetector(modelPath, manifest);

        // 첫 장은 커널 올리기가 섞인다. 두 번 돌려 뒤엣것을 적는다.
        detector.Detect(imagePath, classes, score);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var found = detector.Detect(imagePath, classes, score);
        watch.Stop();

        Console.WriteLine($"그림: {Path.GetFileName(imagePath)}");
        Console.WriteLine($"찾은 것 {found.Count}개 (문턱 {score:P0}) · 전부 {watch.ElapsedMilliseconds}ms 중 추론 {detector.LastInferenceMs:N1}ms");
        Console.WriteLine();

        foreach (var detection in found.Take(10))
        {
            var box = detection.Box;

            Console.WriteLine($"  {detection.Label,-12} {detection.Score:P0}  " +
                              $"가운데({box.CenterX:0.000}, {box.CenterY:0.000})  크기({box.Width:0.000} x {box.Height:0.000})");
        }

        var outputPath = Path.ChangeExtension(imagePath, null) + "-검출.png";
        Draw(imagePath, found, outputPath);

        Console.WriteLine();
        Console.WriteLine($"사각형을 그려 남겼다: {outputPath}");

        return 0;
    }

    /// <summary>찾은 사각형을 원본 위에 그려 파일로 남긴다. 좌표가 어긋나면 여기서 바로 보인다.</summary>
    private static void Draw(string imagePath, IReadOnlyList<Minguk.Tools.Vision.Inference.Detection> found, string outputPath)
    {
        var frame = BitmapFrame.Create(new Uri(imagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawImage(frame, new Rect(0, 0, frame.PixelWidth, frame.PixelHeight));

            var pen = new Pen(Brushes.Lime, Math.Max(2, frame.PixelWidth / 400.0));
            var typeface = new Typeface("Segoe UI");

            foreach (var detection in found.Take(20))
            {
                var box = detection.Box;
                var rect = new Rect(box.Left * frame.PixelWidth, box.Top * frame.PixelHeight,
                                    box.Width * frame.PixelWidth, box.Height * frame.PixelHeight);

                context.DrawRectangle(null, pen, rect);

                var text = new FormattedText(detection.Describe, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                             typeface, Math.Max(12, frame.PixelWidth / 80.0), Brushes.Lime, 1.0);

                context.DrawText(text, new Point(rect.Left, Math.Max(0, rect.Top - text.Height)));
            }
        }

        var target = new RenderTargetBitmap(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    /// <summary>COCO 80 이름. 사전학습 모델로 배선을 확인할 때만 쓴다.</summary>
    private static readonly string[] CocoNames =
    [
        "사람", "자전거", "자동차", "오토바이", "비행기", "버스", "기차", "트럭", "배", "신호등",
        "소화전", "정지 표지", "주차 미터기", "벤치", "새", "고양이", "개", "말", "양", "소",
        "코끼리", "곰", "얼룩말", "기린", "배낭", "우산", "핸드백", "넥타이", "여행가방", "원반",
        "스키", "스노보드", "공", "연", "야구 배트", "야구 글러브", "스케이트보드", "서프보드", "테니스 라켓", "병",
        "와인잔", "컵", "포크", "칼", "숟가락", "그릇", "바나나", "사과", "샌드위치", "오렌지",
        "브로콜리", "당근", "핫도그", "피자", "도넛", "케이크", "의자", "소파", "화분", "침대",
        "식탁", "변기", "TV", "노트북", "마우스", "리모컨", "키보드", "휴대폰", "전자레인지", "오븐",
        "토스터", "싱크대", "냉장고", "책", "시계", "꽃병", "가위", "곰인형", "헤어드라이어", "칫솔"
    ];
}
