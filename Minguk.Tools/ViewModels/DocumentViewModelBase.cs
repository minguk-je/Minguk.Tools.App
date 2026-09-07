using System.ComponentModel;
using System.Reactive.Disposables;
using System.Windows.Media;
using DevExpress.Mvvm;
using Minguk.Base.Interface;
using Minguk.Base.Utilities;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 문서 탭으로 열리는 화면의 공통 베이스 — 생명주기 담당.
///
/// 화면마다 똑같이 반복되던 것(초기화 순서, 예외 처리, 설정 저장/복구, 자원 해제)을
/// 여기로 올렸다. 화면은 필요한 단계만 override 하면 된다.
///
/// ── 열릴 때 ─────────────────────────────────────────────────────────────
///   ① View 의 Loaded  → OnInitializedCommand
///   ② 부모 주입       → OnParentViewModelChanged (MainViewModel 이 들어온다)
///   ①②가 모두 도착하면 한 번만:
///        InitializeControls()   컨트롤 참조 확보  (UIObjectService)
///      → InitializeObservable() 이벤트 구독       (Disposables 에 등록)
///      → RestoreSettings()      저장값 복구       (AppSettingUtility)
///      → OnLoaded()             실제 진입 작업    (데이터 로드 등)
///
/// ── 닫힐 때 ─────────────────────────────────────────────────────────────
///   OnClose(e)  탭의 X. <see cref="AllowClose"/> 가 false 면 취소된다.
///   OnDestroy() 실제 파기. SaveSettings() → ReleaseResources() → 구독/메신저 해제.
///
/// 순서가 ①②의 합류점인 이유: DevExpress 는 문서를 만들 때 부모를 먼저 넣지만,
/// 그 보장에 기대면 화면을 창으로 따로 띄웠을 때 조용히 어긋난다. 두 신호를 모두 기다린다.
/// 부모 없이 쓰는 화면이면 <see cref="RequiresParentViewModel"/> 를 false 로 돌린다.
/// </summary>
public abstract partial class DocumentViewModelBase : ViewModelBase, IDocumentContent, ILoading
{
    private bool _viewInitialized;
    private bool _parentAttached;
    private bool _initialized;
    private bool _destroyed;

    protected DocumentViewModelBase()
    {
        OnInitializedCommand = new DelegateCommand(MarkViewInitialized, false);
        OnClosingCommand = new DelegateCommand<CancelEventArgs>(OnClosing, false);
        DoCloseCommand = new DelegateCommand(DoClose, false);
    }

    // ── 문서 탭에 보이는 것 ──────────────────────────────────────────────
    // MainView.xaml 의 DocumentPanelStyle 이 이 둘을 바인딩한다.

    /// <summary>문서 탭에 표시할 제목.</summary>
    public string? Caption { get => GetProperty(() => Caption); set => SetProperty(() => Caption, value); }

    /// <summary>문서 탭에 표시할 아이콘.</summary>
    public ImageSource? CaptionImage { get => GetProperty(() => CaptionImage); set => SetProperty(() => CaptionImage, value); }

    // ── 상태 ─────────────────────────────────────────────────────────────

    /// <summary>초기화가 끝날 때까지 true. 화면에서 대기 표시를 걸 때 쓴다.</summary>
    public bool IsLoading { get => GetProperty(() => IsLoading); set => SetProperty(() => IsLoading, value); }

    /// <summary>탭의 X 를 눌렀을 때 닫힐지 여부. 저장 안 한 편집이 있으면 false 로 막는다.</summary>
    protected bool AllowClose { get; set; } = true;

    /// <summary>InitializeObservable() 에서 만든 구독을 여기 담으면 OnDestroy 에서 한꺼번에 끊긴다.</summary>
    protected CompositeDisposable Disposables { get; } = new();

    /// <summary>셸. 문서로 열렸을 때만 들어온다.</summary>
    protected MainViewModel? MainViewModel { get; private set; }

    /// <summary>false 로 두면 부모 주입을 기다리지 않고 View 의 Loaded 만으로 초기화한다.</summary>
    protected virtual bool RequiresParentViewModel => true;

    // ── 커맨드 ───────────────────────────────────────────────────────────
    // View 에서 그대로 바인딩한다. 화면에서 다시 선언하지 말 것.
    //   <dxmvvm:EventToCommand Command="{Binding OnInitializedCommand}" EventName="Loaded" />

    public DelegateCommand OnInitializedCommand { get; }
    public DelegateCommand<CancelEventArgs> OnClosingCommand { get; }
    public DelegateCommand DoCloseCommand { get; }

    // ── 화면이 채워 넣는 자리 ────────────────────────────────────────────
    // 전부 비어 있는 기본 구현이다. 쓰는 것만 override 한다.

    /// <summary>UIObjectService 로 XAML 의 실제 컨트롤을 잡아 온다.</summary>
    protected virtual void InitializeControls() { }

    /// <summary>컨트롤 이벤트를 구독한다. 만든 구독은 <see cref="Disposables"/> 에 넣는다.</summary>
    protected virtual void InitializeObservable() { }

    /// <summary>지난번에 저장해 둔 값을 되돌린다. <see cref="GetSetting{T}"/> 를 쓴다.</summary>
    protected virtual void RestoreSettings() { }

    /// <summary>초기화 마지막 단계. 데이터 조회처럼 실제 진입 작업을 여기서 한다.</summary>
    protected virtual void OnLoaded() { }

    /// <summary>화면이 닫힐 때 남길 값. <see cref="SetSetting"/> 를 쓴다.</summary>
    protected virtual void SaveSettings() { }

    /// <summary>연결·타이머·핸들처럼 직접 정리해야 하는 것을 닫는다.</summary>
    protected virtual void ReleaseResources() { }

    /// <summary>MessengerUtility 수신. override 하면 자동으로 등록/해제된다.</summary>
    protected virtual void OnMessenger(MessengerUtility message) { }

    // ── 초기화 합류점 ────────────────────────────────────────────────────

    private void MarkViewInitialized()
    {
        _viewInitialized = true;
        TryInitialize();
    }

    protected override void OnParentViewModelChanged(object parentViewModel)
    {
        base.OnParentViewModelChanged(parentViewModel);

        Guard(() =>
        {
            MainViewModel = parentViewModel as MainViewModel;
            _parentAttached = true;
        });

        TryInitialize();
    }

    private void TryInitialize()
    {
        if (_initialized || !_viewInitialized)
            return;

        if (RequiresParentViewModel && !_parentAttached)
            return;

        _initialized = true;
        Initialize();
    }

    private void Initialize()
    {
        if (IsInDesignMode)
            return;

        Logger.Trace(string.Empty);

        IsLoading = true;

        try
        {
            Messenger.Default.Register<MessengerUtility>(this, OnMessenger);

            Guard(InitializeControls);
            Guard(InitializeObservable);
            Guard(RestoreSettings);
            Guard(OnLoaded);

            MessengerUtility.SendNotification(nameof(OnLoaded), OwnerViewFullName);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── IDocumentContent ─────────────────────────────────────────────────

    public IDocumentOwner? DocumentOwner { get; set; }

    object? IDocumentContent.Title => Caption;

    /// <summary>탭의 X 를 눌렀을 때. OnClose → OnDestroy 순으로 불린다.</summary>
    public void OnClose(CancelEventArgs e)
    {
        Logger.Trace(string.Empty);

        Guard(() => e.Cancel = !AllowClose);
    }

    /// <summary>문서가 실제로 파기될 때. 두 번 불려도 한 번만 돈다.</summary>
    public void OnDestroy()
    {
        if (_destroyed)
            return;

        _destroyed = true;

        Logger.Trace(string.Empty);

        Guard(SaveSettings);
        Guard(ReleaseResources);

        Guard(() =>
        {
            Disposables.Dispose();

            MessengerUtility.SendNotification(nameof(OnDestroy), OwnerViewFullName);
            Messenger.Default.Unregister(this);
        });
    }

    /// <summary>창으로 띄웠을 때의 닫힘. 문서 탭은 OnClose/OnDestroy 를 탄다.</summary>
    protected virtual void OnClosing(CancelEventArgs e) => OnDestroy();

    /// <summary>화면 안의 "닫기" 버튼용. 문서로 열려 있을 때만 동작한다.</summary>
    protected virtual void DoClose() => Guard(() => DocumentOwner?.Close(this));
}
