using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Minguk.Tools.Vision.Regions;

/// <summary>
/// 이름 붙은 자리들을 데이터셋 폴더의 <c>regions.json</c> 에 담아 둔다.
/// </summary>
/// <remarks>
/// <b>왜 데이터셋 폴더인가</b> - 자리는 그 게임 화면에 매인 것이라, 그 게임의 사진·라벨·모델이 있는 곳에 같이
/// 있어야 한다. 다른 게임을 하면 데이터셋을 바꾸는데 그때 자리도 같이 바뀐다. 노트북으로 옮길 때도 데이터셋
/// 폴더만 복사하면 따라간다. 앱 설정에 두면 게임을 바꿀 때마다 덮어써야 한다.
///
/// <b>이름은 대소문자를 안 가린다.</b> 스크립트에서 <c>숫자읽기("탄약")</c> 이라고 쓸 때 화면에 적어 둔 것과
/// 글자 하나가 달라 못 찾으면, 사람은 자리가 틀린 줄 안다.
///
/// 읽다 터지지 않는다 - 파일이 망가져도 빈 목록으로 가고 로그에 남긴다. 자리 하나 때문에 화면이 안 뜨는 것이
/// 더 나쁘다.
/// </remarks>
public sealed class RegionBook
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public const string FileName = "regions.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,

        // 한글 이름을 \uXXXX 로 흘려 적지 않는다. 사람이 열어 볼 파일이다.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly List<NamedRegion> _regions = [];

    public RegionBook(string folder)
    {
        Folder = folder;
        Path = System.IO.Path.Combine(folder, FileName);
    }

    public string Folder { get; }

    public string Path { get; }

    public IReadOnlyList<NamedRegion> Regions => _regions;

    /// <summary>이름으로 찾는다. 없으면 null.</summary>
    public NamedRegion? Find(string name)
        => _regions.FirstOrDefault(r => string.Equals(r.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 스크립트가 부른 이름을 자리와 칸으로 푼다. <c>"탄약"</c> 은 (자리, null), <c>"탄약.현재"</c> 는 (자리, 칸). 없으면 null.
    /// </summary>
    /// <remarks>이름에 점(.)을 못 쓰게 막아 두었으므로(<see cref="IsValidName"/>) 첫 점에서 자르면 된다.</remarks>
    public (NamedRegion Region, RegionCell? Cell)? Resolve(string name)
    {
        var text = name?.Trim() ?? string.Empty;
        var dot = text.IndexOf('.');

        if (dot < 0) return Find(text) is { } whole ? (whole, null) : null;

        if (Find(text[..dot]) is not { } region) return null;

        return region.FindCell(text[(dot + 1)..]) is { } cell ? (region, cell) : null;
    }

    /// <summary>자리·칸 이름으로 쓸 수 있는가. 비지 않고 점(.)이 없어야 한다 - 스크립트가 <c>자리.칸</c> 으로 가른다.</summary>
    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && !name.Contains('.');

    /// <summary>
    /// 자리를 넣거나 고친다. <b>같은 이름이면 덮어쓴다</b> - 이름이 겹치면 스크립트가 어느 쪽을 볼지 알 수 없다.
    /// </summary>
    public void Put(NamedRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);

        region.Name = region.Name.Trim();

        if (region.Name.Length == 0) throw new ArgumentException("이름이 없는 영역은 넣을 수 없습니다.", nameof(region));

        var existing = Find(region.Name);

        if (existing is not null) _regions.Remove(existing);

        _regions.Add(region);
    }

    public bool Remove(string name)
    {
        var found = Find(name);

        return found is not null && _regions.Remove(found);
    }

    /// <summary>파일에서 읽는다. 없거나 망가졌으면 빈 목록.</summary>
    public static RegionBook Load(string folder)
    {
        var book = new RegionBook(folder);

        try
        {
            if (!File.Exists(book.Path)) return book;

            var loaded = JsonSerializer.Deserialize<List<NamedRegion>>(File.ReadAllText(book.Path));

            if (loaded is not null)
                foreach (var region in loaded.Where(r => r.IsUsable))
                {
                    region.EnsureCells();
                    book.Put(region);
                }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"영역 목록을 못 읽었다: {book.Path}");
        }

        return book;
    }

    /// <summary>파일에 쓴다. BOM 없이 - 다른 도구가 읽을 수 있게.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path, JsonSerializer.Serialize(_regions, Options), new UTF8Encoding(false));
    }
}
