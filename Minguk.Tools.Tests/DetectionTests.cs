using System;
using System.Linq;
using System.Reflection;

using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.Tests;

/// <summary>
/// 모델이 내놓은 것이 우리 좌표로 제대로 옮겨지는지.
/// </summary>
/// <remarks>
/// 여기가 틀리면 사각형이 엉뚱한 자리에 그려지는데, 화면에는 그럴싸하게 뜨므로 사람은
/// <b>모델이 못 배웠다</b>고 여기고 데이터를 더 모으러 간다. 코드가 틀린 것을 몇 시간 뒤에야 안다.
///
/// 진짜 추론은 2.2GB 를 받고 학습까지 해야 볼 수 있어 하네스에 못 넣는다. 대신 모델이
/// 내놓는 <b>모양</b>을 그대로 흉내 내어 옮기는 부분만 본다.
/// </remarks>
internal static partial class Program
{
    private static void TestDetection()
    {
        var classes = new LabelClasses(["슬라임", "버섯"]);

        // ── 픽셀이 0~1 로 바뀌는지 ──
        //
        // 320x200 그림에서 (32, 20)~(160, 100) 은 (0.1, 0.1)~(0.5, 0.5) 여야 한다.
        var prediction = MakePrediction(
            labels: ["버섯"],
            boxes: [32f, 20f, 160f, 100f],
            scores: [0.9f]);

        var found = Convert(prediction, classes, 320, 200, 0.5f);

        Check("픽셀을 0~1 로 바꾼다",
              found.Count == 1
              && Near(found[0].Box.Left, 0.1) && Near(found[0].Box.Top, 0.1)
              && Near(found[0].Box.Right, 0.5) && Near(found[0].Box.Bottom, 0.5),
              found.Count == 0 ? "(없음)" : LabelFile.Format(found[0].Box));

        Check("이름을 번호로 되돌린다 - 손으로 찍은 것과 같은 색이 되게",
              found.Count == 1 && found[0].ClassId == 1 && found[0].Label == "버섯",
              found.Count == 0 ? "(없음)" : $"{found[0].Label} = {found[0].ClassId}번");

        Check("이름과 점수를 같이 적는다",
              found.Count == 1 && found[0].Describe.Contains("버섯") && found[0].Describe.Contains("90"),
              found.Count == 0 ? "(없음)" : found[0].Describe);

        // ── 자신 없는 것은 버리는지 ──
        var mixed = MakePrediction(
            labels: ["슬라임", "버섯", "슬라임"],
            boxes: [0f, 0f, 32f, 20f, 32f, 20f, 64f, 40f, 64f, 40f, 96f, 60f],
            scores: [0.9f, 0.3f, 0.7f]);

        var kept = Convert(mixed, classes, 320, 200, 0.5f);

        Check("자신 없는 것은 버린다", kept.Count == 2, $"3개 중 {kept.Count}개 남음");

        Check("자신 있는 것부터 준다",
              kept.Count == 2 && kept[0].Score >= kept[1].Score,
              string.Join(" > ", kept.Select(k => k.Describe)));

        // ── 길이가 안 맞을 때 ──
        //
        // 사각형은 넷씩 묶여 있어 개수가 어긋나면 엉뚱한 이름이 엉뚱한 자리에 붙는다.
        // 화면에는 그럴싸하게 그려져 눈으로는 못 잡으므로 여기서 막아야 한다.
        var ragged = MakePrediction(
            labels: ["슬라임"],
            boxes: [0f, 0f, 32f, 20f, 32f, 20f, 64f, 40f],   // 사각형 2개인데 이름은 1개
            scores: [0.9f]);

        var safe = Convert(ragged, classes, 320, 200, 0.5f);

        Check("길이가 안 맞으면 짧은 쪽에 맞춘다", safe.Count == 1, $"{safe.Count}개");

        // ── 비었을 때 ──
        Check("아무것도 못 찾으면 빈 목록",
              Convert(MakePrediction([], [], []), classes, 320, 200, 0.5f).Count == 0,
              "터지지 않음");

        Check("점 하나짜리는 버린다",
              Convert(MakePrediction(["슬라임"], [10f, 10f, 10f, 10f], [0.9f]), classes, 320, 200, 0.5f).Count == 0,
              "넓이 0");

        // ── 모르는 이름 ──
        //
        // 학습한 뒤 몹 목록이 바뀌었을 수 있다. 그때 터지면 안 되고, 이름은 그대로 보여야 한다.
        var unknown = Convert(
            MakePrediction(["없는몹"], [32f, 20f, 160f, 100f], [0.9f]), classes, 320, 200, 0.5f);

        Check("모르는 이름도 그대로 보여 준다",
              unknown.Count == 1 && unknown[0].Label == "없는몹" && unknown[0].ClassId == 0,
              unknown.Count == 0 ? "(없음)" : unknown[0].Describe);

        // ── 열 이름 ──
        //
        // 이름이 어긋나면 조용히 빈 배열이 와서 "못 찾았다" 로 보인다. 읽는 쪽과 쓰는 쪽이
        // 같은 상수를 보므로 서로 어긋날 일은 없지만, 그 상수가 ML.NET 이 실제로 내놓는
        // 이름과 같아야 한다. 아래 값은 스크래치에서 학습해 보고 눈으로 확인한 것이다
        // (나온 열: ..., PredictedLabel, Score, PredictedBoundingBoxes).
        Check("열 이름이 ML.NET 이 내놓는 것과 같다",
              Minguk.Tools.Vision.Training.DetectorTrainer.PredictedLabelColumn == "PredictedLabel"
              && Minguk.Tools.Vision.Training.DetectorTrainer.PredictedBoxColumn == "PredictedBoundingBoxes"
              && Minguk.Tools.Vision.Training.DetectorTrainer.ScoreColumn == "Score",
              string.Join(", ",
                  Minguk.Tools.Vision.Training.DetectorTrainer.PredictedLabelColumn,
                  Minguk.Tools.Vision.Training.DetectorTrainer.PredictedBoxColumn,
                  Minguk.Tools.Vision.Training.DetectorTrainer.ScoreColumn));

    }

    /// <summary>
    /// 모델이 내놓는 모양을 흉내 낸다.
    /// </summary>
    /// <remarks>
    /// <c>DetectionPrediction</c> 과 <c>Convert</c> 는 internal 이다. 밖에 열어 두면 화면이
    /// 그걸 직접 만질 수 있게 되는데, 그러라고 만든 것이 아니다. 하네스에서만 리플렉션으로 본다.
    /// </remarks>
    private static object MakePrediction(string[] labels, float[] boxes, float[] scores)
    {
        var type = typeof(DetectorModel).Assembly.GetType("Minguk.Tools.Vision.Inference.DetectionPrediction")!;
        var instance = Activator.CreateInstance(type)!;

        type.GetProperty("PredictedLabel")!.SetValue(instance, labels);
        type.GetProperty("PredictedBoundingBoxes")!.SetValue(instance, boxes);
        type.GetProperty("Score")!.SetValue(instance, scores);

        return instance;
    }

    private static System.Collections.Generic.IReadOnlyList<Detection> Convert(
        object prediction, LabelClasses classes, int width, int height, float minimumScore)
    {
        var method = typeof(DetectorModel).GetMethod(
            "Convert", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (System.Collections.Generic.IReadOnlyList<Detection>)method.Invoke(
            null, [prediction, classes, width, height, minimumScore])!;
    }
}
