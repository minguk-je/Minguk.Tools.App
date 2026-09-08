using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;

using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Minguk.Tools.Capture;

/// <summary>
/// Windows.Graphics.Capture(WGC) 기반 캡처 세션. 윈도우에서 가장 빠른 화면 캡처 경로다.
///
/// 왜 WGC 인가:
///   - DWM 이 합성한 프레임을 GPU 텍스처 그대로 넘겨준다. GDI(BitBlt/PrintWindow)처럼
///     CPU 로 내렸다가 다시 올리는 왕복이 없다.
///   - DXGI Desktop Duplication 과 달리 창 단위 캡처가 되고, 창이 가려져도 잡힌다.
///   - 프레임은 화면이 실제로 갱신될 때만 온다. 놀고 있는 화면에서 CPU 를 쓰지 않는다.
///
/// 지연을 줄이려고 잡아 둔 것들:
///   - <see cref="Direct3D11CaptureFramePool.CreateFreeThreaded"/> — 콜백이 UI 스레드를 타지 않는다.
///     여기서 Dispatcher 로 넘기면 그 순간 캡처 속도가 UI 응답 속도로 떨어진다.
///   - 버퍼 2장. 늘리면 프레임이 큐에 쌓여 지연만 커진다.
///   - CPU 리드백은 선택. 켜면 스테이징 텍스처 하나를 계속 재사용하고, 크기가 바뀔 때만 다시 만든다.
///   - 커서 합성 끔. 그릴 비용도 들고, 비전 입력에는 노이즈다.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WgcCaptureSession : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>창 캡처가 시작 후 이 시간 동안 한 장도 못 받으면 모니터 캡처로 갈아탄다.</summary>
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromMilliseconds(1500);

    private readonly object _sessionLock = new();
    private readonly bool _cpuReadback;

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winrtDevice;

    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;

    private ID3D11Texture2D? _stagingTexture;
    private int _stagingWidth;
    private int _stagingHeight;
    private SizeInt32 _lastSize;

    private Timer? _monitorFallbackWatchdog;
    private long _frameId;
    private bool _running;
    private bool _disposed;

    public WgcCaptureSession(CaptureTarget target, bool cpuReadback = false, bool autoFallbackToMonitor = true)
    {
        Target = target;
        _cpuReadback = cpuReadback;
        AutoFallbackToMonitor = autoFallbackToMonitor;
    }

    /// <summary>프레임 한 장이 도착할 때마다. 워커 스레드에서 불린다 — UI 를 직접 만지면 안 된다.</summary>
    public event EventHandler<CapturedFrameEventArgs>? FrameArrived;

    /// <summary>대상이 사라졌거나 폴백이 일어났을 때. 사람이 읽을 한 줄.</summary>
    public event EventHandler<string>? Notice;

    /// <summary>지금 캡처 중인 대상. 폴백이 일어나면 바뀐다.</summary>
    public CaptureTarget Target { get; private set; }

    /// <summary>
    /// 창 캡처가 프레임을 못 받을 때 그 창이 있는 모니터 캡처로 자동 전환한다.
    /// 게임이 진짜 독점 전체화면으로 넘어가면 창 캡처가 멎는데, 그때 살아남는 경로가 모니터 캡처다.
    /// </summary>
    public bool AutoFallbackToMonitor { get; }

    private int _targetFps;
    private long _minimumFrameIntervalTicks;
    private long _lastProcessedTimestamp;

    /// <summary>
    /// 캡처 상한(fps). 0 이면 제한하지 않는다.
    ///
    /// WGC 는 화면 주사율만큼 프레임을 준다. 그보다 느리게 받고 싶으면 여기서 솎아 낸다.
    /// 솎아 낸 프레임은 리드백도 이벤트도 타지 않으므로 그만큼 실제 일이 준다.
    /// (WGC 가 프레임을 만드는 것 자체는 막을 수 없다 — 그건 시스템이 하는 일이다.)
    ///
    /// 돌아가는 중에 바꿔도 곧바로 반영된다.
    /// </summary>
    public int TargetFps
    {
        get => _targetFps;
        set
        {
            _targetFps = value;

            // 목표 주기의 90%. 딱 1/N 로 두면 주사율과 경계가 겹쳐 절반이 버려진다.
            _minimumFrameIntervalTicks = value > 0 ? Stopwatch.Frequency * 9 / (value * 10L) : 0;
        }
    }

    public bool IsRunning
    {
        get { lock (_sessionLock) return _running; }
    }

    /// <summary>
    /// 캡처가 쓰는 D3D11 디바이스. 프레임 텍스처는 이 디바이스 소유라, 후처리(전처리·추론)를
    /// 붙이려면 같은 디바이스를 써야 한다. 세션이 살아 있는 동안에만 유효하다.
    /// </summary>
    public ID3D11Device? Device
    {
        get { lock (_sessionLock) return _device; }
    }

    /// <summary>
    /// <see cref="Device"/> 의 즉시 컨텍스트. 멀티스레드 보호가 켜져 있으므로
    /// 캡처 콜백 밖에서 써도 되지만, 호출자끼리의 순서는 스스로 책임져야 한다.
    /// </summary>
    public ID3D11DeviceContext? Context
    {
        get { lock (_sessionLock) return _context; }
    }

    /// <summary>이 PC 에서 WGC 를 쓸 수 있는지. Windows 10 1903 미만이면 false.</summary>
    public static bool IsSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && GraphicsCaptureSession.IsSupported();

    public void Start()
    {
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_running)
                return;

            StartCore(Target);
            _running = true;
        }

        ArmFallbackWatchdog();
    }

    public void Stop()
    {
        lock (_sessionLock)
        {
            if (!_running)
                return;

            _running = false;
            StopCore();
        }

        DisarmFallbackWatchdog();
    }

    public void Dispose()
    {
        Stop();

        lock (_sessionLock)
        {
            if (_disposed)
                return;

            _disposed = true;

            _stagingTexture?.Dispose();
            _stagingTexture = null;

            _context?.Dispose();
            _context = null;

            _device?.Dispose();
            _device = null;
            _winrtDevice = null;
        }
    }

    // ── 시작/정지 본체. 반드시 _sessionLock 를 쥔 채로 부른다 ───────────────────────

    private void StartCore(CaptureTarget target)
    {
        if (!IsSupported)
            throw new NotSupportedException("이 Windows 에서는 Windows.Graphics.Capture 를 쓸 수 없다. (Windows 10 2004 이상 필요)");

        EnsureDevice();

        _captureItem = CaptureInterop.CreateItem(target);
        _captureItem.Closed += OnItemClosed;

        // 버퍼 2장 = 최소 지연. free-threaded 라 콜백이 스레드풀에서 돈다.
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            size: _captureItem.Size);

        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(_captureItem);
        _session.IsCursorCaptureEnabled = false;

        // 캡처 중임을 알리는 노란 테두리. Windows 11 부터 끌 수 있다.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
            ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            try
            {
                _session.IsBorderRequired = false;
            }
            catch (Exception ex)
            {
                // 정책상 막힌 환경이 있다. 테두리가 남을 뿐 캡처 자체는 된다.
                Logger.Debug(ex, "IsBorderRequired 를 끄지 못했다.");
            }
        }

        _frameId = 0;
        _lastSize = _captureItem.Size;
        _session.StartCapture();

        Logger.Info($"캡처 시작: {target.Display} (리드백 {(_cpuReadback ? "켬" : "끔")})");
    }

    private void StopCore()
    {
        if (_framePool is not null)
            _framePool.FrameArrived -= OnFrameArrived;

        if (_captureItem is not null)
            _captureItem.Closed -= OnItemClosed;

        _session?.Dispose();
        _session = null;

        _framePool?.Dispose();
        _framePool = null;

        _captureItem = null;
    }

    private void EnsureDevice()
    {
        if (_device is not null)
            return;

        // BgraSupport 는 WinRT 상호 운용의 전제 조건이다. 없으면 디바이스 래핑에서 실패한다.
        var hr = D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 },
            out var device,
            out var context);

        if (hr.Failure)
        {
            // 외장 GPU 가 없거나 드라이버가 막힌 환경. 느려도 도는 편이 낫다.
            hr = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 },
                out device,
                out context);

            hr.CheckError();
            Logger.Warn("하드웨어 D3D11 디바이스를 만들지 못해 WARP(소프트웨어)로 떨어졌다. 캡처가 느려진다.");
        }

        _device = device;
        _context = context;

        // 캡처 콜백(스레드풀)과 저장/미리보기(다른 스레드)가 같은 컨텍스트를 만질 수 있다.
        using (var multithread = _device.QueryInterface<ID3D11Multithread>())
        {
            multithread.SetMultithreadProtected(true);
        }

        _winrtDevice = CaptureInterop.CreateDirect3DDevice(_device);
    }

    // ── 프레임 ──────────────────────────────────────────────────────────────

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var frameArrivedTimestamp = Stopwatch.GetTimestamp();
        SizeInt32 newSize = default;
        var resized = false;

        try
        {
            lock (_sessionLock)
            {
                if (!_running || _disposed || _framePool is null)
                    return;

                using var frame = sender.TryGetNextFrame();
                if (frame is null)
                    return;

                var size = frame.ContentSize;
                if (size.Width != _lastSize.Width || size.Height != _lastSize.Height)
                {
                    _lastSize = size;
                    newSize = size;
                    resized = true;
                }

                if (size.Width <= 0 || size.Height <= 0)
                    return;

                // 상한에 걸리면 이 프레임은 버린다.
                // using 이 frame 을 놓아 주므로 프레임 풀은 그대로 돈다 —
                // 여기서 return 해도 다음 프레임은 정상적으로 들어온다.
                if (_minimumFrameIntervalTicks > 0 && frameArrivedTimestamp - _lastProcessedTimestamp < _minimumFrameIntervalTicks)
                    return;

                _lastProcessedTimestamp = frameArrivedTimestamp;

                ProcessFrame(frame, frameArrivedTimestamp);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "프레임 처리 실패");
            Notice?.Invoke(this, $"프레임 처리 중 오류: {ex.Message}");
        }

        // Recreate 는 프레임을 놓아준 뒤에 해야 한다.
        if (resized)
        {
            lock (_sessionLock)
            {
                if (_running && !_disposed && _framePool is not null)
                    _framePool.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, newSize);
            }
        }
    }

    private void ProcessFrame(Direct3D11CaptureFrame frame, long frameArrivedTimestamp)
    {
        var frameId = ++_frameId;

        // SystemRelativeTime 은 QPC 기준이라 Stopwatch 와 같은 시계다. 그래서 그냥 빼면 지연이 나온다.
        var now = TimeSpan.FromSeconds((double)frameArrivedTimestamp / Stopwatch.Frequency);
        var latencyMs = Math.Max(0d, (now - frame.SystemRelativeTime).TotalMilliseconds);

        using var texture = CaptureInterop.GetTexture(frame.Surface);

        var width = frame.ContentSize.Width;
        var height = frame.ContentSize.Height;

        var pixels = IntPtr.Zero;
        var rowPitch = 0;
        var readbackMs = 0d;
        var mapped = false;

        if (_cpuReadback)
        {
            var readbackStartTimestamp = Stopwatch.GetTimestamp();
            EnsureStaging(width, height);

            _context!.CopyResource(_stagingTexture!, texture);

            var map = _context.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            mapped = true;
            pixels = map.DataPointer;
            rowPitch = (int)map.RowPitch;
            readbackMs = Elapsed(readbackStartTimestamp);
        }

        try
        {
            FrameArrived?.Invoke(this, new CapturedFrameEventArgs
            {
                FrameId = frameId,
                Width = width,
                Height = height,
                LatencyMs = latencyMs,
                ReadbackMs = readbackMs,
                ProcessMs = Elapsed(frameArrivedTimestamp),
                Texture = texture,
                PixelData = pixels,
                RowPitch = rowPitch
            });
        }
        finally
        {
            if (mapped)
                _context!.Unmap(_stagingTexture!, 0);
        }
    }

    /// <summary>
    /// 리드백용 스테이징 텍스처. 크기가 바뀔 때만 다시 만든다.
    /// 프레임마다 만들면 그 할당 비용이 캡처보다 커진다.
    /// </summary>
    private void EnsureStaging(int width, int height)
    {
        if (_stagingTexture is not null && _stagingWidth == width && _stagingHeight == height)
            return;

        _stagingTexture?.Dispose();

        _stagingTexture = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });

        _stagingWidth = width;
        _stagingHeight = height;
    }

    private static double Elapsed(long from) => (Stopwatch.GetTimestamp() - from) * 1000d / Stopwatch.Frequency;

    // ── 전체화면 폴백 ────────────────────────────────────────────────────────

    /// <summary>
    /// 창 캡처를 걸었는데 한 장도 안 들어오면, 그 창이 올라가 있는 모니터 캡처로 바꾼다.
    /// "한 장도 못 받았을 때"로 제한한 이유는, 화면이 멈춰 있어서 프레임이 안 오는
    /// 정상적인 경우와 구분하기 위해서다.
    /// </summary>
    private void ArmFallbackWatchdog()
    {
        if (!AutoFallbackToMonitor || Target.Kind != CaptureTargetKind.Window)
            return;

        _monitorFallbackWatchdog = new Timer(_ => TryFallbackToMonitor(), null, FallbackDelay, Timeout.InfiniteTimeSpan);
    }

    private void DisarmFallbackWatchdog()
    {
        _monitorFallbackWatchdog?.Dispose();
        _monitorFallbackWatchdog = null;
    }

    private void TryFallbackToMonitor()
    {
        CaptureTarget? monitor;

        lock (_sessionLock)
        {
            if (!_running || _disposed || _frameId > 0 || Target.Kind != CaptureTargetKind.Window)
                return;

            monitor = CaptureTarget.MonitorOf(Target.Handle);
            if (monitor is null)
            {
                Notice?.Invoke(this, "창에서 프레임이 오지 않는데 대응하는 모니터를 찾지 못했다.");
                return;
            }

            try
            {
                StopCore();
                StartCore(monitor);
                Target = monitor;
            }
            catch (Exception ex)
            {
                _running = false;
                Logger.Error(ex, "모니터 캡처 폴백 실패");
                Notice?.Invoke(this, $"모니터 캡처로 전환하지 못했다: {ex.Message}");
                return;
            }
        }

        Notice?.Invoke(this, $"창에서 프레임이 오지 않아 {monitor.Title} 전체 캡처로 전환했다. (독점 전체화면으로 보인다)");
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        Notice?.Invoke(this, "캡처 대상이 닫혔다.");
        Stop();
    }
}
