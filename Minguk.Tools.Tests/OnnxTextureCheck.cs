using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

using Minguk.Tools.Inference;
using Minguk.Tools.Vision.Inference.Onnx;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// GPU 전처리 길이 CPU 길과 같은 답을 내는지(설계 3단계).
/// <c>--onnx-texture --model=경로.onnx --image=경로.png [--size=640x640]</c>
/// </summary>
/// <remarks>
/// <b>왜</b> - 화면에서는 캡처 텍스처를 셰이더로 바로 텐서에 넣는다(디스크·리드백 없음). 그 셰이더가 CPU 로
/// 하던 것과 조금이라도 다르게 줄이거나 여백을 다르게 두면 사각형이 어긋나는데, 눈으로는 "모델이 좀 못 찾네" 로
/// 보인다. 같은 그림을 두 길로 넣어 찾은 자리를 견준다.
/// </remarks>
internal static class OnnxTextureCheck
{
    public static int Run(string[] args)
    {
        var modelPath = Program.ArgValue(args, "--model=");
        var imagePath = Program.ArgValue(args, "--image=");

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

        var classes = args.Contains("--coco") ? new LabelClasses(CocoNames) : new LabelDataset(LabelDataset.ConfiguredRoot).LoadClasses();
        var score = 0.35f;

        using var detector = new OnnxDetector(modelPath, manifest);

        // ── CPU 길: 그림 파일을 읽어 손으로 줄인다 ──
        var byFile = detector.Detect(imagePath, classes, score);

        // ── GPU 길: 그림을 텍스처에 올리고 셰이더로 줄인다 ──
        var (pixels, width, height) = ReadBgra(imagePath);

        D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out var device, out var context).CheckError();

        using (device)
        using (context)
        {
            using var texture = device!.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource
            }, [new SubresourceData(System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(pixels, 0), (uint)(width * 4))]);

            using var preprocessor = new FramePreprocessor(device, context!, detector.InputSpec, pipelined: false);

            if (!preprocessor.Process(texture, width, height))
            {
                Console.WriteLine("셰이더 전처리가 값을 못 냈다.");
                return 1;
            }

            var map = LetterboxMap.For(width, height, detector.InputSpec.Width, detector.InputSpec.Height, detector.InputSpec.Letterbox);
            var byTexture = detector.Detect(preprocessor.Tensor.ToArray(), map, classes, score);

            Console.WriteLine($"그림 {Path.GetFileName(imagePath)} ({width}x{height}) · 입력 {detector.InputSpec.Width}x{detector.InputSpec.Height}");
            Console.WriteLine($"  CPU 길  {byFile.Count}개: {Describe(byFile)}");
            Console.WriteLine($"  GPU 길  {byTexture.Count}개: {Describe(byTexture)}");
            Console.WriteLine($"  셰이더 {preprocessor.LastDispatchMs:N2}ms + 내려받기 {preprocessor.LastReadbackMs:N2}ms");

            var worst = 0.0;
            var pairs = Math.Min(byFile.Count, byTexture.Count);

            for (var i = 0; i < pairs; i++)
            {
                worst = Math.Max(worst, Math.Abs(byFile[i].Box.CenterX - byTexture[i].Box.CenterX));
                worst = Math.Max(worst, Math.Abs(byFile[i].Box.CenterY - byTexture[i].Box.CenterY));
            }

            var same = byFile.Count == byTexture.Count && worst < 0.01;

            Console.WriteLine();
            Console.WriteLine(same
                ? $"== 두 길이 같은 자리를 찾는다 (가장 큰 차이 {worst:P1}) =="
                : $"== 다르다: 개수 {byFile.Count} vs {byTexture.Count}, 가장 큰 차이 {worst:P1} ==");

            return same ? 0 : 1;
        }
    }

    private static string Describe(System.Collections.Generic.IReadOnlyList<Minguk.Tools.Vision.Inference.Detection> found)
        => found.Count == 0
            ? "(없음)"
            : string.Join(", ", found.Take(3).Select(d => $"{d.Label} {d.Score:P0} ({d.Box.CenterX:0.000}, {d.Box.CenterY:0.000})"));

    private static (byte[] Pixels, int Width, int Height) ReadBgra(string imagePath)
    {
        var frame = BitmapFrame.Create(new Uri(imagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];

        converted.CopyPixels(pixels, stride, 0);

        return (pixels, converted.PixelWidth, converted.PixelHeight);
    }

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
