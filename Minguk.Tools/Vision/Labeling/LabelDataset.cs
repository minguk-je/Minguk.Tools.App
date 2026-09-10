using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Minguk.Tools.Vision.Labeling;

/// <summary>
/// 라벨을 붙일 그림 한 장.
/// </summary>
/// <param name="ImagePath">그림 파일.</param>
/// <param name="LabelPath">그 그림의 라벨 파일. 아직 없을 수 있다.</param>
public readonly record struct LabelItem(string ImagePath, string LabelPath)
{
    public string Name => Path.GetFileName(ImagePath);

    /// <summary>이미 한 번이라도 찍었는지. 목록에서 남은 일을 세는 데 쓴다.</summary>
    public bool HasLabel => File.Exists(LabelPath);
}

/// <summary>
/// 그림과 라벨을 담아 두는 폴더.
/// </summary>
/// <remarks>
/// <b>폴더 짜임</b>
/// <code>
/// &lt;데이터셋&gt;/
///   images/        그림 (.png · .jpg)
///   labels/        라벨 (.txt) - 그림과 이름이 같다
///   classes.txt    몹 이름 목록
/// </code>
///
/// <b>왜 그림과 라벨을 다른 폴더에 두는가</b>
///
/// 한 폴더에 섞어 두는 방식도 있지만, 그러면 그림만 골라 다른 데로 옮기거나 라벨만 지우는
/// 흔한 일에 매번 확장자로 걸러야 한다. 나누는 쪽이 널리 쓰는 관습이기도 하다.
/// 짝은 <b>확장자를 뺀 이름</b>으로 맞춘다 - <c>images/a.png</c> 의 라벨은 <c>labels/a.txt</c> 다.
///
/// <b>같은 이름에 확장자만 다른 그림은 두지 않는다</b>
///
/// <c>a.png</c> 와 <c>a.jpg</c> 가 같이 있으면 라벨 파일 하나를 둘이 나눠 갖게 된다.
/// 담을 때 이름을 시각으로 짓기 때문에 실제로 겹칠 일은 없지만, 남의 폴더를 열었을 때를 대비해
/// <see cref="FindDuplicateStems"/> 로 미리 알아볼 수 있게 해 둔다.
/// </remarks>
public sealed class LabelDataset
{
    /// <summary>여는 그림 확장자.</summary>
    /// <remarks>
    /// 캡처가 떨어뜨리는 것은 PNG 다. JPEG 도 여는 것은 남이 모아 둔 것을 그대로 쓰기 위해서다.
    /// </remarks>
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    public LabelDataset(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("데이터셋 폴더가 비었다.", nameof(root));

        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ImageDirectory => Path.Combine(Root, "images");

    public string LabelDirectory => Path.Combine(Root, "labels");

    public string ClassesPath => Path.Combine(Root, LabelClasses.FileName);

    /// <summary>앱이 기본으로 쓰는 자리.</summary>
    public static string DefaultRoot => Path.Combine(Helper.UserDataPaths.Root, "Datasets", "몹");

    /// <summary>
    /// 화면들이 <b>함께</b> 보는 데이터셋 자리.
    /// </summary>
    /// <remarks>
    /// 캡처 모니터가 담고 라벨링이 찍는다. 둘이 각자 설정을 들면 사람이 라벨링에서만 폴더를
    /// 바꿔 놓고, 담은 그림이 왜 안 보이는지 한참 찾게 된다. 화면별 키가 아니라
    /// 앱 전체 키 하나를 쓰는 이유다.
    /// </remarks>
    public static string ConfiguredRoot
    {
        get
        {
            var saved = Minguk.Base.Utilities.AppSettingUtility.Get(RootSettingKey, string.Empty);

            return string.IsNullOrWhiteSpace(saved) ? DefaultRoot : saved;
        }
        set => Minguk.Base.Utilities.AppSettingUtility.Set(RootSettingKey, value ?? string.Empty);
    }

    private const string RootSettingKey = "Vision.DatasetRoot";

    /// <summary>없는 폴더를 만든다. 이미 있으면 아무 일도 안 한다.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(ImageDirectory);
        Directory.CreateDirectory(LabelDirectory);
    }

    /// <summary>그림 하나에 대응하는 라벨 파일 자리. 파일이 있든 없든 자리는 정해져 있다.</summary>
    public string LabelPathFor(string imagePath)
        => Path.Combine(LabelDirectory, Path.GetFileNameWithoutExtension(imagePath) + LabelFile.Extension);

    /// <summary>
    /// 그림을 이름순으로 훑는다. 폴더가 없으면 빈 목록 - 아직 아무것도 안 담았다는 뜻이다.
    /// </summary>
    /// <remarks>
    /// 이름을 시각으로 짓기 때문에 이름순이 곧 담은 순서다. 찍다 만 자리로 돌아오기 쉽다.
    /// </remarks>
    public IReadOnlyList<LabelItem> EnumerateItems()
    {
        if (!Directory.Exists(ImageDirectory)) return [];

        return Directory.EnumerateFiles(ImageDirectory)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(path => new LabelItem(path, LabelPathFor(path)))
            .ToArray();
    }

    /// <summary>이름(확장자 뺀)이 겹치는 그림들. 겹치는 것이 없으면 빈 목록이다.</summary>
    public IReadOnlyList<string> FindDuplicateStems()
    {
        if (!Directory.Exists(ImageDirectory)) return [];

        return Directory.EnumerateFiles(ImageDirectory)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(stem => stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public LabelClasses LoadClasses() => LabelClasses.Load(ClassesPath);

    public void SaveClasses(LabelClasses classes) => classes.Save(ClassesPath);

    /// <summary>
    /// 담을 그림의 이름을 짓는다. 시각으로 지어 이름순이 곧 담은 순서가 되게 한다.
    /// </summary>
    /// <remarks>
    /// 1초에 여러 장을 담을 수 있어 밀리초까지 넣고, 그래도 겹치면 뒤에 번호를 붙인다.
    /// 겹친 채로 덮어쓰면 이미 찍어 둔 라벨이 다른 그림에 붙는다.
    /// </remarks>
    public string NextImagePath(DateTime now, string extension = ".png")
    {
        EnsureCreated();

        var stem = now.ToString("yyyyMMdd-HHmmss-fff");
        var path = Path.Combine(ImageDirectory, stem + extension);

        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(ImageDirectory, $"{stem}-{n}{extension}");

        return path;
    }
}
