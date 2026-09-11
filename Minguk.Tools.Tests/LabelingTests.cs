using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Training;

namespace Minguk.Tools.Tests;

/// <summary>
/// 라벨이 찍은 그대로 파일에 남고 그대로 돌아오는지.
/// </summary>
/// <remarks>
/// 여기가 틀리면 사람이 반나절 찍어 둔 것이 조용히 어긋난다. 그런데 어긋나도 화면에는
/// 그럴싸한 사각형이 떠서 눈으로는 알아채기 어렵다 - 학습을 다 돌리고 나서야 안다.
/// </remarks>
internal static partial class Program
{
    private static void TestLabeling()
    {
        TestBoxEditing();
        TestTracking();
        TestImageCache();

        // ── 두 점 → 사각형 ──

        var box = LabelBox.FromCorners(0, 0.2, 0.4, 0.6, 0.8);

        Check("두 점으로 사각형 만들기",
              Near(box.CenterX, 0.4) && Near(box.CenterY, 0.6)
              && Near(box.Width, 0.4) && Near(box.Height, 0.4),
              $"가운데 ({box.CenterX:0.##}, {box.CenterY:0.##}) 크기 {box.Width:0.##}x{box.Height:0.##}");

        // 오른쪽 아래에서 왼쪽 위로 끌어도 같은 사각형이어야 한다.
        var backwards = LabelBox.FromCorners(0, 0.6, 0.8, 0.2, 0.4);

        Check("거꾸로 끌어도 같은 사각형", backwards == box,
              $"{LabelFile.Format(backwards)} / {LabelFile.Format(box)}");

        // 그림 밖으로 끌면 가장자리에서 멈춘다.
        var outside = LabelBox.FromCorners(1, -0.5, -0.5, 1.5, 0.5);

        Check("그림 밖으로는 안 나간다",
              Near(outside.Left, 0d) && Near(outside.Top, 0d) && Near(outside.Right, 1d),
              LabelFile.Format(outside));

        Check("점 하나짜리는 걸러 낸다",
              LabelBox.FromCorners(0, 0.5, 0.5, 0.5, 0.5).IsTooSmall,
              "넓이 0");

        // ── 한 줄 왕복 ──

        var line = LabelFile.Format(box);

        Check("한 줄로 적는 형식", line == "0 0.4 0.6 0.4 0.4", $"[{line}]");

        Check("적은 줄을 그대로 되읽는다",
              LabelFile.TryParse(line, out var parsed) && parsed == box,
              LabelFile.Format(parsed));

        // ── 이 형식이 아닌 줄 ──

        var wrong = new (string Line, string Why)[]
        {
            ("0 0.4 0.6 0.4", "값이 넷"),
            ("0 0.4 0.6 0.4 0.4 0.4", "값이 여섯"),
            ("-1 0.4 0.6 0.4 0.4", "몹 번호가 음수"),
            ("0 100 200 50 50", "0~1 이 아니다 - 픽셀로 적힌 파일"),
            ("0 0.4 0.6 0 0", "넓이 0"),
            ("몹 0.4 0.6 0.4 0.4", "번호 자리에 이름"),
            (string.Empty, "빈 줄")
        };

        var accepted = wrong.Where(w => LabelFile.TryParse(w.Line, out _)).ToArray();

        Check("이 형식이 아닌 줄은 안 받는다", accepted.Length == 0,
              accepted.Length == 0
                  ? $"{wrong.Length}가지 모두 걸렀다"
                  : string.Join(", ", accepted.Select(a => $"[{a.Line}] ({a.Why})")));

        // ── 소수점이 . 인지 ──
        //
        // 지역을 유럽으로 둔 PC 에서 "0,5" 로 찍히면 빈칸으로 나눈 값의 개수부터 달라진다.
        // 실제로 문화권을 바꿔 놓고 확인한다 - 코드를 읽어서는 이걸 못 잡는다.
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var german = LabelFile.Format(box);

            Check("소수점은 지역을 안 탄다",
                  german == line && LabelFile.TryParse(german, out var back) && back == box,
                  $"de-DE 에서 [{german}]");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // ── 파일 왕복 ──

        var root = Path.Combine(Path.GetTempPath(), "minguk-dataset-" + Guid.NewGuid().ToString("N"));

        try
        {
            var dataset = new LabelDataset(root);
            dataset.EnsureCreated();

            Check("데이터셋 폴더를 만든다",
                  Directory.Exists(dataset.ImageDirectory) && Directory.Exists(dataset.LabelDirectory),
                  dataset.Root);

            // 그림 자리에 아무 파일이나 하나 둔다. 여기서 보는 것은 짝짓기지 그림 내용이 아니다.
            var imagePath = dataset.NextImagePath(new DateTime(2026, 9, 10, 13, 5, 7, 250));
            File.WriteAllBytes(imagePath, [0]);

            Check("그림 이름을 시각으로 짓는다",
                  Path.GetFileName(imagePath) == "20260910-130507-250.png",
                  Path.GetFileName(imagePath));

            // 같은 시각으로 한 번 더 부르면 겹치지 않게 번호가 붙어야 한다.
            var second = dataset.NextImagePath(new DateTime(2026, 9, 10, 13, 5, 7, 250));
            File.WriteAllBytes(second, [0]);

            Check("같은 시각이어도 안 겹친다",
                  second != imagePath && Path.GetFileName(second) == "20260910-130507-250-2.png",
                  Path.GetFileName(second));

            var labelPath = dataset.LabelPathFor(imagePath);

            Check("그림과 라벨을 이름으로 짝짓는다",
                  Path.GetFileNameWithoutExtension(labelPath) == Path.GetFileNameWithoutExtension(imagePath)
                  && Path.GetExtension(labelPath) == LabelFile.Extension,
                  Path.GetFileName(labelPath));

            LabelBox[] written =
            [
                LabelBox.FromCorners(0, 0.1, 0.1, 0.3, 0.5),
                LabelBox.FromCorners(2, 0.6, 0.2, 0.9, 0.7)
            ];

            LabelFile.Save(labelPath, written);
            var read = LabelFile.Load(labelPath, out var skipped);

            Check("라벨을 파일에 쓰고 다시 읽기",
                  skipped == 0 && read.SequenceEqual(written),
                  string.Join(" / ", read.Select(LabelFile.Format)));

            // 망가진 줄이 섞여도 나머지는 살아야 한다.
            File.WriteAllLines(labelPath,
            [
                LabelFile.Format(written[0]),
                "이건 망가진 줄",
                LabelFile.Format(written[1])
            ]);

            var survived = LabelFile.Load(labelPath, out var dropped);

            Check("망가진 줄만 버리고 나머지는 살린다",
                  dropped == 1 && survived.SequenceEqual(written),
                  $"{survived.Count}개 살고 {dropped}줄 버림");

            // 다 지우면 파일도 없어져야 한다 - 빈 파일이 남으면 배경 사진으로 학습에 들어간다.
            LabelFile.Save(labelPath, []);

            Check("라벨을 다 지우면 파일도 지운다", !File.Exists(labelPath), Path.GetFileName(labelPath));

            // ── 목록 ──

            var items = dataset.EnumerateItems();

            // -2 가 먼저다. '-' 이 '.' 보다 앞이라 "...250-2.png" 가 "...250.png" 보다 앞선다.
            Check("그림을 이름순으로 훑는다",
                  items.Count == 2
                  && items[0].ImagePath == second && items[1].ImagePath == imagePath
                  && items.All(i => !i.HasLabel),
                  string.Join(", ", items.Select(i => i.Name)));

            LabelFile.Save(items[0].LabelPath, written);

            Check("찍은 것과 안 찍은 것을 가른다",
                  dataset.EnumerateItems().Count(i => i.HasLabel) == 1,
                  "2장 중 1장");

            // 이름이 같고 확장자만 다른 그림은 라벨 하나를 나눠 갖게 된다.
            File.WriteAllBytes(Path.ChangeExtension(imagePath, ".jpg"), [0]);

            Check("확장자만 다른 그림을 알아본다",
                  dataset.FindDuplicateStems().SequenceEqual([Path.GetFileNameWithoutExtension(imagePath)]),
                  string.Join(", ", dataset.FindDuplicateStems()));

            // ── 몹 이름 ──

            var classes = new LabelClasses(["슬라임", "버섯"]);

            Check("이름을 번호로 바꿔 준다",
                  classes.NameOf(0) == "슬라임" && classes.NameOf(1) == "버섯",
                  string.Join(", ", classes.Names));

            Check("없는 번호도 터지지 않는다", classes.NameOf(7) == "7번", classes.NameOf(7));

            Check("이미 있는 이름을 더하면 그 자리를 준다",
                  classes.Add("슬라임") == 0 && classes.Count == 2,
                  $"{classes.Count}개");

            classes.Rename(0, "왕슬라임");

            Check("이름을 바꿔도 번호는 그대로",
                  classes.NameOf(0) == "왕슬라임" && classes.IndexOf("왕슬라임") == 0,
                  string.Join(", ", classes.Names));

            Check("있는 이름으로는 못 바꾼다",
                  Throws(() => classes.Rename(0, "버섯")),
                  "버섯으로 바꾸기 거절");

            Check("이름에 줄 바꿈을 못 넣는다",
                  classes.Add("두\n줄") is var added && !classes.Names[added].Contains('\n'),
                  $"[{classes.Names[^1]}]");

            dataset.SaveClasses(classes);
            var reloaded = dataset.LoadClasses();

            Check("몹 이름을 파일에 쓰고 다시 읽기",
                  reloaded.Names.SequenceEqual(classes.Names),
                  string.Join(", ", reloaded.Names));

            TestTrainingShape(dataset, imagePath);

            Check("classes.txt 가 없으면 빈 목록",
                  LabelClasses.Load(Path.Combine(root, "없는파일.txt")).Count == 0,
                  "터지지 않음");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (Exception) { }
        }
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// 옮기기·크기 조절 계산. 마우스 없이 숫자로 확인한다.
    /// </summary>
    /// <remarks>
    /// 캔버스에 묻어 두면 손으로 끌어 봐야만 알 수 있다. 가장자리에서 멈추는지, 반대편을
    /// 넘겨 끌면 뒤집히는지 같은 것은 눈으로 보면 놓치기 쉽다.
    /// </remarks>
    private static void TestBoxEditing()
    {
        var box = LabelBox.FromCorners(0, 0.2, 0.2, 0.4, 0.5);   // 0.2x0.3

        // ── 옮기기 ──

        var moved = LabelBoxEdit.Move(box, 0.1, -0.1);

        Check("옮기면 크기는 그대로",
              Near(moved.Left, 0.3) && Near(moved.Top, 0.1) && Near(moved.Width, 0.2) && Near(moved.Height, 0.3),
              LabelFile.Format(moved));

        var pushed = LabelBoxEdit.Move(box, 5, 5);

        Check("가장자리에서 멈추고 찌그러지지 않는다",
              Near(pushed.Right, 1) && Near(pushed.Bottom, 1) && Near(pushed.Width, 0.2) && Near(pushed.Height, 0.3),
              LabelFile.Format(pushed));

        var pulled = LabelBoxEdit.Move(box, -5, -5);

        Check("왼쪽 위로도 멈춘다",
              Near(pulled.Left, 0) && Near(pulled.Top, 0) && Near(pulled.Width, 0.2),
              LabelFile.Format(pulled));

        // ── 크기 조절 ──

        var grown = LabelBoxEdit.Resize(box, BoxHandle.BottomRight, 0.6, 0.9);

        Check("오른쪽 아래 모서리를 끌면 왼쪽 위는 그대로",
              Near(grown.Left, 0.2) && Near(grown.Top, 0.2) && Near(grown.Right, 0.6) && Near(grown.Bottom, 0.9),
              LabelFile.Format(grown));

        var narrowed = LabelBoxEdit.Resize(box, BoxHandle.Left, 0.3, 0.99);

        Check("왼쪽 변만 끌면 세로는 안 바뀐다",
              Near(narrowed.Left, 0.3) && Near(narrowed.Right, 0.4) && Near(narrowed.Top, 0.2) && Near(narrowed.Bottom, 0.5),
              LabelFile.Format(narrowed));

        var flipped = LabelBoxEdit.Resize(box, BoxHandle.Left, 0.7, 0);

        Check("반대편을 넘겨 끌면 뒤집히되 너비는 양수",
              Near(flipped.Left, 0.4) && Near(flipped.Right, 0.7) && flipped.Width > 0,
              LabelFile.Format(flipped));

        var collapsed = LabelBoxEdit.Resize(box, BoxHandle.Right, 0.2, 0);

        Check("점이 될 만큼 줄이면 원래 것을 지킨다", collapsed == box, LabelFile.Format(collapsed));

        Check("안쪽을 잡은 것은 크기 조절이 아니다",
              LabelBoxEdit.Resize(box, BoxHandle.Inside, 0.9, 0.9) == box, string.Empty);

        // ── 손잡이 짚기 (화면 픽셀: 100,100 ~ 300,200, 손잡이 8px) ──

        BoxHandle Hit(double x, double y) => LabelBoxEdit.HitHandle(100, 100, 300, 200, x, y, 8);

        Check("모서리는 변보다 먼저 잡힌다", Hit(302, 98) == BoxHandle.TopRight, Hit(302, 98).ToString());
        Check("변 가운데는 그 변", Hit(200, 203) == BoxHandle.Bottom, Hit(200, 203).ToString());
        Check("왼쪽 변", Hit(95, 150) == BoxHandle.Left, Hit(95, 150).ToString());
        Check("안쪽은 옮기기", Hit(200, 150) == BoxHandle.Inside, Hit(200, 150).ToString());
        Check("바깥은 아무것도 아님", Hit(200, 250) == BoxHandle.None, Hit(200, 250).ToString());
        Check("변에서 떨어진 바깥 자리는 변이 아니다", Hit(320, 100) == BoxHandle.None, Hit(320, 100).ToString());
    }

    /// <summary>
    /// 프레임 간 추적. 한 번 튄 헛것은 안 내놓고, 두 번 본 것은 내놓고, 잠깐 놓쳐도 잇고, 오래 놓치면 버린다.
    /// </summary>
    private static void TestTracking()
    {
        static Detection At(double cx, double cy, float score = 0.8f) => new("봇", new LabelBox
        {
            ClassId = 0, CenterX = cx, CenterY = cy, Width = 0.1, Height = 0.1
        }, score);

        var tracker = new DetectionTracker();

        // 1) 처음 본 것은 후보일 뿐이다.
        var first = tracker.Update([At(0.3, 0.3)]);
        Check("처음 본 것은 아직 안 내놓는다", first.Count == 0, $"{first.Count}개");

        // 2) 같은 자리에 또 보이면 내놓는다.
        var second = tracker.Update([At(0.31, 0.3)]);
        Check("두 번 연속 보이면 내놓는다", second.Count == 1, $"{second.Count}개");

        // 3) 한 프레임 놓쳐도 이어 준다.
        var missedOnce = tracker.Update([]);
        Check("한 번 놓쳐도 이어 준다", missedOnce.Count == 1, $"{missedOnce.Count}개");

        // 4) 세 번 연속 놓치면 버린다 (MaxMisses 2).
        tracker.Update([]);
        var gone = tracker.Update([]);
        Check("세 번 연속 놓치면 버린다", gone.Count == 0, $"{gone.Count}개, 추적 {tracker.Tracks.Count}개");

        // 5) 한 프레임짜리 헛것은 진짜 옆에 나와도 안 내놓는다.
        tracker.Reset();
        tracker.Update([At(0.5, 0.5)]);
        var withGhost = tracker.Update([At(0.5, 0.5), At(0.9, 0.9, 0.35f)]);
        Check("한 프레임짜리 헛것은 안 내놓는다", withGhost.Count == 1 && Near(withGhost[0].Box.CenterX, 0.5),
              $"{withGhost.Count}개");

        // 6) 자리는 새 값 쪽으로 부드럽게 옮긴다 (0.6 비중). 0.03 은 폭 0.1 사각형에서 IoU 0.54 라 같은 몹으로 이어진다.
        tracker.Reset();
        tracker.Update([At(0.2, 0.2)]);
        var moved = tracker.Update([At(0.23, 0.2)]);
        Check("자리는 새 값 쪽으로 부드럽게", moved.Count == 1 && Near(moved[0].Box.CenterX, 0.218),
              moved.Count == 1 ? $"x={moved[0].Box.CenterX:0.###}" : "없음");

        // 7) 멀리 떨어진 둘은 따로 잇는다.
        tracker.Reset();
        tracker.Update([At(0.2, 0.2), At(0.8, 0.8)]);
        var two = tracker.Update([At(0.2, 0.2), At(0.8, 0.8)]);
        Check("떨어진 둘은 따로 잇는다", two.Count == 2, $"{two.Count}개");

        // 8) 제 폭만큼 옮겨 가 안 겹쳐도(IoU 0) 가운데가 가까우면 같은 몹이다 - 유령이 안 생긴다.
        tracker.Reset();
        tracker.Update([At(0.2, 0.2)]);
        var jumped = tracker.Update([At(0.31, 0.2)]);
        Check("폭만큼 옮겨도 같은 몹으로 잇는다", jumped.Count == 1 && tracker.Tracks.Count == 1,
              $"내놓음 {jumped.Count}개 · 추적 {tracker.Tracks.Count}개");

        // 9) 폭의 두 배 넘게 옮기면 다른 몹이다.
        tracker.Reset();
        tracker.Update([At(0.2, 0.2)]);
        tracker.Update([At(0.5, 0.2)]);
        Check("멀리 뛰면 다른 몹이다", tracker.Tracks.Count == 2, $"추적 {tracker.Tracks.Count}개");
    }

    /// <summary>
    /// 학습 그림 캐시. 모델 크기로 줄여 두고, 원본이 안 바뀌면 다시 안 만들고, 바뀌면 다시 만든다.
    /// </summary>
    private static void TestImageCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "minguk-cache-" + Guid.NewGuid().ToString("N"));

        try
        {
            var dataset = new LabelDataset(root);
            dataset.EnsureCreated();

            var source = Path.Combine(dataset.ImageDirectory, "big.png");
            WritePng(source, 1920, 1080);

            var map = TrainingImageCache.Ensure(dataset, [source], 640, 360);
            var cached = map[source];

            ImageSize.TryRead(cached, out var w, out var h);
            Check("캐시는 모델 크기로 줄인 PNG 다", File.Exists(cached) && w == 640 && h == 360, $"{w}x{h} {Path.GetFileName(cached)}");
            Check("캐시는 크기별 폴더에 든다", cached.Contains(Path.Combine("cache", "640x360")), cached);

            var first = File.GetLastWriteTimeUtc(cached);
            Thread.Sleep(30);
            TrainingImageCache.Ensure(dataset, [source], 640, 360);
            Check("원본이 그대로면 다시 안 만든다", File.GetLastWriteTimeUtc(cached) == first, string.Empty);

            // 원본이 새로 담기면(시각이 뒤) 그 장만 다시 만든다.
            Thread.Sleep(30);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(5));
            TrainingImageCache.Ensure(dataset, [source], 640, 360);
            Check("원본이 바뀌면 다시 만든다", File.GetLastWriteTimeUtc(cached) > first, string.Empty);

            var other = TrainingImageCache.Ensure(dataset, [source], 320, 180)[source];
            ImageSize.TryRead(other, out var w2, out _);
            Check("다른 크기는 다른 캐시", other != cached && w2 == 320, other);
        }
        catch (Exception ex)
        {
            Check("그림 캐시", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (Exception) { }
        }
    }

    private static void WritePng(string path, int width, int height)
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), new byte[width * height * 4], width * 4, 0);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
