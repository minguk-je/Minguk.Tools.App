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
    Reference
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

    public bool IsFolder => Kind is ScriptNodeKind.Folder or ScriptNodeKind.Project;

    /// <summary>시작 파일. 굵게 그린다.</summary>
    public bool IsEntry
    {
        get => GetProperty(() => IsEntry);
        set => SetProperty(() => IsEntry, value);
    }

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

    public ImageSource? Glyph => FreeImage.Instance?.CacheImageSource(Kind switch
    {
        ScriptNodeKind.Project => "axialispureflat/development/16x16/project_csharp.png",
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
        _ => ScriptNodeKind.Resource
    };
}
