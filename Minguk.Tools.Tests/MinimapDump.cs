using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Tools.Vision.Minimap;

namespace Minguk.Tools.Tests;

/// <summary>
/// <c>--minimap-dump --image=미니맵조각.png [--spec=minimap.json] [--marker=퀘스트] [--out=경로.png]</c> -
/// 스크립트가 미니맵에서 보는 것을 그림으로 뽑는다(사용자, 2026-09-26 "니가 보고 있는 미니맵 마스킹 된 이미지 좀 보여줘봐").
/// </summary>
/// <remarks>
/// 왼쪽은 원본(4배), 오른쪽은 판정: 바닥 칸(길찾기가 밟는 칸)은 초록, 아닌 칸은 붉게 덮고, 화살표 중심 십자, 점 종류마다 동그라미와 이름,
/// 가장 가까운 표시까지 찾은 길은 노란 선. 아래에 글로도 적는다(방위·거리·픽셀 수). 그림은 1080p 미니맵 조각(246×164)이어야 실제와 같다.
/// </remarks>
internal static class MinimapDump
{
    private const int Zoom = 4;

    public static int Run(string[] args)
    {
        var imagePath = Program.ArgValue(args, "--image=");

        if (imagePath is null || !File.Exists(imagePath))
        {
            Console.WriteLine("--image=<미니맵 조각.png> 가 있어야 한다. --spec=<minimap.json> 을 주면 그 규칙으로(없으면 기본), --marker=<이름> 은 길을 그릴 점(기본 퀘스트).");
            return 1;
        }

        var specPath = Program.ArgValue(args, "--spec=");
        var spec = (specPath is not null ? MinimapSpec.Load(specPath) : null) ?? new MinimapSpec();
        var markerName = Program.ArgValue(args, "--marker=") ?? "퀘스트";
        var outputPath = Program.ArgValue(args, "--out=") ?? Path.ChangeExtension(imagePath, null) + "-판정.png";

        var (pixels, width, height) = LoadBgra(imagePath);
        var arrow = MinimapReader.FindArrow(pixels, width, height, spec);
        var centerX = arrow?.CenterX ?? width / 2.0;
        var centerY = arrow?.CenterY ?? height / 2.0;
        var floor = MinimapPathFinder.FloorGrid(pixels, width, height, spec.FloorMinBrightness, out var columns, out var rows, spec.FloorMinCoolMinusRed);

        Console.WriteLine($"조각 {width}×{height} · 화살표 {(arrow is null ? "없음" : $"({arrow.CenterX:0},{arrow.CenterY:0}) 머리 {arrow.HeadAngle:0}도")} · 바닥 기준 밝기 {spec.FloorMinBrightness} · 서늘함 {spec.FloorMinCoolMinusRed}");

        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            var original = BitmapFrame.Create(new Uri(Path.GetFullPath(imagePath)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var left = new Rect(0, 0, width * Zoom, height * Zoom);
            var right = new Rect(width * Zoom + 8, 0, width * Zoom, height * Zoom);

            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, (width * Zoom * 2) + 8, height * Zoom));
            dc.DrawImage(original, left);
            dc.DrawImage(original, right);

            // 바닥 판정 덮기
            var floorBrush = new SolidColorBrush(Color.FromArgb(70, 0, 255, 0));
            var wallBrush = new SolidColorBrush(Color.FromArgb(90, 255, 0, 0));

            for (var cy = 0; cy < rows; cy++)
                for (var cx = 0; cx < columns; cx++)
                {
                    var cell = new Rect(right.X + (cx * MinimapPathFinder.CellPixels * Zoom), cy * MinimapPathFinder.CellPixels * Zoom,
                                        MinimapPathFinder.CellPixels * Zoom, MinimapPathFinder.CellPixels * Zoom);

                    dc.DrawRectangle(floor[(cy * columns) + cx] ? floorBrush : wallBrush, null, cell);
                }

            // 점들 - 종류마다
            var typeface = new Typeface("Malgun Gothic");
            var colors = new[] { Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.Orange, Colors.White };
            var index = 0;
            (double X, double Y)? goal = null;

            // 「목표」(노란 ▼·●)는 markers 표 밖의 기본 규칙(spec.Marker) - 같이 그린다.
            var rules = spec.Markers.Where(kv => kv.Key != "목표").Select(kv => (kv.Key, kv.Value)).Prepend(("목표", spec.Marker ?? new MinimapMarkerSpec()));

            foreach (var (name, rule) in rules)
            {
                var color = colors[index++ % colors.Length];
                var pen = new Pen(new SolidColorBrush(color), 2);
                var marks = MinimapReader.Markers(pixels, width, height, spec, rule, centerX, centerY);
                var matching = 0;

                for (var i = 0; i + 2 < pixels.Length; i += 4)
                    if (rule.Matches(pixels[i + 2], pixels[i + 1], pixels[i])) matching++;

                Console.WriteLine($"  점 「{name}」 {marks.Count}개 (색 맞는 픽셀 {matching}, 뭉치 최소 {rule.MinPixels}px): {string.Join(" · ", marks.Select(m => $"{m.Bearing:0}도 {m.Distance:0}px {m.Pixels}px²"))}");

                foreach (var mark in marks)
                {
                    var radians = mark.Bearing * Math.PI / 180;
                    var x = centerX + (mark.Distance * Math.Sin(radians));
                    var y = centerY - (mark.Distance * Math.Cos(radians));

                    dc.DrawEllipse(null, pen, new Point(right.X + (x * Zoom), y * Zoom), 6 * Zoom / 2.0, 6 * Zoom / 2.0);
                    dc.DrawText(new FormattedText($"{name} {mark.Distance:0}px", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, new SolidColorBrush(color), 1.0),
                                new Point(right.X + (x * Zoom) + 14, (y * Zoom) - 8));

                    if (name == markerName && (goal is null || mark.Distance < Distance((x, y), (centerX, centerY)))) goal = (x, y);
                }
            }

            // 화살표 중심
            var cross = new Pen(Brushes.White, 2);
            dc.DrawLine(cross, new Point(right.X + (centerX * Zoom) - 10, centerY * Zoom), new Point(right.X + (centerX * Zoom) + 10, centerY * Zoom));
            dc.DrawLine(cross, new Point(right.X + (centerX * Zoom), (centerY * Zoom) - 10), new Point(right.X + (centerX * Zoom), (centerY * Zoom) + 10));

            // 가장 가까운 표시까지 길
            if (goal is { } g)
            {
                var found = MinimapPathFinder.FindDetailed(pixels, width, height, (centerX, centerY), g, spec.FloorMinBrightness, spec.FloorMinCoolMinusRed);

                if (found is null)
                {
                    Console.WriteLine($"  길: 없음 (표시 ({g.X:0},{g.Y:0}))");
                }
                else
                {
                    Console.WriteLine($"  길: {found.Path.Count}칸 → 표시 ({g.X:0},{g.Y:0})");

                    var pathPen = new Pen(Brushes.Yellow, 3);
                    var previous = (centerX, centerY);

                    foreach (var p in found.Path)
                    {
                        dc.DrawLine(pathPen, new Point(right.X + (previous.Item1 * Zoom), previous.Item2 * Zoom), new Point(right.X + (p.X * Zoom), p.Y * Zoom));
                        previous = (p.X, p.Y);
                    }
                }
            }
            else
            {
                Console.WriteLine($"  길: 「{markerName}」 점이 없어 안 그림");
            }
        }

        var bitmap = new RenderTargetBitmap((width * Zoom * 2) + 8, height * Zoom, 96, 96, PixelFormats.Pbgra32);

        bitmap.Render(visual);

        using (var file = File.Create(outputPath))
        {
            var encoder = new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(file);
        }

        Console.WriteLine($"저장: {outputPath}");
        return 0;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static (byte[] Pixels, int Width, int Height) LoadBgra(string path)
    {
        var frame = BitmapFrame.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource source = frame.Format == PixelFormats.Bgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];

        source.CopyPixels(pixels, source.PixelWidth * 4, 0);

        return (pixels, source.PixelWidth, source.PixelHeight);
    }
}
