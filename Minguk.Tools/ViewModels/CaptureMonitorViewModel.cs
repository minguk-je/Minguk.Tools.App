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
    public double Fps { get; set; }
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

    private const int PreviewTargetFps = 60;

    /// <summary>
    /// 미리보기 최소 간격. 목표 주기(1/60)보다 10% 짧게 잡는다.
    ///
    /// 딱 1/60 로 두면 60fps 캡처와 주기가 겹쳐서, 프레임이 경계 직전에 도착할 때마다
    /// 버려지고 다음 장까지 33ms 를 기다리게 된다. 실측으로 미리보기가 35fps 에 묶였다.
    /// 여유를 조금 줘서 그 경계를 없앤다.
    /// </summary>
    private static readonly long PreviewIntervalTicks = Stopwatch.Frequency * 9 / (PreviewTargetFps * 10);

    /// <summary>
    /// 미리보기로 만들 최대 높이.
    ///
    /// 원본을 그대로 올리면 2560x1440 BGRA 한 장이 14.7MB 다. 복사(콜백) + WritePixels(UI) 로
    /// 프레임당 30MB 를 옮기게 된다. 어차피 320px 칸에 줄여 보여 주므로 옮길 때 미리 솎아 낸다.
    /// </summary>
    private const int PreviewMaxHeight = 400;

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

    /// <summary>화면에 올리고 있는 프레임. XAML 의 Image 가 이걸 문다.</summary>
    public WriteableBitmap? PreviewImage
    {
        get => GetProperty(() => PreviewImage);
        set => SetProperty(() => PreviewImage, value);
    }

    /// <summary>미리보기가 실제로 초당 몇 장 올라갔는지.</summary>
    public int PreviewFps
    {
        get => GetProperty(() => PreviewFps);
        set => SetProperty(() => PreviewFps, value);
    }

    public virtual ObservableCollection<CaptureTarget> Targets { get; set; } = new();

    public virtual ObservableCollection<FrameLogRow> Rows { get; set; } = new();

    public static CaptureMonitorViewModel Create() => ViewModelSource.Create(() => new CaptureMonitorViewModel());

    public CaptureMonitorViewModel()
    {
        Caption = "캡처 모니터";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/screen.png");

        // 탭을 닫으면 화면은 사라져도 캡처 세션은 남는다. 여기서 끊어 준다.
        OnUnloadedCommand = new DelegateCommand(DisposeSession, false);
        RefreshTargetsCommand = new DelegateCommand(RefreshTargets, () => !IsRunning, false);
        DoStartCommand = new DelegateCommand(DoStart, () => !IsRunning && SelectedTarget is not null, false);
        DoStopCommand = new DelegateCommand(DoStop, () => IsRunning, false);
        DoClearCommand = new DelegateCommand(DoClear, false);
        SaveFrameCommand = new DelegateCommand(DoSaveFrame, () => IsRunning && EnableCpuReadback, false);
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

            SelectedTarget = Targets.FirstOrDefault(x => x.Handle == previous?.Handle && x.Kind == previous.Kind)
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

            _session = new WgcCaptureSession(SelectedTarget, EnableCpuReadback);
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

        if (ShowPreview && e.HasPixels)
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
        if (now - _lastPreviewTicks < PreviewIntervalTicks)
            return;

        if (Interlocked.CompareExchange(ref _previewBusy, 1, 0) != 0)
            return;

        try
        {
            _lastPreviewTicks = now;

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

    private void OnShowPreviewChanged()
    {
        if (ShowPreview)
        {
            // 미리보기는 CPU 로 내려온 픽셀이 있어야 한다.
            if (!EnableCpuReadback)
            {
                EnableCpuReadback = true;

                if (IsRunning)
                    Note("미리보기는 CPU 리드백이 필요하다. 중지 후 다시 시작해야 나온다.");
            }
        }
        else
        {
            PreviewImage = null;
            _previewBitmap = null;
            PreviewFps = 0;
        }
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
