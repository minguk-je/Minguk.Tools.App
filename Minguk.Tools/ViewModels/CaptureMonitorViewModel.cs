using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using Minguk.Image;
using Minguk.Tools.Capture;
using Minguk.Tools.Helper;
using Newtonsoft.Json;
using Minguk.Base.Extension;
using Minguk.Base.Utilities;
using Minguk.Base.Views;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Diagnostics;
using DevExpress.Xpf.LayoutControl;
using Minguk.Tools.Automation;
using Minguk.Tools.Capture.Input;
using Minguk.Tools.Input;
using Minguk.Tools.Vision.Labeling;
using System.Windows.Input;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 1초치 캡처 통계 한 줄. 그리드 바인딩용 모델이다.
///
/// 프레임마다 한 줄씩 넣지 않는 이유는 단순하다. 240fps 를 그대로 그리드에 밀어 넣으면
/// UI 스레드가 그리다가 죽고, 그 부하가 다시 캡처 지연으로 되돌아온다.
/// </summary>
public class FrameLogRow
{
    public DateTime Timestamp { get; set; }
    public long FrameId { get; set; }
    public int Frames { get; set; }
    /// <summary>캡처가 초당 몇 장 들어왔는지. 미리보기 상한과 무관하다 — WGC 가 주는 속도 그대로다.</summary>
    public double Fps { get; set; }

    /// <summary>그중 실제로 화면에 올린 장수. 미리보기 상한(PreviewTargetFps)에 걸린다.</summary>
    public int PreviewFps { get; set; }
    public double AvgLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public double AvgReadbackMs { get; set; }
    public string? Resolution { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// 캡처 모니터. Windows.Graphics.Capture 로 창/모니터를 잡고 그 성능을 눈으로 본다.
///
/// 프레임 콜백은 스레드풀에서 돈다(<see cref="WgcCaptureSession"/> 참조).
/// 그래서 콜백에서는 카운터만 올리고, 그리드에 넣는 일은 1초마다 한 번 디스패처로 넘긴다.
/// </summary>
public partial class CaptureMonitorViewModel : DocumentViewModelBase, IDisposable
{
    /// <summary>그리드에 남겨 둘 줄 수. 오래 켜 두면 메모리를 먹으니 잘라 낸다.</summary>
    private const int MaxRows = 600;

    private readonly object _statisticsLock = new();

    /// <summary>
    /// 지금 돌고 있는 캡처 어댑터. 구체 타입이 아니라 인터페이스로 들고 있어서
    /// 캡처 방식을 바꿔도 이 화면은 손댈 것이 없다.
    /// </summary>
    private IScreenCaptureAdapter? _captureSession;
    private Timer? _statisticsFlushTimer;

    /// <summary>타이머 스레드에서 서비스 컨테이너를 뒤지지 않도록, 시작할 때 UI 스레드에서 한 번 꺼내 둔다.</summary>
    private IDispatcherService? _uiDispatcher;

    // 콜백에서 쌓고 1초마다 비우는 통계
    private int _frameCountInSecond;
    private long _lastFrameId;
    private double _latencyMsSum;
    private double _latencyMsMax;
    private double _readbackMsSum;
    private int _lastFrameWidth;
    private int _lastFrameHeight;
    private string? _pendingNoteText;

    // 저장 요청. 다음 프레임 한 장만 파일로 떨어뜨린다.
    private int _isSaveFrameRequested;

    // 담기 요청. 다음 프레임 한 장만 데이터셋으로 보낸다.
    private int _isCollectFrameRequested;

    // ── 미리보기 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 미리보기 최소 간격(틱). <see cref="PreviewTargetFps"/> 가 바뀌면 다시 계산한다.
    ///
    /// 목표 주기보다 10% 짧게 잡는 것이 요점이다. 60fps 목표에 간격을 딱 1/60 로 두면
    /// 60fps 캡처와 주기가 겹쳐서, 프레임이 경계 직전에 도착할 때마다 버려지고
    /// 다음 장까지 33ms 를 기다리게 된다. 실측으로 미리보기가 35fps 에 묶였다.
    /// </summary>
    private long _previewMinimumIntervalTicks = Stopwatch.Frequency * 9 / (60 * 10);

    /// <summary>
    /// 미리보기로 만들 최대 높이.
    ///
    /// 원본을 그대로 올리면 2560x1440 BGRA 한 장이 14.7MB 다. 복사(콜백) + WritePixels(UI) 로
    /// 프레임당 30MB 를 옮기게 된다. 어차피 320px 칸에 줄여 보여 주므로 옮길 때 미리 솎아 낸다.
    /// </summary>
    private const int PreviewMaxHeight = 400;

    /// <summary>미리보기 칸의 기본 높이(px).</summary>
    private const double DefaultPreviewHeight = 320;

    /// <summary>클릭을 넘긴 뒤 이 앱으로 돌아오기까지 기다리는 시간(ms).</summary>
    private const int ReturnToThisWindowDelayMs = 120;

    /// <summary>
    /// 대상 창을 앞으로 가져온 뒤 클릭을 보내기까지 기다리는 시간(ms).
    ///
    /// SetForegroundWindow 는 바로 돌아오지만 포그라운드 전환은 창 관리자가 나중에 처리한다.
    /// 그 전에 클릭을 보내면 대상이 그것을 활성화 클릭으로 먹어서 아무 일도 안 일어난다.
    /// 이미 앞에 있던 창이면 기다리지 않는다.
    /// </summary>
    private const int ActivationSettleDelayMs = 80;

    /// <summary>미리보기를 껐다 켤 때 되살릴 높이. 끄면 행이 0 으로 접히므로 따로 기억한다.</summary>
    private double _lastPreviewGroupHeight = DefaultPreviewHeight;

    /// <summary>
    /// 지난번에 고른 대상의 표시 이름.
    ///
    /// 핸들(HWND/HMONITOR)은 실행마다 바뀌므로 저장해도 소용이 없다.
    /// 사람이 보는 이름으로 되찾는다 — 모니터는 "[모니터] 디스플레이 1 (2560×1440)" 처럼
    /// 구성이 그대로면 같은 문자열이 나온다.
    /// </summary>
    private string _lastSelectedTargetDisplay = string.Empty;

    /// <summary>미리보기 칸. 높이를 직접 넣고 빼려고 들고 있는다.</summary>
    private LayoutGroup? _previewLayoutGroup;

    /// <summary>미리보기 Image 컨트롤. 누른 자리를 원본 좌표로 바꾸려면 컨트롤 크기가 필요하다.</summary>
    private System.Windows.Controls.Image? _previewImage;

    /// <summary>입력을 받는 Border. 키를 받으려면 여기에 포커스가 있어야 한다.</summary>
    private System.Windows.Controls.Border? _previewSurface;

    /// <summary>통계 그리드의 뷰. 새 줄이 들어올 때 맨 위를 유지하려고 들고 있는다.</summary>
    private DevExpress.Xpf.Grid.TableView? _gridView;

    /// <summary>미리보기에서 일어난 입력을 대상 창으로 흘려보내는 쪽.</summary>
    private PreviewInputRouter? _inputRouter;

    /// <summary>요소 검사에 쓰는 UI 자동화 경로.</summary>
    private IUiAutomationAdapter? _uiAutomation;

    /// <summary>
    /// 클릭 한 번이 아직 처리 중인지.
    ///
    /// 클릭 경로가 비동기라서(활성화 대기 → 클릭 → 돌아오기) 그 사이에 들어온 클릭을
    /// 그대로 받으면 같은 자리를 여러 번 누르게 된다. 실제로 로그에 같은 좌표가
    /// 밀리초 단위로 수십 번 찍혔다.
    /// </summary>
    private bool _isForwardingClick;

    /// <summary>
    /// GPU 경로. 캡처 텍스처를 CPU 를 거치지 않고 바로 화면에 올린다.
    /// 만들기가 실패하면(원격 데스크톱 등) <see cref="_isGpuPreviewUnavailable"/> 를 세우고
    /// 아래 WriteableBitmap 경로로 떨어진다.
    /// </summary>
    private D3DImageBridge? _gpuPreviewBridge;
    private bool _isGpuPreviewUnavailable;
    private bool _isPreviewSurfaceRequestPending;

    /// <summary>공유 표면에 새 프레임이 들어왔으면 1. 캡처 스레드가 세우고 렌더 콜백이 내린다.</summary>
    private int _hasUnpresentedGpuFrame;

    /// <summary>화면 반영을 이미 걸어 두었으면 1. 같은 요청을 겹쳐 쌓지 않는다.</summary>
    private int _isPresentScheduled;

    /// <summary>화면 반영 시도가 초당 몇 번 있었는지.</summary>
    private int _presentAttemptCountInSecond;

    /// <summary>CompositionTarget.Rendering 구독 여부. UI 스레드에서만 만진다.</summary>
    private bool _isRenderLoopHooked;

    private WriteableBitmap? _cpuPreviewBitmap;
    private byte[]? _cpuPreviewBuffer;
    private int _cpuPreviewWidth;
    private int _cpuPreviewHeight;

    /// <summary>UI 가 앞 장을 아직 그리는 중이면 1. 그동안 들어온 프레임은 버린다.</summary>
    private int _isCpuPreviewBlitInProgress;

    private long _lastPreviewTimestamp;
    private int _presentedFrameCountInSecond;

    // CommandManager 의 자동 재조회는 사용자 입력 때만 돈다. 여기 상태는 캡처 스레드/타이머에서 바뀌므로
    // useCommandManager: false 로 만들고 RaiseCanExecuteChanged 를 직접 부른다.
    public DelegateCommand OnUnloadedCommand { get; set; }
    public DelegateCommand RefreshTargetsCommand { get; set; }
    public DelegateCommand DoStartCommand { get; set; }
    public DelegateCommand DoStopCommand { get; set; }
    public DelegateCommand DoClearCommand { get; set; }
    public DelegateCommand SaveFrameCommand { get; set; }

    public DelegateCommand CollectFrameCommand { get; set; } = null!;

    public bool IsRunning
    {
        get => GetProperty(() => IsRunning);
        set => SetProperty(() => IsRunning, value, OnRunningChanged);
    }

    /// <summary>캡처 중에는 대상을 못 바꾸게 막는다. XAML 에서 부정 컨버터를 쓰지 않으려고 둔 프로퍼티다.</summary>
    public bool IsNotRunning => !IsRunning;

    /// <summary>
    /// GPU 프레임을 CPU 메모리로 내린다. 추론을 CPU 에서 돌리거나 화면을 저장하려면 필요하다.
    /// 켜면 프레임마다 GPU→CPU 복사가 붙으므로 그만큼 느려진다 — 그 비용이 리드백(ms) 열에 찍힌다.
    /// </summary>
    public bool EnableCpuReadback
    {
        get => GetProperty(() => EnableCpuReadback);
        set => SetProperty(() => EnableCpuReadback, value, OnReadbackChanged);
    }

    // ── 몹 찾기 ──────────────────────────────────────────────────────────

    /// <summary>학습한 모델로 프레임에서 몹을 찾을지.</summary>
    public bool IsMobDetectionOn
    {
        get => GetProperty(() => IsMobDetectionOn);
        set => SetProperty(() => IsMobDetectionOn, value, OnMobDetectionChanged);
    }

    /// <summary>이보다 자신 없는 것은 안 보여 준다.</summary>
    public double DetectMinimumScore
    {
        get => GetProperty(() => DetectMinimumScore);
        set => SetProperty(() => DetectMinimumScore, value);
    }

    /// <summary>몇 마리를 몇 ms 에 찾았는지. 실제 속도가 여기 그대로 뜬다.</summary>
    public string? DetectionStatus
    {
        get => GetProperty(() => DetectionStatus);
        set => SetProperty(() => DetectionStatus, value);
    }

    /// <summary>찾은 것들. 미리보기 위에 겹쳐 그린다.</summary>
    public System.Collections.ObjectModel.ObservableCollection<Markup.PredictedBox> Detections { get; } = [];

    /// <summary>가장 자신 있는 몹을 누른다.</summary>
    public DelegateCommand ClickDetectionCommand { get; set; } = null!;

    public CaptureTarget? SelectedTarget
    {
        get => GetProperty(() => SelectedTarget);
        set => SetProperty(() => SelectedTarget, value, () => DoStartCommand.RaiseCanExecuteChanged());
    }

    public string? StatusText
    {
        get => GetProperty(() => StatusText);
        set => SetProperty(() => StatusText, value);
    }

    /// <summary>
    /// 캡처 중인 화면을 그대로 보여 준다.
    ///
    /// CPU 로 내려온 픽셀이 있어야 하므로 켜면 <see cref="EnableCpuReadback"/> 도 같이 켜진다.
    /// 그만큼 프레임마다 GPU→CPU 복사가 붙는다 — 그 비용은 리드백(ms) 열에 그대로 나온다.
    /// </summary>
    public bool ShowPreview
    {
        get => GetProperty(() => ShowPreview);
        set => SetProperty(() => ShowPreview, value, OnShowPreviewChanged);
    }

    /// <summary>
    /// 화면에 올리고 있는 프레임. XAML 의 Image 가 이걸 문다.
    /// GPU 경로면 D3DImage, 폴백이면 WriteableBitmap 이 들어온다 — 둘 다 ImageSource 다.
    /// </summary>
    public ImageSource? PreviewImage
    {
        get => GetProperty(() => PreviewImage);
        set => SetProperty(() => PreviewImage, value);
    }

    /// <summary>
    /// 그리드의 열 너비·순서·정렬·필터를 문자열로 뽑고 되돌린다.
    /// View 의 &lt;dxmvvm:LayoutSerializationService x:Name="GridLayoutService" /&gt; 가 실체다.
    /// </summary>
    private ILayoutSerializationService GridLayoutService
        => ServiceContainer.GetService<ILayoutSerializationService>("GridLayoutService");

    /// <summary>
    /// 그리드 열 구성의 판 번호. 열을 추가·삭제·개명하면 올린다.
    ///
    /// 저장된 레이아웃은 그때의 열 구성을 담고 있어서, 열이 바뀐 뒤 그대로 되돌리면
    /// 새 열이 숨겨진 채로 나온다. 판이 다르면 저장본을 버리고 기본 배치로 시작한다.
    /// </summary>
    private const int GridLayoutVersion = 2;

    /// <summary>열 너비를 내용에 맞춘다. 끄면 사용자가 조절한 너비가 유지된다.</summary>
    public bool IsColumnAutoWidth
    {
        get => GetProperty(() => IsColumnAutoWidth);
        set => SetProperty(() => IsColumnAutoWidth, value);
    }

    /// <summary>
    /// 캡처 상한(fps). 이 값을 넘는 프레임은 세션이 아예 처리하지 않는다 —
    /// 리드백도 미리보기도 타지 않으므로 그만큼 일이 준다.
    /// </summary>
    public int CaptureTargetFps
    {
        get => GetProperty(() => CaptureTargetFps);
        set => SetProperty(() => CaptureTargetFps, value, OnCaptureTargetFpsChanged);
    }

    /// <summary>캡처/미리보기 상한 콤보에 함께 쓰는 값들. 5 단위로 60 까지.</summary>
    public virtual ObservableCollection<int> FpsOptions { get; set; } = new(new[] { 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60 });

    /// <summary>
    /// 미리보기에서 누른 것을 대상 창으로 넘길지.
    ///
    /// 기본은 꺼 둔다. 켜져 있으면 미리보기를 클릭하는 순간 실제 창이 눌리므로,
    /// 화면만 보려던 참에 잘못 누르는 일이 생긴다.
    /// </summary>
    public bool IsInputForwardingEnabled
    {
        get => GetProperty(() => IsInputForwardingEnabled);
        set => SetProperty(() => IsInputForwardingEnabled, value);
    }

    /// <summary>
    /// 클릭을 넘긴 뒤 이 앱으로 돌아올지.
    ///
    /// 클릭은 커서를 대상 위로 옮겨 놓고 일어난다. 그대로 두면 커서도 포커스도
    /// 대상 쪽에 남아서 미리보기를 다시 누를 수 없다. 켜 두면 클릭이 대상에 닿은 뒤
    /// 커서를 제자리로 돌리고 이 창을 다시 앞으로 가져온다.
    ///
    /// 대상을 계속 조작하려면 끄면 된다 — 그때는 대상 창이 앞에 남는다.
    /// </summary>
    public bool IsReturnFocusAfterClickEnabled
    {
        get => GetProperty(() => IsReturnFocusAfterClickEnabled);
        set => SetProperty(() => IsReturnFocusAfterClickEnabled, value);
    }

    /// <summary>
    /// 미리보기를 누르면 그 자리의 UI 요소가 무엇인지 알아본다.
    ///
    /// 켜져 있는 동안은 입력을 전달하지 않는다. 자동화를 짜기 전에
    /// "이 버튼의 식별자가 뭐지" 를 눈으로 확인하는 용도다.
    /// </summary>
    public bool IsElementInspectEnabled
    {
        get => GetProperty(() => IsElementInspectEnabled);
        set => SetProperty(() => IsElementInspectEnabled, value, OnElementInspectEnabledChanged);
    }

    /// <summary>
    /// 입력 전달을 켤 수 있는 상태인지.
    ///
    /// 요소 검사가 켜져 있으면 클릭이 조회로 가로채여 입력이 나가지 않는다.
    /// 그 사실이 화면에 안 보이면 "입력 전달을 켰는데 왜 안 되지" 로 헤매게 된다.
    /// 실제로 그렇게 헤맸다.
    /// </summary>
    public bool IsInputForwardingAvailable => !IsElementInspectEnabled;

    private void OnElementInspectEnabledChanged()
    {
        RaisePropertyChanged(() => IsInputForwardingAvailable);

        if (IsElementInspectEnabled)
            StatusText = "요소 검사 중 - 미리보기 클릭은 조회로만 쓰인다. 입력을 보내려면 요소 검사를 끈다.";
    }

    /// <summary>콤보에 채울 입력 경로 목록.</summary>
    public virtual ObservableCollection<InputBackend> InputBackends { get; set; }
        = new((InputBackend[])Enum.GetValues(typeof(InputBackend)));

    /// <summary>
    /// 지금 쓰는 입력 경로. 바꾸면 돌아가는 중에도 바로 갈아끼운다.
    ///
    /// SendInput  - 거의 모든 대상에 통하지만 대상이 앞으로 나오고 커서가 옮겨간다.
    /// PostMessage - 포커스도 커서도 안 뺏기지만 창 메시지를 보는 대상에만 통한다.
    /// </summary>
    public InputBackend SelectedInputBackend
    {
        get => GetProperty(() => SelectedInputBackend);
        set => SetProperty(() => SelectedInputBackend, value, OnSelectedInputBackendChanged);
    }

    public DelegateCommand<MouseButtonEventArgs> OnPreviewMouseDownCommand { get; private set; } = null!;
    public DelegateCommand<MouseWheelEventArgs> OnPreviewMouseWheelCommand { get; private set; } = null!;
    public DelegateCommand<KeyEventArgs> OnPreviewKeyDownCommand { get; private set; } = null!;
    public DelegateCommand<KeyEventArgs> OnPreviewKeyUpCommand { get; private set; } = null!;

    /// <summary>미리보기 갱신 상한(fps). 캡처 상한과 별개다 — 캡처는 받고 화면에만 덜 올릴 수 있다.</summary>
    public int PreviewTargetFps
    {
        get => GetProperty(() => PreviewTargetFps);
        set => SetProperty(() => PreviewTargetFps, value, OnPreviewTargetFpsChanged);
    }

    /// <summary>미리보기가 실제로 초당 몇 장 올라갔는지.</summary>
    public int PreviewFps
    {
        get => GetProperty(() => PreviewFps);
        set => SetProperty(() => PreviewFps, value, () => PreviewFpsText = $"실제 {value} fps");
    }

    /// <summary>
    /// 툴바에 그대로 뿌리는 문자열.
    /// DevExpress TextEdit 은 StringFormat 이 먹지 않아서 문자열을 만들어 넘긴다.
    /// </summary>
    public string? PreviewFpsText
    {
        get => GetProperty(() => PreviewFpsText);
        set => SetProperty(() => PreviewFpsText, value);
    }

    public virtual ObservableCollection<CaptureTarget> Targets { get; set; } = new();

    public virtual ObservableCollection<FrameLogRow> Rows { get; set; } = new();

    public static CaptureMonitorViewModel Create() => ViewModelSource.Create(() => new CaptureMonitorViewModel());

    public CaptureMonitorViewModel()
    {
        Caption = "캡처 모니터";
        PreviewTargetFps = 60;
        CaptureTargetFps = 60;
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/screen.png");

        // 탭을 닫으면 화면은 사라져도 캡처 세션은 남는다. 여기서 끊어 준다.
        OnUnloadedCommand = new DelegateCommand(DisposeSession, false);
        RefreshTargetsCommand = new DelegateCommand(RefreshTargets, () => !IsRunning, false);
        DoStartCommand = new DelegateCommand(DoStart, () => !IsRunning && SelectedTarget is not null, false);
        DoStopCommand = new DelegateCommand(DoStop, () => IsRunning, false);
        DoClearCommand = new DelegateCommand(DoClear, false);
        SaveFrameCommand = new DelegateCommand(DoSaveFrame, () => IsRunning && EnableCpuReadback, false);
        CollectFrameCommand = new DelegateCommand(DoCollectFrame, () => IsRunning, false);
        ClickDetectionCommand = new DelegateCommand(DoClickDetection, () => Detections.Count > 0, false);

        OnPreviewMouseDownCommand = new DelegateCommand<MouseButtonEventArgs>(OnPreviewMouseDown, false);
        OnPreviewMouseWheelCommand = new DelegateCommand<MouseWheelEventArgs>(OnPreviewMouseWheel, false);
        OnPreviewKeyDownCommand = new DelegateCommand<KeyEventArgs>(OnPreviewKeyDown, false);
        OnPreviewKeyUpCommand = new DelegateCommand<KeyEventArgs>(OnPreviewKeyUp, false);

        _inputRouter = new PreviewInputRouter(
            () => SelectedTarget,
            InputAdapterFactory.Create(SelectedInputBackend, GetTargetWindowHandle));

        _uiAutomation = UiAutomationAdapterFactory.Create();

        // 돌아오기는 켜 둔다. 꺼져 있으면 한 번 클릭한 뒤 이 앱이 대상 창 뒤로 숨는다.
        IsReturnFocusAfterClickEnabled = true;
    }

    /// <summary>XAML 의 컨트롤을 잡아 온다. 베이스가 초기화 첫 단계에서 불러 준다.</summary>
    protected override void InitializeControls()
    {
        _previewLayoutGroup = FindControl<LayoutGroup>("PreviewGroupObjectService");
        _previewImage = FindControl<System.Windows.Controls.Image>("PreviewImageObjectService");
        _previewSurface = FindControl<System.Windows.Controls.Border>("PreviewSurfaceObjectService");
        _gridView = FindControl<DevExpress.Xpf.Grid.GridControl>("GridObjectService")?.View as DevExpress.Xpf.Grid.TableView;
    }

    /// <summary>지난번에 쓰던 설정을 되살린다. 베이스가 OnLoaded 직전에 불러 준다.</summary>
    protected override void RestoreSettings()
    {
        _lastPreviewGroupHeight = GetSetting(nameof(_lastPreviewGroupHeight), DefaultPreviewHeight);

        // 켜진 채로 복구하지 않는다 - 화면을 열자마자 모델 68MB 를 읽으면 뜨는 것이 느려진다.
        DetectMinimumScore = GetSetting(nameof(DetectMinimumScore), 0.5);

        if (_lastPreviewGroupHeight < 80)
            _lastPreviewGroupHeight = DefaultPreviewHeight;

        if (_previewLayoutGroup is not null)
            _previewLayoutGroup.Height = _lastPreviewGroupHeight;

        // 한글을 그대로 넣으면 설정 파일에서 되읽을 때 깨진다(실측으로 "[紐⑤땲.." 로 나왔다).
        // Base64 로 감싸서 ASCII 로만 저장한다.
        // 이 방식 이전에 저장된 값은 생 문자열이므로 그때는 그대로 쓴다.
        var savedTarget = GetSetting(nameof(SelectedTarget), string.Empty);

        _lastSelectedTargetDisplay = Base64Utility.IsBase64(savedTarget)
            ? Base64Utility.Decode(savedTarget)
            : savedTarget;

        CaptureTargetFps = GetSetting(nameof(CaptureTargetFps), 60);
        PreviewTargetFps = GetSetting(nameof(PreviewTargetFps), 60);

        IsElementInspectEnabled = GetSetting(nameof(IsElementInspectEnabled), false);

        SelectedInputBackend = Enum.TryParse<InputBackend>(
            GetSetting(nameof(SelectedInputBackend), nameof(InputBackend.SendInput)), out var backend)
            ? backend
            : InputBackend.SendInput;
        ShowPreview = GetSetting(nameof(ShowPreview), false);

        // 자동 너비와 배치 복원은 반드시 이 순서로, 그리드가 자리를 잡은 뒤에 넣는다.
        //
        // IsColumnAutoWidth 는 첨부 속성을 거쳐 ApplyColumnAutoWidth 를 부르는데,
        // 그게 모든 열의 Width 를 "지금 그려진 너비(ActualWidth)"로 고정해 버린다.
        // 배치를 먼저 복원해도 이게 나중에 돌면 복원한 너비가 그대로 지워진다.
        // ContextIdle 은 Background 보다 낮다. 그리드 로딩과 그에 딸린 바인딩 적용
        // (IsColumnAutoWidth -> ApplyColumnAutoWidth, 이게 열 너비를 다시 쓴다)이
        // 모두 끝난 뒤에 우리 배치를 얹기 위해 이 우선순위를 쓴다.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                IsColumnAutoWidth = GetSetting(nameof(IsColumnAutoWidth), false);
                RestoreGridLayout();
            }));
    }

    /// <summary>
    /// 지난번 그리드 상태를 되돌린다.
    ///
    /// 저장본이 깨져 있거나 열 구성이 바뀌었으면 예외가 난다. 그때는 그냥 기본 배치로 둔다 —
    /// 그리드 하나 때문에 화면 전체가 안 열리면 곤란하다.
    /// </summary>
    private void RestoreGridLayout()
    {
        if (GetSetting(nameof(GridLayoutVersion), 0) != GridLayoutVersion)
        {
            Logger.Debug("그리드 열 구성이 바뀌었다. 저장된 배치를 버리고 기본으로 시작한다.");
            return;
        }

        var layout = GetSetting(nameof(GridLayoutService), string.Empty);
        if (string.IsNullOrEmpty(layout))
            return;

        try
        {
            GridLayoutService.Deserialize(layout);

            var grid = FindControl<DevExpress.Xpf.Grid.GridControl>("GridObjectService");
            var widths = grid is null
                ? "(그리드 못 잡음)"
                : string.Join(", ", grid.Columns.Select(column =>
                    $"{column.FieldName}:{column.Width.Value}/{column.Width.UnitType}/실제{column.ActualWidth:n0}"));

            Logger.Debug($"그리드 상태 복원 완료. 너비=[{widths}]");

        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "그리드 상태 복원 실패. 기본 배치로 시작한다.");
        }
    }

    protected override void SaveSettings()
    {
        // 켜져 있을 때의 높이만 의미가 있다. 꺼져 있으면 그룹이 숨겨져 있어 값이 미덥지 않다.
        if (ShowPreview && _previewLayoutGroup is { Height: > 0 })
            _lastPreviewGroupHeight = _previewLayoutGroup.Height;

        SetSetting(nameof(_lastPreviewGroupHeight), _lastPreviewGroupHeight);
        SetSetting(nameof(ShowPreview), ShowPreview);
        SetSetting(nameof(CaptureTargetFps), CaptureTargetFps);
        SetSetting(nameof(PreviewTargetFps), PreviewTargetFps);
        SetSetting(nameof(IsColumnAutoWidth), IsColumnAutoWidth);
        SetSetting(nameof(SelectedInputBackend), SelectedInputBackend.ToString());
        SetSetting(nameof(IsElementInspectEnabled), IsElementInspectEnabled);

        if (SelectedTarget is not null)
            SetSetting(nameof(SelectedTarget), Base64Utility.Encode(SelectedTarget.Display));

        try
        {
            SetSetting(nameof(GridLayoutService), GridLayoutService.Serialize());
            SetSetting(nameof(GridLayoutVersion), GridLayoutVersion);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "그리드 상태 저장 실패");
        }
    }

    protected override void OnLoaded()
    {
        Logger.Trace(string.Empty);

        if (!WgcCaptureSession.IsSupported)
        {
            StatusText = "이 Windows 에서는 Windows.Graphics.Capture 를 쓸 수 없다. (Windows 10 2004 이상 필요)";
            return;
        }

        RefreshTargets();
    }

    /// <summary>캡처할 수 있는 창과 모니터를 다시 훑는다.</summary>
    private void RefreshTargets()
    {
        try
        {
            var previouslySelected = SelectedTarget;

            Targets.Clear();

            // 모니터를 위에 둔다. 전체화면 게임은 결국 이쪽으로 잡는 경우가 많다.
            foreach (var monitor in CaptureTarget.EnumerateMonitors())
                Targets.Add(monitor);

            foreach (var window in CaptureTarget.EnumerateWindows().OrderBy(target => target.ProcessName).ThenBy(target => target.Title))
                Targets.Add(window);

            // ① 방금 전까지 보던 것 → ② 지난 실행에서 고른 것 → ③ 목록의 첫 번째
            SelectedTarget = Targets.FirstOrDefault(target => target.Handle == previouslySelected?.Handle && target.Kind == previouslySelected.Kind)
                             ?? Targets.FirstOrDefault(target => target.Display == _lastSelectedTargetDisplay)
                             ?? Targets.FirstOrDefault();

            var hasSavedTarget = Targets.Any(target => target.Display == _lastSelectedTargetDisplay);
            Logger.Debug($"대상 복구: 저장='{_lastSelectedTargetDisplay}' / 후보 {Targets.Count}개 / 일치 {hasSavedTarget} / 선택='{SelectedTarget?.Display}'");

            StatusText = $"대상 {Targets.Count}개 (모니터 + 창)";
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void DoStart()
    {
        if (SelectedTarget is null)
            return;

        try
        {
            ResetStats();

            _uiDispatcher = GetService<IDispatcherService>();
            if (_uiDispatcher is null)
            {
                StatusText = "IDispatcherService 가 없다. View 에 dxmvvm:DispatcherService 를 등록할 것.";
                return;
            }

            _captureSession = ScreenCaptureAdapterFactory.Create(SelectedTarget, EnableCpuReadback);
            _captureSession.TargetFps = CaptureTargetFps;
            _captureSession.FrameArrived += OnFrameArrived;
            _captureSession.Notice += OnSessionNotice;
            _captureSession.Start();

            // 통계를 그리드로 옮기는 건 1초에 한 번. 콜백에서 직접 하면 UI 가 캡처를 붙잡는다.
            _statisticsFlushTimer = new Timer(_ => FlushStats(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            IsRunning = true;
            StatusText = $"캡처 중: {_captureSession.Target.Display}";
            MessengerUtility.SendMainMessage("캡처를 시작했습니다.");
        }
        catch (Exception ex)
        {
            DisposeSession();
            IsRunning = false;
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void DoStop()
    {
        try
        {
            DisposeSession();
            IsRunning = false;
            StatusText = "중지됨";
            MessengerUtility.SendMainMessage("캡처를 중지했습니다.");
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void DoClear() => Rows.Clear();

    /// <summary>
    /// 그리드를 항상 맨 위가 보이게 유지한다.
    ///
    /// 새 줄은 0번 자리에 끼워 넣는다. 그러면 보고 있던 줄이 한 칸씩 아래로 밀리고
    /// 포커스도 그 줄을 따라 내려가서, 가만히 둬도 화면이 계속 흘러내린다.
    /// 최신 줄을 보는 화면이므로 맨 위에 붙여 둔다.
    ///
    /// 값이 이미 0 일 때는 건드리지 않는다. 1초마다 같은 값을 다시 넣으면
    /// 그때마다 포커스 변경이 돌아 사용자가 고른 셀이 풀린다.
    /// </summary>
    private void KeepGridAtTop()
    {
        if (_gridView is null)
            return;

        if (_gridView.TopRowIndex != 0)
            _gridView.TopRowIndex = 0;

        if (_gridView.FocusedRowHandle != 0)
            _gridView.FocusedRowHandle = 0;
    }

    /// <summary>다음 프레임 한 장을 PNG 로 떨어뜨린다. 캡처 내용을 눈으로 확인하는 용도.</summary>
    private void DoSaveFrame() => Interlocked.Exchange(ref _isSaveFrameRequested, 1);

    /// <summary>
    /// 다음 프레임 한 장을 <b>데이터셋</b>에 담는다. 라벨링 화면이 그 폴더를 읽는다.
    /// </summary>
    /// <remarks>
    /// "프레임 저장" 과 나눠 둔 이유 - 저장은 캡처가 무엇을 잡고 있는지 눈으로 보는 용도라
    /// captures 폴더에 아무렇게나 쌓아도 된다. 담기는 학습에 쓸 것이라 이름 규칙과 폴더가
    /// 정해져 있어야 하고, 라벨 파일과 짝이 맞아야 한다.
    /// </remarks>
    private void DoCollectFrame()
    {
        // 픽셀이 CPU 로 안 내려오면 담을 것이 없다. 버튼을 회색으로 두고 이유를 안 알려 주면
        // "몹을 모을 수가 없다" 가 된다 - 실제로 그랬다. 알아서 켜고 그렇게 적는다.
        EnsureCpuReadback("데이터셋에 담으려면 픽셀이 필요합니다");

        Interlocked.Exchange(ref _isCollectFrameRequested, 1);
    }

    /// <summary>
    /// CPU 리드백이 꺼져 있으면 켠다. <b>도는 중이면 세션을 다시 시작한다.</b>
    /// </summary>
    /// <remarks>
    /// 리드백은 세션을 만들 때 정해진다(<see cref="ScreenCaptureAdapterFactory.Create"/>).
    /// 도는 중에 값만 바꾸면 아무것도 안 달라진다 - 몹 찾기에서 "알아서 켜 준다" 고 해 놓고
    /// 실제로는 헛것이었다. 껐다 켜는 것이 유일한 길이고, 통계 몇 초가 사라지는 것 말고는
    /// 잃는 것이 없다.
    /// </remarks>
    private void EnsureCpuReadback(string why)
    {
        if (EnableCpuReadback) return;

        EnableCpuReadback = true;

        if (!IsRunning)
        {
            StatusText = $"CPU 리드백을 켰습니다 - {why}.";
            return;
        }

        DoStop();
        DoStart();

        StatusText = $"CPU 리드백을 켜고 다시 시작했습니다 - {why}.";
    }

    // ── 캡처 콜백. 여기는 스레드풀이다 ────────────────────────────────────────

    private void OnFrameArrived(object? sender, CapturedFrameEventArgs e)
    {
        lock (_statisticsLock)
        {
            _frameCountInSecond++;
            _lastFrameId = e.FrameId;
            _latencyMsSum += e.LatencyMs;
            _readbackMsSum += e.ReadbackMs;

            if (e.LatencyMs > _latencyMsMax)
                _latencyMsMax = e.LatencyMs;

            _lastFrameWidth = e.Width;
            _lastFrameHeight = e.Height;
        }

        if (Interlocked.CompareExchange(ref _isSaveFrameRequested, 0, 1) == 1)
            TrySaveFrame(e);

        if (Interlocked.CompareExchange(ref _isCollectFrameRequested, 0, 1) == 1)
            TryCollectFrame(e);

        // 0.25초에 한 번만, 앞의 것이 끝났을 때만. 여기서 기다리면 프레임이 밀린다.
        MaybeDetect(e);

        if (ShowPreview)
            TryPushPreview(e);
    }

    // ── 미리보기 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 프레임 한 장을 미리보기 버퍼로 옮기고 UI 에 넘긴다. 여기는 스레드풀이다.
    ///
    /// 두 가지로 걸러 낸다.
    ///   ① 목표 간격이 안 됐으면 건너뛴다
    ///   ② UI 가 앞 장을 아직 그리는 중이면 버린다
    /// ②를 큐로 쌓으면 지연만 늘고 결국 못 따라간다. 최신 한 장만 보여 주는 게 맞다.
    /// </summary>
    private void TryPushPreview(CapturedFrameEventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastPreviewTimestamp < _previewMinimumIntervalTicks)
            return;

        // GPU 경로는 복사가 GPU 안에서 끝나므로 UI 상태를 볼 필요가 없다.
        if (!_isGpuPreviewUnavailable)
        {
            _lastPreviewTimestamp = now;
            PushPreviewOnGpu(e);
            return;
        }

        // 폴백 경로는 CPU 로 옮기는 비용이 커서 UI 가 앞 장을 그리는 중이면 건너뛴다.
        if (!e.HasPixels)
            return;

        if (Interlocked.CompareExchange(ref _isCpuPreviewBlitInProgress, 1, 0) != 0)
            return;

        _lastPreviewTimestamp = now;
        PushPreviewOnCpu(e);
    }

    /// <summary>
    /// GPU 경로. 캡처 텍스처를 공유 표면으로 복사하고 UI 에는 "바뀌었다"만 알린다.
    /// 픽셀이 CPU 로 내려오지 않으므로 CPU 리드백이 꺼져 있어도 된다.
    /// </summary>
    private void PushPreviewOnGpu(CapturedFrameEventArgs e)
    {
        try
        {
            var bridge = _gpuPreviewBridge;

            // 표면이 아직 없거나 해상도가 바뀌었으면 UI 스레드에서 만들어야 한다.
            // 만드는 동안 들어오는 프레임은 건너뛴다 — 한두 장이다.
            if (bridge is null || !bridge.IsReady)
            {
                RequestPreviewSurface(e.Width, e.Height);
                return;
            }

            var context = _captureSession?.Context;
            if (context is null || !bridge.CopyFrom(context, e.Texture))
                return;

            // 렌더 이벤트 안에서 화면 반영을 하면 WPF 가 쥔 잠금과 부딪힌다.
            // Render 우선순위로 따로 넣어 그리기 직전에 처리되게 한다.
            Interlocked.Exchange(ref _hasUnpresentedGpuFrame, 1);
            SchedulePresent();
        }
        catch (Exception ex)
        {
            FallBackToCpuPreview(ex);
        }
    }

    /// <summary>UI 스레드에서 공유 표면을 만든다. 겹쳐 요청하지 않는다.</summary>
    private void RequestPreviewSurface(int width, int height)
    {
        if (_isPreviewSurfaceRequestPending)
            return;

        _isPreviewSurfaceRequestPending = true;

        _uiDispatcher?.BeginInvoke(() =>
        {
            try
            {
                var device = _captureSession?.Device;
                if (device is null)
                    return;

                _gpuPreviewBridge ??= new D3DImageBridge();

                if (_gpuPreviewBridge.EnsureSurface(device, width, height))
                {
                    PreviewImage = _gpuPreviewBridge.Image;
                    HookPreviewRendering(true);
                }
                else
                {
                    FallBackToCpuPreview(null);
                }
            }
            catch (Exception ex)
            {
                FallBackToCpuPreview(ex);
            }
            finally
            {
                _isPreviewSurfaceRequestPending = false;
            }
        });
    }

    /// <summary>
    /// WPF 가 한 프레임 그릴 때마다 불린다(모니터 주사율, 보통 초당 60회).
    /// 새 프레임이 와 있으면 그때 화면에 반영한다.
    ///
    /// 갱신 주기를 화면 주사율에 맞추는 것이 요점이다. 캡처 프레임마다 디스패처로
    /// 밀어 넣으면 그 대기가 곧 상한이 된다 — 그 방식으로는 55fps 에서 막혔다.
    /// </summary>
    /// <summary>
    /// 화면 반영을 UI 스레드에 건다. 이미 걸려 있으면 겹쳐 넣지 않는다.
    ///
    /// 데이터 표시(_hasUnpresentedGpuFrame)와 스케줄 여부(_isPresentScheduled)를 따로 둔 이유가 있다.
    /// 하나로 합쳤더니, TryLock 이 한 번 실패해 표시를 되돌려 놓는 순간
    /// "이미 표시가 서 있으니 새로 걸지 않는다"가 되어 루프가 영구히 멈췄다(60fps -> 0).
    /// </summary>
    private void SchedulePresent()
    {
        if (Interlocked.Exchange(ref _isPresentScheduled, 1) != 0)
            return;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(PresentPreview));
    }

    private void PresentPreview()
    {
        Interlocked.Exchange(ref _isPresentScheduled, 0);
        Interlocked.Increment(ref _presentAttemptCountInSecond);

        if (Interlocked.Exchange(ref _hasUnpresentedGpuFrame, 0) == 0)
            return;

        try
        {
            if (_gpuPreviewBridge?.Present() == true)
            {
                Interlocked.Increment(ref _presentedFrameCountInSecond);
                return;
            }

            // WPF 가 표면을 쓰는 중이었다. 이 장은 버린다.
            //
            // 여기서 곧바로 다시 걸면 라이브락이 된다. 실제로 그렇게 만들었더니
            // 화면 반영 시도가 초당 49,000회까지 치솟았다 — Render 우선순위로 꽉 채우는 바람에
            // WPF 가 정작 그리질 못하고, 그래서 표면도 영영 안 풀려서 미리보기가 0fps 로 죽었다.
            //
            // 다음 캡처 프레임(16.7ms 뒤)이 어차피 새로 걸어 준다. 그 박자에 맡긴다.
        }
        catch (Exception ex)
        {
            FallBackToCpuPreview(ex);
        }
    }

    /// <summary>
    /// 화면이 계속 그려지도록 붙잡아 두는 빈 핸들러.
    /// D3DImage 의 갱신만으로는 WPF 가 렌더 루프를 계속 돌린다는 보장이 없다.
    /// </summary>
    private void OnKeepRendering(object? sender, EventArgs e)
    {
    }

    /// <summary>
    /// 렌더 시점 구독을 붙였다 뗀다. UI 스레드에서만 만진다.
    /// 떼는 것을 빠뜨리면 미리보기를 꺼도 매 프레임 핸들러가 돌고,
    /// ViewModel 이 CompositionTarget 에 붙들려 탭을 닫아도 살아남는다.
    /// </summary>
    private void HookPreviewRendering(bool hook)
    {
        if (hook == _isRenderLoopHooked)
            return;

        _isRenderLoopHooked = hook;

        if (hook)
            CompositionTarget.Rendering += OnKeepRendering;
        else
            CompositionTarget.Rendering -= OnKeepRendering;
    }

    /// <summary>
    /// GPU 경로를 못 쓰는 환경(원격 데스크톱, 하드웨어 가속 없음)에서 느린 경로로 돌아간다.
    /// 그쪽은 CPU 리드백이 있어야 하므로 켜 준다.
    /// </summary>
    private void FallBackToCpuPreview(Exception? ex)
    {
        if (_isGpuPreviewUnavailable)
            return;

        _isGpuPreviewUnavailable = true;

        HookPreviewRendering(false);

        if (ex is not null)
            Logger.Warn(ex, "GPU 미리보기를 쓸 수 없다. CPU 경로로 전환한다.");
        else
            Logger.Warn("GPU 미리보기를 쓸 수 없다. CPU 경로로 전환한다.");

        Note("GPU 미리보기를 못 써서 CPU 경로로 전환했다.");

        PreviewImage = null;

        _gpuPreviewBridge?.Dispose();
        _gpuPreviewBridge = null;

        if (!EnableCpuReadback)
            Note("CPU 리드백을 켜고 다시 시작해야 미리보기가 나온다.");
    }

    /// <summary>폴백 경로. 픽셀을 CPU 에서 옮겨 WriteableBitmap 에 올린다.</summary>
    private void PushPreviewOnCpu(CapturedFrameEventArgs e)
    {
        try
        {

            // 정수 배수로만 솎아 낸다. 인덱스 계산이 곱셈 하나로 끝나고 화질도 충분하다.
            int step = Math.Max(1, (int)Math.Ceiling(e.Height / (double)PreviewMaxHeight));

            int width = e.Width / step;
            int height = e.Height / step;
            int stride = width * 4;
            int required = stride * height;

            if (_cpuPreviewBuffer is null || _cpuPreviewBuffer.Length < required)
                _cpuPreviewBuffer = new byte[required];

            // RowPitch 는 Width*4 보다 클 수 있다(GPU 정렬). 원본에서 step 간격으로 집어 온다.
            unsafe
            {
                var source = (byte*)e.PixelData;

                fixed (byte* destinationStart = _cpuPreviewBuffer)
                {
                    if (step == 1 && e.RowPitch == stride)
                    {
                        Buffer.MemoryCopy(source, destinationStart, required, required);
                    }
                    else
                    {
                        for (int y = 0; y < height; y++)
                        {
                            var sourceRow = (uint*)(source + (long)(y * step) * e.RowPitch);
                            var destinationRow = (uint*)(destinationStart + (long)y * stride);

                            for (int x = 0; x < width; x++)
                                destinationRow[x] = sourceRow[x * step];
                        }
                    }
                }
            }

            _cpuPreviewWidth = width;
            _cpuPreviewHeight = height;

            _uiDispatcher?.BeginInvoke(BlitPreview);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _isCpuPreviewBlitInProgress, 0);
            Logger.Error(ex, "미리보기 프레임 복사 실패");
        }
    }

    /// <summary>버퍼를 WriteableBitmap 에 올린다. UI 스레드.</summary>
    private void BlitPreview()
    {
        try
        {
            var buffer = _cpuPreviewBuffer;
            if (buffer is null)
                return;

            int width = _cpuPreviewWidth;
            int height = _cpuPreviewHeight;

            // 해상도가 바뀌면(대상 변경 등) 비트맵을 새로 만든다.
            if (_cpuPreviewBitmap is null ||
                _cpuPreviewBitmap.PixelWidth != width ||
                _cpuPreviewBitmap.PixelHeight != height)
            {
                _cpuPreviewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                PreviewImage = _cpuPreviewBitmap;
            }

            _cpuPreviewBitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, width * 4, 0);
            Interlocked.Increment(ref _presentedFrameCountInSecond);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "미리보기 갱신 실패");
        }
        finally
        {
            Interlocked.Exchange(ref _isCpuPreviewBlitInProgress, 0);
        }
    }

    /// <summary>목표 fps 가 바뀌면 스로틀 간격을 다시 잡는다.</summary>
    private void OnPreviewTargetFpsChanged()
    {
        var fps = Math.Clamp(PreviewTargetFps, 1, 240);

        // 목표 주기의 90%. 캡처 주기와 경계가 겹쳐 절반이 버려지는 것을 막는다.
        _previewMinimumIntervalTicks = Stopwatch.Frequency * 9 / (fps * 10);
    }

    /// <summary>캡처 상한이 바뀌면 돌고 있는 세션에 바로 반영한다.</summary>
    private void OnCaptureTargetFpsChanged()
    {
        if (_captureSession is not null)
            _captureSession.TargetFps = CaptureTargetFps;
    }

    // ── 미리보기 입력 전달 ────────────────────────────────────────────────

    /// <summary>
    /// 미리보기에서 일어난 마우스 이벤트를 넘겨도 되는 상태인지.
    /// 전달이 꺼져 있거나, 캡처 중이 아니거나, 컨트롤을 못 잡았으면 아무것도 하지 않는다.
    /// </summary>
    private bool CanForwardInput => IsInputForwardingEnabled && IsRunning && _previewImage is not null && _inputRouter is not null;

    /// <summary>미리보기 Image 컨트롤의 현재 크기와 캡처 원본 크기.</summary>
    private (System.Windows.Size Control, System.Windows.Size Source) PreviewSizes => (
        new System.Windows.Size(_previewImage!.ActualWidth, _previewImage.ActualHeight),
        new System.Windows.Size(_lastFrameWidth, _lastFrameHeight));

    /// <summary>
    /// 미리보기를 눌렀다. 대상 창의 같은 자리를 누르고 뗀다.
    ///
    /// 세 단계로 나뉜다.
    ///   ① 좌표를 풀고 그 자리의 창을 앞으로 가져온다
    ///   ② 포그라운드 전환이 반영될 때까지 잠깐 기다린다
    ///   ③ 누르고 뗀다 (한 번의 SendInput 으로 같이 보낸다)
    ///
    /// ②가 필요한 이유는 SetForegroundWindow 가 바로 돌아오기 때문이다. 전환이
    /// 끝나기 전에 클릭이 도착하면 대상은 그것을 활성화 클릭으로 먹고 아무 일도 안 한다.
    /// 실제로 이것 때문에 아무리 눌러도 반응이 없었다.
    ///
    /// 누름과 뗌을 나눠 보내지 않는 이유는, 누르는 순간 진짜 커서가 대상 창으로
    /// 옮겨 가서 사용자가 버튼을 떼는 것을 이 화면이 못 보기 때문이다.
    /// </summary>
    private async void OnPreviewMouseDown(MouseButtonEventArgs args)
    {
        // 전달이 꺼져 있어도 포커스는 준다. 켜자마자 키가 들어오게 하려는 것이다.
        // Image 는 Focusable 이 아니라서 여기가 아니라 Border 를 잡아야 한다.
        _previewSurface?.Focus();

        // 요소 검사가 켜져 있으면 입력을 보내지 않고 무엇인지만 알아본다.
        if (IsElementInspectEnabled)
        {
            InspectElementAt(args.GetPosition(_previewImage));
            args.Handled = true;
            return;
        }

        if (!CanForwardInput || _isForwardingClick)
            return;

        args.Handled = true;

        var (control, source) = PreviewSizes;
        var button = ToBackendButton(args.ChangedButton);
        var pointInControl = args.GetPosition(_previewImage);

        _isForwardingClick = true;

        try
        {
            var prepared = _inputRouter!.PrepareClick(
                pointInControl, control, source, out var screenPoint, out var didActivate);

            if (prepared != Capture.Input.InputForwardResult.Sent)
            {
                ReportInputForward(prepared, "클릭");
                return;
            }

            await SendClickAsync(screenPoint, didActivate, button, "클릭");
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }
        finally
        {
            _isForwardingClick = false;
        }
    }

    /// <summary>
    /// 화면 좌표 한 곳을 누른다. 끌어올린 뒤 기다리는 것과 포커스를 되돌리는 것까지.
    /// </summary>
    /// <remarks>
    /// 미리보기를 손으로 누른 것과 모델이 찾아낸 몹을 누르는 것이 <b>같은 길</b>을 타야 한다.
    /// 두 벌로 두면 한쪽만 고쳐져 손으로 누를 때는 되는데 자동으로는 안 되는 일이 생긴다.
    /// </remarks>
    private async System.Threading.Tasks.Task<Capture.Input.InputForwardResult> SendClickAsync(
        Point screenPoint, bool didActivate, Input.MouseButton button, string what)
    {
        // 커서를 옮기기 전에 지금 자리를 적어 둔다. 돌아올 때 쓴다.
        var cursorBeforeClick = _inputRouter!.InputAdapter.GetCursorPosition();

        // 창을 새로 끌어올렸을 때만 기다린다. 이미 앞에 있었으면 곧바로 누른다.
        if (didActivate)
            await System.Threading.Tasks.Task.Delay(ActivationSettleDelayMs);

        var result = _inputRouter.ClickAt(screenPoint, button);
        ReportInputForward(result, what);

        if (result == Capture.Input.InputForwardResult.Sent && IsReturnFocusAfterClickEnabled)
            await ReturnToThisWindowAsync(cursorBeforeClick);

        return result;
    }

    /// <summary>
    /// 클릭이 대상에 닿은 뒤 커서와 포커스를 이 앱으로 되돌린다.
    ///
    /// 곧바로 되돌리지 않고 조금 기다린다. 누름·뗌은 이미 커널 입력 큐에 들어가 있지만,
    /// 대상 프로그램이 그것을 꺼내 처리하면서 커서 위치를 따로 읽는 경우가 있다.
    /// 그 전에 커서를 빼 버리면 클릭이 엉뚱한 자리에 찍힌 것으로 보인다.
    /// </summary>
    private async System.Threading.Tasks.Task ReturnToThisWindowAsync((int X, int Y)? cursorBeforeClick)
    {
        await System.Threading.Tasks.Task.Delay(ReturnToThisWindowDelayMs);

        if (cursorBeforeClick is { } cursor)
            _inputRouter?.InputAdapter.MoveMouseTo(cursor.X, cursor.Y);

        Application.Current?.MainWindow?.Activate();
        _previewSurface?.Focus();
    }

    /// <summary>
    /// 휠은 그 자리로 옮긴 뒤 굴린다.
    ///
    /// 마우스 이동은 전달하지 않는다. 전달하면 미리보기 위를 지나가기만 해도 진짜 커서가
    /// 대상 화면으로 끌려가서 이 앱을 조작할 수 없게 된다. 누른 순간에만 옮긴다.
    /// </summary>
    private void OnPreviewMouseWheel(MouseWheelEventArgs args)
    {
        if (!CanForwardInput)
            return;

        var (control, source) = PreviewSizes;

        ReportInputForward(_inputRouter!.TryScroll(args.GetPosition(_previewImage), control, source, args.Delta), "휠");
        args.Handled = true;
    }

    private void OnPreviewKeyDown(KeyEventArgs args)
    {
        if (!CanForwardInput)
            return;

        ReportInputForward(_inputRouter!.SendKey((ushort)KeyInterop.VirtualKeyFromKey(args.Key), isKeyUp: false), $"키↓ {args.Key}");
        args.Handled = true;
    }

    private void OnPreviewKeyUp(KeyEventArgs args)
    {
        if (!CanForwardInput)
            return;

        ReportInputForward(_inputRouter!.SendKey((ushort)KeyInterop.VirtualKeyFromKey(args.Key), isKeyUp: true), $"키↑ {args.Key}");
        args.Handled = true;
    }

    /// <summary>
    /// 미리보기에서 누른 자리에 무엇이 있는지 알아본다.
    ///
    /// 좌표를 화면 좌표로 바꾼 다음 UI Automation 에 묻는다.
    /// 한 번 부를 때마다 프로세스 경계를 넘어가므로 클릭했을 때만 부른다 —
    /// 주기적으로 훑으면 미리보기 fps 가 떨어진다(실측으로 확인했다).
    /// </summary>
    private void InspectElementAt(System.Windows.Point pointInControl)
    {
        if (_inputRouter is null || _uiAutomation is null || _previewImage is null)
            return;

        var (control, source) = PreviewSizes;

        var mapped = _inputRouter.TryResolveScreenPoint(pointInControl, control, source, out var screenPoint);
        if (mapped != Capture.Input.InputForwardResult.Sent)
        {
            ReportInputForward(mapped, "요소 검사");
            return;
        }

        if (!_uiAutomation.TryGetElementAt((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y), out var element))
        {
            StatusText = $"요소를 못 찾았다 ({screenPoint.X:n0}, {screenPoint.Y:n0}) - 접근성 정보를 내놓지 않는 화면이다";
            return;
        }

        StatusText = $"요소: {element}  ({element.Bounds.Width:n0}×{element.Bounds.Height:n0}"
                     + (element.IsEnabled ? string.Empty : ", 비활성") + ")";

        Logger.Debug($"요소 검사: {element} bounds={element.Bounds} enabled={element.IsEnabled}");
    }

    /// <summary>
    /// 입력 경로를 갈아끼운다. 라우터는 어댑터를 물고만 있으므로 통째로 바꿔 주면 된다.
    /// </summary>
    private void OnSelectedInputBackendChanged()
    {
        if (_inputRouter is null)
            return;

        // 드라이버가 있어야 도는 경로(Interception)는 만들어 봐야 쓸 수 있는지 알 수 있다.
        var selection = InputAdapterFactory.CreateWithFallback(
            SelectedInputBackend, InputBackend.SendInput, GetTargetWindowHandle);

        // 이전 어댑터를 버리지 않으면 드라이버 컨텍스트가 그대로 샌다.
        _inputRouter.InputAdapter.Dispose();
        _inputRouter.InputAdapter = selection.Adapter;

        if (selection.FellBack)
        {
            // 조용히 다른 경로로 보내면 안 된다. 무엇이 왜 밀려났는지 그대로 띄운다.
            SelectedInputBackend = InputBackend.SendInput;
            StatusText = $"{selection.FellBackFrom} 을 쓸 수 없다 - {selection.Reason} "
                       + $"→ {_inputRouter.AdapterName} 으로 바꿨다";
            Logger.Warn($"{selection.FellBackFrom} 사용 불가: {selection.Reason}");
            return;
        }

        StatusText = $"입력 경로: {_inputRouter.AdapterName}";
        Logger.Debug($"입력 경로를 {_inputRouter.AdapterName} 으로 바꿨다.");
    }

    /// <summary>PostMessage 경로가 메시지를 넣을 창. 대상이 모니터면 보낼 곳이 없다.</summary>
    private IntPtr GetTargetWindowHandle()
        => SelectedTarget is { Kind: CaptureTargetKind.Window } target ? target.Handle : IntPtr.Zero;

    /// <summary>
    /// 전달이 안 됐으면 왜 안 됐는지 상태 줄에 띄운다.
    /// 조용히 넘기면 "클릭이 안 된다" 는 것만 보이고 이유를 알 수 없다.
    /// </summary>
    private void ReportInputForward(Capture.Input.InputForwardResult result, string inputKind)
    {
        if (result == Capture.Input.InputForwardResult.Sent)
        {
            // 성공도 남긴다. 이게 없으면 "보냈는데 대상이 안 받은" 것과
            // "애초에 안 보낸" 것을 로그로 구분할 수 없다.
            // 종류를 함께 남기는 이유는, 좌표가 같은 줄이 쏟아졌을 때 그게
            // 클릭이 여러 번인지 키가 반복된 것인지 구분하지 못했기 때문이다.
            var point = _inputRouter?.LastScreenPoint;

            Logger.Debug($"입력 전달함({inputKind}). 경로 {_inputRouter?.AdapterName}"
                         + $", 화면 좌표 {point?.X:f0},{point?.Y:f0}");
            return;
        }

        var reason = Capture.Input.InputForwardResultText.Describe(result);

        StatusText = $"입력 전달 안 됨({inputKind}) - {reason}";
        Logger.Debug($"입력 전달 안 됨({inputKind}): {result}");
    }

    private static Minguk.Tools.Input.MouseButton ToBackendButton(System.Windows.Input.MouseButton button) => button switch
    {
        System.Windows.Input.MouseButton.Right => Minguk.Tools.Input.MouseButton.Right,
        System.Windows.Input.MouseButton.Middle => Minguk.Tools.Input.MouseButton.Middle,
        _ => Minguk.Tools.Input.MouseButton.Left
    };

    private void OnShowPreviewChanged()
    {
        if (ShowPreview)
        {
            // GPU 경로는 픽셀을 CPU 로 내리지 않으므로 리드백이 필요 없다.
            // 그 경로를 못 쓰는 환경에서만 FallBackToCpuPreview 가 리드백을 요구한다.
            if (_previewLayoutGroup is not null)
                _previewLayoutGroup.Height = _lastPreviewGroupHeight;

            return;
        }

        // 끄기 전에 지금 높이를 기억해 둔다. 다시 켜면 그 높이로 돌아온다.
        if (_previewLayoutGroup is { Height: > 0 })
            _lastPreviewGroupHeight = _previewLayoutGroup.Height;

        HookPreviewRendering(false);

        PreviewImage = null;
        _cpuPreviewBitmap = null;
        PreviewFps = 0;

        _gpuPreviewBridge?.Dispose();
        _gpuPreviewBridge = null;
    }

    private void TrySaveFrame(CapturedFrameEventArgs e)
    {
        try
        {
            var path = Path.Combine(UserDataPaths.Root, "captures", $"frame_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            FrameSnapshot.SavePng(e, path);
            Note($"저장: {path}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "프레임 저장 실패");
            Note($"저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 프레임 한 장을 데이터셋 images/ 에 담는다. 캡처 콜백 스레드에서 돈다.
    /// </summary>
    /// <remarks>
    /// 이름은 <see cref="LabelDataset.NextImagePath"/> 가 시각으로 짓는다. 여기서 따로
    /// 지으면 라벨링 쪽이 기대하는 규칙과 어긋난다.
    /// </remarks>
    private void TryCollectFrame(CapturedFrameEventArgs e)
    {
        try
        {
            var dataset = new LabelDataset(LabelDataset.ConfiguredRoot);
            var path = dataset.NextImagePath(DateTime.Now);

            FrameSnapshot.SavePng(e, path);

            // 몇 장째인지 센다. "담겼다" 만으로는 모으는 사람이 어디까지 왔는지 모른다.
            var count = dataset.EnumerateItems().Count;

            // 통계 표의 비고 칸에만 적으면 사실상 안 보인다 - 실제로 "담을 수가 없다" 는 말이
            // 나왔다. 상태 줄과 아래 바에 같이 적는다. 어느 폴더인지도 같이 - 라벨링에서
            // 다른 폴더를 보고 있으면 "담았는데 왜 안 보이지" 로 헤맨다.
            var message = $"데이터셋에 담음: {Path.GetFileName(path)} - 지금까지 {count}장 ({dataset.Root})";

            Note(message);
            _uiDispatcher?.BeginInvoke(() =>
            {
                StatusText = message;
                MessengerUtility.SendMainMessage($"데이터셋에 담았습니다 - {count}장째");
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "데이터셋에 담지 못했다");

            var message = $"담기 실패: {ex.Message}";

            Note(message);
            _uiDispatcher?.BeginInvoke(() => StatusText = message);
        }
    }

    private void OnSessionNotice(object? sender, string message)
    {
        Note(message);
        Logger.Info(message);
    }

    /// <summary>다음 1초 요약 줄에 붙일 메모. 어느 스레드에서 불려도 된다.</summary>
    private void Note(string message)
    {
        lock (_statisticsLock)
        {
            _pendingNoteText = _pendingNoteText is null ? message : $"{_pendingNoteText} / {message}";
        }
    }

    // ── 1초마다 통계를 UI 로 ─────────────────────────────────────────────────

    private void FlushStats()
    {
        int frames;
        long frameId;
        double latencySum, latencyMax, readbackSum;
        int width, height;
        string? note;

        lock (_statisticsLock)
        {
            frames = _frameCountInSecond;
            frameId = _lastFrameId;
            latencySum = _latencyMsSum;
            latencyMax = _latencyMsMax;
            readbackSum = _readbackMsSum;
            width = _lastFrameWidth;
            height = _lastFrameHeight;
            note = _pendingNoteText;

            _frameCountInSecond = 0;
            _latencyMsSum = 0;
            _latencyMsMax = 0;
            _readbackMsSum = 0;
            _pendingNoteText = null;
        }

        // 프레임도 없고 알릴 것도 없으면 굳이 줄을 만들지 않는다.
        // (화면이 안 바뀌면 WGC 는 프레임을 주지 않는다 — 정상이다.)
        if (frames == 0 && note is null)
            return;

        var row = new FrameLogRow
        {
            Timestamp = DateTime.Now,
            FrameId = frameId,
            Frames = frames,
            Fps = frames,
            AvgLatencyMs = frames > 0 ? latencySum / frames : 0,
            MaxLatencyMs = latencyMax,
            AvgReadbackMs = frames > 0 ? readbackSum / frames : 0,
            Resolution = width > 0 ? $"{width}×{height}" : null,
            Note = note
        };

        _uiDispatcher?.BeginInvoke(() => AddRow(row));
    }

    private void AddRow(FrameLogRow row)
    {
        Rows.Insert(0, row);

        while (Rows.Count > MaxRows)
            Rows.RemoveAt(Rows.Count - 1);

        KeepGridAtTop();

        PreviewFps = Interlocked.Exchange(ref _presentedFrameCountInSecond, 0);
        row.PreviewFps = PreviewFps;

        if (ShowPreview)
            Logger.Debug($"미리보기 {PreviewFps}fps (캡처 {row.Fps:n0}fps, 경로 {(_isGpuPreviewUnavailable ? "CPU" : "GPU")}" +
                         $", 반영시도 {Interlocked.Exchange(ref _presentAttemptCountInSecond, 0)}회" +
                         $", 프론트버퍼 {_gpuPreviewBridge?.IsFrontBufferAvailable}" +
                         $", GPU복사 {_gpuPreviewBridge?.LastCopyMs:n2}ms, 화면반영 {_gpuPreviewBridge?.LastPresentMs:n2}ms)");

        if (_captureSession is not null)
            StatusText = $"캡처 중: {_captureSession.Target.Display} — {row.Fps:n0} fps, 지연 {row.AvgLatencyMs:n2} ms";
    }

    // ── 정리 ─────────────────────────────────────────────────────────────────

    private void OnRunningChanged()
    {
        RaisePropertyChanged(() => IsNotRunning);

        DoStartCommand.RaiseCanExecuteChanged();
        DoStopCommand.RaiseCanExecuteChanged();
        RefreshTargetsCommand.RaiseCanExecuteChanged();
        SaveFrameCommand.RaiseCanExecuteChanged();
        CollectFrameCommand.RaiseCanExecuteChanged();
    }

    private void OnReadbackChanged()
    {
        SaveFrameCommand.RaiseCanExecuteChanged();
        CollectFrameCommand.RaiseCanExecuteChanged();
    }

    private void ResetStats()
    {
        lock (_statisticsLock)
        {
            _frameCountInSecond = 0;
            _lastFrameId = 0;
            _latencyMsSum = 0;
            _latencyMsMax = 0;
            _readbackMsSum = 0;
            _lastFrameWidth = 0;
            _lastFrameHeight = 0;
            _pendingNoteText = null;
        }

        Interlocked.Exchange(ref _isSaveFrameRequested, 0);
    }

    private void DisposeSession()
    {
        // 어댑터가 든 자원(Interception 의 드라이버 컨텍스트)을 놓아 준다.
        _inputRouter?.InputAdapter.Dispose();

        Interlocked.Exchange(ref _presentedFrameCountInSecond, 0);
        PreviewFps = 0;

        // 공유 표면은 세션의 D3D11 디바이스에 묶여 있다. 세션이 죽으면 같이 버린다.
        HookPreviewRendering(false);
        Interlocked.Exchange(ref _hasUnpresentedGpuFrame, 0);
        Interlocked.Exchange(ref _isPresentScheduled, 0);
        PreviewImage = null;
        _gpuPreviewBridge?.Dispose();
        _gpuPreviewBridge = null;

        _statisticsFlushTimer?.Dispose();
        _statisticsFlushTimer = null;

        if (_captureSession is not null)
        {
            _captureSession.FrameArrived -= OnFrameArrived;
            _captureSession.Notice -= OnSessionNotice;
            _captureSession.Dispose();
            _captureSession = null;
        }
    }

    /// <summary>탭이 닫힐 때. 베이스가 SaveSettings 다음에 불러 준다.</summary>
    protected override void ReleaseResources()
    {
        // 모델은 68MB 를 물고 있고 libtorch 는 GPU 메모리를 잡는다. 화면을 닫으면 놓는다.
        ReleaseDetector();

        DisposeSession();
    }

    public void Dispose() => DisposeSession();
}
