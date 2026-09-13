using System.Linq;
using System.Windows;

using DevExpress.Mvvm.UI.Interactivity;
using DevExpress.Xpf.Docking;
using DevExpress.Xpf.Docking.Base;

using Minguk.Tools.ViewModels;

namespace Minguk.Tools.Markup;

/// <summary>
/// 도킹 관리자의 문서 탭을 프로젝트 작업 공간(<see cref="ScriptProjectWorkspace"/>)과 잇는다 - VS 의 문서 탭처럼.
/// </summary>
/// <remarks>
/// 탭은 <c>DocumentGroup.ItemsSource = Documents</c> 로 만들어진다. 그것만으로 안 되는 둘을 여기서 한다.
///   - <b>닫기</b>: 탭의 ×·가운데 클릭·Ctrl+F4 로 닫을 때 도킹이 곧바로 치우지 않게 막고, 작업 공간에 맡긴다. 저장 안 한 것은
///     작업 공간이 묻고, 닫기로 하면 컬렉션에서 빠져 탭이 사라진다. 막지 않으면 "취소" 를 눌러도 탭은 이미 없다.
///   - <b>활성 탭</b>: 탭을 누르면 <see cref="ScriptProjectWorkspace.ActiveDocument"/> 를, 작업 공간이 파일을 열면(탐색기 더블 클릭)
///     그 탭을 앞으로. 서로 되부르지 않게 같은 것이면 건너뛴다.
/// 코드 비하인드에 두지 않는 이유 - 도킹 이벤트를 ViewModel 이 알면 화면과 묶인다(CLAUDE.md ViewModel 규칙).
/// </remarks>
public sealed class ScriptDocumentsBehavior : Behavior<DockLayoutManager>
{
    public static readonly DependencyProperty WorkspaceProperty = DependencyProperty.Register(
        nameof(Workspace), typeof(ScriptProjectWorkspace), typeof(ScriptDocumentsBehavior),
        new PropertyMetadata(null, (d, e) => ((ScriptDocumentsBehavior)d).OnWorkspaceChanged(e.OldValue as ScriptProjectWorkspace, e.NewValue as ScriptProjectWorkspace)));

    public ScriptProjectWorkspace? Workspace
    {
        get => (ScriptProjectWorkspace?)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        AssociatedObject.DockItemClosing += OnClosing;
        AssociatedObject.DockItemActivated += OnActivated;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.DockItemClosing -= OnClosing;
        AssociatedObject.DockItemActivated -= OnActivated;
        OnWorkspaceChanged(Workspace, null);

        base.OnDetaching();
    }

    private void OnWorkspaceChanged(ScriptProjectWorkspace? old, ScriptProjectWorkspace? now)
    {
        if (old is not null) old.PropertyChanged -= OnWorkspacePropertyChanged;
        if (now is not null) now.PropertyChanged += OnWorkspacePropertyChanged;
    }

    private void OnClosing(object sender, ItemCancelEventArgs e)
    {
        if (e.Item?.DataContext is not ScriptDocument doc || Workspace is null) return;

        // 도킹이 치우지 않게 막는다. 닫을지는 작업 공간이 정하고, 닫으면 컬렉션에서 빠져 탭이 사라진다.
        e.Cancel = true;
        Workspace.CloseDocument(doc);
    }

    /// <summary>
    /// 코드가 앞으로 가져오라고 한 문서. 그 문서의 "활성화됨" 이 올 때까지 다른 탭의 알림은 무시한다.
    /// </summary>
    /// <remarks>
    /// 도킹의 활성화 알림은 한 박자 늦게(Normal 우선순위로) 온다. 새 탭을 열면 옛 탭의 늦은 알림이 도착해 활성 문서를 되돌리고,
    /// 되돌린 것이 다시 탭을 가져오고… 가 끝없이 돌았다(실측: 각 66,280번, 화면이 멈춤). 요청한 것이 올 때까지 나머지는 버린다.
    /// </remarks>
    private ScriptDocument? _requested;

    private void OnActivated(object sender, DockItemActivatedEventArgs e)
    {
        if (e.Item?.DataContext is not ScriptDocument doc || Workspace is null) return;

        if (_requested is not null)
        {
            if (!ReferenceEquals(doc, _requested)) return;
            _requested = null;
        }

        if (!ReferenceEquals(Workspace.ActiveDocument, doc)) Workspace.ActiveDocument = doc;
    }

    private void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScriptProjectWorkspace.ActiveDocument) || AssociatedObject is null) return;
        if (Workspace?.ActiveDocument is not { } doc) return;

        // 이미 앞에 있으면 할 일이 없다 - 탭을 눌러서 온 알림이다.
        if (AssociatedObject.ActiveDockItem?.DataContext is ScriptDocument active && ReferenceEquals(active, doc)) return;

        _requested = doc;

        // 탭은 컬렉션이 바뀐 뒤 조금 늦게 만들어진다 - 만들어진 뒤에 앞으로 가져온다.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new System.Action(() =>
        {
            if (!ReferenceEquals(_requested, doc)) return;

            var panel = AssociatedObject.GetItems().OfType<DocumentPanel>().FirstOrDefault(p => ReferenceEquals(p.DataContext, doc));

            if (panel is null || ReferenceEquals(AssociatedObject.ActiveDockItem, panel))
            {
                _requested = null;
                return;
            }

            AssociatedObject.DockController.Activate(panel);
        }));
    }
}
