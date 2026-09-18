using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using DevExpress.Mvvm;

using Minguk.Tools.Capture;
using Minguk.Tools.Capture.Input;
using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Ocr;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 잡은 화면에서 글자를 읽는다 - 이름 붙인 자리 중 "계속 읽기" 를 켠 곳, 그리고 몹 머리 위 이름표.
/// </summary>
/// <remarks>
/// <b>자리를 정해서 읽는다</b> - 화면 전체를 읽으면 느리고(1080p 에 수백 ms) 엉뚱한 글이 섞인다. 스테이지 이름, 체력 숫자처럼
/// 늘 같은 자리에 뜨는 글이 목표라, 자리(<see cref="Vision.Regions.NamedRegion"/>)를 잘라 넣는다.
/// 옛 "글자 영역"(한 곳, 설정에 저장)은 자리로 합쳤다(2026-09-15) - 저장값은 처음 한 번 「글자」 자리로 옮긴다.
///
/// <b>주기</b> - 0.5초에 한 번, 켠 자리를 차례로. 글자는 몹처럼 빨리 안 바뀐다. 앞의 것이 끝났을 때만 돈다.
/// </remarks>
public abstract partial class RecognizingCaptureViewModelBase
{
    private const int OcrIntervalMs = 500;

    private IOcrEngine? _ocr;
    private int _isOcrRunning;
    private long _lastOcrTicks;

    /// <summary>계속 읽을 자리들. 캡처 스레드가 읽으므로 UI 스레드가 배열째 바꿔 끼운다.</summary>
    private volatile Vision.Regions.NamedRegion[] _liveRegions = [];

    /// <summary>상태 줄에 짧게 보이는 글자 읽기 상태. "글자 2곳 (120ms)".</summary>
    public string? OcrStatus
    {
        get => GetProperty(() => OcrStatus);
        set => SetProperty(() => OcrStatus, value);
    }

    /// <summary>계속 읽기를 켠 자리를 다시 모은다. 켠 곳이 있으면 엔진과 CPU 픽셀을 준비한다.</summary>
    private void UpdateLiveRegions() => Guard(() =>
    {
        _liveRegions = [.. Regions.Where(region => region.KeepReading)];

        if (_liveRegions.Length == 0)
        {
            OcrStatus = null;
            return;
        }

        if (!EnsureOcrEngine(out var problem))
        {
            OcrStatus = $"글자 읽기 엔진을 열지 못했습니다. {problem}";
            return;
        }

        // 픽셀이 CPU 로 안 내려오면 읽을 것이 없다. 검출과 같은 길.
        EnsureCpuReadback("글자를 읽으려면 픽셀이 필요합니다");

        OcrStatus = $"글자 {_liveRegions.Length}곳 읽는 중 ({_ocr!.Name})";
    });

    private readonly object _ocrGate = new();

    /// <summary>스크립트의 읽기()가 쓸 엔진. 없으면 null - 스크립트가 그 이유를 말한다.</summary>
    protected IOcrEngine? OcrEngineForScripts() => EnsureOcrEngine(out _) ? _ocr : null;

    /// <summary>
    /// OCR 엔진을 한 번만 만든다. 영역 읽기와 이름표 읽기가 같이 쓴다.
    /// </summary>
    /// <remarks>
    /// UI 스레드(토글)와 검출 스레드(이름표) 어디서든 부른다. 학습이 시작·끝나면 엔진을 버리므로(<see cref="DropOcrEngine"/>),
    /// 읽기 직전에 늘 여기로 확보해야 한다 - 안 그러면 설정을 되살리는 순서에 따라 이름표가
    /// 조용히 빈 글로 나온다(실제로 그랬다).
    /// </remarks>
    private bool EnsureOcrEngine(out string? problem)
    {
        problem = null;

        lock (_ocrGate)
        {
            if (_ocr is not null) return true;

            try
            {
                _ocr = OcrEngineFactory.Create(out var fallback);

                if (fallback is not null) DispatcherService?.BeginInvoke(() => StatusText = fallback);

                return true;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// 엔진을 버린다. 다음 읽기가 그때에 맞는 쪽(학습 중이면 CPU, 아니면 GPU)으로 다시 만든다.
    /// </summary>
    /// <remarks>놓기는 백그라운드에서 - 읽는 중이면 엔진이 그 읽기가 끝나기를 기다린다(<see cref="Vision.Ocr.Paddle.PaddleOcrEngine.Dispose"/>).</remarks>
    private void DropOcrEngine()
    {
        IOcrEngine? stale;

        lock (_ocrGate)
        {
            stale = _ocr;
            _ocr = null;
        }

        if (stale is not null) _ = Task.Run(stale.Dispose);
    }

    // ── 몹 머리 위 이름표 ─────────────────────────────────────────────────

    /// <summary>스크립트가 화면을 읽고 싶어 할 때(허브 <c>WantsFrames</c>) 프레임을 허브에 올리는 간격(ms).</summary>
    /// <remarks>스크립트의 읽기는 1.5초를 기다리므로 이보다 촘촘할 필요가 없다. 1080p 한 장 복사가 수 ms 라 캡처를 잡지 않게 솎는다.</remarks>
    private const int PublishFrameIntervalMs = 100;

    private byte[]? _publishCopy;
    private long _lastPublishTicks;

    /// <summary>
    /// 스크립트가 읽을 프레임을 허브에 올린다. 캡처 스레드.
    /// </summary>
    /// <remarks>
    /// 허브는 제 버퍼에 다시 복사하므로 여기 버퍼는 바로 다시 써도 된다. 스크립트의 <c>몹.이름표</c> 도 이 프레임을 자른다.
    /// </remarks>
    private void MaybePublishFrame(CapturedFrameEventArgs e)
    {
        if (!Hub.WantsFrames) return;

        // 리드백이 꺼져 있으면 CPU 로 내려온 픽셀이 없다 - 스크립트가 읽으려 하면 켠다(세션이 다시 시작된다).
        // 계속 읽기는 켤 때 이미 이렇게 한다. 스크립트만 빠져 있어 "프레임이 들어오지 않습니다" 로 끝났다(실측 2026-09-16).
        if (!e.HasPixels)
        {
            RequestReadbackForScript();

            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastPublishTicks < PublishFrameIntervalMs) return;

        _lastPublishTicks = now;

        var stride = e.Width * 4;
        var needed = stride * e.Height;

        if (_publishCopy is null || _publishCopy.Length < needed) _publishCopy = new byte[needed];

        for (var y = 0; y < e.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(e.PixelData + (y * e.RowPitch), _publishCopy, y * stride, stride);

        Hub.PublishFrame(_publishCopy, e.Width, e.Height);
    }


    private long _readbackAskedTicks;

    /// <summary>스크립트가 읽을 수 있게 리드백을 켠다. 캡처 스레드에서 불리므로 UI 스레드로 넘긴다.</summary>
    /// <remarks>켜는 동안(세션 재시작) 프레임이 계속 들어와 여기로 또 오므로, 3초에 한 번만 청한다.</remarks>
    private void RequestReadbackForScript()
    {
        var now = Environment.TickCount64;

        if (EnableCpuReadback || now - _readbackAskedTicks < 3000) return;

        _readbackAskedTicks = now;

        // 켜는 동안(캡처 재시작) 스크립트가 더 기다리게 알린다.
        Hub.PreparingFrames();

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Turn() => EnsureCpuReadback("스크립트가 화면 글자를 읽습니다");

        if (dispatcher is null || dispatcher.CheckAccess()) Turn();
        else dispatcher.BeginInvoke((Action)Turn);
    }


    // ── 계속 읽기 ────────────────────────────────────────────────────────

    /// <summary>프레임마다 불린다. 캡처 스레드. 시간이 됐고 앞의 것이 끝났을 때만, 켠 자리들을 잘라 백그라운드로 넘긴다.</summary>
    private void MaybeReadRegions(CapturedFrameEventArgs e)
    {
        var live = _liveRegions;

        if (live.Length == 0 || !e.HasPixels) return;

        var now = Environment.TickCount64;
        if (now - _lastOcrTicks < OcrIntervalMs) return;
        if (Interlocked.CompareExchange(ref _isOcrRunning, 1, 0) != 0) return;

        _lastOcrTicks = now;

        (Vision.Regions.NamedRegion Region, Vision.Regions.RegionTarget Target, BitmapSource Crop, Rect Bounds, int Width, int Height)[] crops;

        try
        {
            // 픽셀은 이 콜백이 돌아가면 사라진다. 칸(돌린 칸은 감싸는 상자)만 지금 복사하고, 세우기·읽기는 백그라운드에서.
            crops = [.. live.Where(region => region.Width > 0 && region.Height > 0)
                .SelectMany(region => Vision.Regions.RegionTargets.Of(region, null).Select(target =>
                {
                    var bounds = Vision.Regions.RegionTargets.Bounds(target, e.Width, e.Height);

                    return (region, target, CropFrame(e, bounds), bounds, e.Width, e.Height);
                }))];
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "읽을 자리를 자르지 못했다");
            Interlocked.Exchange(ref _isOcrRunning, 0);
            return;
        }

        _ = Task.Run(() => ReadRegionsAsync(crops));
    }

    /// <summary>프레임의 한 부분을 복사해 Bgra32 그림으로 만든다. 전체를 복사하지 않는다.</summary>
    private static BitmapSource CropFrame(CapturedFrameEventArgs e, Rect region)
    {
        var left = Math.Clamp((int)Math.Floor(region.X * e.Width), 0, e.Width - 1);
        var top = Math.Clamp((int)Math.Floor(region.Y * e.Height), 0, e.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(region.Right * e.Width), left + 1, e.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom * e.Height), top + 1, e.Height);

        var width = right - left;
        var height = bottom - top;

        var start = e.PixelData + (top * e.RowPitch) + (left * 4);
        var size = ((height - 1) * e.RowPitch) + (width * 4);

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, start, size, e.RowPitch);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>칸마다 읽어 칸의 <c>LastText</c> 에, 자리에는 이은 글을 적는다. 지금 읽기와 같은 길이다.</summary>
    private async Task ReadRegionsAsync((Vision.Regions.NamedRegion Region, Vision.Regions.RegionTarget Target, BitmapSource Crop, Rect Bounds, int Width, int Height)[] crops)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<(Vision.Regions.NamedRegion Region, Vision.Regions.RegionCell Cell, string Text)>(crops.Length);

        try
        {
            if (!EnsureOcrEngine(out var problem) || _ocr is not { } ocr)
            {
                DispatcherService?.BeginInvoke(() => OcrStatus = $"글자 읽기 엔진을 열지 못했습니다. {problem}");
                return;
            }

            foreach (var (region, target, crop, bounds, width, height) in crops)
            {
                var upright = Vision.Regions.RegionTargets.Upright(crop, bounds, target, width, height);
                var outcome = await ocr.RecognizeAsync(upright);

                results.Add((region, target.Cell, outcome.Text.Replace(Environment.NewLine, " ").Trim()));
            }

            DispatcherService?.BeginInvoke(() => Guard(() =>
            {
                foreach (var group in results.GroupBy(r => r.Region))
                {
                    if (!group.Key.KeepReading) continue;

                    // 「숫자만」 이면 숫자 덩어리만 보인다 - 스크립트의 숫자읽기와 같은 눈으로.
                    foreach (var (_, cell, text) in group) cell.LastText = Vision.Regions.NamedRegion.Shown(group.Key, cell, text);

                    group.Key.LastText = Vision.Regions.NamedRegion.Shown(group.Key, null, Vision.Regions.RegionTargets.Combine([.. group.Select(r => r.Text)]).Text);
                }

                OcrStatus = $"글자 {results.Select(r => r.Region).Distinct().Count()}곳 ({watch.Elapsed.TotalMilliseconds:0}ms)";
                RegionsRevision++;
            }));
        }
        catch (ObjectDisposedException)
        {
            // 학습이 시작·끝나 엔진을 바꾸는 사이에 걸렸다. 다음 주기에 새 엔진으로 읽는다.
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "자리 글자를 읽지 못했다");

            var message = Vision.Training.TrainingActivity.IsGpuLost(ex)
                ? Vision.Training.TrainingActivity.GpuLostMessage
                : $"글자 읽기 실패 - 로그를 보세요(0x{ex.HResult:X8})";

            DispatcherService?.BeginInvoke(() => OcrStatus = message);
        }
        finally
        {
            Interlocked.Exchange(ref _isOcrRunning, 0);
        }
    }

    // ── 설정 ─────────────────────────────────────────────────────────────

    /// <summary>옛 "글자 영역" 저장 키. 읽어서 자리로 옮기고 지운다.</summary>
    private const string OcrRegionSettingKey = "OcrRegion";

    /// <summary>옛 글자 영역이 설정에 있으면 「글자」 자리로 옮기고 설정을 비운다(한 번만).</summary>
    private void MigrateOcrRegionSetting()
    {
        var parts = GetSettingOrLegacy(OcrRegionSettingKey, string.Empty).Split(',');

        if (parts.Length == 4
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
            && w > 0 && h > 0)
        {
            MigrateOldOcrRegion(new Rect(x, y, w, h));
        }

        SetSetting(OcrRegionSettingKey, string.Empty);
    }

    private void ReleaseOcr()
    {
        _liveRegions = [];
        _ocr?.Dispose();
        _ocr = null;
    }
}

