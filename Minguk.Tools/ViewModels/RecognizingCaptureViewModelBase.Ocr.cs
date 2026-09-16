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
            OcrStatus = $"글자 읽기 엔진을 만들지 못했습니다 - Windows OCR 언어 팩을 확인하세요. {problem}";
            return;
        }

        // 픽셀이 CPU 로 안 내려오면 읽을 것이 없다. 검출과 같은 길.
        EnsureCpuReadback("글자를 읽으려면 픽셀이 필요합니다");

        OcrStatus = $"글자 {_liveRegions.Length}곳 읽는 중 ({_ocr!.Language})";
    });

    /// <summary>
    /// 깔린 OCR 언어들. 없으면 팩터리의 기본 하나만 보여 준다.
    /// </summary>
    /// <remarks>
    /// 한국어 팩은 숫자 0 을 "이" 로 읽기도 하고, 영어 팩은 한글을 못 읽는다. 읽을 것이 숫자면
    /// en-US, 이름표면 ko 로 고른다. 한 번에 한 언어다 - 둘 다 돌려 고르는 것은 필요해지면.
    /// </remarks>
    public IReadOnlyList<string> OcrLanguages { get; } =
        WindowsOcrEngine.AvailableLanguages.Count > 0 ? WindowsOcrEngine.AvailableLanguages : [OcrEngineFactory.PreferredLanguage];

    /// <summary>읽을 언어. 바꾸면 엔진을 새로 만든다(다음 읽기부터).</summary>
    public string? SelectedOcrLanguage
    {
        get => GetProperty(() => SelectedOcrLanguage);
        set => SetProperty(() => SelectedOcrLanguage, value, () =>
        {
            // 엔진은 언어에 묶여 있다. 버리면 다음 읽기에서 새 언어로 다시 만든다.
            lock (_ocrGate)
            {
                _ocr?.Dispose();
                _ocr = null;
            }

            if (_liveRegions.Length > 0 || IsNameplateOcrOn) StatusText = $"OCR 언어를 {SelectedOcrLanguage} 로 바꿨습니다.";
            if (_liveRegions.Length > 0) UpdateLiveRegions();
        });
    }

    private readonly object _ocrGate = new();

    /// <summary>스크립트의 읽기()가 쓸 엔진. 없으면 null - 스크립트가 그 이유를 말한다.</summary>
    protected IOcrEngine? OcrEngineForScripts() => EnsureOcrEngine(out _) ? _ocr : null;

    /// <summary>
    /// OCR 엔진을 한 번만 만든다. 영역 읽기와 이름표 읽기가 같이 쓴다.
    /// </summary>
    /// <remarks>
    /// UI 스레드(토글)와 검출 스레드(이름표) 어디서든 부른다. 언어를 바꾸면 엔진을 버리므로,
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
                _ocr = OcrEngineFactory.Create(SelectedOcrLanguage ?? OcrEngineFactory.PreferredLanguage);
                return true;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
                return false;
            }
        }
    }

    // ── 몹 머리 위 이름표 ─────────────────────────────────────────────────

    /// <summary>
    /// 찾은 몹마다 머리 위 이름표를 읽어 캡션에 붙일지.
    /// </summary>
    /// <remarks>
    /// 이름표 자리는 몹마다 다르니 고정 영역으로는 못 읽는다. 검출이 끝나면 사각형마다
    /// <see cref="NameplateRegion.Above"/> 를 원본 프레임에서 잘라 읽는다. 검출은 줄인 그림으로
    /// 하지만 글자는 12px 남짓이라 원본이 있어야 한다 - 그래서 검출 주기마다 프레임을 한 벌 복사해 둔다.
    /// </remarks>
    public bool IsNameplateOcrOn
    {
        get => GetProperty(() => IsNameplateOcrOn);
        set => SetProperty(() => IsNameplateOcrOn, value, () =>
        {
            if (!IsNameplateOcrOn) return;

            if (!EnsureOcrEngine(out var problem))
            {
                IsNameplateOcrOn = false;
                StatusText = problem!;
                return;
            }

            StatusText = IsMobDetectionOn
                ? "이름표 읽기: 찾은 몹마다 머리 위 글자를 읽어 캡션에 붙입니다."
                : "이름표 읽기는 몹 찾기가 켜져 있을 때 돕니다. 몹 찾기를 켜세요.";
        });
    }

    /// <summary>스크립트가 화면을 읽고 싶어 할 때(허브 <c>WantsFrames</c>) 프레임을 허브에 올리는 간격(ms).</summary>
    /// <remarks>스크립트의 읽기는 1.5초를 기다리므로 이보다 촘촘할 필요가 없다. 1080p 한 장 복사가 수 ms 라 캡처를 잡지 않게 솎는다.</remarks>
    private const int PublishFrameIntervalMs = 100;

    private byte[]? _publishCopy;
    private long _lastPublishTicks;

    /// <summary>
    /// 스크립트가 읽을 프레임을 허브에 올린다. 캡처 스레드.
    /// </summary>
    /// <remarks>
    /// 이름표 복사(<see cref="CopyFrameForNameplates"/>)와 버퍼를 나눈다 - 이름표는 검출 스레드가 나중에 읽는데
    /// 같은 버퍼에 덮어쓰면 반쯤 바뀐 그림을 읽는다. 허브는 제 버퍼에 다시 복사하므로 여기 버퍼는 바로 다시 써도 된다.
    /// </remarks>
    private void MaybePublishFrame(CapturedFrameEventArgs e)
    {
        if (!Hub.WantsFrames) return;

        // 리드백이 꺼져 있으면 CPU 로 내려온 픽셀이 없다 - 스크립트가 읽으려 하면 켠다(세션이 다시 시작된다).
        // 이름표 읽기·계속 읽기는 켤 때 이미 이렇게 한다. 스크립트만 빠져 있어 "프레임이 들어오지 않습니다" 로 끝났다(실측 2026-09-16).
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

    /// <summary>검출 주기에 맞춰 복사해 둔 원본 프레임(Bgra32, 줄 간격 = 너비*4). 이름표를 여기서 자른다.</summary>
    private byte[]? _frameCopy;
    private int _frameCopyWidth;
    private int _frameCopyHeight;

    /// <summary>프레임 전체를 복사한다. 캡처 스레드. 8MB 를 0.65초에 한 번이라 부담이 없다.</summary>
    private void CopyFrameForNameplates(CapturedFrameEventArgs e)
    {
        var stride = e.Width * 4;
        var needed = stride * e.Height;

        if (_frameCopy is null || _frameCopy.Length < needed) _frameCopy = new byte[needed];

        for (var y = 0; y < e.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(e.PixelData + (y * e.RowPitch), _frameCopy, y * stride, stride);

        _frameCopyWidth = e.Width;
        _frameCopyHeight = e.Height;
    }

    /// <summary>
    /// 찾은 몹들의 이름표를 읽는다. 백그라운드(검출 스레드)에서 검출 직후에 부른다.
    /// 못 읽은 자리는 빈 글이다. 순서는 <paramref name="found"/> 와 같다.
    /// </summary>
    private string[] ReadNameplates(IReadOnlyList<Detection> found)
    {
        var names = new string[found.Count];

        if (found.Count == 0 || _frameCopy is null || _frameCopyWidth == 0) return names;
        if (!EnsureOcrEngine(out _)) return names;

        var width = _frameCopyWidth;
        var height = _frameCopyHeight;
        var stride = width * 4;

        for (var i = 0; i < found.Count; i++)
        {
            try
            {
                var region = NameplateRegion.Above(found[i].Box);
                if (region.IsEmpty) continue;

                var left = Math.Clamp((int)Math.Floor(region.X * width), 0, width - 1);
                var top = Math.Clamp((int)Math.Floor(region.Y * height), 0, height - 1);
                var right = Math.Clamp((int)Math.Ceiling(region.Right * width), left + 1, width);
                var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom * height), top + 1, height);

                var w = right - left;
                var h = bottom - top;
                var pixels = new byte[w * 4 * h];

                for (var y = 0; y < h; y++)
                    Buffer.BlockCopy(_frameCopy, ((top + y) * stride) + (left * 4), pixels, y * w * 4, w * 4);

                var crop = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
                crop.Freeze();

                // 빨간 글자만 남겨 키운다. 그대로 넣으면 빈 글이 나온다 - NameplateInk 의 사연.
                names[i] = _ocr.RecognizeAsync(NameplateInk.Prepare(crop)).GetAwaiter().GetResult().Text.Replace(Environment.NewLine, " ").Trim();
            }
            catch (Exception ex)
            {
                Logger.Debug($"이름표를 못 읽었다: {ex.Message}");
            }
        }

        return names;
    }

    // ── 계속 읽기 ────────────────────────────────────────────────────────

    /// <summary>프레임마다 불린다. 캡처 스레드. 시간이 됐고 앞의 것이 끝났을 때만, 켠 자리들을 잘라 백그라운드로 넘긴다.</summary>
    private void MaybeReadRegions(CapturedFrameEventArgs e)
    {
        var live = _liveRegions;

        if (live.Length == 0 || _ocr is null || !e.HasPixels) return;

        var now = Environment.TickCount64;
        if (now - _lastOcrTicks < OcrIntervalMs) return;
        if (Interlocked.CompareExchange(ref _isOcrRunning, 1, 0) != 0) return;

        _lastOcrTicks = now;

        (Vision.Regions.NamedRegion Region, BitmapSource Crop)[] crops;

        try
        {
            // 픽셀은 이 콜백이 돌아가면 사라진다. 자리만 지금 복사한다.
            crops = [.. live.Where(region => region.Width > 0 && region.Height > 0).Select(region => (region, CropFrame(e, region.Rect)))];
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

    /// <summary>언어별 엔진. 자리마다 언어가 다를 수 있어 하나씩 만들어 들고 있는다 - 만드는 데 0.1초가 든다.</summary>
    private readonly Dictionary<string, Vision.Ocr.IOcrEngine?> _engineByLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>이 자리를 읽을 엔진. 자리에 언어가 안 적혀 있으면 화면에서 고른 언어.</summary>
    private Vision.Ocr.IOcrEngine RegionEngine(Vision.Regions.NamedRegion region, Vision.Ocr.IOcrEngine fallback)
        => region.Language.Length > 0 ? EngineFor(region.Language) ?? fallback : fallback;

    /// <summary>쓴 엔진과 다른 언어의 엔진(숫자에 강한 영문 ↔ 화면 언어). 없으면 null.</summary>
    private Vision.Ocr.IOcrEngine? OtherEngine(Vision.Ocr.IOcrEngine used)
    {
        var wanted = string.Equals(used.Language, "en-US", StringComparison.OrdinalIgnoreCase)
            ? SelectedOcrLanguage ?? Vision.Ocr.OcrEngineFactory.PreferredLanguage
            : "en-US";

        return string.Equals(wanted, used.Language, StringComparison.OrdinalIgnoreCase) ? null : EngineFor(wanted);
    }

    private Vision.Ocr.IOcrEngine? EngineFor(string language)
    {
        if (_engineByLanguage.TryGetValue(language, out var engine)) return engine;

        engine = Vision.Ocr.OcrEngineFactory.TryCreate(language);
        _engineByLanguage[language] = engine;

        return engine;
    }

    /// <summary>자리마다 그 자리의 손질·언어로 읽어 <c>LastText</c> 에 적는다. 지금 읽기와 같은 길이다.</summary>
    private async Task ReadRegionsAsync((Vision.Regions.NamedRegion Region, BitmapSource Crop)[] crops)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var results = new List<(Vision.Regions.NamedRegion Region, string Text)>(crops.Length);

        try
        {
            if (_ocr is not { } ocr) return;

            foreach (var (region, crop) in crops)
            {
                var prepared = region.Preprocess.Prepare(crop, region.PreprocessOptions);
                var engine = RegionEngine(region, ocr);
                var outcome = await engine.RecognizeAsync(prepared);
                var text = outcome.Text.Replace(Environment.NewLine, " ").Trim();

                // 자리에 적어 둔 언어로 안 읽히면 다른 언어로 한 번 더 - 같은 숫자를 ko 는 읽고 en-US 는 못 읽는 자리가 있다.
                if (text.Length == 0 && OtherEngine(engine) is { } other)
                    text = (await other.RecognizeAsync(prepared)).Text.Replace(Environment.NewLine, " ").Trim();

                results.Add((region, text));
            }

            DispatcherService?.BeginInvoke(() => Guard(() =>
            {
                foreach (var (region, text) in results)
                    if (region.KeepReading) region.LastText = text;

                OcrStatus = $"글자 {results.Count}곳 ({watch.Elapsed.TotalMilliseconds:0}ms)";
                RegionsRevision++;
            }));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "자리 글자를 읽지 못했다");
            DispatcherService?.BeginInvoke(() => OcrStatus = $"글자 읽기 실패 - 로그를 보세요(0x{ex.HResult:X8})");
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

        foreach (var engine in _engineByLanguage.Values) engine?.Dispose();
        _engineByLanguage.Clear();
    }
}

