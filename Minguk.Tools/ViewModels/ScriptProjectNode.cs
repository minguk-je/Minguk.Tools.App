using System;
using System.Windows.Media;

using DevExpress.Mvvm;

using Minguk.Image;
using Minguk.Tools.Input.Scripting.Projects;

namespace Minguk.Tools.ViewModels;

/// <summary>프로젝트 트리의 줄 종류.</summary>
public enum ScriptNodeKind
{
    Project,
    Folder,
    Source,
    Resource,
    Reference,

    /// <summary>물고 있는 프로젝트(공유 프로젝트). 아래에 그 프로젝트의 소스가 달린다.</summary>
    ProjectReference,

    /// <summary>솔루션 - 탐색기 뿌리. 아래에 솔루션의 프로젝트들이 달린다.</summary>
    Solution
}

/// <summary>
/// 프로젝트 트리의 한 줄. 그리드의 트리 보기(자기 참조 - <see cref="Id"/>·<see cref="ParentId"/>)가 이것을 그린다.
/// </summary>
/// <remarks>
/// <b>이름은 칸에서 바로 고친다</b>(VS 처럼 F2). 칸이 값을 바꾸면 <see cref="Rename"/> 이 디스크·목록을 옮기고, 실패하면 옛 이름으로 되돌린다 -
/// 그대로 두면 트리에는 새 이름이 보이는데 파일은 옛 이름이라 열리지 않는다.
/// </remarks>
public sealed class ScriptProjectNode : ViewModelBase
{
    private readonly Func<ScriptProjectNode, string, bool>? _rename;
    private string _name;

    public ScriptProjectNode(string id, string parentId, string name, ScriptNodeKind kind, Func<ScriptProjectNode, string, bool>? rename)
    {
        Id = id;
        ParentId = parentId;
        Kind = kind;
        _name = name;
        _rename = rename;
    }

    /// <summary>프로젝트 기준 상대 경로. 프로젝트 줄은 빈 문자열.</summary>
    public string Id { get; }

    /// <summary>부모 폴더의 <see cref="Id"/>. 프로젝트 줄은 자기를 가리키지 않게 "&lt;없음&gt;".</summary>
    public string ParentId { get; }

    public ScriptNodeKind Kind { get; }

    public bool IsFolder => Kind is ScriptNodeKind.Folder or ScriptNodeKind.Project or ScriptNodeKind.Solution;

    /// <summary>시작 파일. 굵게 그린다.</summary>
    public bool IsEntry
    {
        get => GetProperty(() => IsEntry);
        set => SetProperty(() => IsEntry, value);
    }

    /// <summary>
    /// 물고 있는 프로젝트의 파일이다(<see cref="Id"/> 가 전체 경로). 열어 고칠 수는 있지만 이름 바꾸기·삭제·제외·시작 파일은 안 된다 -
    /// 그 목록은 이 프로젝트 것이 아니라서, 여기서 고치면 그 프로젝트를 문 다른 프로젝트들이 영문 모르게 깨진다.
    /// </summary>
    public bool IsExternal { get; init; }

    /// <summary>목록에는 있는데 디스크에 없다. 흐리게 그린다.</summary>
    public bool IsMissing
    {
        get => GetProperty(() => IsMissing);
        set => SetProperty(() => IsMissing, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            var wanted = (value ?? string.Empty).Trim();
            if (wanted.Length == 0 || string.Equals(wanted, _name, StringComparison.Ordinal)) { RaisePropertyChanged(nameof(Name)); return; }

            if (_rename is not null && !_rename(this, wanted))
            {
                // 칸에는 이미 새 이름이 들어가 있다 - 되돌려 알린다.
                RaisePropertyChanged(nameof(Name));
                return;
            }

            _name = wanted;
            RaisePropertyChanged(nameof(Name));
        }
    }

    /// <summary>디스크의 전체 경로 - 파일 줄만(미리보기가 읽는다). 폴더·프로젝트 줄은 null.</summary>
    public string? FullPath { get; init; }

    /// <summary>그림 파일 줄 - 마우스를 올리면 미리보기를 띄운다(사용자, 2026-09-26 "이미지면 미리보기 보여줘").</summary>
    public bool IsImage => Kind == ScriptNodeKind.Resource && FullPath is not null && IsImageName(Name);

    private static bool IsImageName(string name) => System.IO.Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    /// <summary>미리보기 긴 변(px) - 1080p 캡처를 원본 크기로 풀면 한 장 8MB 라 줄여 푼다.</summary>
    private const int PreviewPixels = 480;

    private ImageSource? _preview;
    private string _previewCaption = string.Empty;
    private DateTime _previewStamp;

    /// <summary>그림 미리보기. 처음 볼 때 풀고, 파일이 바뀌었으면 다시 푼다. 못 읽으면 null.</summary>
    public ImageSource? Preview
    {
        get
        {
            LoadPreview();
            return _preview;
        }
    }

    /// <summary>미리보기 아래 줄 - 이름과 원본 크기.</summary>
    public string PreviewCaption
    {
        get
        {
            LoadPreview();
            return _previewCaption;
        }
    }

    private void LoadPreview()
    {
        if (!IsImage) return;

        try
        {
            var stamp = System.IO.File.GetLastWriteTimeUtc(FullPath!);

            if (_preview is not null && stamp == _previewStamp) return;

            int width, height;

            // 원본 크기만 먼저 읽는다(픽셀은 안 푼다) - 줄여 풀 비율을 정하고 캡션에 쓴다.
            using (var stream = System.IO.File.OpenRead(FullPath!))
            {
                var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                                                                              System.Windows.Media.Imaging.BitmapCacheOption.None).Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }

            // OnLoad 로 다 읽고 파일을 놓는다 - 붙들면 스크립트·캡처 저장이 그 파일을 못 덮는다.
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(FullPath!);

            if (width >= height && width > PreviewPixels) image.DecodePixelWidth = PreviewPixels;
            else if (height > width && height > PreviewPixels) image.DecodePixelHeight = PreviewPixels;

            image.EndInit();
            image.Freeze();

            _preview = image;
            _previewCaption = $"{Name}  ·  {width}×{height}";
            _previewStamp = stamp;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            _preview = null;
            _previewCaption = $"{Name}  ·  그림을 읽지 못했습니다";
        }
    }

    public ImageSource? Glyph => FreeImage.Instance?.CacheImageSource(Kind switch
    {
        ScriptNodeKind.Project or ScriptNodeKind.ProjectReference => "axialispureflat/development/16x16/project_csharp.png",
        ScriptNodeKind.Solution => "axialis/basic/16x16/folder_open.png",
        ScriptNodeKind.Folder => "axialis/basic/16x16/folder.png",
        ScriptNodeKind.Source => "axialispureflat/development/16x16/file_csharp.png",
        ScriptNodeKind.Reference => "axialispureflat/development/16x16/dll.png",
        _ => System.IO.Path.GetExtension(Name).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => "axialis/imaging/16x16/picture.png",
            ".wav" or ".mp3" => "axialis/multimedia/16x16/sound.png",
            _ => "axialis/basic/16x16/document_text.png"
        }
    });

    public static ScriptNodeKind KindOf(ScriptItemKind kind) => kind switch
    {
        ScriptItemKind.Source => ScriptNodeKind.Source,
        ScriptItemKind.Reference => ScriptNodeKind.Reference,
        ScriptItemKind.ProjectReference => ScriptNodeKind.ProjectReference,
        _ => ScriptNodeKind.Resource
    };
}
