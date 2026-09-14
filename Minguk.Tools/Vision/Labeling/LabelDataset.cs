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
///   Images/        그림 (.png · .jpg)
///   Labels/        라벨 (.txt) - 그림과 이름이 같다
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

    /// <remarks>
    /// 폴더 이름은 대문자로 시작한다(<see cref="Vision.ProjectPaths.ImagesFolder"/>). Windows 는 대소문자를 안 가려 옛 <c>images</c> 도 그대로 열린다.
    /// YOLO 학습 설정에는 소문자 <c>images</c> 로 적는다 - Ultralytics 가 경로의 <c>\images\</c> 를 <c>\labels\</c> 로 바꿔 라벨을 찾는다(<c>도구\yolo-학습.py</c>).
    /// </remarks>
    public string ImageDirectory => Path.Combine(Root, Vision.ProjectPaths.ImagesFolder);

    public string LabelDirectory => Path.Combine(Root, Vision.ProjectPaths.LabelsFolder);

    public string ClassesPath => Path.Combine(Root, LabelClasses.FileName);

    /// <summary>앱이 기본으로 쓰는 자리.</summary>
    /// <remarks>
    /// 프로젝트 폴더 아래다(사용자 결정 2026-09-14) - 학습 환경(도구)과 내가 만든 것(사진·라벨)은 설정을 나눠 둔다.
    /// 옛 자리에 이미 담아 둔 것이 있으면 그것을 쓴다 - 사진 수백 장을 말없이 잃어버리면 안 된다.
    /// </remarks>
    public static string DefaultRoot
    {
        get
        {
            foreach (var legacy in new[]
                     {
                         Path.Combine(Training.TrainingPaths.Root, "Datasets", "몹"),
                         Path.Combine(Helper.UserDataPaths.Root, "Datasets", "몹")
                     })
            {
                if (Directory.Exists(legacy)) return legacy;
            }

            return Path.Combine(Vision.ProjectPaths.Datasets, "몹");
        }
    }

    /// <summary>
    /// 화면들이 <b>함께</b> 보는 데이터셋 자리. Automation 에서 프로젝트를 골랐으면 <b>그 프로젝트 폴더</b>다.
    /// </summary>
    /// <remarks>
    /// 캡처 모니터가 담고 라벨링이 찍는다. 둘이 각자 설정을 들면 사람이 라벨링에서만 폴더를
    /// 바꿔 놓고, 담은 그림이 왜 안 보이는지 한참 찾게 된다. 그래서 하나를 같이 본다.
    ///
    /// <b>솔루션을 쓰면서 기준이 고른 프로젝트로 바뀌었다</b>(2026-09-14). 예전에는 앱 전체 키 <c>Vision.DatasetRoot</c> 에 적었고
    /// Automation 화면이 프로젝트를 고를 때마다 그 키를 고쳐 쓰는 다리를 뒀다. 이제 읽을 때 고른 프로젝트를 보므로 다리가 없다 -
    /// 스크립트 자리·프레임 저장 자리와 같은 기준(<c>SolutionWorkspace.StartupDirectory</c>)이다.
    /// 솔루션이 없을 때만 옛 키(<see cref="LegacyRoot"/>)를 본다.
    /// </remarks>
    public static string ConfiguredRoot
        => global::Minguk.Tools.Projects.SolutionWorkspace.StartupDirectory ?? LegacyRoot;

    /// <summary>
    /// 솔루션을 쓰기 전의 데이터셋 자리(<c>Vision.DatasetRoot</c>, 없으면 기본 자리). 옛 자리를 옮기는 이주가 이것을 본다.
    /// </summary>
    public static string LegacyRoot
    {
        get
        {
            var saved = Minguk.Base.Utilities.AppSettingUtility.Get(RootSettingKey, string.Empty);

            return string.IsNullOrWhiteSpace(saved) ? DefaultRoot : saved;
        }
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

    /// <summary>몹 색(사람이 고른 것만). classes.txt 옆에 둔다 - 그 파일은 YOLO 형식이라 색을 못 넣는다.</summary>
    public string PalettePath => Path.Combine(Root, LabelPalette.FileName);

    public LabelPalette LoadPalette() => LabelPalette.Load(PalettePath);

    public void SavePalette(LabelPalette palette) => palette.Save(PalettePath);

    /// <summary>그 번호의 사각형이 든 라벨 파일 이름들. 비어 있으면 아무 데도 안 쓰인 몹이다.</summary>
    /// <remarks>그림이 없는 고아 라벨까지 본다 - 그림 목록으로 훑으면 놓친다.</remarks>
    public IReadOnlyList<string> FindLabelsUsing(int classId)
    {
        if (!Directory.Exists(LabelDirectory)) return [];

        return Directory.EnumerateFiles(LabelDirectory, "*" + LabelFile.Extension)
            .Where(path => LabelFile.Load(path).Any(box => box.ClassId == classId))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    /// <summary>
    /// 몹 하나를 지운다. 뒤 번호는 하나씩 당기고, 그 번호가 든 라벨 파일과 색도 같이 당긴다.
    /// </summary>
    /// <remarks>
    /// <b>아무 라벨에도 안 쓰인 몹만 지운다.</b> 쓰인 것을 지우면 찍어 둔 사각형을 잃거나(지우면) 다른 몹을 가리키게
    /// 된다(안 지우면). 부르기 전에 <see cref="FindLabelsUsing"/> 으로 보고, 쓰였으면 여기서 터진다.
    /// 시험으로 넣은 몹처럼 아직 아무것도 안 찍은 것은 지워도 잃는 것이 없다 - 그것 때문에 열었다(2026-09-14).
    /// </remarks>
    /// <returns>번호를 당겨 고쳐 쓴 라벨 파일 수.</returns>
    public int RemoveClass(LabelClasses classes, int classId)
    {
        var used = FindLabelsUsing(classId);

        if (used.Count > 0)
            throw new InvalidOperationException($"{classes.NameOf(classId)} ({classId}번) 은 라벨 {used.Count}장에 쓰여 못 지운다: {string.Join(", ", used.Take(3))}");

        var rewritten = 0;

        if (Directory.Exists(LabelDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(LabelDirectory, "*" + LabelFile.Extension).ToArray())
            {
                var boxes = LabelFile.Load(path);
                if (!boxes.Any(box => box.ClassId > classId)) continue;

                LabelFile.Save(path, boxes.Select(box => box.ClassId > classId ? box with { ClassId = box.ClassId - 1 } : box));
                rewritten++;
            }
        }

        classes.RemoveAt(classId);
        SaveClasses(classes);

        var palette = LoadPalette();
        palette.RemoveAt(classId);
        SavePalette(palette);

        return rewritten;
    }

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
