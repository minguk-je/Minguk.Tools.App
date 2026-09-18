using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Minguk.Tools.Vision.Labeling;

/// <summary>
/// 라벨 파일 한 장을 읽고 쓴다. 그림 하나에 <c>.txt</c> 하나다.
/// </summary>
/// <remarks>
/// <b>형식</b> — 한 줄에 사각형 하나, 빈칸으로 나눈 다섯 값이다.
/// <code>
/// &lt;검출 번호&gt; &lt;가운데 x&gt; &lt;가운데 y&gt; &lt;너비&gt; &lt;높이&gt;
/// </code>
/// 좌표는 모두 0~1 이다. 널리 쓰는 YOLO 형식 그대로다 - 우리끼리 쓸 형식을 새로 만들면
/// 남이 만든 학습 코드에 넣을 때마다 옮겨 적어야 하고, 라벨을 눈으로 볼 도구도 없어진다.
///
/// <b>왜 CultureInfo.InvariantCulture 인가</b>
///
/// 한국어 Windows 는 소수점이 <c>.</c> 지만, 지역을 유럽으로 둔 PC 에서는 <c>,</c> 로 찍힌다.
/// 그대로 두면 <c>0,5 0,5</c> 가 되어 빈칸으로 나눈 값의 <b>개수부터</b> 달라진다.
/// 읽을 때도 마찬가지라, 읽고 쓰는 양쪽을 다 못 박는다.
/// </remarks>
public static class LabelFile
{
    public const string Extension = ".txt";

    /// <summary>
    /// 읽는다. 파일이 없으면 빈 목록 - 아직 안 찍은 그림이라는 뜻이지 오류가 아니다.
    /// </summary>
    /// <remarks>
    /// 망가진 줄은 건너뛰고 나머지는 살린다. 한 줄 때문에 통째로 버리면 사람이 반나절
    /// 찍어 둔 것이 날아간다. 대신 몇 줄을 버렸는지는 <paramref name="skipped"/> 로 알린다.
    /// </remarks>
    public static IReadOnlyList<LabelBox> Load(string path, out int skipped)
    {
        skipped = 0;

        if (!File.Exists(path)) return [];

        var boxes = new List<LabelBox>();

        foreach (var line in File.ReadAllLines(path))
        {
            if (TryParse(line, out var box)) boxes.Add(box);
            else if (line.Trim().Length > 0) skipped++;
        }

        return boxes;
    }

    public static IReadOnlyList<LabelBox> Load(string path) => Load(path, out _);

    /// <summary>
    /// 쓴다. 사각형이 하나도 없으면 <b>파일을 지운다</b>.
    /// </summary>
    /// <remarks>
    /// 빈 파일을 남기면 "아직 안 본 그림" 과 "보고 나서 아무것도 없다고 정한 그림" 이
    /// 구분되지 않는다... 는 반대로 보일 수 있지만, 학습 쪽 관습은 빈 파일도 "배경 사진" 으로
    /// 쳐서 넣는다. 여기서는 <b>지운다</b> - 라벨을 다 지운 그림이 배경 사진으로 학습에 들어가면
    /// 사람이 의도하지 않은 것을 가르치게 된다. 배경 사진을 일부러 넣고 싶으면 그때 갈래를 둔다.
    /// </remarks>
    public static void Save(string path, IEnumerable<LabelBox> boxes)
    {
        var kept = boxes.Where(b => !b.IsTooSmall).ToArray();

        if (kept.Length == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllLines(path, kept.Select(Format), new UTF8Encoding(false));
    }

    /// <summary>
    /// 한 줄로 적는다. 소수점 여섯 자리까지 - 1920px 에서 0.002px 라 눈으로 볼 차이가 없다.
    /// </summary>
    public static string Format(LabelBox box) => string.Join(' ',
        box.ClassId.ToString(CultureInfo.InvariantCulture),
        Number(box.CenterX),
        Number(box.CenterY),
        Number(box.Width),
        Number(box.Height));

    public static bool TryParse(string line, out LabelBox box)
    {
        box = default;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 5) return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var classId)
            || classId < 0)
            return false;

        var values = new double[4];

        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }

        // 0~1 을 벗어난 값은 이 형식이 아니다. 픽셀로 적힌 파일을 잘못 열었을 때
        // 조용히 받아 두면 학습에서 엉뚱한 데가 터진다.
        if (values.Any(v => v is < 0d or > 1d)) return false;

        box = new LabelBox
        {
            ClassId = classId,
            CenterX = values[0],
            CenterY = values[1],
            Width = values[2],
            Height = values[3]
        };

        return !box.IsTooSmall;
    }

    // 자릿수는 LabelBox 가 든다. 여기서 따로 적으면 둘이 조용히 어긋난다.
    private static string Number(double value)
        => Math.Round(value, LabelBox.Digits).ToString("0.######", CultureInfo.InvariantCulture);
}
