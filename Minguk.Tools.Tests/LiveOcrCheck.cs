using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Capture;
using Minguk.Tools.Vision.Ocr.Paddle;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 켜 둔 게임 창을 앱과 같은 WGC 로 몇 장 잡아, 프로젝트의 자리·칸을 스크립트처럼 잘라 읽는다 - <c>--live-ocr --project=폴더 [--process=Overwatch] [--frames=3] [--out=폴더]</c>.
/// </summary>
/// <remarks>프레임 전체와 칸 조각(4배)을 PNG 로 남긴다 - 칸이 글자를 자르는지 눈으로 본다. 입력은 보내지 않는다.</remarks>
internal static class LiveOcrCheck
{
    public static int Run(string[] args)
    {
        var project = Program.ArgValue(args, "--project=");
        var process = Program.ArgValue(args, "--process=") ?? "Overwatch";
        var frames = int.TryParse(Program.ArgValue(args, "--frames="), out var f) ? f : 3;
        var outFolder = Program.ArgValue(args, "--out=") ?? Path.Combine(Path.GetTempPath(), "minguk-live-ocr");

        if (project is null || !File.Exists(Path.Combine(project, "regions.json")))
        {
            Console.WriteLine("--project=<regions.json 이 든 프로젝트 폴더> 가 있어야 한다.");
            return 2;
        }

        var window = CaptureTarget.EnumerateWindows().FirstOrDefault(w => w.ProcessName?.Contains(process, StringComparison.OrdinalIgnoreCase) == true);

        if (window is null)
        {
            Console.WriteLine($"'{process}' 창을 못 찾았다. 떠 있는 창: {string.Join(" / ", CaptureTarget.EnumerateWindows().Select(w => w.ProcessName).Distinct())}");
            return 2;
        }

        Directory.CreateDirectory(outFolder);
        Console.WriteLine($"대상: {window.Display}");

        var regions = RegionBook.Load(project).Regions.Where(r => r.IsUsable).ToList();
        using var ocr = PaddleOcrEngine.Create(useGpu: true);

        var shots = Grab(window, frames);

        if (shots.Count == 0 || shots.All(IsBlack))
        {
            Console.WriteLine("창 캡처가 비었거나 검다 - 모니터로 잡는다(전체화면).");
            if (CaptureTarget.MonitorOf(window.Handle) is { } monitor) shots = Grab(monitor, frames);
        }

        for (var n = 0; n < shots.Count; n++)
        {
            var shot = shots[n];
            Save(shot, Path.Combine(outFolder, $"frame{n + 1}.png"));
            Console.WriteLine();
            Console.WriteLine($"── 프레임 {n + 1} ({shot.PixelWidth}x{shot.PixelHeight}) ──");

            foreach (var region in regions)
            {
                var whole = Crop(shot, region.Rect);
                var wholeText = Read(ocr, whole);
                Save(Scale(whole, 4), Path.Combine(outFolder, $"frame{n + 1}-{region.Name}.png"));

                var cells = RegionTargets.Of(region, null)
                    .Select(target =>
                    {
                        var crop = CropCell(shot, target);
                        Save(Scale(crop, 4), Path.Combine(outFolder, $"frame{n + 1}-{region.Name}.{target.Cell.Name}.png"));
                        return (target.Cell.Name, Text: Read(ocr, crop), crop.PixelWidth, crop.PixelHeight);
                    })
                    .ToList();

                var joined = RegionTargets.Combine([.. cells.Select(c => c.Text)]);

                Console.WriteLine($"  [{region.Name}] 자리 통째: '{wholeText}' · 칸 이음: '{joined.Text}' 숫자 [{string.Join(", ", joined.Numbers)}]");
                foreach (var c in cells) Console.WriteLine($"      .{c.Name} ({c.PixelWidth}x{c.PixelHeight}): '{c.Text}'");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"그림: {outFolder}");
        return 0;
    }

    private static System.Collections.Generic.List<BitmapSource> Grab(CaptureTarget target, int count)
    {
        var shots = new System.Collections.Generic.List<BitmapSource>();
        var last = 0L;

        using var session = ScreenCaptureAdapterFactory.Create(target, cpuReadback: true);
        session.TargetFps = 10;
        session.FrameArrived += (_, e) =>
        {
            if (!e.HasPixels || Environment.TickCount64 - last < 700) return;

            lock (shots)
            {
                if (shots.Count >= count) return;

                var stride = e.Width * 4;
                var pixels = new byte[stride * e.Height];
                for (var y = 0; y < e.Height; y++) Marshal.Copy(e.PixelData + (y * e.RowPitch), pixels, y * stride, stride);

                var image = BitmapSource.Create(e.Width, e.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                image.Freeze();
                shots.Add(image);
                last = Environment.TickCount64;
            }
        };

        session.Start();

        var deadline = Environment.TickCount64 + 3000 + (count * 1000);
        while (Environment.TickCount64 < deadline) { lock (shots) if (shots.Count >= count) break; Thread.Sleep(50); }

        session.Stop();
        lock (shots) return [.. shots];
    }

    private static bool IsBlack(BitmapSource image)
    {
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4 * 97)
            if (pixels[i] > 10 || pixels[i + 1] > 10 || pixels[i + 2] > 10) return false;

        return true;
    }

    private static string Read(PaddleOcrEngine ocr, BitmapSource crop) => ocr.RecognizeAsync(crop).GetAwaiter().GetResult().Text.Replace(Environment.NewLine, " ").Trim();

    private static BitmapSource CropCell(BitmapSource image, RegionTarget target)
    {
        if (RegionTargets.IsUpright(target.Angle)) return Crop(image, target.Box);

        var bounds = RegionTargets.Bounds(target, image.PixelWidth, image.PixelHeight);
        return RegionTargets.Upright(Crop(image, bounds), bounds, target, image.PixelWidth, image.PixelHeight);
    }

    private static BitmapSource Crop(BitmapSource image, Rect region)
    {
        var w = image.PixelWidth;
        var h = image.PixelHeight;
        var left = Math.Clamp((int)Math.Floor(region.X * w), 0, w - 1);
        var top = Math.Clamp((int)Math.Floor(region.Y * h), 0, h - 1);
        var right = Math.Clamp((int)Math.Ceiling(region.Right * w), left + 1, w);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom * h), top + 1, h);

        var crop = new CroppedBitmap(image, new Int32Rect(left, top, right - left, bottom - top));
        crop.Freeze();
        return crop;
    }

    private static BitmapSource Scale(BitmapSource image, double factor)
    {
        var scaled = new TransformedBitmap(image, new ScaleTransform(factor, factor));
        RenderOptions.SetBitmapScalingMode(scaled, BitmapScalingMode.NearestNeighbor);
        scaled.Freeze();
        return scaled;
    }

    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
