using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using DevExpress.Mvvm.UI.Interactivity;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Grid.TreeList;

using Minguk.Tools.ViewModels;

namespace Minguk.Tools.Markup;

/// <summary>
/// 솔루션 탐색기(그리드의 트리 보기)를 작업 공간과 잇는다 - VS 의 조작을 그대로.
/// </summary>
/// <remarks>
///   - <b>더블 클릭·Enter</b>: 연다(글 파일은 탭, 나머지는 윈도우 기본 프로그램).
///   - <b>F2</b>: 이름 칸을 연다. 새 파일·새 폴더를 만들어도 작업 공간이 알려 이 칸이 열린다.
///   - <b>Delete</b>: 삭제(휴지통). 이름을 고치는 중의 Delete 는 글자를 지우는 것이라 건드리지 않는다.
///   - <b>끌어다 놓기</b>: 윈도우 탐색기에서 파일·폴더를 놓으면 놓은 줄의 폴더(파일이면 그 부모)로 복사해 넣는다.
///   - <b>프로젝트 줄 끌기</b>: 다른 프로젝트 줄 위에 놓으면 솔루션의 프로젝트 순서가 바뀐다(아래로 끌면 그 뒤, 위로 끌면 그 앞) - 솔루션 화면 콤보도 이 순서.
/// 이름 칸은 F2 로만 연다(<c>EditorShowMode</c> 를 막는다) - 한 번 누를 때마다 칸이 열리면 더블 클릭으로 열 수가 없다.
/// </remarks>
public sealed class ProjectTreeBehavior : Behavior<GridControl>
{
    public static readonly DependencyProperty WorkspaceProperty = DependencyProperty.Register(
        nameof(Workspace), typeof(ScriptProjectWorkspace), typeof(ProjectTreeBehavior),
        new PropertyMetadata(null, (d, e) => ((ProjectTreeBehavior)d).OnWorkspaceChanged(e.OldValue as ScriptProjectWorkspace, e.NewValue as ScriptProjectWorkspace)));

    public ScriptProjectWorkspace? Workspace
    {
        get => (ScriptProjectWorkspace?)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    private bool _allowEdit;

    private TreeListView? View => AssociatedObject?.View as TreeListView;

    protected override void OnAttached()
    {
        base.OnAttached();

        AssociatedObject.AllowDrop = true;
        AssociatedObject.PreviewDragOver += OnDragOver;
        AssociatedObject.PreviewDrop += OnDrop;
        AssociatedObject.PreviewKeyDown += OnKeyDown;
        AssociatedObject.Loaded += OnLoaded;
        AssociatedObject.PreviewMouseLeftButtonDown += OnMouseDown;
        AssociatedObject.PreviewMouseMove += OnMouseMove;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PreviewDragOver -= OnDragOver;
        AssociatedObject.PreviewDrop -= OnDrop;
        AssociatedObject.PreviewKeyDown -= OnKeyDown;
        AssociatedObject.Loaded -= OnLoaded;
        AssociatedObject.PreviewMouseLeftButtonDown -= OnMouseDown;
        AssociatedObject.PreviewMouseMove -= OnMouseMove;

        if (View is { } view)
        {
            view.RowDoubleClick -= OnRowDoubleClick;
            view.ShowingEditor -= OnShowingEditor;
            view.HiddenEditor -= OnHiddenEditor;
            view.NodeChanged -= OnNodeChanged;
            view.NodeExpanded -= OnNodeExpanded;
            view.NodeCollapsed -= OnNodeCollapsed;
        }

        OnWorkspaceChanged(Workspace, null);
        base.OnDetaching();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (View is not { } view) return;

        view.RowDoubleClick -= OnRowDoubleClick;
        view.RowDoubleClick += OnRowDoubleClick;
        view.ShowingEditor -= OnShowingEditor;
        view.ShowingEditor += OnShowingEditor;
        view.HiddenEditor -= OnHiddenEditor;
        view.HiddenEditor += OnHiddenEditor;
        view.NodeChanged -= OnNodeChanged;
        view.NodeChanged += OnNodeChanged;
        view.NodeExpanded -= OnNodeExpanded;
        view.NodeExpanded += OnNodeExpanded;
        view.NodeCollapsed -= OnNodeCollapsed;
        view.NodeCollapsed += OnNodeCollapsed;
    }

    // ── 펼침 기억 ────────────────────────────────────────────────────────
    //    줄이 생길 때 작업 공간이 기억한 대로 펼치거나 접고(기본은 펼침), 사람이 펼치고 접으면 적는다. 트리를 다시 만들어도, 앱을 다시 켜도 그대로.

    /// <summary>우리가 펼치고 접는 중 - 그건 사람이 한 것이 아니라 적지 않는다.</summary>
    private bool _applyingExpansion;

    private void OnNodeChanged(object sender, TreeListNodeChangedEventArgs e)
    {
        // 줄이 새로 생겼을 때만(펼침·접힘 알림도 이 이벤트로 온다).
        if (e.ChangeType.ToString() != "Add" || e.Node.Content is not ScriptProjectNode node || Workspace is not { } workspace) return;

        _applyingExpansion = true;

        try
        {
            e.Node.IsExpanded = !workspace.IsCollapsed(node);
        }
        finally
        {
            _applyingExpansion = false;
        }
    }

    private void OnNodeExpanded(object sender, TreeListNodeEventArgs e)
    {
        if (!_applyingExpansion && e.Node.Content is ScriptProjectNode node) Workspace?.SetCollapsed(node, false);
    }

    private void OnNodeCollapsed(object sender, TreeListNodeEventArgs e)
    {
        if (!_applyingExpansion && e.Node.Content is ScriptProjectNode node) Workspace?.SetCollapsed(node, true);
    }

    private void OnWorkspaceChanged(ScriptProjectWorkspace? old, ScriptProjectWorkspace? now)
    {
        if (old is not null) old.EditNodeRequested -= OnEditRequested;
        if (now is not null) now.EditNodeRequested += OnEditRequested;
    }

    private void OnRowDoubleClick(object sender, RowDoubleClickEventArgs e)
    {
        if (AssociatedObject.GetRow(e.HitInfo.RowHandle) is not ScriptProjectNode node || Workspace is null) return;

        Workspace.SelectedNode = node;

        // 폴더는 VS 처럼 펼치고 접는다. 파일은 연다.
        if (node.IsFolder) View?.ChangeNodeExpanded(e.HitInfo.RowHandle, !(View.GetNodeByRowHandle(e.HitInfo.RowHandle)?.IsExpanded ?? false));
        else Workspace.Open(node);

        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Workspace is null || View is not { } view) return;

        // 이름을 고치는 중이면 글자 편집이 먼저다.
        if (view.ActiveEditor is not null) return;

        switch (e.Key)
        {
            case Key.F2 when Workspace.RenameCommand.CanExecute(null):
                Workspace.RenameCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete when Workspace.DeleteCommand.CanExecute(null):
                Workspace.DeleteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter when Workspace.SelectedNode is { IsFolder: false } node:
                Workspace.Open(node);
                e.Handled = true;
                break;
        }
    }

    private void OnEditRequested(object? sender, ScriptProjectNode node)
    {
        // 줄은 트리를 다시 만든 뒤에 생긴다 - 그 뒤에 그 줄로 가서 칸을 연다.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (View is not { } view) return;

            var handle = AssociatedObject.FindRow(node);
            if (handle == GridControl.InvalidRowHandle) return;

            view.FocusedRowHandle = handle;
            _allowEdit = true;
            view.ShowEditor(selectAll: true);
        }));
    }

    private void OnShowingEditor(object sender, TreeListShowingEditorEventArgs e)
    {
        // F2·새 항목일 때만 열린다. 프로젝트 줄은 이름이 프로젝트 파일 이름이라 여기서 안 바꾼다.
        // 참조 줄·공유 프로젝트 파일도 여기서 안 바꾼다 - 그 목록은 이 프로젝트 것이 아니다.
        e.Cancel = !_allowEdit || e.Node?.Content is ScriptProjectNode { Kind: ScriptNodeKind.Project or ScriptNodeKind.ProjectReference or ScriptNodeKind.Solution } or ScriptProjectNode { IsExternal: true };
    }

    private void OnHiddenEditor(object sender, TreeListEditorEventArgs e) => _allowEdit = false;

    // ── 끌어다 놓기 ──────────────────────────────────────────────────────

    /// <summary>트리 안에서 끄는 프로젝트 줄의 형식 이름 - 윈도우 탐색기 파일과 가른다.</summary>
    private const string ProjectNodeFormat = "Minguk.Tools.ProjectNode";

    private Point? _pressAt;
    private ScriptProjectNode? _pressed;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressAt = null;
        _pressed = null;

        if (View is not { } view || e.OriginalSource is not DependencyObject source) return;

        var hit = view.CalcHitInfo(source);

        if (hit.InRow && AssociatedObject.GetRow(hit.RowHandle) is ScriptProjectNode node && ScriptProjectWorkspace.IsMovableProject(node))
        {
            _pressAt = e.GetPosition(AssociatedObject);
            _pressed = node;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is not { } node || _pressAt is not { } start) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pressAt = null;
            _pressed = null;
            return;
        }

        // 윈도우가 정한 만큼 움직여야 끌기다 - 그냥 누르는 것(고르기·더블 클릭)과 가른다.
        var delta = e.GetPosition(AssociatedObject) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _pressAt = null;
        _pressed = null;

        DragDrop.DoDragDrop(AssociatedObject, new DataObject(ProjectNodeFormat, node), DragDropEffects.Move);
    }

    /// <summary>끄는 동안 마우스 밑의 프로젝트 줄. 프로젝트 줄이 아니면 null.</summary>
    private ScriptProjectNode? ProjectUnder(DragEventArgs e)
    {
        if (View is not { } view || e.OriginalSource is not DependencyObject source) return null;

        var hit = view.CalcHitInfo(source);

        return hit.InRow && AssociatedObject.GetRow(hit.RowHandle) is ScriptProjectNode node && ScriptProjectWorkspace.IsMovableProject(node) ? node : null;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(ProjectNodeFormat) is ScriptProjectNode moving)
        {
            e.Effects = ProjectUnder(e) is { } target && !ReferenceEquals(target, moving) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = Workspace?.IsOpen == true && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(ProjectNodeFormat) is ScriptProjectNode moving)
        {
            e.Handled = true;

            if (Workspace is { } projects && ProjectUnder(e) is { } target)
            {
                try
                {
                    projects.MoveProject(moving, target);
                }
                catch (Exception ex)
                {
                    // 원문 예외는 로그에만 - 화면 글은 한국어로(CLAUDE.md).
                    NLog.LogManager.GetCurrentClassLogger().Warn(ex, "프로젝트 순서를 못 바꿨다");
                    MessageBox.Show("프로젝트 순서를 바꾸지 못했습니다 - 솔루션 파일(.mtsln)에 쓸 수 없습니다. 파일이 읽기 전용이거나 다른 프로그램이 쥐고 있는지 보세요.",
                                    "프로젝트 순서", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            return;
        }

        if (Workspace is not { IsOpen: true } workspace || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        e.Handled = true;

        var folderId = ScriptProjectWorkspace.RootId;

        if (View is { } view && e.OriginalSource is DependencyObject source)
        {
            var hit = view.CalcHitInfo(source);

            if (hit.InRow && AssociatedObject.GetRow(hit.RowHandle) is ScriptProjectNode node)
                folderId = node.IsFolder ? node.Id : node.ParentId;
        }

        try
        {
            workspace.Drop(paths.Where(p => File.Exists(p) || Directory.Exists(p)), folderId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "끌어다 놓기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
