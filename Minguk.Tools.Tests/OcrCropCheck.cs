using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Ocr;

namespace Minguk.Tools.Tests;

/// <summary>
/// 그림의 한 자리를 잘라 글자를 읽어 본다 - 스크립트에 넣을 <c>읽기</c>·<c>숫자읽기</c> 의 자리를 찾는 데 쓴다.
/// <c>--ocr-crop --image=경로.png --region=x,y,w,h</c>
/// </summary>
/// <remarks>
/// <b>왜</b> - 스크립트의 <c>숫자읽기(x, y, w, h)</c> 는 0~1 비율을 받는데, 그 네 숫자를 눈대중으로 맞히기 어렵다.
/// 게임을 켜고 스크립트를 돌려 가며 찾으면 한 번에 몇 분씩 걸린다. 찍어 둔 그림으로 여기서 맞춰 보고
/// 나온 숫자를 그대로 스크립트에 적으면 된다.
///
/// 잘라 낸 조각은 <c>-오려낸.png</c> 로 남긴다 - 읽은 글이 이상하면 자리가 틀린 것인지 글자가 작은 것인지
/// 눈으로 갈라야 한다.
/// </remarks>
internal static class OcrCropCheck
{
    public static int Run(string[] args)
    {
        var imagePath = Program.ArgValue(args, "--image=");
        var region = Program.ArgValue(args, "--region=");

        if (imagePath is null || !File.Exists(imagePath) || region is null)
        {
            Console.WriteLine("--image=<경로.png> --region=<x,y,w,h> 가 있어야 한다 (0~1 비율).");
            Console.WriteLine("보기: --ocr-crop --image=화면.png --region=0.92,0.88,0.05,0.03");

            return 1;
        }

        var parts = region.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();

        if (parts.Length != 4)
        {
            Console.WriteLine("--region 은 x,y,w,h 네 개다.");
            return 1;
        }

        var frame = BitmapFrame.Create(new Uri(imagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;

        var box = new Int32Rect(
            (int)Math.Round(parts[0] * width),
            (int)Math.Round(parts[1] * height),
            Math.Max(1, (int)Math.Round(parts[2] * width)),
            Math.Max(1, (int)Math.Round(parts[3] * height)));

        // 그림 밖으로 나가면 자른다 - 비율을 조금 크게 준 것뿐이니 터뜨릴 일이 아니다.
        box.X = Math.Clamp(box.X, 0, width - 1);
        box.Y = Math.Clamp(box.Y, 0, height - 1);
        box.Width = Math.Min(box.Width, width - box.X);
        box.Height = Math.Min(box.Height, height - box.Y);

        var crop = new CroppedBitmap(frame, box);

        crop.Freeze();

        var outputPath = Path.ChangeExtension(imagePath, null) + "-오려낸.png";

        using (var file = File.Create(outputPath))
        {
            var encoder = new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(crop));
            encoder.Save(file);
        }

        using var ocr = OcrEngineFactory.Create(out var fallback);
        var outcome = ocr.RecognizeAsync(crop).GetAwaiter().GetResult();

        Console.WriteLine($"그림 {Path.GetFileName(imagePath)} ({width}x{height})");
        Console.WriteLine($"오려낸 자리 {box.X},{box.Y} {box.Width}x{box.Height} · {ocr.Name}{(fallback is null ? string.Empty : $" ({fallback})")}");
        Console.WriteLine();
        Console.WriteLine($"읽은 글: 「{outcome.Text}」  ({outcome.Elapsed.TotalMilliseconds:N0}ms)");

        foreach (var line in outcome.Lines)
            Console.WriteLine($"  줄: 「{line.Text}」  낱말 {string.Join(" · ", line.Words.Select(w => w.Text))}");

        var digits = new string([.. outcome.Text.Where(char.IsDigit)]);

        Console.WriteLine();
        Console.WriteLine(digits.Length > 0 ? $"숫자만: {digits}" : "숫자가 없다.");
        Console.WriteLine($"오려낸 그림: {outputPath}");

        return 0;
    }
}
