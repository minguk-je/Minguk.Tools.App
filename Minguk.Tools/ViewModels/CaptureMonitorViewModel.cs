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
public class CaptureMonitorViewModel : DocumentViewModelBase, IDisposable
{
    /// <summary>그리드에 남겨 둘 줄 수. 오래 켜 두면 메모리를 먹으니 잘라 낸다.</summary>
    private const int MaxRows = 600;

    private readonly object _statsGate = new();

    private WgcCaptureSession? _session;
    private Timer? _flushTimer;

    /// <summary>타이머 스레드에서 서비스 컨테이너를 뒤지지 않도록, 시작할 때 UI 스레드에서 한 번 꺼내 둔다.</summary>
    private IDispatcherService? _dispatcher;

    // 콜백에서 쌓고 1초마다 비우는 통계
    private int _frames;
    private long _lastFrameId;
    private double _latencySum;
    private double _latencyMax;
    private double _readbackSum;
    private int _width;
    private int _height;
    private string? _pendingNote;

    // 저장 요청. 다음 프레임 한 장만 파일로 떨어뜨린다.
    private int _saveRequested;

    // ── 미리보기 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 미리보기 최소 간격(틱). <see cref="PreviewTargetFps"/> 가 바뀌면 다시 계산한다.
    ///
    /// 목표 주기보다 10% 짧게 잡는 것이 요점이다. 60fps 목표에 간격을 딱 1/60 로 두면
    /// 60fps 캡처와 주기가 겹쳐서, 프레임이 경계 직전에 도착할 때마다 버려지고
    /// 다음 장까지 33ms 를 기다리게 된다. 실측으로 미리보기가 35fps 에 묶였다.
    /// </summary>
    private long _previewIntervalTicks = Stopwatch.Frequency * 9 / (60 * 10);

    /// <summary>
    /// 미리보기로 만들 최대 높이.
    ///
    /// 원본을 그대로 올리면 2560x1440 BGRA 한 장이 14.7MB 다. 복사(콜백) + WritePixels(UI) 로
    /// 프레임당 30MB 를 옮기게 된다. 어차피 320px 칸에 줄여 보여 주므로 옮길 때 미리 솎아 낸다.
    /// </summary>
    private const int PreviewMaxHeight = 400;

    /// <summary>미리보기 칸의 기본 높이(px).</summary>
    private const double DefaultPreviewHeight = 320;

    /// <summary>미리보기를 껐다 켤 때 되살릴 높이. 끄면 행이 0 으로 접히므로 따로 기억한다.</summary>
    private double _lastPreviewHeight = DefaultPreviewHeight;

    /// <summary>
    /// 지난번에 고른 대상의 표시 이름.
    ///
    /// 핸들(HWND/HMONITOR)은 실행마다 바뀌므로 저장해도 소용이 없다.
    /// 사람이 보는 이름으로 되찾는다 — 모니터는 "[모니터] 디스플레이 1 (2560×1440)" 처럼
    /// 구성이 그대로면 같은 문자열이 나온다.
    /// </summary>
    private string _lastTargetDisplay = string.Empty;

    /// <summary>미리보기 칸. 높이를 직접 넣고 빼려고 들고 있는다.</summary>
    private LayoutGroup? _previewGroup;

    /// <summary>
    /// GPU 경로. 캡처 텍스처를 CPU 를 거치지 않고 바로 화면에 올린다.
    /// 만들기가 실패하면(원격 데스크톱 등) <see cref="_gpuPreviewFailed"/> 를 세우고
    /// 아래 WriteableBitmap 경로로 떨어진다.
    /// </summary>
    private D3DImageBridge? _previewBridge;
    private bool _gpuPreviewFailed;
    private bool _previewSurfacePending;

    /// <summary>공유 표면에 새 프레임이 들어왔으면 1. 캡처 스레드가 세우고 렌더 콜백이 내린다.</summary>
    private int _gpuFrameReady;

    /// <summary>화면 반영을 이미 걸어 두었으면 1. 같은 요청을 겹쳐 쌓지 않는다.</summary>
    private int _presentScheduled;

    /// <summary>화면 반영 시도가 초당 몇 번 있었는지.</summary>
    private int _renderCallbacks;

    /// <summary>CompositionTarget.Rendering 구독 여부. UI 스레드에서만 만진다.</summary>
    private bool _previewRenderHooked;

    private WriteableBitmap? _previewBitmap;
    private byte[]? _previewBuffer;
    private int _previewWidth;
    private int _previewHeight;

    /// <summary>UI 가 앞 장을 아직 그리는 중이면 1. 그동안 들어온 프레임은 버린다.</summary>
    private int _previewBusy;

    private long _lastPreviewTicks;
    private int _previewFrames;

    // CommandManager 의 자동 재조회는 사용자 입력 때만 돈다. 여기 상태는 캡처 스레드/타이머에서 바뀌므로
    // useCommandManager: false 로 만들고 RaiseCanExecuteChanged 를 직접 부른다.
    public DelegateCommand OnUnloadedCommand { get; set; }
    public DelegateCommand RefreshTargetsCommand { get; set; }
    public DelegateCommand DoStartCommand { get; set; }
    public DelegateCommand DoStopCommand { get; set; }
    public DelegateCommand DoClearCommand { get; set; }
    public DelegateCommand SaveFrameCommand { get; set; }

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
    }

    /// <summary>XAML 의 컨트롤을 잡아 온다. 베이스가 초기화 첫 단계에서 불러 준다.</summary>
    protected override void InitializeControls()
    {
        _previewGroup = FindControl<LayoutGroup>("PreviewGroupObjectService");
    }

    /// <summary>지난번에 쓰던 설정을 되살린다. 베이스가 OnLoaded 직전에 불러 준다.</summary>
    protected override void RestoreSettings()
    {
        _lastPreviewHeight = GetSetting(nameof(_lastPreviewHeight), DefaultPreviewHeight);

        if (_lastPreviewHeight < 80)
            _lastPreviewHeight = DefaultPreviewHeight;

        if (_previewGroup is not null)
            _previewGroup.Height = _lastPreviewHeight;

        _lastTargetDisplay = GetSetting(nameof(SelectedTarget), string.Empty);

        CaptureTargetFps = GetSetting(nameof(CaptureTargetFps), 60);
        PreviewTargetFps = GetSetting(nameof(PreviewTargetFps), 60);
        IsColumnAutoWidth = GetSetting(nameof(IsColumnAutoWidth), false);
        ShowPreview = GetSetting(nameof(ShowPreview), false);

        RestoreGridLayout();
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
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "그리드 상태 복원 실패. 기본 배치로 시작한다.");
        }
    }

    protected override void SaveSettings()
    {
        // 켜져 있을 때의 높이만 의미가 있다. 꺼져 있으면 그룹이 숨겨져 있어 값이 미덥지 않다.
        if (ShowPreview && _previewGroup is { Height: > 0 })
            _lastPreviewHeight = _previewGroup.Height;

        SetSetting(nameof(_lastPreviewHeight), _lastPreviewHeight);
        SetSetting(nameof(ShowPreview), ShowPreview);
        SetSetting(nameof(CaptureTargetFps), CaptureTargetFps);
        SetSetting(nameof(PreviewTargetFps), PreviewTargetFps);
        SetSetting(nameof(IsColumnAutoWidth), IsColumnAutoWidth);

        if (SelectedTarget is not null)
            SetSetting(nameof(SelectedTarget), SelectedTarget.Display);

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
            var previous = SelectedTarget;

            Targets.Clear();

            // 모니터를 위에 둔다. 전체화면 게임은 결국 이쪽으로 잡는 경우가 많다.
            foreach (var monitor in CaptureTarget.EnumerateMonitors())
                Targets.Add(monitor);

            foreach (var window in CaptureTarget.EnumerateWindows().OrderBy(x => x.ProcessName).ThenBy(x => x.Title))
                Targets.Add(window);

            // ① 방금 전까지 보던 것 → ② 지난 실행에서 고른 것 → ③ 목록의 첫 번째
            SelectedTarget = Targets.FirstOrDefault(x => x.Handle == previous?.Handle && x.Kind == previous.Kind)
                             ?? Targets.FirstOrDefault(x => x.Display == _lastTargetDisplay)
                             ?? Targets.FirstOrDefault();

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

            _dispatcher = GetService<IDispatcherService>();
            if (_dispatcher is null)
            {
                StatusText = "IDispatcherService 가 없다. View 에 dxmvvm:DispatcherService 를 등록할 것.";
                return;
            }

            _session = new WgcCaptureSession(SelectedTarget, EnableCpuReadback)
            {
                TargetFps = CaptureTargetFps
            };
            _session.FrameArrived += OnFrameArrived;
            _session.Notice += OnSessionNotice;
            _session.Start();

            // 통계를 그리드로 옮기는 건 1초에 한 번. 콜백에서 직접 하면 UI 가 캡처를 붙잡는다.
            _flushTimer = new Timer(_ => FlushStats(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            IsRunning = true;
            StatusText = $"캡처 중: {_session.Target.Display}";
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

    /// <summary>다음 프레임 한 장을 PNG 로 떨어뜨린다. 캡처 내용을 눈으로 확인하는 용도.</summary>
    private void DoSaveFrame() => Interlocked.Exchange(ref _saveRequested, 1);

    // ── 캡처 콜백. 여기는 스레드풀이다 ────────────────────────────────────────

    private void OnFrameArrived(object? sender, CapturedFrameEventArgs e)
    {
        lock (_statsGate)
        {
            _frames++;
            _lastFrameId = e.FrameId;
            _latencySum += e.LatencyMs;
            _readbackSum += e.ReadbackMs;

            if (e.LatencyMs > _latencyMax)
                _latencyMax = e.LatencyMs;

            _width = e.Width;
            _height = e.Height;
        }

        if (Interlocked.CompareExchange(ref _saveRequested, 0, 1) == 1)
            TrySaveFrame(e);

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
        if (now - _lastPreviewTicks < _previewIntervalTicks)
            return;

        // GPU 경로는 복사가 GPU 안에서 끝나므로 UI 상태를 볼 필요가 없다.
        if (!_gpuPreviewFailed)
        {
            _lastPreviewTicks = now;
            PushPreviewOnGpu(e);
            return;
        }

        // 폴백 경로는 CPU 로 옮기는 비용이 커서 UI 가 앞 장을 그리는 중이면 건너뛴다.
        if (!e.HasPixels)
            return;

        if (Interlocked.CompareExchange(ref _previewBusy, 1, 0) != 0)
            return;

        _lastPreviewTicks = now;
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
            var bridge = _previewBridge;

            // 표면이 아직 없거나 해상도가 바뀌었으면 UI 스레드에서 만들어야 한다.
            // 만드는 동안 들어오는 프레임은 건너뛴다 — 한두 장이다.
            if (bridge is null || !bridge.IsReady)
            {
                RequestPreviewSurface(e.Width, e.Height);
                return;
            }

            var context = _session?.Context;
            if (context is null || !bridge.CopyFrom(context, e.Texture))
                return;

            // 렌더 이벤트 안에서 화면 반영을 하면 WPF 가 쥔 잠금과 부딪힌다.
            // Render 우선순위로 따로 넣어 그리기 직전에 처리되게 한다.
            Interlocked.Exchange(ref _gpuFrameReady, 1);
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
        if (_previewSurfacePending)
            return;

        _previewSurfacePending = true;

        _dispatcher?.BeginInvoke(() =>
        {
            try
            {
                var device = _session?.Device;
                if (device is null)
                    return;

                _previewBridge ??= new D3DImageBridge();

                if (_previewBridge.EnsureSurface(device, width, height))
                {
                    PreviewImage = _previewBridge.Image;
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
                _previewSurfacePending = false;
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
    /// 데이터 표시(_gpuFrameReady)와 스케줄 여부(_presentScheduled)를 따로 둔 이유가 있다.
    /// 하나로 합쳤더니, TryLock 이 한 번 실패해 표시를 되돌려 놓는 순간
    /// "이미 표시가 서 있으니 새로 걸지 않는다"가 되어 루프가 영구히 멈췄다(60fps -> 0).
    /// </summary>
    private void SchedulePresent()
    {
        if (Interlocked.Exchange(ref _presentScheduled, 1) != 0)
            return;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(PresentPreview));
    }

    private void PresentPreview()
    {
        Interlocked.Exchange(ref _presentScheduled, 0);
        Interlocked.Increment(ref _renderCallbacks);

        if (Interlocked.Exchange(ref _gpuFrameReady, 0) == 0)
            return;

        try
        {
            if (_previewBridge?.Present() == true)
            {
                Interlocked.Increment(ref _previewFrames);
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
        if (hook == _previewRenderHooked)
            return;

        _previewRenderHooked = hook;

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
        if (_gpuPreviewFailed)
            return;

        _gpuPreviewFailed = true;

        HookPreviewRendering(false);

        if (ex is not null)
            Logger.Warn(ex, "GPU 미리보기를 쓸 수 없다. CPU 경로로 전환한다.");
        else
            Logger.Warn("GPU 미리보기를 쓸 수 없다. CPU 경로로 전환한다.");

        Note("GPU 미리보기를 못 써서 CPU 경로로 전환했다.");

        PreviewImage = null;

        _previewBridge?.Dispose();
        _previewBridge = null;

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

            if (_previewBuffer is null || _previewBuffer.Length < required)
                _previewBuffer = new byte[required];

            // RowPitch 는 Width*4 보다 클 수 있다(GPU 정렬). 원본에서 step 간격으로 집어 온다.
            unsafe
            {
                var source = (byte*)e.PixelData;

                fixed (byte* destinationStart = _previewBuffer)
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

            _previewWidth = width;
            _previewHeight = height;

            _dispatcher?.BeginInvoke(BlitPreview);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _previewBusy, 0);
            Logger.Error(ex, "미리보기 프레임 복사 실패");
        }
    }

    /// <summary>버퍼를 WriteableBitmap 에 올린다. UI 스레드.</summary>
    private void BlitPreview()
    {
        try
        {
            var buffer = _previewBuffer;
            if (buffer is null)
                return;

            int width = _previewWidth;
            int height = _previewHeight;

            // 해상도가 바뀌면(대상 변경 등) 비트맵을 새로 만든다.
            if (_previewBitmap is null ||
                _previewBitmap.PixelWidth != width ||
                _previewBitmap.PixelHeight != height)
            {
                _previewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                PreviewImage = _previewBitmap;
            }

            _previewBitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, width * 4, 0);
            Interlocked.Increment(ref _previewFrames);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "미리보기 갱신 실패");
        }
        finally
        {
            Interlocked.Exchange(ref _previewBusy, 0);
        }
    }

    /// <summary>목표 fps 가 바뀌면 스로틀 간격을 다시 잡는다.</summary>
    private void OnPreviewTargetFpsChanged()
    {
        var fps = Math.Clamp(PreviewTargetFps, 1, 240);

        // 목표 주기의 90%. 캡처 주기와 경계가 겹쳐 절반이 버려지는 것을 막는다.
        _previewIntervalTicks = Stopwatch.Frequency * 9 / (fps * 10);
    }

    /// <summary>캡처 상한이 바뀌면 돌고 있는 세션에 바로 반영한다.</summary>
    private void OnCaptureTargetFpsChanged()
    {
        if (_session is not null)
            _session.TargetFps = CaptureTargetFps;
    }

    private void OnShowPreviewChanged()
    {
        if (ShowPreview)
        {
            // GPU 경로는 픽셀을 CPU 로 내리지 않으므로 리드백이 필요 없다.
            // 그 경로를 못 쓰는 환경에서만 FallBackToCpuPreview 가 리드백을 요구한다.
            if (_previewGroup is not null)
                _previewGroup.Height = _lastPreviewHeight;

            return;
        }

        // 끄기 전에 지금 높이를 기억해 둔다. 다시 켜면 그 높이로 돌아온다.
        if (_previewGroup is { Height: > 0 })
            _lastPreviewHeight = _previewGroup.Height;

        HookPreviewRendering(false);

        PreviewImage = null;
        _previewBitmap = null;
        PreviewFps = 0;

        _previewBridge?.Dispose();
        _previewBridge = null;
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

    private void OnSessionNotice(object? sender, string message)
    {
        Note(message);
        Logger.Info(message);
    }

    /// <summary>다음 1초 요약 줄에 붙일 메모. 어느 스레드에서 불려도 된다.</summary>
    private void Note(string message)
    {
        lock (_statsGate)
        {
            _pendingNote = _pendingNote is null ? message : $"{_pendingNote} / {message}";
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

        lock (_statsGate)
        {
            frames = _frames;
            frameId = _lastFrameId;
            latencySum = _latencySum;
            latencyMax = _latencyMax;
            readbackSum = _readbackSum;
            width = _width;
            height = _height;
            note = _pendingNote;

            _frames = 0;
            _latencySum = 0;
            _latencyMax = 0;
            _readbackSum = 0;
            _pendingNote = null;
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

        _dispatcher?.BeginInvoke(() => AddRow(row));
    }

    private void AddRow(FrameLogRow row)
    {
        Rows.Insert(0, row);

        while (Rows.Count > MaxRows)
            Rows.RemoveAt(Rows.Count - 1);

        PreviewFps = Interlocked.Exchange(ref _previewFrames, 0);
        row.PreviewFps = PreviewFps;

        if (ShowPreview)
            Logger.Debug($"미리보기 {PreviewFps}fps (캡처 {row.Fps:n0}fps, 경로 {(_gpuPreviewFailed ? "CPU" : "GPU")}" +
                         $", 반영시도 {Interlocked.Exchange(ref _renderCallbacks, 0)}회" +
                         $", 프론트버퍼 {_previewBridge?.IsFrontBufferAvailable}" +
                         $", GPU복사 {_previewBridge?.LastCopyMs:n2}ms, 화면반영 {_previewBridge?.LastPresentMs:n2}ms)");

        if (_session is not null)
            StatusText = $"캡처 중: {_session.Target.Display} — {row.Fps:n0} fps, 지연 {row.AvgLatencyMs:n2} ms";
    }

    // ── 정리 ─────────────────────────────────────────────────────────────────

    private void OnRunningChanged()
    {
        RaisePropertyChanged(() => IsNotRunning);

        DoStartCommand.RaiseCanExecuteChanged();
        DoStopCommand.RaiseCanExecuteChanged();
        RefreshTargetsCommand.RaiseCanExecuteChanged();
        SaveFrameCommand.RaiseCanExecuteChanged();
    }

    private void OnReadbackChanged() => SaveFrameCommand.RaiseCanExecuteChanged();

    private void ResetStats()
    {
        lock (_statsGate)
        {
            _frames = 0;
            _lastFrameId = 0;
            _latencySum = 0;
            _latencyMax = 0;
            _readbackSum = 0;
            _width = 0;
            _height = 0;
            _pendingNote = null;
        }

        Interlocked.Exchange(ref _saveRequested, 0);
    }

    private void DisposeSession()
    {
        Interlocked.Exchange(ref _previewFrames, 0);
        PreviewFps = 0;

        // 공유 표면은 세션의 D3D11 디바이스에 묶여 있다. 세션이 죽으면 같이 버린다.
        HookPreviewRendering(false);
        Interlocked.Exchange(ref _gpuFrameReady, 0);
        Interlocked.Exchange(ref _presentScheduled, 0);
        PreviewImage = null;
        _previewBridge?.Dispose();
        _previewBridge = null;

        _flushTimer?.Dispose();
        _flushTimer = null;

        if (_session is not null)
        {
            _session.FrameArrived -= OnFrameArrived;
            _session.Notice -= OnSessionNotice;
            _session.Dispose();
            _session = null;
        }
    }

    /// <summary>탭이 닫힐 때. 베이스가 SaveSettings 다음에 불러 준다.</summary>
    protected override void ReleaseResources() => DisposeSession();

    public void Dispose() => DisposeSession();
}
