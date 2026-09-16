using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;

using Minguk.Tools.Vision.Ocr.Paddle;

namespace Minguk.Tools.Tests;

/// <summary>
/// PP-OCRv5 글자 읽기. 순수 조각(자르기·디코드·후처리·줄 묶기)은 ONNX 없이, 엔진은 그린 글자로 CPU·GPU 둘 다 본다.
/// </summary>
internal static partial class Program
{
    private static void TestPaddleOcr()
    {
        TestPaddleOcrModels();
        TestBgraImage();
        TestCtcDecoder();
        TestDbPostProcess();
        TestOcrLineGrouping();
        TestSeparatorSplit();
        TestPaddleOcrEngine();
    }

    private static void TestPaddleOcrModels()
    {
        var missing = PaddleOcrModels.Missing();

        Check("글자 읽기 모델이 출력에 있다", missing.Count == 0,
              missing.Count == 0 ? PaddleOcrModels.Folder : $"없음: {string.Join(", ", missing)}");

        var lines = File.Exists(PaddleOcrModels.Dictionary) ? File.ReadAllLines(PaddleOcrModels.Dictionary, Encoding.UTF8) : [];

        Check("사전 11,945자에 숫자·영문이 다 있다",
              lines.Length == 11945 && "0123456789AZaz".All(c => lines.Contains(c.ToString())),
              $"{lines.Length}자");
    }

    private static void TestBgraImage()
    {
        // 4x2, 픽셀마다 B 칸에 번호(0~7)를 적는다.
        var pixels = new byte[4 * 2 * 4];
        for (var i = 0; i < 8; i++) { pixels[i * 4] = (byte)i; pixels[(i * 4) + 3] = 255; }

        var image = new BgraImage(4, 2, pixels);
        var crop = image.Crop(1, 0, 2, 2)!;

        Check("자르면 그 자리 픽셀이 온다", crop.Width == 2 && crop.Height == 2
              && crop.Pixels[0] == 1 && crop.Pixels[4] == 2 && crop.Pixels[8] == 5 && crop.Pixels[12] == 6,
              $"{crop.Width}x{crop.Height} B={crop.Pixels[0]},{crop.Pixels[4]},{crop.Pixels[8]},{crop.Pixels[12]}");

        var clamped = image.Crop(3, 1, 10, 10)!;
        Check("밖으로 나간 자르기는 안쪽만", clamped.Width == 1 && clamped.Height == 1 && clamped.Pixels[0] == 7, $"{clamped.Width}x{clamped.Height}");
        Check("빈 자르기는 null", image.Crop(4, 0, 2, 2) is null && image.Crop(0, 0, 0, 1) is null, "");

        // 검정 | 흰색 두 칸을 네 칸으로 - 끝은 그대로, 가운데는 사이 값.
        var ramp = new BgraImage(2, 1, [0, 0, 0, 255, 255, 255, 255, 255]);
        var wide = ramp.Resize(4, 1);
        Check("늘리면 끝은 그대로 사이는 이어진다", wide.Pixels[0] == 0 && wide.Pixels[12] == 255
              && wide.Pixels[4] > 0 && wide.Pixels[4] < wide.Pixels[8] && wide.Pixels[8] < 255,
              $"{wide.Pixels[0]},{wide.Pixels[4]},{wide.Pixels[8]},{wide.Pixels[12]}");

        var flat = new BgraImage(3, 3, [.. Enumerable.Repeat(new byte[] { 10, 20, 30, 255 }, 9).SelectMany(p => p)]).Resize(7, 5);
        Check("한 색은 늘려도 그 색", flat.Pixels.Chunk(4).All(p => p[0] == 10 && p[1] == 20 && p[2] == 30), "");

        var drawn = BgraImage.From(DrawText("7", 20, 30, 20));
        Check("WPF 그림에서 픽셀을 옮긴다", drawn.Width == 30 && drawn.Height == 20 && drawn.Pixels.Length == 30 * 20 * 4, $"{drawn.Width}x{drawn.Height}");
    }

    private static void TestCtcDecoder()
    {
        // 빈칸 0 · 가 1 · 나 2 · 1 3 · 띄어쓰기 4
        var decoder = new CtcDecoder(["가", "나", "1"]);

        Check("글자 수는 사전 + 빈칸 + 띄어쓰기", decoder.ClassCount == 5, $"{decoder.ClassCount}");

        var read = decoder.Decode(Steps(5, 1, 1, 0, 1, 3, 4, 2), 7, 5);
        Check("이어진 같은 글자는 하나, 빈칸을 사이에 두면 둘", read.Text == "가가1 나", $"[{read.Text}]");
        Check("믿음은 고른 글자 확률의 평균", Math.Abs(read.Confidence - 0.9f) < 0.001f, $"{read.Confidence:0.###}");

        Check("빈칸뿐이면 빈 글, 믿음 0", decoder.Decode(Steps(5, 0, 0, 0), 3, 5) is { Text: "", Confidence: 0f }, "");

        var jamo = new CtcDecoder(["\u1100", "\u1161"]);   // ᄀ ᅡ
        var composed = jamo.Decode(Steps(4, 1, 2), 2, 4).Text;
        Check("조합용 자모는 음절로 합친다", composed == "가", $"[{composed}]");

        var thrown = false;
        try { decoder.Decode(Steps(6, 1), 1, 6); }
        catch (InvalidOperationException) { thrown = true; }

        Check("모델과 사전의 글자 수가 다르면 멈춘다", thrown, "");
    }

    /// <summary>칸마다 고른 글자에 0.9, 나머지에 나눠 준 확률.</summary>
    private static float[] Steps(int classes, params int[] picks)
    {
        var scores = new float[picks.Length * classes];

        for (var t = 0; t < picks.Length; t++)
            for (var c = 0; c < classes; c++)
                scores[(t * classes) + c] = c == picks[t] ? 0.9f : 0.1f / (classes - 1);

        return scores;
    }

    private static void TestDbPostProcess()
    {
        const int W = 40, H = 20;

        var one = new float[W * H];
        Fill(one, W, 5, 3, 10, 4, 0.9f);   // x 5~14, y 3~6

        var boxes = DbPostProcess.Extract(one, W, H);
        Check("덩어리 하나는 사각형 하나", boxes.Count == 1, $"{boxes.Count}개");

        if (boxes.Count == 1)
        {
            var b = boxes[0];
            Check("사각형은 덩어리보다 넓힌다(글자 가장자리까지)", b.Left < 5 && b.Top < 3 && b.Right > 15 && b.Bottom > 7,
                  $"{b.Left:0.#},{b.Top:0.#}~{b.Right:0.#},{b.Bottom:0.#}");
            Check("점수는 덩어리 확률의 평균", Math.Abs(b.Score - 0.9f) < 0.001f, $"{b.Score:0.###}");
        }

        var two = new float[W * H];
        Fill(two, W, 2, 2, 8, 4, 0.8f);
        Fill(two, W, 25, 10, 10, 5, 0.8f);
        Check("떨어진 덩어리 둘은 둘", DbPostProcess.Extract(two, W, H).Count == 2, "");

        var weak = new float[W * H];
        Fill(weak, W, 5, 5, 10, 5, 0.5f);   // 문턱 0.3 은 넘고 상자 0.6 은 못 넘는다
        Check("점수가 낮은 덩어리는 버린다", DbPostProcess.Extract(weak, W, H).Count == 0, "");

        var tiny = new float[W * H];
        Fill(tiny, W, 10, 10, 2, 2, 0.95f);
        Check("점만 한 덩어리는 버린다", DbPostProcess.Extract(tiny, W, H).Count == 0, "");

        var corner = new float[W * H];
        Fill(corner, W, 0, 0, 12, 6, 0.9f);
        var edge = DbPostProcess.Extract(corner, W, H);
        Check("넓혀도 맵 밖으로 안 나간다", edge.Count == 1 && edge[0].Left >= 0 && edge[0].Top >= 0,
              edge.Count == 1 ? $"{edge[0].Left},{edge[0].Top}" : $"{edge.Count}개");
    }

    private static void Fill(float[] map, int width, int left, int top, int w, int h, float value)
    {
        for (var y = top; y < top + h; y++)
            for (var x = left; x < left + w; x++)
                map[(y * width) + x] = value;
    }

    private static void TestOcrLineGrouping()
    {
        (DbBox, string)[] words =
        [
            (new DbBox(60, 12, 90, 28, 0.9f), "225"),
            (new DbBox(10, 50, 40, 66, 0.9f), "탄약"),
            (new DbBox(10, 10, 50, 30, 0.9f), "체력"),
            (new DbBox(50, 52, 70, 64, 0.9f), "17"),
        ];

        var lines = OcrLineGrouping.Group(words, 100, 80);

        Check("세로로 겹치는 조각은 한 줄, 위에서 아래로", lines.Count == 2 && lines[0].Text == "체력 225" && lines[1].Text == "탄약 17",
              string.Join(" / ", lines.Select(l => l.Text)));
        Check("낱말 자리는 0~1 비율",
              lines.SelectMany(l => l.Words).All(w => w.Box.Left >= 0 && w.Box.Right <= 1 && w.Box.Top >= 0 && w.Box.Bottom <= 1)
              && Math.Abs(lines[0].Words[0].Box.Left - 0.1) < 0.001, "");
        Check("조각이 없으면 줄도 없다", OcrLineGrouping.Group([], 10, 10).Count == 0, "");
    }

    /// <summary>
    /// 구분선(외따로 선 아주 가는 세로 획)에서 줄을 가른다 - 오버워치 탄약 「17 | 24」 의 1px 청록 선을 흉내 낸다.
    /// </summary>
    private static void TestSeparatorSplit()
    {
        static string Parts(BgraImage line) => string.Join(" ", SeparatorSplit.Split(line).Select(p => $"{p.Start}~{p.End}"));

        // 어두운 바탕 60x30 에 흰 덩어리 둘(숫자 자리) + 가운데 1px 청록 선
        var hud = Line(60, 30, (5, 15, 5, 26, White), (24, 25, 5, 26, Cyan), (34, 46, 5, 26, White));
        Check("가는 세로 획에서 둘로 가르고 획은 뺀다", Parts(hud) == "0~24 25~60", Parts(hud));

        var plain = Line(60, 30, (5, 15, 5, 26, White), (34, 46, 5, 26, White));
        Check("구분선이 없으면 줄 하나 그대로", Parts(plain) == "0~60", Parts(plain));

        var speck = Line(60, 30, (5, 15, 5, 26, White), (24, 25, 14, 17, Cyan), (34, 46, 5, 26, White));
        Check("키가 작은 점은 구분선이 아니다", Parts(speck) == "0~60", Parts(speck));

        var touching = Line(60, 30, (5, 15, 5, 26, White), (16, 17, 5, 26, Cyan), (34, 46, 5, 26, White));
        Check("글자에 붙은 획은 구분선이 아니다(한글 획을 자르지 않게)", Parts(touching) == "0~60", Parts(touching));

        var one = Line(60, 30, (5, 15, 5, 26, White), (22, 26, 5, 26, White), (34, 46, 5, 26, White));
        Check("숫자 1 만큼 굵은 획은 글자다", Parts(one) == "0~60", Parts(one));

        var edge = Line(60, 30, (2, 3, 5, 26, Cyan), (10, 20, 5, 26, White), (30, 42, 5, 26, White));
        Check("맨 끝 획은 양옆에 글자가 없어 구분선이 아니다", Parts(edge) == "0~60", Parts(edge));
    }

    private static readonly (byte R, byte G, byte B) White = (240, 240, 240);
    private static readonly (byte R, byte G, byte B) Cyan = (4, 180, 242);

    /// <summary>바탕 (40,40,40) 에 (왼, 오른(제외), 위, 아래(제외), 색) 사각형들을 칠한 줄 그림.</summary>
    private static BgraImage Line(int width, int height, params (int Left, int Right, int Top, int Bottom, (byte R, byte G, byte B) Color)[] blocks)
    {
        var pixels = new byte[width * height * 4];

        for (var i = 0; i < width * height; i++)
        {
            pixels[i * 4] = pixels[(i * 4) + 1] = pixels[(i * 4) + 2] = 40;
            pixels[(i * 4) + 3] = 255;
        }

        foreach (var (left, right, top, bottom, color) in blocks)
            for (var y = top; y < bottom; y++)
                for (var x = left; x < right; x++)
                {
                    var o = ((y * width) + x) * 4;
                    pixels[o] = color.B;
                    pixels[o + 1] = color.G;
                    pixels[o + 2] = color.R;
                }

        return new BgraImage(width, height, pixels);
    }

    private static void TestPaddleOcrEngine()
    {
        foreach (var useGpu in new[] { false, true })
        {
            var device = useGpu ? "GPU" : "CPU";
            PaddleOcrEngine engine;

            try
            {
                engine = PaddleOcrEngine.Create(useGpu);
            }
            catch (Exception ex)
            {
                Check($"PP-OCRv5 엔진을 만든다 ({device})", false, ex.Message);
                continue;
            }

            Check($"PP-OCRv5 엔진을 만든다 ({device})", true, engine.Name);

            using (engine)
            {
                string Read(System.Windows.Media.Imaging.BitmapSource image, out Minguk.Tools.Vision.Ocr.OcrOutcome outcome)
                {
                    outcome = engine.RecognizeAsync(image).GetAwaiter().GetResult();
                    return outcome.Text.Replace(" ", string.Empty).Replace(Environment.NewLine, "|");
                }

                // 첫 호출은 세션 준비가 섞인다 - 속도는 둘째부터 본다.
                var big = Read(DrawText("HP 1234", 40, 320, 90), out _);
                Read(DrawText("HP 1234", 40, 320, 90), out var second);
                Check($"숫자를 읽는다 ({device})", big.Contains("1234"), $"[{big}] 둘째 {second.Elapsed.TotalMilliseconds:0}ms");

                var mixed = Read(DrawText("고블린 전사 Lv.37", 28, 420, 70, Brushes.Black, Brushes.White, "Malgun Gothic", 12), out _);
                Check($"한글·영문·숫자를 한 모델로 ({device})", mixed == "고블린전사Lv.37", $"[{mixed}]");

                var small = Read(DrawText("LV 57", 14, 90, 24), out _);
                Check($"게임 UI 크기 글자 ({device})", small.Contains("57"), $"[{small}]");

                // 옛 길이 HudInk 로만 읽던 것 - 밝은 주황 바탕의 흰 숫자, 손질 없이.
                var hud = Read(DrawText("17 | 24", 22, 160, 48, new SolidColorBrush(Color.FromRgb(240, 170, 60)), Brushes.White, "Arial", 20), out _);
                Check($"주황 바탕 흰 숫자를 손질 없이 ({device})", hud.Contains("17") && hud.Contains("24"), $"[{hud}]");

                // 옛 길이 NameplateInk 로만 읽던 것 - 회색 바탕의 작은 빨간 글자.
                var plate = Read(DrawText("일반 봇", 13, 224, 72, Brushes.Gray, Brushes.Red, "Malgun Gothic", 90), out _);
                Check($"회색 바탕 빨간 이름표를 손질 없이 ({device})", plate.Contains("일반"), $"[{plate}]");

                var two = Read(DrawText("체력 225\n탄약 17", 24, 240, 90), out var twoOutcome);
                Check($"두 줄은 두 줄로 ({device})", twoOutcome.Lines.Count == 2 && two.StartsWith("체력225", StringComparison.Ordinal), $"[{two}]");

                var inside = twoOutcome.Lines.SelectMany(l => l.Words).All(w => w.Box.Left >= 0 && w.Box.Top >= 0 && w.Box.Right <= 1 && w.Box.Bottom <= 1);
                Check($"낱말 자리는 0~1 안 ({device})", twoOutcome.Lines.Count > 0 && inside, "");

                var blank = Read(DrawText(string.Empty, 20, 120, 40), out _);
                Check($"빈 그림은 빈 글 ({device})", blank.Length == 0, $"[{blank}]");
            }
        }
    }
}
