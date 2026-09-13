using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using DevExpress.Mvvm;

using Minguk.Base.Extension;
using Minguk.Base.Utilities;
using Minguk.Tools.Input.Scripting;
using Minguk.Tools.Input.Scripting.Projects;

namespace Minguk.Tools.ViewModels;

/// <summary>작업 공간이 바깥에서 빌려 쓰는 것들. 화면이 제 서비스를 빌려 준다 - 하네스는 가짜를 꽂는다.</summary>
public sealed class ScriptProjectWorkspaceHost
{
    public required Action<Action> OnUi { get; init; }

    /// <summary>예·아니요·취소를 묻는다. 없으면(하네스) <see cref="MessageResult.Yes"/> 로 본다.</summary>
    public Func<string, MessageButton, MessageResult>? Ask { get; init; }

    public Func<IOpenFileDialogService?>? OpenDialog { get; init; }

    public Func<ISaveFileDialogService?>? SaveDialog { get; init; }

    /// <summary>상태 줄·아래 바에 한 줄 적는다.</summary>
    public Action<string>? Notify { get; init; }

    /// <summary>Delete 가 파일을 보내는 곳. 없으면 셸 휴지통.</summary>
    public Helper.IFileRecycler? Recycler { get; init; }
}

/// <summary>
/// 열린 스크립트 프로젝트 - VS 의 솔루션 탐색기와 문서 탭 뒤에 있는 것.
/// </summary>
/// <remarks>
/// <b>화면과 떼어 둔다.</b> 트리 줄(<see cref="Nodes"/>)·탭(<see cref="Documents"/>)·명령이 다 여기 있고 화면은 묶기만 한다.
/// 그래서 파일 추가·이름 바꾸기·저장 안 한 탭 닫기를 하네스가 마우스 없이 본다.
///
/// 동작은 VS 를 따른다.
///   - 새 파일·새 폴더는 이름을 먼저 묻지 않고 만들어 넣은 뒤 그 줄의 이름 칸을 연다(<see cref="EditNodeRequested"/>).
///   - 삭제(Delete)는 VS 처럼 파일을 지우되 <b>휴지통으로</b> 보낸다(묻고 나서). 파일을 남기려면 <b>프로젝트에서 제외</b>.
///   - 저장 안 한 탭을 닫거나 프로젝트를 닫으면 저장할지 묻는다(예·아니요·취소).
///   - 더블 클릭: 글 파일은 탭으로, 그림·소리·DLL 은 윈도우 기본 프로그램으로.
/// </remarks>
public sealed class ScriptProjectWorkspace : ViewModelBase, IDisposable
{
    public const string RootId = "";
    private const string NoParent = "<없음>";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csx", ".cs", ".json", ".txt", ".csv", ".xml", ".md", ".ini", ".yaml", ".yml", ".py", ".js"
    };

    private readonly ScriptProjectWorkspaceHost _host;

    public ScriptProjectWorkspace(ScriptProjectWorkspaceHost host)
    {
        _host = host;

        NewProjectCommand = new DelegateCommand(DoNewProject, false);
        OpenProjectCommand = new DelegateCommand(DoOpenProject, false);
        CloseProjectCommand = new DelegateCommand(() => CloseProject(), () => IsOpen, false);
        SaveCommand = new DelegateCommand(() => ActiveDocument?.Save(), () => ActiveDocument is not null, false);
        SaveAllCommand = new DelegateCommand(SaveAll, () => Documents.Count > 0, false);
        AddNewFileCommand = new DelegateCommand(() => AddNewFile(TargetFolderId()), () => IsOpen, false);
        AddFolderCommand = new DelegateCommand(() => AddFolder(TargetFolderId()), () => IsOpen, false);
        AddExistingCommand = new DelegateCommand(DoAddExisting, () => IsOpen, false);
        RenameCommand = new DelegateCommand(() => { if (SelectedNode is { } node) EditNodeRequested?.Invoke(this, node); }, () => SelectedNode is { Kind: not ScriptNodeKind.Project }, false);
        DeleteCommand = new DelegateCommand(() => { if (SelectedNode is { } node) Delete(node); }, () => SelectedNode is { Kind: not ScriptNodeKind.Project }, false);
        ExcludeCommand = new DelegateCommand(() => { if (SelectedNode is { } node) Exclude(node); }, () => SelectedNode is { Kind: not ScriptNodeKind.Project }, false);
        SetEntryCommand = new DelegateCommand(() => { if (SelectedNode is { } node) SetEntry(node); }, () => SelectedNode is { Kind: ScriptNodeKind.Source }, false);
        OpenNodeCommand = new DelegateCommand(() => { if (SelectedNode is { } node) Open(node); }, () => SelectedNode is not null, false);
        CloseDocumentCommand = new DelegateCommand<ScriptDocument?>(doc => CloseDocument(doc ?? ActiveDocument), _ => ActiveDocument is not null, false);
        OpenFolderInExplorerCommand = new DelegateCommand(DoOpenFolderInExplorer, () => IsOpen, false);
    }

    // ── 알림 ─────────────────────────────────────────────────────────────

    /// <summary>글이나 목록이 바뀌었다. 워크벤치가 다시 검사한다.</summary>
    public event EventHandler? Changed;

    /// <summary>이 줄의 이름 칸을 열어 달라(VS 의 새 파일·F2). 화면이 트리 편집을 연다.</summary>
    public event EventHandler<ScriptProjectNode>? EditNodeRequested;

    /// <summary>프로젝트를 열거나 닫았다.</summary>
    public event EventHandler? ProjectChanged;

    // ── 상태 ─────────────────────────────────────────────────────────────

    public ScriptProject? Project
    {
        get => GetProperty(() => Project);
        private set => SetProperty(() => Project, value, () =>
        {
            RaisePropertyChanged(nameof(IsOpen));
            RaisePropertyChanged(nameof(Title));
            RaiseCommands();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public bool IsOpen => Project is not null;

    /// <summary>솔루션 탐색기 머리. VS 의 "솔루션 '이름'".</summary>
    public string Title => Project is null ? "열린 프로젝트 없음" : $"프로젝트 '{Project.Name}'";

    public ObservableCollection<ScriptProjectNode> Nodes { get; } = [];

    public ScriptProjectNode? SelectedNode
    {
        get => GetProperty(() => SelectedNode);
        set => SetProperty(() => SelectedNode, value, RaiseCommands);
    }

    public ObservableCollection<ScriptDocument> Documents { get; } = [];

    /// <summary>탭 편집기가 묶을 공용 설정(워크벤치). 새 문서에 넘긴다.</summary>
    public object? EditorSettings { get; set; }

    public ScriptDocument? ActiveDocument
    {
        get => GetProperty(() => ActiveDocument);
        set => SetProperty(() => ActiveDocument, value, RaiseCommands);
    }

    // ── 명령 ─────────────────────────────────────────────────────────────

    public DelegateCommand NewProjectCommand { get; }
    public DelegateCommand OpenProjectCommand { get; }
    public DelegateCommand CloseProjectCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand SaveAllCommand { get; }
    public DelegateCommand AddNewFileCommand { get; }
    public DelegateCommand AddFolderCommand { get; }
    public DelegateCommand AddExistingCommand { get; }
    public DelegateCommand RenameCommand { get; }
    public DelegateCommand DeleteCommand { get; }
    public DelegateCommand ExcludeCommand { get; }
    public DelegateCommand SetEntryCommand { get; }
    public DelegateCommand OpenNodeCommand { get; }
    public DelegateCommand<ScriptDocument?> CloseDocumentCommand { get; }
    public DelegateCommand OpenFolderInExplorerCommand { get; }

    private void RaiseCommands()
    {
        foreach (var command in new DelegateCommand[] { CloseProjectCommand, SaveCommand, SaveAllCommand, AddNewFileCommand, AddFolderCommand, AddExistingCommand, RenameCommand, DeleteCommand, ExcludeCommand, SetEntryCommand, OpenNodeCommand, OpenFolderInExplorerCommand })
            command.RaiseCanExecuteChanged();

        CloseDocumentCommand.RaiseCanExecuteChanged();
    }

    // ── 프로젝트 열기·만들기·닫기 ─────────────────────────────────────────

    /// <summary>
    /// 새 프로젝트. <paramref name="projectFilePath"/> 가 <c>...\사격장.mtsproj</c> 면 <c>...\사격장\사격장.mtsproj</c> 로 만든다 - VS 처럼 프로젝트마다 폴더.
    /// </summary>
    public ScriptProject CreateProject(string projectFilePath, string entrySource)
    {
        if (!CloseProject()) throw new OperationCanceledException();

        var name = Path.GetFileNameWithoutExtension(projectFilePath);
        var parent = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;

        // 사람이 이미 그 이름의 폴더 안을 골랐으면 한 겹 더 만들지 않는다.
        var folder = string.Equals(Path.GetFileName(parent), name, StringComparison.OrdinalIgnoreCase) ? parent : Path.Combine(parent, name);

        var project = ScriptProject.Create(folder, name, entrySource);
        Attach(project);

        if (Nodes.FirstOrDefault(n => n.Id == project.Entry) is { } entry) Open(entry);

        _host.Notify?.Invoke($"프로젝트 '{name}' 을(를) 만들었습니다 - {folder}");
        return project;
    }

    public bool OpenProject(string projectFilePath)
    {
        if (!CloseProject()) return false;

        var project = ScriptProject.Load(projectFilePath);
        Attach(project);

        if (Nodes.FirstOrDefault(n => n.Id == project.Entry) is { } entry) Open(entry);

        _host.Notify?.Invoke($"프로젝트 '{project.Name}' 을(를) 열었습니다.");
        return true;
    }

    /// <summary>닫는다. 저장 안 한 탭이 있으면 묻는다. 취소하면 false.</summary>
    public bool CloseProject()
    {
        if (Project is null) return true;

        var dirty = Documents.Where(d => d.IsDirty).ToList();

        if (dirty.Count > 0)
        {
            var answer = Ask($"다음 파일의 변경 내용을 저장하시겠습니까?\n\n{string.Join("\n", dirty.Select(d => Path.GetFileName(d.FilePath)))}", MessageButton.YesNoCancel);

            if (answer == MessageResult.Cancel) return false;
            if (answer == MessageResult.Yes) foreach (var doc in dirty) doc.Save();
        }

        StopWatchingFolder();

        foreach (var doc in Documents) Detach(doc);

        Documents.Clear();
        ActiveDocument = null;
        Nodes.Clear();
        SelectedNode = null;
        Project = null;

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Attach(ScriptProject project)
    {
        Project = project;
        RebuildNodes();
        WatchFolder(project);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ── 폴더 감시 ────────────────────────────────────────────────────────

    private FileSystemWatcher? _folderWatcher;
    private System.Threading.Timer? _folderDebounce;
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingGate = new();

    /// <summary>폴더 알림을 묶는 시간. 탐색기로 파일 수십 개를 붙여 넣으면 알림이 쏟아진다 - 한 번에 갱신한다.</summary>
    private const int FolderDebounceMs = 500;

    /// <summary>
    /// 프로젝트 폴더를 하위 폴더까지 지켜보다 파일·폴더가 생기거나 없어지면 목록을 고친다(사용자 요청 2026-09-13).
    /// </summary>
    /// <remarks>
    /// VS 는 목록에 없는 파일을 안 보이지만 여기서는 폴더에 넣은 것이 곧바로 탐색기에 뜨길 바랐다. 그래서 <b>새로 생긴 것</b>은 넣고
    /// <b>없어진 것</b>은 뺀다. 이미 있던 파일을 "프로젝트에서 제외" 한 것은 다시 넣지 않는다 - 알림은 생길 때만 오기 때문이다.
    /// 우리가 만든 파일(새 파일·끌어다 놓기·이름 바꾸기)은 목록을 먼저 고치므로 알림이 와도 할 일이 없다.
    /// 무시하는 것: 프로젝트 파일, .git·.vs·bin·obj 폴더, 임시 파일(~ 로 시작, .tmp, .swp).
    /// </remarks>
    private void WatchFolder(ScriptProject project)
    {
        StopWatchingFolder();

        try
        {
            _folderWatcher = new FileSystemWatcher(project.Directory)
            {
                IncludeSubdirectories = true,
                // 내용 변경(LastWrite)도 받는다 - 다른 화면(스크립트 화면)이 도우미 파일을 고쳐 저장하거나 프로젝트에 파일을 넣으면
                // 같은 프로젝트를 연 이 화면(플레이)도 다시 검사하고 목록을 다시 읽어야 한다.
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024
            };

            // 폴더에 오는 "바뀜" 은 버린다 - 안의 것이 생기거나 지워질 때마다 부모 폴더(뿌리까지)에 온다. 뿌리 알림은 전체 다시 훑기를
            // 부르는데, 폴더를 지우는 도중이면 아직 남은 폴더를 다시 목록에 넣었다(실측: 지운 폴더가 안 빠짐). 파일 것만 받는다.
            _folderWatcher.Changed += (_, e) => { if (!Directory.Exists(e.FullPath)) QueueFolderChange(e.FullPath); };
            _folderWatcher.Created += (_, e) => QueueFolderChange(e.FullPath);
            _folderWatcher.Deleted += (_, e) => QueueFolderChange(e.FullPath);
            _folderWatcher.Renamed += (_, e) => { QueueFolderChange(e.OldFullPath); QueueFolderChange(e.FullPath); };
            // 알림이 넘치면(버퍼 초과) 어느 것이 바뀌었는지 모른다 - 폴더 전체를 다시 훑는다.
            _folderWatcher.Error += (_, _) => QueueFolderChange(project.Directory);
            _folderWatcher.EnableRaisingEvents = true;

            _folderDebounce = new System.Threading.Timer(_ => _host.OnUi(ApplyFolderChanges), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"프로젝트 폴더를 지켜보지 못했다: {project.Directory}");
        }
    }

    private void StopWatchingFolder()
    {
        _folderWatcher?.Dispose();
        _folderWatcher = null;
        _folderDebounce?.Dispose();
        _folderDebounce = null;

        lock (_pendingGate) _pendingPaths.Clear();
    }

    private void QueueFolderChange(string path)
    {
        lock (_pendingGate) _pendingPaths.Add(path);

        _folderDebounce?.Change(FolderDebounceMs, System.Threading.Timeout.Infinite);
    }

    /// <summary>모인 알림을 목록에 반영한다. UI 스레드.</summary>
    public void ApplyFolderChanges()
    {
        if (Project is not { } project) return;

        string[] paths;
        lock (_pendingGate)
        {
            paths = [.. _pendingPaths];
            _pendingPaths.Clear();
        }

        var changed = false;
        var contentChanged = false;

        foreach (var path in paths)
        {
            if (string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), project.Directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                changed |= ScanFolder(project);
                continue;
            }

            // 프로젝트 파일을 밖(다른 화면)에서 고쳤다 - 목록을 다시 읽는다. 우리가 저장해서 온 알림이면 같아서 할 일이 없다.
            if (string.Equals(Path.GetFullPath(path), project.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                changed |= ReloadProjectFile(project);
                continue;
            }

            // 목록에 있는 파일의 내용이 바뀌었다 - 목록은 그대로, 검사만 다시.
            if (File.Exists(path) && project.RelativePath(path) is { } listed && project.Find(listed) is not null)
            {
                contentChanged = true;
                continue;
            }

            if (project.RelativePath(path) is not { } relative || IsIgnored(relative)) continue;

            if (Directory.Exists(path))
            {
                if (!project.Folders.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    project.AddFolder(relative);
                    changed = true;
                }

                // 폴더째 붙여 넣으면 안의 파일은 알림이 따로 안 올 수 있다 - 안을 훑는다.
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    if (project.RelativePath(file) is { } inner && !IsIgnored(inner) && project.Find(inner) is null) { project.Add(inner); changed = true; }

                foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    if (project.RelativePath(directory) is { } inner && !IsIgnored(inner) && !project.Folders.Contains(inner, StringComparer.OrdinalIgnoreCase)) { project.AddFolder(inner); changed = true; }
            }
            else if (File.Exists(path))
            {
                if (project.Find(relative) is null)
                {
                    project.Add(relative);
                    changed = true;
                }
            }
            else
            {
                // 없어졌다. 폴더였으면 안의 항목까지, 파일이면 그것만.
                if (project.Folders.Contains(relative, StringComparer.OrdinalIgnoreCase))
                {
                    project.RemoveFolder(relative);
                    changed = true;
                }
                else if (project.Find(relative) is not null)
                {
                    project.Remove(relative);
                    changed = true;
                }

                CloseMissingDocuments();
            }
        }

        if (!changed)
        {
            if (contentChanged) Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        project.Save();
        RebuildNodes();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 디스크의 프로젝트 파일이 지금 들고 있는 목록과 다르면 그것으로 갈아 끼운다. 다르면 true.
    /// </summary>
    /// <remarks>
    /// 같은 프로젝트를 두 화면(스크립트·플레이)이 열어 둔다. 한쪽이 파일을 넣고 저장하면 다른 쪽은 옛 목록으로 돌아 새 파일의 함수가
    /// "없다" 가 된다. 저장한 쪽의 알림도 여기로 오지만 그때는 목록이 같아 아무 일도 없다.
    /// </remarks>
    private bool ReloadProjectFile(ScriptProject project)
    {
        ScriptProject disk;

        try
        {
            disk = ScriptProject.Load(project.FilePath);
        }
        catch (Exception ex)
        {
            // 쓰는 도중에 읽었을 수 있다 - 다음 알림에 다시 본다.
            Logger.Debug(ex, "프로젝트 파일을 다시 읽지 못했다");
            return false;
        }

        return project.ReplaceListWith(disk);
    }

    /// <summary>알림이 넘쳤을 때 - 디스크와 목록을 통째로 견준다(없어진 것은 빼고 새것은 넣는다).</summary>
    private bool ScanFolder(ScriptProject project)
    {
        var changed = false;

        foreach (var item in project.Items.ToList())
            if (!File.Exists(project.FullPath(item.Path))) { project.Remove(item.Path); changed = true; }

        foreach (var folder in project.Folders.ToList())
            if (!Directory.Exists(project.FullPath(folder))) { project.RemoveFolder(folder); changed = true; }

        foreach (var directory in Directory.EnumerateDirectories(project.Directory, "*", SearchOption.AllDirectories))
            if (project.RelativePath(directory) is { } relative && !IsIgnored(relative) && !project.Folders.Contains(relative, StringComparer.OrdinalIgnoreCase)) { project.AddFolder(relative); changed = true; }

        foreach (var file in Directory.EnumerateFiles(project.Directory, "*", SearchOption.AllDirectories))
            if (project.RelativePath(file) is { } relative && !IsIgnored(relative) && project.Find(relative) is null) { project.Add(relative); changed = true; }

        CloseMissingDocuments();
        return changed;
    }

    /// <summary>밖에서 지운 파일의 탭. 고친 것이 없으면 닫고, 있으면 남겨 두고 알린다 - 저장하면 되살릴 수 있다.</summary>
    private void CloseMissingDocuments()
    {
        foreach (var doc in Documents.Where(d => !File.Exists(d.FilePath)).ToList())
        {
            if (doc.IsDirty)
            {
                _host.Notify?.Invoke($"{Path.GetFileName(doc.FilePath)} 이(가) 밖에서 지워졌습니다. 탭에 고친 글이 남아 있어 닫지 않았습니다 - 저장하면 되살아납니다.");
                continue;
            }

            var index = Documents.IndexOf(doc);
            Detach(doc);
            Documents.Remove(doc);

            if (ReferenceEquals(ActiveDocument, doc))
                ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Clamp(index - 1, 0, Documents.Count - 1)];
        }
    }

    private bool IsIgnored(string relative)
    {
        if (string.Equals(Path.GetExtension(relative), ScriptProject.Extension, StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var part in relative.Split('/'))
        {
            if (part is ".git" or ".vs" or "bin" or "obj") return true;
        }

        var name = Path.GetFileName(relative);

        return name.StartsWith('~') || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".swp", StringComparison.OrdinalIgnoreCase);
    }

    private void DoNewProject() => Try(() =>
    {
        if (_host.SaveDialog?.Invoke() is not { } dialog) return;

        dialog.Filter = $"스크립트 프로젝트 (*{ScriptProject.Extension})|*{ScriptProject.Extension}";
        dialog.DefaultExt = ScriptProject.Extension.TrimStart('.');
        dialog.DefaultFileName = "새 프로젝트" + ScriptProject.Extension;
        dialog.InitialDirectory = ScriptFiles.DefaultDirectory;

        if (!dialog.ShowDialog()) return;

        CreateProject(dialog.File.GetFullName(), DefaultEntrySource);
    });

    private void DoOpenProject() => Try(() =>
    {
        if (_host.OpenDialog?.Invoke() is not { } dialog) return;

        dialog.Filter = $"스크립트 프로젝트 (*{ScriptProject.Extension})|*{ScriptProject.Extension}";
        dialog.InitialDirectory = ScriptFiles.DefaultDirectory;

        if (!dialog.ShowDialog()) return;

        OpenProject(dialog.File.GetFullName());
    });

    /// <summary>새 프로젝트의 main.csx.</summary>
    public const string DefaultEntrySource = "// 시작 파일입니다. 같은 프로젝트의 다른 .csx 에 만든 함수를 그대로 부를 수 있습니다.\n// 리소스: 리소스글(\"설정.json\") · 리소스경로(\"적.png\") · 소리(\"알림.wav\")\n\n출력(\"안녕하세요\");\n";

    // ── 트리 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 목록으로 트리 줄을 다시 만든다. 고른 줄은 같은 경로면 다시 고른다.
    /// </summary>
    /// <remarks>폴더가 먼저, 그다음 파일 - 각각 이름 순. VS 의 솔루션 탐색기와 같다.</remarks>
    public void RebuildNodes()
    {
        var selected = SelectedNode?.Id;
        Nodes.Clear();

        if (Project is not { } project) return;

        Nodes.Add(new ScriptProjectNode(RootId, NoParent, project.Name, ScriptNodeKind.Project, null));

        foreach (var folder in project.Folders.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            Nodes.Add(new ScriptProjectNode(folder, ParentOf(folder), Path.GetFileName(folder), ScriptNodeKind.Folder, TryRename)
            {
                IsMissing = !Directory.Exists(project.FullPath(folder))
            });

        foreach (var item in project.Items.OrderBy(i => Path.GetFileName(i.Path), StringComparer.CurrentCultureIgnoreCase))
            Nodes.Add(new ScriptProjectNode(item.Path, ParentOf(item.Path), Path.GetFileName(item.Path), ScriptProjectNode.KindOf(item.Kind), TryRename)
            {
                IsEntry = string.Equals(item.Path, project.Entry, StringComparison.OrdinalIgnoreCase),
                IsMissing = !File.Exists(project.FullPath(item.Path))
            });

        SelectedNode = Nodes.FirstOrDefault(n => n.Id == selected) ?? Nodes.FirstOrDefault();
    }

    private static string ParentOf(string relative)
    {
        var index = relative.LastIndexOf('/');
        return index < 0 ? RootId : relative[..index];
    }

    /// <summary>새 항목이 들어갈 폴더 - 고른 줄이 폴더면 그것, 파일이면 그 부모, 없으면 뿌리.</summary>
    public string TargetFolderId() => SelectedNode switch
    {
        null => RootId,
        { IsFolder: true } node => node.Id,
        { } node => node.ParentId
    };

    // ── 파일·폴더 ────────────────────────────────────────────────────────

    /// <summary>새 C# 파일. 겹치지 않는 이름을 붙여 만들고 열고, 이름 칸을 연다.</summary>
    public ScriptProjectNode? AddNewFile(string folderId, string baseName = "새 스크립트")
    {
        if (Project is not { } project) return null;

        var relative = UniqueName(folderId, baseName, ScriptFiles.Extension(ScriptLanguage.CSharp));
        ScriptProject.WriteText(project.FullPath(relative), "// 함수·클래스를 여기에 둡니다. 시작 파일에서 그대로 부릅니다.\n");

        project.Add(relative, ScriptItemKind.Source);
        return Commit(relative, open: true, edit: true);
    }

    public ScriptProjectNode? AddFolder(string parentId, string baseName = "새 폴더")
    {
        if (Project is not { } project) return null;

        var relative = UniqueName(parentId, baseName, string.Empty);
        Directory.CreateDirectory(project.FullPath(relative));

        project.AddFolder(relative);
        return Commit(relative, open: false, edit: true);
    }

    /// <summary>
    /// 기존 파일을 넣는다. 프로젝트 밖의 것은 그 폴더로 <b>복사</b>한다 - VS 의 "기존 항목 추가" 와 같다.
    /// </summary>
    /// <remarks>원본을 옮기지 않는다. 사람이 다른 곳에서 쓰던 파일이 갑자기 사라지면 안 된다.</remarks>
    public IReadOnlyList<ScriptProjectNode> AddExisting(IEnumerable<string> paths, string folderId)
    {
        if (Project is not { } project) return [];

        var added = new List<string>();

        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            var relative = project.RelativePath(full);

            if (relative is null)
            {
                relative = UniqueName(folderId, Path.GetFileNameWithoutExtension(full), Path.GetExtension(full));
                Directory.CreateDirectory(Path.GetDirectoryName(project.FullPath(relative))!);
                File.Copy(full, project.FullPath(relative));
            }

            project.Add(relative);
            added.Add(relative);
        }

        project.Save();
        RebuildNodes();
        Changed?.Invoke(this, EventArgs.Empty);

        return [.. Nodes.Where(n => added.Contains(n.Id, StringComparer.OrdinalIgnoreCase))];
    }

    private void DoAddExisting() => Try(() =>
    {
        if (_host.OpenDialog?.Invoke() is not { } dialog) return;

        dialog.Filter = "모든 파일 (*.*)|*.*|C# 스크립트 (*.csx)|*.csx|그림 (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|데이터 (*.json;*.csv;*.txt)|*.json;*.csv;*.txt|소리 (*.wav)|*.wav|참조 (*.dll)|*.dll";
        dialog.Multiselect = true;
        dialog.InitialDirectory = Project?.Directory;

        if (!dialog.ShowDialog()) return;

        var nodes = AddExisting(dialog.Files.Select(f => f.GetFullName()), TargetFolderId());
        _host.Notify?.Invoke($"{nodes.Count}개를 프로젝트에 넣었습니다.");
    });

    /// <summary>
    /// 줄의 이름을 바꾼다. 파일·폴더를 디스크에서 옮기고, 열린 탭의 경로도 따라간다.
    /// </summary>
    /// <returns>못 바꿨으면 false - 칸이 옛 이름으로 돌아간다.</returns>
    public bool TryRename(ScriptProjectNode node, string newName)
    {
        if (Project is not { } project || node.Kind == ScriptNodeKind.Project) return false;

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _host.Notify?.Invoke($"이름에 쓸 수 없는 글자가 있습니다: {newName}");
            return false;
        }

        // 확장자를 지우면 붙여 준다 - VS 는 묻지만, 여기서 .csx 가 빠지면 소스로 안 잡혀 조용히 컴파일에서 빠진다.
        if (!node.IsFolder && Path.GetExtension(newName).Length == 0) newName += Path.GetExtension(node.Id);

        var parent = node.ParentId;
        var target = parent.Length == 0 ? newName : parent + "/" + newName;

        if (!string.Equals(target, node.Id, StringComparison.OrdinalIgnoreCase) &&
            (File.Exists(project.FullPath(target)) || Directory.Exists(project.FullPath(target))))
        {
            _host.Notify?.Invoke($"같은 이름이 이미 있습니다: {newName}");
            return false;
        }

        try
        {
            var before = project.FullPath(node.Id);

            project.Move(node.Id, target);
            project.Save();

            // 열린 탭이 옛 경로를 들고 있으면 저장할 때 옛 자리에 파일을 다시 만든다.
            foreach (var doc in Documents)
            {
                if (string.Equals(doc.FilePath, before, StringComparison.OrdinalIgnoreCase))
                    doc.MovedTo(project.FullPath(target));
                else if (node.IsFolder && doc.FilePath.StartsWith(before + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    doc.MovedTo(project.FullPath(target) + doc.FilePath[before.Length..]);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"이름을 못 바꿨다: {node.Id} → {target}");
            _host.Notify?.Invoke($"이름을 바꾸지 못했습니다: {ex.Message}");
            return false;
        }

        // 트리는 이 호출이 끝난 뒤에 다시 만든다 - 칸이 값을 넣는 도중에 줄을 갈아 끼우면 그리드가 옛 줄에 값을 쓴다.
        _host.OnUi(() => { RebuildNodes(); SelectedNode = Nodes.FirstOrDefault(n => n.Id == target); Changed?.Invoke(this, EventArgs.Empty); });
        return true;
    }

    /// <summary>프로젝트에서 제외 - 목록에서만 뺀다(파일은 남는다). 열린 탭은 닫는다. VS 의 "프로젝트에서 제외".</summary>
    public bool Exclude(ScriptProjectNode node)
    {
        if (Project is null || node.Kind == ScriptNodeKind.Project) return false;

        var what = node.IsFolder ? $"폴더 '{node.Name}' 과(와) 그 안의 항목" : $"'{node.Name}'";

        if (Ask($"{what}을(를) 프로젝트에서 제외할까요?\n파일은 지우지 않습니다.", MessageButton.OKCancel) is not (MessageResult.OK or MessageResult.Yes))
            return false;

        return Unlist(node);
    }

    /// <summary>
    /// 삭제 - VS 의 Delete. 묻고, 열린 탭을 닫고, 파일(폴더)을 <b>휴지통으로</b> 보내고 목록에서 뺀다.
    /// </summary>
    /// <remarks>영구 삭제가 아니다(사용자 결정) - 잘못 눌러도 휴지통에서 되찾는다. 파일을 남기려면 <see cref="Exclude"/>.</remarks>
    public bool Delete(ScriptProjectNode node)
    {
        if (Project is not { } project || node.Kind == ScriptNodeKind.Project) return false;

        var what = node.IsFolder ? $"폴더 '{node.Name}' 과(와) 그 안의 모든 파일" : $"'{node.Name}'";

        if (Ask($"{what}이(가) 휴지통으로 이동됩니다.", MessageButton.OKCancel) is not (MessageResult.OK or MessageResult.Yes))
            return false;

        var full = project.FullPath(node.Id);

        // 탭부터 닫는다 - 저장 안 한 것을 저장하겠다고 하면 지우기 전에 쓰고, 취소면 지우지 않는다.
        if (!CloseDocumentsUnder(full, node.IsFolder)) return false;

        try
        {
            (_host.Recycler ?? Helper.FileRecyclerFactory.Create()).Recycle(full);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"휴지통으로 못 보냈다: {full}");
            _host.Notify?.Invoke($"삭제하지 못했습니다: {ex.Message}");
            return false;
        }

        return Unlist(node);
    }

    private bool Unlist(ScriptProjectNode node)
    {
        var project = Project!;

        if (!CloseDocumentsUnder(project.FullPath(node.Id), node.IsFolder)) return false;

        if (node.IsFolder) project.RemoveFolder(node.Id);
        else project.Remove(node.Id);

        project.Save();
        RebuildNodes();
        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    private bool CloseDocumentsUnder(string full, bool isFolder)
    {
        foreach (var doc in Documents.Where(d => string.Equals(d.FilePath, full, StringComparison.OrdinalIgnoreCase) ||
                                                 (isFolder && d.FilePath.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            if (!CloseDocument(doc)) return false;
        }

        return true;
    }

    /// <summary>
    /// 탐색기에 끌어다 놓은 파일·폴더. <b>놓은 폴더로 복사</b>하고 목록에 넣는다.
    /// </summary>
    /// <remarks>
    /// "기존 항목 추가" 와 달리 프로젝트 안의 파일이라도 다른 폴더에서 끌어왔으면 놓은 폴더로 복사한다 - 놓은 자리가 사람이 원한 자리다.
    /// 같은 폴더에 같은 이름이 있으면 덮지 않고 뒤에 숫자를 붙인다. 폴더를 놓으면 안의 파일까지 통째로 복사한다. 원본은 그대로 둔다.
    /// </remarks>
    public IReadOnlyList<ScriptProjectNode> Drop(IEnumerable<string> paths, string folderId)
    {
        if (Project is not { } project) return [];

        var added = new List<string>();
        var targetFull = folderId.Length == 0 ? project.Directory : project.FullPath(folderId);

        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);

            if (Directory.Exists(full))
            {
                // 자기 안으로 복사하면 끝없이 커진다.
                if (targetFull.StartsWith(full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(targetFull, full, StringComparison.OrdinalIgnoreCase))
                {
                    _host.Notify?.Invoke($"폴더를 자기 안으로 복사할 수 없습니다: {Path.GetFileName(full)}");
                    continue;
                }

                var folderRelative = UniqueName(folderId, Path.GetFileName(full), string.Empty);
                project.AddFolder(folderRelative);

                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    var inner = folderRelative + "/" + Path.GetRelativePath(full, file).Replace('\\', '/');
                    Directory.CreateDirectory(Path.GetDirectoryName(project.FullPath(inner))!);
                    File.Copy(file, project.FullPath(inner));
                    project.Add(inner);
                    added.Add(inner);
                }

                foreach (var directory in Directory.EnumerateDirectories(full, "*", SearchOption.AllDirectories))
                    project.AddFolder(folderRelative + "/" + Path.GetRelativePath(full, directory).Replace('\\', '/'));

                added.Add(folderRelative);
                continue;
            }

            if (!File.Exists(full)) continue;

            var alreadyThere = string.Equals(Path.GetDirectoryName(full), targetFull.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            string relative;

            if (alreadyThere)
            {
                // 그 폴더에 이미 있는 파일을 놓았다 - 복사하지 않고 목록에만 넣는다(탐색기에서 넣어 둔 것을 목록에 올릴 때).
                relative = project.RelativePath(full)!;
            }
            else
            {
                relative = UniqueName(folderId, Path.GetFileNameWithoutExtension(full), Path.GetExtension(full));
                Directory.CreateDirectory(Path.GetDirectoryName(project.FullPath(relative))!);
                File.Copy(full, project.FullPath(relative));
            }

            project.Add(relative);
            added.Add(relative);
        }

        project.Save();
        RebuildNodes();
        Changed?.Invoke(this, EventArgs.Empty);

        _host.Notify?.Invoke($"{added.Count}개를 '{(folderId.Length == 0 ? project.Name : folderId)}' 에 넣었습니다.");

        return [.. Nodes.Where(n => added.Contains(n.Id, StringComparer.OrdinalIgnoreCase))];
    }

    public void SetEntry(ScriptProjectNode node)
    {
        if (Project is not { } project || node.Kind != ScriptNodeKind.Source) return;

        project.Entry = node.Id;
        project.Save();

        foreach (var each in Nodes) each.IsEntry = each.Id == node.Id;

        Changed?.Invoke(this, EventArgs.Empty);
        _host.Notify?.Invoke($"시작 파일을 '{node.Name}' 로 바꿨습니다.");
    }

    private ScriptProjectNode? Commit(string relative, bool open, bool edit)
    {
        Project!.Save();
        RebuildNodes();

        var node = Nodes.FirstOrDefault(n => string.Equals(n.Id, relative, StringComparison.OrdinalIgnoreCase));
        SelectedNode = node;

        if (node is not null && open) Open(node);
        if (node is not null && edit) EditNodeRequested?.Invoke(this, node);

        Changed?.Invoke(this, EventArgs.Empty);
        return node;
    }

    private string UniqueName(string folderId, string baseName, string extension)
    {
        var project = Project!;
        var prefix = folderId.Length == 0 ? string.Empty : folderId + "/";

        for (var n = 0; ; n++)
        {
            var candidate = prefix + baseName + (n == 0 ? string.Empty : n.ToString()) + extension;
            var full = project.FullPath(candidate);

            if (!File.Exists(full) && !Directory.Exists(full) && project.Find(candidate) is null) return candidate;
        }
    }

    // ── 문서(탭) ─────────────────────────────────────────────────────────

    /// <summary>줄을 연다. 글 파일은 탭(이미 열려 있으면 그 탭으로), 나머지는 윈도우 기본 프로그램.</summary>
    public ScriptDocument? Open(ScriptProjectNode node)
    {
        if (Project is not { } project || node.IsFolder) return null;

        var full = project.FullPath(node.Id);

        if (!File.Exists(full))
        {
            _host.Notify?.Invoke($"파일이 없습니다: {full}");
            return null;
        }

        if (!TextExtensions.Contains(Path.GetExtension(full)))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(full) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _host.Notify?.Invoke($"열 수 없습니다: {ex.Message}");
            }

            return null;
        }

        return OpenFile(full);
    }

    public ScriptDocument OpenFile(string fullPath)
    {
        var existing = Documents.FirstOrDefault(d => string.Equals(d.FilePath, Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new ScriptDocument(fullPath, _host.OnUi) { Settings = EditorSettings };
            existing.TextChanged += OnDocumentTextChanged;
            Documents.Add(existing);
        }

        ActiveDocument = existing;
        RaiseCommands();

        return existing;
    }

    /// <summary>탭을 닫는다. 저장 안 했으면 묻는다. 취소하면 false.</summary>
    public bool CloseDocument(ScriptDocument? doc)
    {
        if (doc is null) return true;

        if (doc.IsDirty)
        {
            var answer = Ask($"{Path.GetFileName(doc.FilePath)} 의 변경 내용을 저장하시겠습니까?", MessageButton.YesNoCancel);

            if (answer == MessageResult.Cancel) return false;
            if (answer == MessageResult.Yes) doc.Save();
        }

        var index = Documents.IndexOf(doc);
        Detach(doc);
        Documents.Remove(doc);

        if (ReferenceEquals(ActiveDocument, doc))
            ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Clamp(index - 1, 0, Documents.Count - 1)];

        // 저장 안 한 채 닫았으면 디스크 글로 다시 검사해야 한다.
        Changed?.Invoke(this, EventArgs.Empty);
        RaiseCommands();

        return true;
    }

    public void SaveAll()
    {
        foreach (var doc in Documents.Where(d => d.IsDirty)) doc.Save();

        Project?.Save();
    }

    private void Detach(ScriptDocument doc)
    {
        doc.TextChanged -= OnDocumentTextChanged;
        doc.Dispose();
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    // ── 컴파일에 넘길 것 ─────────────────────────────────────────────────

    /// <summary>열린 탭의 글(경로 → 글). 저장 안 한 것도 이것으로 돈다.</summary>
    public IReadOnlyDictionary<string, string> OpenTexts
        => Documents.ToDictionary(d => d.FilePath, d => d.Text, StringComparer.OrdinalIgnoreCase);

    public ScriptUnit? ToUnit() => Project?.ToUnit(OpenTexts);

    /// <summary>그 파일이 이 프로젝트 것이면 컴파일 한 벌을, 아니면 null. 완성·분류가 부른다(다른 스레드).</summary>
    public ScriptUnit? UnitFor(string filePath)
    {
        var project = Project;
        if (project is null || project.Find(filePath) is null) return null;

        // 탭 목록은 UI 스레드 것이다. 복사해 둔 것을 읽는다.
        return project.ToUnit(_openTextsSnapshot);
    }

    private IReadOnlyDictionary<string, string> _openTextsSnapshot = new Dictionary<string, string>();

    /// <summary>UI 스레드에서 열린 글을 떠 둔다. 완성·분류가 다른 스레드에서 읽는다.</summary>
    public void SnapshotOpenTexts() => _openTextsSnapshot = OpenTexts;

    // ── 안쪽 ─────────────────────────────────────────────────────────────

    private MessageResult Ask(string message, MessageButton button) => _host.Ask?.Invoke(message, button) ?? MessageResult.Yes;

    private void DoOpenFolderInExplorer()
    {
        if (Project is null) return;

        var path = SelectedNode is { } node && node.Kind != ScriptNodeKind.Project ? Project.FullPath(node.Id) : Project.Directory;
        var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{(Directory.Exists(path) ? path : Project.Directory)}\"";

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    /// <summary>명령에서 난 예외는 사람에게 말한다. 조용히 삼키면 눌러도 아무 일 없는 것처럼 보인다.</summary>
    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "프로젝트 명령이 실패했다");
            Minguk.Base.Views.ExceptionViewer.Show(ex, System.Reflection.MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void Dispose()
    {
        StopWatchingFolder();

        foreach (var doc in Documents) Detach(doc);
        Documents.Clear();
    }
}
