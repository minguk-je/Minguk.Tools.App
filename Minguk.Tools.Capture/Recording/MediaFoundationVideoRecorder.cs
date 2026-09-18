using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using Vortice.MediaFoundation;

namespace Minguk.Tools.Capture.Recording;

/// <summary>
/// Media Foundation 으로 H.264 <b>조각 mp4</b>(fragmented MP4)를 쓴다. Windows 기본 구성이라 따로 받을 것이 없다.
/// </summary>
/// <remarks>
/// - 입력은 RGB32(BGRA) 그대로 넘기고 색 변환(NV12)은 SinkWriter 가 붙이는 변환기가 한다. 하드웨어 인코더가 있으면 그것을 쓴다.
/// - H.264 는 가로·세로가 짝수여야 해서 홀수면 오른쪽·아래 한 줄을 버린다.
/// - SinkWriter 는 <b>첫 프레임을 받은 쓰기 스레드에서</b> 만든다 - 크기를 그때 알고, 만드는 데 수백 ms 라 캡처 스레드에서 하면 프레임이 밀린다.
/// - 크기가 도중에 바뀌면(창 크기 조절) 그 뒤 프레임은 버리고 <see cref="Error"/> 에 적는다. 한 파일에 크기가 섞일 수 없다.
/// - 쓰기 줄은 0.2초어치(최소 6장). 넘치면 버린다 - 1080p 한 장이 8MB 라 쌓아 두면 메모리가 금방 찬다.
/// - <b>한 파일에 이어 쓰고, 앱이 죽어도 그때까지는 트는 파일이다</b>(사용자, 2026-09-15 "10초나 1분에 한번씩 디스크에"). 보통 mp4 는 닫을 때
///   목차(moov)를 써서 앱이 죽으면 통째로 못 튼다. 조각 mp4 는 머리에 목차를 먼저 쓰고 조각(moof+mdat)을 이어 붙여, 죽어도 마지막으로 끝난 조각까지는 튼다.
/// - <b>조각 간격은 싱크가 정한다</b>(MFCreateFMPEG4MediaSink, 실측 2026-09-15): 키프레임과 상관없이 약 9장(30fps 0.3초)마다 붙는다 -
///   키프레임은 [0, 9, 90] 인데 150장에 조각 17개. <c>MF_MT_MAX_KEYFRAME_SPACING</c> 은 1초·10초가 바이트까지 같아 안 먹는다.
///   그래서 "10초·1분마다 확정" 을 따로 둘 필요가 없다 - 요구보다 촘촘히 확정된다. 검사 <c>--vision</c> 의 녹화 줄(쓰는 도중 사본을 떠서 튼다).
/// </remarks>
public sealed class MediaFoundationVideoRecorder : IVideoRecorder
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // 표준 서브타입 GUID (mfapi.h). FourCC 'H264' 와 D3DFMT_X8R8G8B8(22).
    private static readonly Guid H264Subtype = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid Rgb32Subtype = new("00000016-0000-0010-8000-00AA00389B71");

    private const uint InterlaceProgressive = 2;

    /// <summary>쓰기를 기다리는 프레임 상한 - 0.2초어치(최소 6장). 1080p 한 장이 8MB 라 더 쌓으면 메모리가 찬다.</summary>
    private readonly int _queueLimit;

    private readonly BlockingCollection<Frame> _queue;
    private readonly Thread _writerThread;
    private readonly int _frameRate;
    private int _bitrate;

    /// <summary>프레임 사이 간격(Stopwatch 틱)과 다음 프레임을 받을 시각. 고른 fps 보다 빨리 오는 프레임은 솎는다.</summary>
    private readonly double _frameInterval;
    private long _nextDue;

    private int _framesWritten;
    private int _framesDropped;
    private int _framesSkipped;
    private long _firstTimestamp = -1;
    private long _lastTimestamp;
    private int _width;
    private int _height;
    private volatile Exception? _error;
    private int _finished;

    private readonly record struct Frame(byte[] Pixels, int Length, long Timestamp);

    /// <summary>파일 하나를 쓰는 데 드는 것들. 닫을 때 거꾸로 푼다.</summary>
    private sealed record Output(IMFByteStream Stream, IMFMediaSink Sink, IMFSinkWriter Writer, int StreamIndex);

    /// <param name="filePath">쓸 mp4 경로. 폴더는 만든다.</param>
    /// <param name="frameRate">
    /// 저장할 초당 프레임 - 화면캡처 fps 콤보 값. <b>이보다 빨리 오는 프레임은 솎는다</b>: 캡처 세션을 나눠 쓰는 다른 화면이 더 높은 fps 를
    /// 원하면 세션은 그 fps 로 돈다(<c>SharedCaptureHub</c> 는 큰 값을 쓴다). 실제 시각은 프레임마다 받은 timestamp 다.
    /// </param>
    /// <param name="bitrate">평균 비트레이트(bps). 안 주면 첫 프레임 크기·fps 로 정한다(<see cref="DefaultBitrate"/>).</param>
    public MediaFoundationVideoRecorder(string filePath, int frameRate = 30, int? bitrate = null)
    {
        FilePath = Path.GetFullPath(filePath);
        _frameRate = Math.Clamp(frameRate, 1, 240);
        _requestedBitrate = bitrate is { } given ? Math.Max(500_000, given) : null;
        _frameInterval = Stopwatch.Frequency / (double)_frameRate;
        _queueLimit = Math.Max(6, _frameRate / 5);
        _queue = new BlockingCollection<Frame>(_queueLimit);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        _writerThread = new Thread(WriteLoop) { IsBackground = true, Name = "녹화 쓰기" };
        _writerThread.Start();
    }

    /// <summary>픽셀 하나에 쓰는 비트. 0.9 면 게임 화면과 눈으로 구별하기 어렵다(1080p 30fps ≈ 56Mbps).</summary>
    /// <remarks>
    /// <b>화질을 게임 화면에 맞춘다</b>(사용자, 2026-09-16 - "용량은 상관없어"). 녹화 영상은 두 가지로 쓰인다:
    /// 캡처 대상으로 걸어 글자를 읽고, 라벨링에서 그림을 뽑아 학습한다. 8Mbps 에서는 HUD 의 얇은 흰 획이 먼저 뭉개져
    /// 같은 자리가 화면 사진으로는 20장 중 20장, 영상에서 뽑은 장으로는 20장 중 3장만 읽혔다(실측). 뭉갠 그림으로 학습하면 모델도 같이 나빠진다.
    /// </remarks>
    public const double BitsPerPixel = 0.9;

    /// <summary>
    /// 크기·fps 에 맞춘 비트레이트. 1080p 30fps ≈ 56Mbps(1분에 약 420MB), 60fps ≈ 112Mbps.
    /// </summary>
    /// <remarks>넓은 화면일수록, fps 가 높을수록 그만큼 올린다 - 고정값은 2560x1440 이나 60fps 에서 모자란다. 8Mbps 아래·200Mbps 위로는 안 간다.</remarks>
    public static int DefaultBitrate(int width, int height, int frameRate)
        => (int)Math.Clamp((double)width * height * frameRate * BitsPerPixel, 8_000_000, 200_000_000);

    public string Name => "Media Foundation H.264 (조각 mp4)";

    /// <summary>저장하는 초당 프레임(고른 값).</summary>
    public int FrameRate => _frameRate;

    /// <summary>사람이 준 비트레이트. 없으면 첫 프레임 크기를 보고 정한다.</summary>
    private readonly int? _requestedBitrate;

    /// <summary>고른 fps 보다 빨리 와서 솎은 장수. 버림(<see cref="FramesDropped"/>, 쓰기가 밀림)과 다르다 - 정상이다.</summary>
    public int FramesSkipped => Volatile.Read(ref _framesSkipped);

    public string FilePath { get; }

    public int FramesWritten => Volatile.Read(ref _framesWritten);

    public int FramesDropped => Volatile.Read(ref _framesDropped);

    public TimeSpan Duration
    {
        get
        {
            var first = Interlocked.Read(ref _firstTimestamp);
            return first < 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(first, Interlocked.Read(ref _lastTimestamp));
        }
    }

    public Exception? Error => _error;

    public long FileSizeBytes
    {
        get
        {
            try
            {
                // 녹화기가 쓰기로 쥐고 있다 - 읽기로 열되 쓰기·지우기 공유를 허락해야 열린다. 길이만 보고 곧바로 닫는다.
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return stream.Length;
            }
            catch (Exception)
            {
                // 아직 첫 프레임 전이라 파일이 없거나, 닫는 중이다. 크기를 못 보여 줄 뿐이다.
                return 0;
            }
        }
    }

    public bool TryAddFrame(IntPtr pixels, int rowPitch, int width, int height, long timestamp)
    {
        if (_error is not null || Volatile.Read(ref _finished) != 0 || _queue.IsAddingCompleted) return false;
        if (pixels == IntPtr.Zero || width < 2 || height < 2 || rowPitch < width * 4) return false;

        // H.264 는 짝수 크기만 받는다. 크기가 바뀐 것은 솎기보다 먼저 본다 - 솎일 프레임이라도 크기가 바뀌었으면 녹화를 멈춰야 한다.
        var evenWidth = width & ~1;
        var evenHeight = height & ~1;

        if (_width == 0)
        {
            _width = evenWidth;
            _height = evenHeight;
        }
        else if (evenWidth != _width || evenHeight != _height)
        {
            _error ??= new InvalidOperationException($"녹화 중에 캡처 크기가 바뀌었습니다({_width}x{_height} → {evenWidth}x{evenHeight}). 녹화를 다시 시작하세요.");
            return false;
        }

        // 고른 fps 보다 빨리 온 프레임은 솎는다. 캡처 간격은 흔들리므로 1/3 간격만큼은 이르게 와도 받는다(FrameRateLimiter 와 같은 규칙 -
        // 1/4 이면 60Hz 를 60 으로 녹화할 때 일찍 온 장이 버려진다). 다음 받을 시각은 간격씩 밀되, 한참 늦었으면 지금부터 다시 센다.
        // FrameRateLimiter 를 그대로 못 쓰는 이유 - 쓰기 줄이 차서 버린 장은 박자를 쓰지 않아야 한다(아래).
        if (_nextDue != 0 && timestamp < _nextDue - (long)(_frameInterval / 3))
        {
            Interlocked.Increment(ref _framesSkipped);
            return false;
        }

        // 쓰기가 밀려 있으면 복사도 하지 않고 버린다 - 캡처 스레드의 시간을 아낀다.
        if (_queue.Count >= _queueLimit)
        {
            Interlocked.Increment(ref _framesDropped);
            return false;
        }

        // 받기로 했을 때만 다음 시각을 민다 - 쓰기가 밀려 버린 프레임이 자리를 차지하면 다음 프레임까지 솎여 두 장이 빈다.
        _nextDue = _nextDue == 0 || timestamp - _nextDue > _frameInterval
            ? timestamp + (long)_frameInterval
            : _nextDue + (long)_frameInterval;

        var stride = evenWidth * 4;
        var length = stride * evenHeight;
        var buffer = ArrayPool<byte>.Shared.Rent(length);

        unsafe
        {
            fixed (byte* destination = buffer)
            {
                var source = (byte*)pixels;

                for (var y = 0; y < evenHeight; y++)
                    Buffer.MemoryCopy(source + (long)y * rowPitch, destination + (long)y * stride, stride, stride);
            }
        }

        Interlocked.CompareExchange(ref _firstTimestamp, timestamp, -1);
        Interlocked.Exchange(ref _lastTimestamp, timestamp);

        if (!_queue.TryAdd(new Frame(buffer, length, timestamp)))
        {
            ArrayPool<byte>.Shared.Return(buffer);
            Interlocked.Increment(ref _framesDropped);
            return false;
        }

        return true;
    }

    private void WriteLoop()
    {
        Output? output = null;
        var started = false;
        var startTimestamp = 0L;
        var previousTime = -1L;      // 직전 시각(100ns)

        try
        {
            MediaFactory.MFStartup(true).CheckError();
            started = true;

            foreach (var frame in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (_error is not null) continue;

                    if (output is null)
                    {
                        output = CreateOutput();
                        startTimestamp = frame.Timestamp;
                    }

                    // 100ns 단위, 첫 프레임이 0. 같거나 거꾸로 가는 시각은 SinkWriter 가 싫어한다 - 1 틱씩 민다.
                    var time = (long)((frame.Timestamp - startTimestamp) * (10_000_000.0 / Stopwatch.Frequency));
                    if (time <= previousTime) time = previousTime + 1;

                    var duration = previousTime < 0 ? 10_000_000L / _frameRate : Math.Max(1, time - previousTime);
                    previousTime = time;

                    using var buffer = MediaFactory.MFCreateMemoryBuffer(frame.Length);

                    buffer.Lock(out var destination, out _, out _);
                    try
                    {
                        Marshal.Copy(frame.Pixels, 0, destination, frame.Length);
                    }
                    finally
                    {
                        buffer.Unlock();
                    }

                    buffer.CurrentLength = frame.Length;

                    using var sample = MediaFactory.MFCreateSample();
                    sample.AddBuffer(buffer);
                    sample.SampleTime = time;
                    sample.SampleDuration = duration;

                    output.Writer.WriteSample(output.StreamIndex, sample);
                    Interlocked.Increment(ref _framesWritten);
                }
                catch (Exception ex)
                {
                    _error ??= ex;
                    Logger.Error(ex, $"녹화 프레임을 쓰지 못했다: {FilePath}");
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(frame.Pixels);
                }
            }
        }
        catch (Exception ex)
        {
            _error ??= ex;
            Logger.Error(ex, $"녹화를 시작하지 못했다: {FilePath}");
        }
        finally
        {
            if (output is not null) Close(output);
            if (started) MediaFactory.MFShutdown();
        }
    }

    /// <summary>
    /// 파일을 닫는다. Finalize 는 마지막 조각(끝나지 않은 키프레임 묶음)을 쓴다. 한 장도 없으면 빈 파일을 지운다.
    /// </summary>
    /// <remarks>SinkWriter 를 미디어 싱크로 만들면 SinkWriter 가 싱크를 내리지 않는다 - 싱크 Shutdown·바이트 스트림 Close 는 우리가 한다.</remarks>
    private void Close(Output output)
    {
        var frames = FramesWritten;

        try
        {
            if (frames > 0) output.Writer.Finalize();
        }
        catch (Exception ex)
        {
            _error ??= ex;
            Logger.Error(ex, $"녹화 파일을 닫지 못했다: {FilePath}");
        }
        finally
        {
            output.Writer.Dispose();

            // 싱크가 바이트 스트림을 쥐고 있어 Shutdown 이 스트림까지 닫는다 - 그 뒤에 Close 를 또 부르면 E_INVALIDARG 다(실측 2026-09-16, 녹화를 멈출 때마다 경고).
            var sinkDown = true;

            try { output.Sink.Shutdown(); }
            catch (Exception ex) { sinkDown = false; Logger.Warn(ex, $"녹화 싱크를 내리지 못했다: {FilePath}"); }

            output.Sink.Dispose();

            // 싱크를 못 내렸을 때만 우리가 닫는다 - 안 닫으면 파일 손잡이가 남는다.
            if (!sinkDown)
            {
                try { output.Stream.Close(); }
                catch (Exception ex) { Logger.Warn(ex, $"녹화 파일 스트림을 닫지 못했다: {FilePath}"); }
            }

            output.Stream.Dispose();
        }

        if (frames == 0)
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception) { /* 지우지 못해도 결과는 같다 - 빈 파일이다 */ }
        }
        else
        {
            Logger.Info($"녹화 파일을 닫았다: {FilePath} · {frames}장");
        }
    }

    private Output CreateOutput()
    {
        IMFByteStream? stream = null;
        IMFMediaSink? sink = null;

        try
        {
            stream = MediaFactory.MFCreateFile(FileAccessMode.MfAccessModeWrite, FileOpenMode.MfOpenModeDeleteIfExist, FileFlags.None, FilePath);

            using (var output = MediaFactory.MFCreateMediaType())
            {
                output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                output.Set(MediaTypeAttributeKeys.Subtype, H264Subtype);
                // 크기는 첫 프레임에서 정해진다 - 비트레이트도 여기서 그 크기에 맞춘다.
                _bitrate = _requestedBitrate ?? DefaultBitrate(_width, _height, _frameRate);

                output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)_bitrate);
                output.Set(MediaTypeAttributeKeys.InterlaceMode, InterlaceProgressive);
                output.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)_width, (uint)_height));
                output.Set(MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(_frameRate, 1));
                output.Set(MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1));

                MediaFactory.MFCreateFMPEG4MediaSink(stream, output, null!, out sink).CheckError();
            }

            using var attributes = MediaFactory.MFCreateAttributes(2);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);

            var writer = MediaFactory.MFCreateSinkWriterFromMediaSink(sink, attributes);

            try
            {
                const int streamIndex = 0;   // 싱크를 만들 때 준 영상 스트림

                using (var input = MediaFactory.MFCreateMediaType())
                {
                    input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    input.Set(MediaTypeAttributeKeys.Subtype, Rgb32Subtype);
                    input.Set(MediaTypeAttributeKeys.InterlaceMode, InterlaceProgressive);
                    input.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)_width, (uint)_height));
                    input.Set(MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(_frameRate, 1));
                    input.Set(MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1));

                    // 양수 스트라이드 = 위에서 아래로. RGB32 의 기본은 아래에서 위라 안 적으면 영상이 뒤집힌다.
                    input.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(_width * 4));

                    writer.SetInputMediaType(streamIndex, input, null!);
                }

                writer.BeginWriting();

                Logger.Info($"녹화 파일 시작: {FilePath} · {_width}x{_height} · {_frameRate}fps · {_bitrate / 1_000_000.0:0.#}Mbps · 조각 mp4");

                return new Output(stream, sink, writer, streamIndex);
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }
        catch
        {
            sink?.Dispose();
            stream?.Dispose();
            throw;
        }
    }

    public void Finish()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;

        _queue.CompleteAdding();
        _writerThread.Join();

        Logger.Info($"녹화 끝: {FilePath} · {_frameRate}fps · {FramesWritten}장 · 버림 {FramesDropped} · 솎음 {FramesSkipped} · {Duration.TotalSeconds:0.0}초" + (_error is null ? string.Empty : $" · 오류 {_error.Message}"));
    }

    /// <remarks>줄(BlockingCollection)은 Dispose 하지 않는다 - 캡처 스레드가 방금 들고 간 녹화기에 한 장 더 넣으려다 ObjectDisposedException 이 난다. 끝났다는 표시로 막는다.</remarks>
    public void Dispose() => Finish();
}
