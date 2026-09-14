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
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PreviewDragOver -= OnDragOver;
        AssociatedObject.PreviewDrop -= OnDrop;
        AssociatedObject.PreviewKeyDown -= OnKeyDown;
        AssociatedObject.Loaded -= OnLoaded;

        if (View is { } view)
        {
            view.RowDoubleClick -= OnRowDoubleClick;
            view.ShowingEditor -= OnShowingEditor;
            view.HiddenEditor -= OnHiddenEditor;
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
        e.Cancel = !_allowEdit || e.Node?.Content is ScriptProjectNode { Kind: ScriptNodeKind.Project or ScriptNodeKind.ProjectReference } or ScriptProjectNode { IsExternal: true };
    }

    private void OnHiddenEditor(object sender, TreeListEditorEventArgs e) => _allowEdit = false;

    // ── 끌어다 놓기 ──────────────────────────────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Workspace?.IsOpen == true && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
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
