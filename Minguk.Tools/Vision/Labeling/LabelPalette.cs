using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Media;

namespace Minguk.Tools.Vision.Labeling;

/// <summary>
/// 검출 번호마다 색. 사람이 고른 것만 적어 두고, 안 고른 번호는 기본 색(황금각)이다.
/// </summary>
/// <remarks>
/// <b>왜 classes.txt 에 안 넣는가</b> - 그 파일은 YOLO 형식 그대로라 한 줄에 이름 하나다. 색을 붙이면 남의 학습
/// 도구가 이름을 잘못 읽는다. 그래서 옆에 <c>class-colors.json</c> 을 따로 둔다(데이터셋 폴더, 앱 설정이 아니다 -
/// 색은 그 데이터셋의 검출에 매인 것이라 사진·라벨과 같이 옮겨 간다).
///
/// <b>번호로 맨다</b>(이름이 아니라). 라벨이 번호를 쓰므로 이름을 바꿔도 색이 따라오고, 검출을 지워 번호가 당겨지면
/// 색도 같이 당긴다(<see cref="RemoveAt"/>). 안 고른 자리는 <c>null</c> 이라 검출이 늘 때 색을 새로 고르지 않아도 된다 -
/// 처음에는 예전처럼 번호에서 만든 기본 색이 나온다.
/// </remarks>
public sealed class LabelPalette
{
    public const string FileName = "class-colors.json";

    private readonly List<Color?> _colors = [];

    /// <summary>그 번호의 색. 사람이 골랐으면 그것, 아니면 기본 색.</summary>
    public Color ColorOf(int classId)
        => classId >= 0 && classId < _colors.Count && _colors[classId] is { } chosen ? chosen : DefaultColor(classId);

    /// <summary>사람이 고른 색인지. 기본 색이면 false.</summary>
    public bool IsChosen(int classId) => classId >= 0 && classId < _colors.Count && _colors[classId] is not null;

    /// <summary>색을 고른다. 기본 색과 같은 값을 주면 "안 고름" 으로 되돌린다 - 파일에 남길 것이 없다.</summary>
    public void Set(int classId, Color? color)
    {
        if (classId < 0) throw new ArgumentOutOfRangeException(nameof(classId));

        while (_colors.Count <= classId) _colors.Add(null);

        _colors[classId] = color == DefaultColor(classId) ? null : color;

        Trim();
    }

    /// <summary>번호 하나를 빼고 뒤를 당긴다. 검출을 지울 때 라벨 번호와 같이 당긴다.</summary>
    public void RemoveAt(int classId)
    {
        if (classId >= 0 && classId < _colors.Count) _colors.RemoveAt(classId);

        Trim();
    }

    /// <summary>0 부터 <paramref name="count"/> 앞까지의 색. 캔버스에 통째로 넘긴다.</summary>
    public IReadOnlyList<Color> Snapshot(int count)
        => Enumerable.Range(0, Math.Max(count, 0)).Select(ColorOf).ToArray();

    /// <summary>
    /// 번호에서 만드는 기본 색.
    /// </summary>
    /// <remarks>
    /// 황금각(137.5도)씩 돌린다. 번호가 몇 개든 이웃한 번호끼리 색이 가장 멀어져,
    /// 사각형이 겹쳐 있어도 어느 것이 어느 검출인지 눈으로 갈린다.
    /// </remarks>
    public static Color DefaultColor(int classId)
    {
        var hue = (Math.Abs(classId) * 137.508) % 360;

        return FromHsv(hue, 0.85, 0.95);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    // ── 파일 ─────────────────────────────────────────────────────────────

    /// <summary>JSON 배열. 자리가 번호고 값은 "#RRGGBB" 또는 null(기본 색).</summary>
    public static LabelPalette Load(string path)
    {
        var palette = new LabelPalette();

        if (!File.Exists(path)) return palette;

        try
        {
            var entries = JsonSerializer.Deserialize<string?[]>(File.ReadAllText(path, Encoding.UTF8)) ?? [];

            for (var i = 0; i < entries.Length; i++)
            {
                if (TryParse(entries[i], out var color)) palette.Set(i, color);
            }
        }
        catch (JsonException)
        {
            // 망가진 파일 때문에 라벨링을 못 하게 하지 않는다. 기본 색으로 돌고, 다음 저장이 덮는다.
        }

        return palette;
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (_colors.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);   // 고른 색이 하나도 없으면 파일도 없다
            return;
        }

        var entries = _colors.Select(c => c is { } color ? Format(color) : null).ToArray();

        File.WriteAllText(path, JsonSerializer.Serialize(entries), new UTF8Encoding(false));
    }

    public static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParse(string? text, out Color color)
    {
        color = default;

        if (text is null || text.Length != 7 || text[0] != '#') return false;

        try
        {
            color = Color.FromRgb(
                Convert.ToByte(text.Substring(1, 2), 16),
                Convert.ToByte(text.Substring(3, 2), 16),
                Convert.ToByte(text.Substring(5, 2), 16));

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>뒤쪽의 "안 고름" 을 떼어 낸다 - 파일이 null 로 끝없이 길어지지 않게.</summary>
    private void Trim()
    {
        while (_colors.Count > 0 && _colors[^1] is null) _colors.RemoveAt(_colors.Count - 1);
    }
}
