using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;

namespace Minguk.Tools.Capture;

/// <summary>
/// 녹화한 영상 파일을 캡처처럼 흘린다(사용자, 2026-09-15) - 게임을 켜지 않고 몹 찾기·글자 읽기·스크립트 흐름을 시험한다.
/// </summary>
/// <remarks>
/// - Media Foundation SourceReader 가 H.264 를 풀고 RGB32 로 바꾼다(<see cref="SourceReaderAttributeKeys.EnableAdvancedVideoProcessing"/>).
/// - 영상 시각(샘플 시각)에 맞춰 흘린다 - 녹화한 속도 그대로. 끝나면 처음부터 다시 튼다(시험은 되풀이가 흔하다).
/// - 프레임은 WGC 와 같은 얼굴이다: GPU 텍스처(<see cref="CapturedFrameEventArgs.Texture"/>) + 리드백을 켰으면 CPU 픽셀.
///   텍스처가 있어야 GPU 미리보기·GPU 전처리(몹 찾기)가 창 캡처와 같은 길로 간다.
/// - 픽셀은 위에서 아래로, 알파 255 로 고친다 - RGB32 는 알파 칸이 비어(0) 있어 미리보기가 투명해진다.
/// - <see cref="TargetFps"/> 보다 촘촘한 프레임은 솎는다(WGC 와 같은 <see cref="FrameRateLimiter"/>). 늦어지면(풀기가 느림) 기다리지 않고 시계를 다시 맞춘다.
/// - 입력은 받지 않는다 - 부르는 쪽(PreviewInputRouter·실시간 스크립트)이 대상 종류로 막는다.
/// </remarks>
public sealed class VideoFileCaptureSession : IScreenCaptureAdapter
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static readonly Guid Rgb32Subtype = new("00000016-0000-0010-8000-00AA00389B71");

    /// <summary>파일마다 프레임 크기. 목록은 파일을 열지 않아 크기를 모른다 - 자리 계산(<c>CaptureTargetBounds</c>)이 처음 물을 때 한 번 읽는다.</summary>
    private static readonly ConcurrentDictionary<string, (int Width, int Height)> FrameSizes = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();
    private readonly bool _cpuReadback;

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11Texture2D? _texture;

    private Thread? _thread;
    private ManualResetEventSlim? _stopping;
    private volatile bool _running;
    private bool _disposed;

    private int _targetFps;
    private readonly FrameRateLimiter _limiter = new();

    public VideoFileCaptureSession(CaptureTarget target, bool cpuReadback)
    {
        if (target.Kind != CaptureTargetKind.Video || string.IsNullOrEmpty(target.FilePath))
            throw new ArgumentException("영상 대상이 아닙니다.", nameof(target));

        Target = target;
        _cpuReadback = cpuReadback;
    }

    public string Name => "영상 파일(Media Foundation)";

    public CaptureTarget Target { get; }

    public bool IsRunning => _running;

    public int TargetFps
    {
        get => _targetFps;
        set
        {
            _targetFps = value;
            _limiter.Fps = value;
        }
    }

    public ID3D11Device? Device
    {
        get { lock (_gate) return _device; }
    }

    public ID3D11DeviceContext? Context
    {
        get { lock (_gate) return _context; }
    }

    /// <summary>되감았다(끝까지 틀고 처음으로). 하네스가 센다.</summary>
    public int Loops { get; private set; }

    public event EventHandler<CapturedFrameEventArgs>? FrameArrived;

    public event EventHandler<string>? Notice;

    /// <summary>
    /// 영상 한 장의 크기. 한 번 읽으면 기억한다. 못 열면 false.
    /// </summary>
    public static bool TryGetFrameSize(string path, out int width, out int height)
    {
        if (FrameSizes.TryGetValue(path, out var known))
        {
            (width, height) = known;
            return true;
        }

        width = height = 0;

        if (!File.Exists(path)) return false;

        try
        {
            MediaFactory.MFStartup(true).CheckError();

            try
            {
                using var reader = CreateReader(path);
                (width, height) = OutputSize(reader);
            }
            finally
            {
                MediaFactory.MFShutdown();
            }

            if (width <= 0 || height <= 0) return false;

            FrameSizes[path] = (width, height);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"영상 크기를 읽지 못했다: {path}");
            return false;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_running) return;

            if (!File.Exists(Target.FilePath))
                throw new FileNotFoundException($"영상 파일이 없습니다: {Target.FilePath}", Target.FilePath);

            EnsureDevice();

            _stopping = new ManualResetEventSlim(false);
            _running = true;
            _thread = new Thread(PlayLoop) { IsBackground = true, Name = "영상 캡처" };
            _thread.Start(_stopping);
        }

        Logger.Info($"영상 캡처 시작: {Target.FilePath} (리드백 {(_cpuReadback ? "켬" : "끔")})");
    }

    public void Stop()
    {
        Thread? thread;

        lock (_gate)
        {
            if (!_running) return;

            _running = false;
            _stopping?.Set();
            thread = _thread;
            _thread = null;
        }

        // 프레임 콜백이 UI 스레드로 Invoke 하며 기다리는 일은 없다(허브·화면은 BeginInvoke) - 그래도 영영 붙들지 않게 끝을 둔다.
        if (thread is not null && thread != Thread.CurrentThread && !thread.Join(TimeSpan.FromSeconds(5)))
            Logger.Warn("영상 캡처 스레드가 5초 안에 끝나지 않았다.");
    }

    public void Dispose()
    {
        Stop();

        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;

            _texture?.Dispose();
            _texture = null;
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }
    }

    private void EnsureDevice()
    {
        if (_device is not null) return;

        var hr = D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
            out var device, out var context);

        if (hr.Failure)
        {
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1], out device, out context).CheckError();

            Logger.Warn("하드웨어 D3D11 디바이스를 만들지 못해 WARP(소프트웨어)로 떨어졌다.");
        }

        _device = device;
        _context = context;

        // 재생 스레드와 미리보기·전처리가 같은 컨텍스트를 만진다 - WgcCaptureSession 과 같다.
        using var multithread = _device!.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);
    }

    /// <summary>RGB32 로 풀어 주는 리더. 오디오는 끈다(쌓이기만 한다). 영상에서 그림 뽑기(<see cref="Recording.MediaFoundationVideoFrameExtractor"/>)도 쓴다.</summary>
    internal static IMFSourceReader CreateReader(string path)
    {
        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u);

        var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);

        try
        {
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

            using var type = MediaFactory.MFCreateMediaType();
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            type.Set(MediaTypeAttributeKeys.Subtype, Rgb32Subtype);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);

            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    internal static (int Width, int Height) OutputSize(IMFSourceReader reader)
    {
        using var current = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        var packed = current.GetUInt64(MediaTypeAttributeKeys.FrameSize);

        return ((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
    }

    private void PlayLoop(object? state)
    {
        var stopping = (ManualResetEventSlim)state!;
        var started = false;
        byte[]? pixels = null;

        try
        {
            MediaFactory.MFStartup(true).CheckError();
            started = true;

            using var reader = CreateReader(Target.FilePath!);
            var (width, height) = OutputSize(reader);

            if (width <= 0 || height <= 0)
                throw new InvalidDataException($"영상 크기를 알 수 없습니다: {Target.FilePath}");

            FrameSizes[Target.FilePath!] = (width, height);
            pixels = new byte[width * height * 4];

            var frameId = 0L;
            var clockStart = Stopwatch.GetTimestamp();   // 영상 0 초가 이 시각
            var firstSampleTime = -1L;
            _limiter.Reset();

            while (!stopping.IsSet)
            {
                using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out var sampleTime);

                if ((flags & SourceReaderFlag.Error) != 0)
                    throw new InvalidDataException($"영상을 읽다 오류가 났습니다: {Target.FilePath}");

                if ((flags & SourceReaderFlag.EndOfStream) != 0)
                {
                    if (frameId == 0)
                        throw new InvalidDataException($"영상에 프레임이 없습니다: {Target.FilePath}");

                    // 끝 - 처음부터 다시. 시계도 다시 맞춘다.
                    reader.SetCurrentPosition(0);
                    firstSampleTime = -1;
                    Loops++;

                    if (Loops == 1) Notice?.Invoke(this, "영상 끝까지 틀어 처음부터 다시 틉니다.");
                    continue;
                }

                if ((flags & SourceReaderFlag.CurrentMediaTypeChanged) != 0)
                {
                    (width, height) = OutputSize(reader);
                    FrameSizes[Target.FilePath!] = (width, height);
                    pixels = new byte[width * height * 4];
                }

                if (sample is null) continue;

                // 영상 시각에 맞춰 기다린다. 첫 장(또는 되감은 뒤 첫 장)이 지금이다.
                if (firstSampleTime < 0)
                {
                    firstSampleTime = sampleTime;
                    clockStart = Stopwatch.GetTimestamp();
                }

                var due = clockStart + (long)((sampleTime - firstSampleTime) / 10_000_000.0 * Stopwatch.Frequency);
                var wait = due - Stopwatch.GetTimestamp();

                if (wait > 0)
                {
                    if (stopping.Wait(TimeSpan.FromSeconds((double)wait / Stopwatch.Frequency))) break;
                }
                else if (-wait > Stopwatch.Frequency / 2)
                {
                    // 반 초 넘게 늦었다(풀기가 느리거나 멈췄었다) - 따라잡으려 몰아 흘리지 않고 여기서 다시 센다.
                    firstSampleTime = sampleTime;
                    clockStart = Stopwatch.GetTimestamp();
                }

                var arrived = Stopwatch.GetTimestamp();

                if (!_limiter.TryAccept(arrived)) continue;

                CopyPixels(sample, pixels, width, height);
                Emit(++frameId, pixels, width, height, arrived);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"영상 캡처가 멈췄다: {Target.FilePath}");
            Notice?.Invoke(this, $"영상을 틀지 못했습니다 - 파일이 깨졌거나 이 PC 에서 풀 수 없는 형식입니다({Path.GetFileName(Target.FilePath)}, 0x{ex.HResult:X8}).");
            _running = false;
        }
        finally
        {
            if (started) MediaFactory.MFShutdown();
        }
    }

    /// <summary>샘플을 위에서 아래로·알파 255 인 BGRA 로 옮긴다. 2D 버퍼면 줄 폭 부호(아래에서 위)를 따른다.</summary>
    internal static unsafe void CopyPixels(IMFSample sample, byte[] destination, int width, int height)
    {
        using var buffer = sample.ConvertToContiguousBuffer();
        using var buffer2D = buffer.QueryInterfaceOrNull<IMF2DBuffer>();

        IntPtr scan0;
        int pitch;

        if (buffer2D is not null)
        {
            buffer2D.Lock2D(out scan0, out pitch);
        }
        else
        {
            buffer.Lock(out var start, out _, out _);

            // 2D 버퍼가 아니면 RGB32 의 기본(아래에서 위)이다.
            pitch = -width * 4;
            scan0 = start + (height - 1) * width * 4;
        }

        try
        {
            var stride = width * 4;

            fixed (byte* target = destination)
            {
                var source = (byte*)scan0;

                for (var y = 0; y < height; y++)
                {
                    var row = source + (long)y * pitch;
                    var line = target + (long)y * stride;

                    Buffer.MemoryCopy(row, line, stride, stride);

                    for (var x = 3; x < stride; x += 4) line[x] = 255;
                }
            }
        }
        finally
        {
            if (buffer2D is not null) buffer2D.Unlock2D();
            else buffer.Unlock();
        }
    }

    private void Emit(long frameId, byte[] pixels, int width, int height, long arrived)
    {
        ID3D11DeviceContext context;
        ID3D11Texture2D texture;

        lock (_gate)
        {
            if (!_running || _device is null || _context is null) return;

            if (_texture is null || _texture.Description.Width != width || _texture.Description.Height != height)
            {
                _texture?.Dispose();
                _texture = _device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.None,
                    MiscFlags = ResourceOptionFlags.None
                });
            }

            context = _context;
            texture = _texture;
        }

        unsafe
        {
            fixed (byte* data = pixels)
                context.UpdateSubresource(texture, 0, null, (IntPtr)data, (uint)(width * 4), 0);
        }

        unsafe
        {
            fixed (byte* data = pixels)
            {
                FrameArrived?.Invoke(this, new CapturedFrameEventArgs
                {
                    FrameId = frameId,
                    Width = width,
                    Height = height,
                    LatencyMs = 0,
                    ReadbackMs = 0,
                    ProcessMs = (Stopwatch.GetTimestamp() - arrived) * 1000d / Stopwatch.Frequency,
                    Texture = texture,
                    PixelData = _cpuReadback ? (IntPtr)data : IntPtr.Zero,
                    RowPitch = _cpuReadback ? width * 4 : 0
                });
            }
        }
    }
}
