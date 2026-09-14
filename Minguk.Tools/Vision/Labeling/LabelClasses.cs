using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Minguk.Tools.Vision.Labeling;

/// <summary>
/// 몹 이름 목록. 파일에서는 <c>classes.txt</c> 한 줄에 하나다.
/// </summary>
/// <remarks>
/// <b>번호가 아니라 자리가 뜻이다</b>
///
/// 라벨 파일에는 이름이 아니라 번호가 들어간다. 그래서 이 목록의 <b>순서를 바꾸면
/// 이미 찍어 둔 라벨이 전부 다른 몹을 가리킨다</b> - 중간에 하나를 지우면 뒤가 하나씩
/// 당겨져 조용히 어긋난다. 그래서 지우는 것은 열어 두지 않고, 이름 바꾸기와 뒤에 더하기만 연다.
/// (이름만 바꾸는 것은 번호가 그대로라 안전하다.)
///
/// 이름에 줄 바꿈이 들어가면 한 줄에 하나라는 규칙이 깨지므로 받을 때 막는다.
/// </remarks>
public sealed class LabelClasses
{
    public const string FileName = "classes.txt";

    private readonly List<string> _names = [];

    public LabelClasses()
    {
    }

    public LabelClasses(IEnumerable<string> names)
    {
        foreach (var name in names) Add(name);
    }

    public IReadOnlyList<string> Names => _names;

    public int Count => _names.Count;

    /// <summary>번호로 이름을 찾는다. 목록에 없는 번호면 그렇게 적어 돌려준다.</summary>
    /// <remarks>
    /// 없는 번호라고 터뜨리지 않는다. classes.txt 를 잃어버렸거나 남의 데이터셋을 열었을 때
    /// 화면이 아예 안 뜨는 것보다, "13번" 이라고 보이는 편이 무엇이 잘못됐는지 알기 쉽다.
    /// </remarks>
    public string NameOf(int classId)
        => classId >= 0 && classId < _names.Count ? _names[classId] : $"{classId}번";

    public int IndexOf(string name) => _names.FindIndex(n => string.Equals(n, name, StringComparison.Ordinal));

    /// <summary>맨 뒤에 더한다. 이미 있는 이름이면 그 자리를 돌려준다.</summary>
    public int Add(string name)
    {
        var trimmed = Normalize(name);

        if (trimmed.Length == 0) throw new ArgumentException("몹 이름이 비었다.", nameof(name));

        var existing = IndexOf(trimmed);
        if (existing >= 0) return existing;

        _names.Add(trimmed);

        return _names.Count - 1;
    }

    /// <summary>
    /// 이름만 바꾼다. 번호가 그대로라 찍어 둔 라벨은 안 흔들린다.
    /// </summary>
    public void Rename(int classId, string name)
    {
        if (classId < 0 || classId >= _names.Count)
            throw new ArgumentOutOfRangeException(nameof(classId), $"없는 번호다: {classId}");

        var trimmed = Normalize(name);

        if (trimmed.Length == 0) throw new ArgumentException("몹 이름이 비었다.", nameof(name));

        var existing = IndexOf(trimmed);
        if (existing >= 0 && existing != classId)
            throw new ArgumentException($"이미 있는 이름이다: {trimmed}", nameof(name));

        _names[classId] = trimmed;
    }

    /// <summary>
    /// 번호 하나를 빼고 뒤를 당긴다.
    /// </summary>
    /// <remarks>
    /// 라벨에는 번호가 들어 있어, 이것만 부르면 그 번호 뒤의 사각형이 전부 다른 몹을 가리킨다.
    /// 화면은 <see cref="LabelDataset.RemoveClass"/> 로 불러야 한다 - 그쪽이 라벨 파일의 번호도 같이 당긴다.
    /// </remarks>
    public void RemoveAt(int classId)
    {
        if (classId < 0 || classId >= _names.Count)
            throw new ArgumentOutOfRangeException(nameof(classId), $"없는 번호다: {classId}");

        _names.RemoveAt(classId);
    }

    public static LabelClasses Load(string path)
    {
        if (!File.Exists(path)) return new LabelClasses();

        // 빈 줄은 건너뛰되 번호는 밀지 않는다 - 파일 끝의 빈 줄 때문에 번호가 밀리면 안 된다.
        var names = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        return new LabelClasses(names);
    }

    /// <summary>UTF-8(BOM 없이)로 쓴다. 학습 쪽 도구들이 BOM 을 이름의 일부로 읽는 일이 있다.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllLines(path, _names, new UTF8Encoding(false));
    }

    private static string Normalize(string name)
        => name.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
