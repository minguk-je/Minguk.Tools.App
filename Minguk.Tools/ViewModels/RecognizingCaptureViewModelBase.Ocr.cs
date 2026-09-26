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

using Minguk.Base.Utilities;

using Minguk.Tools.Capture;
using Minguk.Tools.Capture.Input;
using Minguk.Tools.Vision.Inference;
using Minguk.Tools.Vision.Ocr;

namespace Minguk.Tools.ViewModels;

/// <summary>영역 그리드의 「OCR 엔진」 콤보 한 줄 - <see cref="EngineName"/> 이 null 이면 "기본"(전체 설정을 따른다).</summary>
public sealed record RegionOcrEngineOption(string? EngineName, string Label);

/// <summary>
/// 잡은 화면에서 글자를 읽는다 - 이름 붙인 자리 중 "계속 읽기" 를 켠 곳, 그리고 검출 머리 위 이름표.
/// </summary>
/// <remarks>
/// <b>자리를 정해서 읽는다</b> - 화면 전체를 읽으면 느리고(1080p 에 수백 ms) 엉뚱한 글이 섞인다. 스테이지 이름, 체력 숫자처럼
/// 늘 같은 자리에 뜨는 글이 목표라, 자리(<see cref="Vision.Regions.NamedRegion"/>)를 잘라 넣는다.
/// 옛 "글자 영역"(한 곳, 설정에 저장)은 자리로 합쳤다(2026-09-15) - 저장값은 처음 한 번 「글자」 자리로 옮긴다.
///
/// <b>주기</b> - 0.5초에 한 번, 켠 자리를 차례로. 글자는 검출처럼 빨리 안 바뀐다. 앞의 것이 끝났을 때만 돈다.
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

    /// <summary>고를 수 있는 글자 읽기 엔진들(콤보).</summary>
    public IReadOnlyList<OcrEngineChoice> OcrEngines => OcrEngineChoice.All;

    /// <summary>
    /// 영역 그리드의 「OCR 엔진」 열이 고르는 것 - 맨 앞은 "기본"(<see cref="Vision.Regions.NamedRegion.OcrEngineName"/> 이 비는 값, null).
    /// 사용자(2026-09-18) "영역별로 어떤 OCR 쓸지 따로 지정 가능하게" · "그리드에 별도로 선택 안 하면 기본으로, 지정하면 그걸로".
    /// </summary>
    /// <remarks>도구 줄 콤보와 같은 이름(<see cref="OcrEngineChoice.Name"/>) - 이름이 짧아 열이 안 넓어진다(사용자, 2026-09-26 "컬럼 넓이 너무 넓혔어" · "이름이 틀리네").</remarks>
    public IReadOnlyList<RegionOcrEngineOption> RegionOcrEngineOptions { get; } =
        [new(null, "기본"), .. OcrEngineChoice.All.Select(c => new RegionOcrEngineOption(c.Kind.ToString(), c.Name))];

    /// <summary>
    /// 고른 엔진. 바꾸면 지금 것을 버리고 다음 읽기에서 새로 만든다 - 읽는 도중이면 그 읽기가 끝난 뒤 놓인다.
    /// </summary>
    /// <remarks>사용자(2026-09-18) "OCR 종류 선택해서 돌려 볼 수 있게". 앱 전체에 하나(설정 키 <see cref="OcrEngineSettingKey"/>) - 스크립트 화면에서 고르면 플레이도 같은 것으로 읽는다.</remarks>
    public OcrEngineChoice SelectedOcrEngine
    {
        get => GetProperty(() => SelectedOcrEngine) ?? OcrEngineChoice.Default;
        set => SetProperty(() => SelectedOcrEngine, value, () =>
        {
            if (value is null) return;

            AppSettingUtility.Set(OcrEngineSettingKey, value.Kind.ToString());
            DropAllOcrEngines();
            OcrStatus = $"글자 읽기 엔진: {value.Name} - 다음 읽기부터";
        });
    }

    /// <summary>화면 이름과 무관한 앱 전체 키 - 어느 화면에서 골라도 같다.</summary>
    private const string OcrEngineSettingKey = "Minguk.Tools.Ocr.Engine";

    /// <summary>이 PC 의 GPU 들(콤보). DirectML 번호 순서.</summary>
    public IReadOnlyList<Vision.Inference.GpuChoice> GpuChoices { get; } = Vision.Inference.GpuAdapters.List();

    /// <summary>
    /// OCR·검출이 쓸 GPU(사용자, 2026-09-26 "GPU 2장인데?" - 게임과 다른 카드로). 앱 전체 설정(<see cref="Minguk.Tools.Inference.DmlDevice"/>).
    /// 바꾸면 OCR 엔진은 버리고 다음 읽기에서 새 카드로 만든다. 검출 모델은 다음에 올릴 때부터.
    /// </summary>
    public Vision.Inference.GpuChoice SelectedGpu
    {
        get => GetProperty(() => SelectedGpu) ?? GpuChoices[0];
        set => SetProperty(() => SelectedGpu, value, () =>
        {
            if (value is null || Minguk.Tools.Inference.DmlDevice.Index == value.Index) return;

            Minguk.Tools.Inference.DmlDevice.Index = value.Index;
            DropAllOcrEngines();
            OcrStatus = $"GPU: {value.Name} - 글자 읽기는 다음 읽기부터, 검출은 다음에 모델을 올릴 때부터";
        });
    }

    /// <summary>저장된 엔진·GPU 를 되살린다. <c>RestoreSettings</c> 에서.</summary>
    protected void RestoreOcrEngineChoice()
    {
        // 저장된 번호가 이 PC 에 없으면(다른 PC 에서 고른 값) 0 으로 - 설정은 앱 전체에 남아 PC 를 옮기면 안 맞을 수 있다.
        var gpu = Minguk.Tools.Inference.DmlDevice.Index;

        if (GpuChoices.All(c => c.Index != gpu)) Minguk.Tools.Inference.DmlDevice.Index = gpu = 0;

        SelectedGpu = GpuChoices.FirstOrDefault(c => c.Index == gpu) ?? GpuChoices[0];
        SelectedOcrEngine = OcrEngineChoice.Parse(AppSettingUtility.Get(OcrEngineSettingKey, OcrEngineChoice.Default.Kind.ToString()));
    }

    /// <summary>스크립트의 읽기()가 쓸 엔진(전체 설정 것). 없으면 null - 스크립트가 그 이유를 말한다.</summary>
    protected IOcrEngine? OcrEngineForScripts() => EnsureOcrEngine(out _) ? _ocr : null;

    /// <summary>
    /// 스크립트의 읽기()가 쓸 엔진 - 자리를 주면 그 자리가 지정한 엔진(없으면 전체 설정 것).
    /// </summary>
    /// <remarks>사용자(2026-09-18) "영역별로 어떤 OCR 쓸지 따로 지정 가능하게" - Windows OCR 은 평범한 글자는 읽어도 게임 HUD 각진 숫자는 못 읽었다.</remarks>
    protected IOcrEngine? OcrEngineForScripts(Vision.Regions.NamedRegion? region)
        => TryGetOcrEngine(region?.OcrEngine ?? SelectedOcrEngine.Kind, out var engine, out _) ? engine : null;

    /// <summary>
    /// OCR 엔진을 한 번만 만든다(종류마다 하나) - 영역 읽기·이름표 읽기·자리별 지정 엔진이 다 같이 쓴다.
    /// </summary>
    /// <remarks>
    /// UI 스레드(토글)와 검출 스레드(이름표) 어디서든 부른다. 학습이 시작·끝나면 다 버리므로(<see cref="DropAllOcrEngines"/>),
    /// 읽기 직전에 늘 여기로 확보해야 한다 - 안 그러면 설정을 되살리는 순서에 따라 이름표가
    /// 조용히 빈 글로 나온다(실제로 그랬다).
    /// </remarks>
    private bool EnsureOcrEngine(out string? problem) => TryGetOcrEngine(SelectedOcrEngine.Kind, out _ocr, out problem);

    /// <summary>종류별로 하나씩 - 자리마다 다른 엔진을 지정해도(사용자, 2026-09-18) 같은 종류면 다시 안 만든다.</summary>
    private readonly Dictionary<OcrEngineKind, IOcrEngine> _ocrEngines = new();

    private bool TryGetOcrEngine(OcrEngineKind requested, out IOcrEngine? engine, out string? problem)
    {
        problem = null;

        // 학습 중이면 GPU 대신 CPU - 같은 카드에서 CUDA 학습과 DirectML 추론이 겹치면 GPU 가 리셋된다(실측).
        var kind = Vision.Training.TrainingActivity.IsBusy && requested == OcrEngineKind.PaddleGpu ? OcrEngineKind.PaddleCpu : requested;

        lock (_ocrGate)
        {
            if (_ocrEngines.TryGetValue(kind, out engine)) return true;

            try
            {
                engine = OcrEngineFactory.Create(kind, out var fallback);
                _ocrEngines[kind] = engine;

                if (fallback is not null) DispatcherService?.BeginInvoke(() => StatusText = fallback);

                return true;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
                engine = null;
                return false;
            }
        }
    }

    // ── 스크립트 시작 전 준비 ───────────────────────────────────────────────

    /// <summary>한 번 깨운 엔진. 버리면(<see cref="DropAllOcrEngines"/>) 같이 비운다 - 새 엔진은 다시 깨운다.</summary>
    private readonly HashSet<IOcrEngine> _warmedOcr = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 스크립트가 곧 화면을 읽는다 - 리드백을 지금 켜고(캡처가 다시 시작된다) 글자 읽기 모델을 깨운다. UI 스레드, 시작 요청 때.
    /// </summary>
    /// <remarks>
    /// 첫 읽기가 이 둘을 도는 동안 치렀다 - 앱을 켜고 처음 돌린 사냥에서 첫 <c>숫자읽기</c> 가 3838ms(모델 둘 올리기 + 캡처 다시 시작 + DirectML 첫 추론, 실측 2026-09-24).
    /// 시작 대기·컴파일과 나란히 돌게 여기서 먼저 한다. 자리가 없는 프로젝트는 읽을 것이 없으니 안 한다.
    /// </remarks>
    protected void PrepareRecognitionForScript()
    {
        if (Regions.Count == 0) return;

        if (IsRunning && !EnableCpuReadback)
        {
            Hub.PreparingFrames();
            EnsureCpuReadback("스크립트가 화면 글자를 읽습니다");
        }

        WarmOcrEngines();
    }

    /// <summary>
    /// 이 프로젝트의 자리들이 쓰는 엔진을 만들고 한 번 읽혀 둔다(백그라운드). 이미 깨운 것은 건너뛴다.
    /// </summary>
    /// <remarks>캡처를 시작할 때도 부른다 - F5 를 누르기 전에 끝나 있으면 첫 실행도 빠르다. 학습 중이면 <see cref="TryGetOcrEngine"/> 이 CPU 로 바꿔 준다.</remarks>
    private void WarmOcrEngines()
    {
        var kinds = Regions.Select(region => region.OcrEngine ?? SelectedOcrEngine.Kind).Distinct().ToList();

        if (kinds.Count == 0) return;

        var sample = OcrWarmUpSample.Value;

        _ = Task.Run(async () =>
        {
            foreach (var kind in kinds)
            {
                if (!TryGetOcrEngine(kind, out var engine, out _) || engine is null) continue;

                lock (_ocrGate)
                {
                    if (!_warmedOcr.Add(engine)) continue;
                }

                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    await engine.RecognizeAsync(sample);
                    Logger.Debug($"글자 읽기 예열 {engine.Name} {watch.ElapsedMilliseconds}ms");
                }
                catch (ObjectDisposedException)
                {
                    // 깨우는 사이에 엔진을 바꿨다(학습 시작·끝, 엔진 콤보). 새 엔진은 첫 읽기가 깨운다.
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"글자 읽기 예열에 실패했다 ({engine.Name}) - 첫 읽기에서 다시 한다");
                }
            }
        });
    }

    /// <summary>
    /// 예열에 읽힐 그림 - 숫자·한글이 든 한 줄. 빈 그림이면 글자를 못 찾아 인식 모델이 안 돌아 깨지 않는다.
    /// </summary>
    /// <remarks>WPF 로 그리므로 처음 꺼낼 때 UI 스레드여야 한다 - <see cref="PrepareRecognitionForScript"/>·캡처 시작이 UI 스레드다.</remarks>
    private static readonly Lazy<BitmapSource> OcrWarmUpSample = new(() =>
    {
        const int width = 360, height = 48;

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

            var text = new FormattedText("7,320 / 10,129 사냥 줍기", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Malgun Gothic"), 24, Brushes.White, 1.0);

            context.DrawText(text, new Point(8, (height - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    });

    /// <summary>
    /// 만든 엔진을 다 버린다. 다음 읽기가 그때에 맞는 쪽(학습 중이면 CPU, 아니면 GPU)으로 다시 만든다.
    /// </summary>
    /// <remarks>놓기는 백그라운드에서 - 읽는 중이면 엔진이 그 읽기가 끝나기를 기다린다(<see cref="Vision.Ocr.Paddle.PaddleOcrEngine.Dispose"/>).</remarks>
    private void DropAllOcrEngines()
    {
        List<IOcrEngine> stale;

        lock (_ocrGate)
        {
            stale = [.. _ocrEngines.Values];
            _ocrEngines.Clear();
            _warmedOcr.Clear();
            _ocr = null;
        }

        foreach (var engine in stale) _ = Task.Run(engine.Dispose);
    }

    // ── 검출 머리 위 이름표 ─────────────────────────────────────────────────

    /// <summary>스크립트가 화면을 읽고 싶어 할 때(허브 <c>WantsFrames</c>) 프레임을 허브에 올리는 간격(ms).</summary>
    /// <remarks>스크립트의 읽기는 1.5초를 기다리므로 이보다 촘촘할 필요가 없다. 1080p 한 장 복사가 수 ms 라 캡처를 잡지 않게 솎는다.</remarks>
    private const int PublishFrameIntervalMs = 100;

    private byte[]? _publishCopy;
    private long _lastPublishTicks;

    /// <summary>
    /// 스크립트가 읽을 프레임을 허브에 올린다. 캡처 스레드.
    /// </summary>
    /// <remarks>
    /// 허브는 제 버퍼에 다시 복사하므로 여기 버퍼는 바로 다시 써도 된다. 스크립트의 <c>검출.이름표</c> 도 이 프레임을 자른다.
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
            foreach (var (region, target, crop, bounds, width, height) in crops)
            {
                // 자리가 엔진을 지정했으면 그것, 아니면 위 콤보에서 고른 것(사용자, 2026-09-18 "영역별로 어떤 OCR 쓸지 따로 지정").
                if (!TryGetOcrEngine(region.OcrEngine ?? SelectedOcrEngine.Kind, out var ocr, out var problem) || ocr is null)
                {
                    DispatcherService?.BeginInvoke(() => OcrStatus = $"「{region.Name}」 글자 읽기 엔진을 열지 못했습니다. {problem}");
                    continue;
                }

                var upright = Vision.Regions.RegionTargets.Upright(crop, bounds, target, width, height);
                var outcome = await ocr.RecognizeAsync(Vision.Ocr.RegionPreprocess.Apply(upright, region));

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

