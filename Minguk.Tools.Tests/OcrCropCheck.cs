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
/// <c>--ocr-crop --image=경로.png --region=x,y,w,h [--lang=ko] [--scale=3 | --height=300] [--prep=plain|bright|dark] [--shear=12]</c>
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

        // 작은 글자는 키워서 넣는다. 게임 HUD 숫자는 20px 안팎이라 그대로는 잘 안 읽힌다.
        var scale = int.TryParse(Program.ArgValue(args, "--scale="), out var s) ? Math.Clamp(s, 1, 8) : 3;
        var language = Program.ArgValue(args, "--lang=") ?? OcrEngineFactory.PreferredLanguage;

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

        // --ink 는 흰 글자만 남긴다. 밝은 배경 위의 HUD 숫자는 그냥은 안 읽힌다(HudInk 참고).
        var ink = args.Contains("--ink");
        var shear = double.TryParse(Program.ArgValue(args, "--shear="), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

        // 손질은 화면·스크립트와 같은 어댑터를 쓴다 - 여기서 잰 것이 앱에서도 그대로 나와야 한다.
        var preprocessor = Minguk.Tools.Vision.Ocr.OcrPreprocessors.Find(Program.ArgValue(args, "--prep=") ?? (ink ? "bright" : "plain"));
        var targetHeight = int.TryParse(Program.ArgValue(args, "--height="), out var h) ? h : crop.PixelHeight * scale;
        // --keep=0,0.5 면 자리를 통째로 넣되 오른쪽 절반을 지워 앞 숫자만 읽는다(좁게 자르면 못 읽는다).
        var keep = (Program.ArgValue(args, "--keep=") ?? "0,1").Split(',');
        var options = new Minguk.Tools.Vision.Ocr.OcrPreprocessOptions(
            targetHeight,
            shear,
            double.Parse(keep[0], CultureInfo.InvariantCulture),
            double.Parse(keep.Length > 1 ? keep[1] : "1", CultureInfo.InvariantCulture));

        BitmapSource scaled = preprocessor.Prepare(crop, options);

        scaled.Freeze();

        var outputPath = Path.ChangeExtension(imagePath, null) + "-오려낸.png";

        using (var file = File.Create(outputPath))
        {
            var encoder = new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(scaled));
            encoder.Save(file);
        }

        using var ocr = OcrEngineFactory.Create(language);
        var outcome = ocr.RecognizeAsync(scaled).GetAwaiter().GetResult();

        Console.WriteLine($"그림 {Path.GetFileName(imagePath)} ({width}x{height})");
        Console.WriteLine($"오려낸 자리 {box.X},{box.Y} {box.Width}x{box.Height} · " +
                          $"{preprocessor.Name} · 높이 {scaled.PixelHeight}px{(shear == 0 ? string.Empty : $" · {shear:0.#}도 세움")} · {ocr.Name} ({ocr.Language})");
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
