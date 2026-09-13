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
/// 잡은 화면의 한 영역에서 글자를 읽는다.
/// </summary>
/// <remarks>
/// <b>영역을 정해서 읽는다</b> - 화면 전체를 읽으면 느리고(1080p 에 수백 ms) 엉뚱한 글이
/// 섞인다. 스테이지 이름, 체력 숫자처럼 늘 같은 자리에 뜨는 글이 목표라, 미리보기에서
/// 사각형을 끌어 정하고 그 부분만 잘라 넣는다. 영역은 0~1 비율로 저장해 해상도가 바뀌어도 맞는다.
///
/// <b>주기</b> - 0.5초에 한 번. 글자는 몹처럼 빨리 안 바뀐다. 검출과 같이 한 번에 하나만 돈다.
///
/// <b>영역 지정 중에는 클릭이 게임으로 안 나간다.</b> 같은 미리보기 위에서 끌기 때문에,
/// 지정 모드가 켜져 있으면 마우스 다운을 여기서 먼저 가로챈다.
/// </remarks>
public abstract partial class RecognizingCaptureViewModelBase
{
    private const int OcrIntervalMs = 500;

    private IOcrEngine? _ocr;
    private int _isOcrRunning;
    private long _lastOcrTicks;

    /// <summary>영역을 끌기 시작한 자리(비율). 없으면 끄는 중이 아니다.</summary>
    private Point? _ocrPickStart;

    /// <summary>글자 읽기를 돌릴지.</summary>
    public bool IsOcrOn
    {
        get => GetProperty(() => IsOcrOn);
        set => SetProperty(() => IsOcrOn, value, () =>
        {
            RaisePropertyChanged(nameof(IsOcrRegionVisible));
            OnOcrChanged();
        });
    }

    /// <summary>켜면 미리보기에서 끄는 사각형이 글자 영역이 된다. 끌고 나면 알아서 꺼진다. 끌지 않고 누르기만 하면 있던 영역을 지운다.</summary>
    public bool IsOcrRegionPicking
    {
        get => GetProperty(() => IsOcrRegionPicking);
        set => SetProperty(() => IsOcrRegionPicking, value, () =>
        {
            RaisePropertyChanged(nameof(IsOcrRegionVisible));

            // 같은 손짓을 두 기능이 나눠 쓴다. 이름 붙인 자리 쪽은 끈다.
            if (IsOcrRegionPicking) IsRegionPicking = false;

            if (IsOcrRegionPicking)
                StatusText = OcrRegion.IsEmpty || OcrRegion.Width <= 0
                    ? "미리보기에서 글자가 있는 자리를 끌어 사각형을 그리세요."
                    : "미리보기에서 끌어 새로 그리거나, 끌지 않고 누르기만 하면 영역을 지웁니다.";
            else _ocrPickStart = null;
        });
    }

    /// <summary>
    /// 글자 영역을 미리보기에 그릴지. 글자 읽기가 켜져 있거나 끄는 중일 때만.
    /// </summary>
    /// <remarks>
    /// 읽지도 않는 영역이 늘 떠 있으면 "저 글자 박스는 뭐지" 가 된다(실제로 그랬다). 저장은 그대로 남겨 두고
    /// 글자 읽기를 켜면 다시 보인다.
    /// </remarks>
    public bool IsOcrRegionVisible => IsOcrOn || IsOcrRegionPicking;

    /// <summary>읽을 자리. 캡처 화면 안의 0~1 비율. 비어 있으면 아직 안 정한 것이다.</summary>
    public Rect OcrRegion
    {
        get => GetProperty(() => OcrRegion);
        set => SetProperty(() => OcrRegion, value);
    }

    /// <summary>끄는 중인 사각형. 놓으면 <see cref="OcrRegion"/> 이 되고 이것은 비운다.</summary>
    public Rect OcrRegionDraft
    {
        get => GetProperty(() => OcrRegionDraft);
        set => SetProperty(() => OcrRegionDraft, value);
    }

    /// <summary>마지막으로 읽은 글. 여러 줄이면 줄바꿈으로 이어져 있다.</summary>
    public string? OcrText
    {
        get => GetProperty(() => OcrText);
        set => SetProperty(() => OcrText, value);
    }

    /// <summary>도구 줄에 짧게 보이는 상태. "글자 (120ms): HP 1234".</summary>
    public string? OcrStatus
    {
        get => GetProperty(() => OcrStatus);
        set => SetProperty(() => OcrStatus, value);
    }

    private void OnOcrChanged() => Guard(() =>
    {
        if (!IsOcrOn)
        {
            OcrStatus = null;
            return;
        }

        if (OcrRegion.IsEmpty || OcrRegion.Width <= 0 || OcrRegion.Height <= 0)
        {
            TurnOffOcr("먼저 글자 영역을 정하세요 - '글자 영역' 을 켜고 미리보기에서 끌기.");
            return;
        }

        if (!EnsureOcrEngine(out var problem))
        {
            TurnOffOcr(problem!);
            return;
        }

        // 픽셀이 CPU 로 안 내려오면 읽을 것이 없다. 검출과 같은 길.
        EnsureCpuReadback("글자를 읽으려면 픽셀이 필요합니다");

        OcrStatus = $"글자 읽는 중 ({_ocr.Name} {_ocr.Language})";
        StatusText = $"글자 읽기: {_ocr.Name} ({_ocr.Language}) 로 0.5초에 한 번 읽습니다.";
    });

    /// <summary>이유를 적고 끈다. 검출과 같은 이유로 먼저 끄고 나서 적는다.</summary>
    private void TurnOffOcr(string reason)
    {
        IsOcrOn = false;
        OcrStatus = reason;
        StatusText = reason;
    }

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

            if (IsOcrOn || IsNameplateOcrOn) StatusText = $"OCR 언어를 {SelectedOcrLanguage} 로 바꿨습니다.";
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

    /// <summary>프레임마다 불린다. 캡처 스레드. 시간이 됐고 앞의 것이 끝났을 때만 하나 띄운다.</summary>
    private void MaybeOcr(CapturedFrameEventArgs e)
    {
        if (!IsOcrOn || _ocr is null || !e.HasPixels) return;

        var region = OcrRegion;
        if (region.IsEmpty || region.Width <= 0 || region.Height <= 0) return;

        var now = Environment.TickCount64;
        if (now - _lastOcrTicks < OcrIntervalMs) return;
        if (Interlocked.CompareExchange(ref _isOcrRunning, 1, 0) != 0) return;

        _lastOcrTicks = now;

        BitmapSource crop;

        try
        {
            // 픽셀은 이 콜백이 돌아가면 사라진다. 영역만 지금 복사한다.
            crop = CropFrame(e, region);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "글자 영역을 자르지 못했다");
            Interlocked.Exchange(ref _isOcrRunning, 0);
            return;
        }

        _ = Task.Run(() => RunOcrAsync(crop));
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

    private async Task RunOcrAsync(BitmapSource crop)
    {
        try
        {
            var outcome = await _ocr!.RecognizeAsync(crop);

            DispatcherService?.BeginInvoke(() => Guard(() =>
            {
                OcrText = outcome.Text;

                var first = outcome.Lines.Count == 0 ? string.Empty : outcome.Lines[0].Text;
                var more = outcome.Lines.Count > 1 ? $" 외 {outcome.Lines.Count - 1}줄" : string.Empty;

                OcrStatus = outcome.Text.Length == 0
                    ? $"글자 없음 ({outcome.Elapsed.TotalMilliseconds:0}ms)"
                    : $"글자 ({outcome.Elapsed.TotalMilliseconds:0}ms): {first}{more}";
            }));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "글자를 읽지 못했다");
            DispatcherService?.BeginInvoke(() => Guard(() => TurnOffOcr($"글자 읽기 실패: {ex.Message}")));
        }
        finally
        {
            Interlocked.Exchange(ref _isOcrRunning, 0);
        }
    }

    // ── 영역 끌기 ────────────────────────────────────────────────────────

    /// <summary>지정 모드면 여기서 마우스 다운을 먹는다. true 면 클릭을 게임으로 보내지 않는다.</summary>
    private bool TryBeginOcrRegionPick(Point pointInControl)
    {
        if (!IsOcrRegionPicking) return false;

        var (control, source) = PreviewSizes;

        if (PreviewInputMapper.TryMapToRatio(pointInControl, control, source, clamp: true, out var ratio))
        {
            _ocrPickStart = ratio;
            OcrRegionDraft = new Rect(ratio, ratio);
        }

        return true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs args)
    {
        if (TryDragRegion(args)) return;

        if (_ocrPickStart is not { } start || _previewImage is null) return;

        var (control, source) = PreviewSizes;

        if (PreviewInputMapper.TryMapToRatio(args.GetPosition(_previewImage), control, source, clamp: true, out var ratio))
            OcrRegionDraft = new Rect(start, ratio);
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs args) => Guard(() =>
    {
        if (TryFinishRegionPick(args)) return;

        if (_ocrPickStart is not { } start || _previewImage is null) return;

        var (control, source) = PreviewSizes;

        _ocrPickStart = null;
        OcrRegionDraft = Rect.Empty;
        IsOcrRegionPicking = false;

        if (!PreviewInputMapper.TryMapToRatio(args.GetPosition(_previewImage), control, source, clamp: true, out var ratio))
            return;

        var rect = new Rect(start, ratio);

        // 클릭과 끌기를 가른다. 점짜리 영역은 읽을 것이 없다 - 있던 영역이 있으면 그것을 지우는 뜻으로 받는다.
        if (rect.Width < 0.005 || rect.Height < 0.005)
        {
            if (!OcrRegion.IsEmpty && OcrRegion.Width > 0)
            {
                OcrRegion = Rect.Empty;
                if (IsOcrOn) TurnOffOcr("글자 영역을 지웠습니다. 다시 읽으려면 영역을 새로 그리세요.");
                else StatusText = "글자 영역을 지웠습니다.";
                return;
            }

            StatusText = "영역이 너무 작습니다. 글자를 감싸도록 끌어 주세요.";
            return;
        }

        OcrRegion = rect;
        StatusText = $"글자 영역: 왼쪽 {rect.X:P0} · 위 {rect.Y:P0} · 폭 {rect.Width:P0} · 높이 {rect.Height:P0}. 글자 읽기를 켜면 여기서 읽습니다.";
    });

    // ── 설정 ─────────────────────────────────────────────────────────────

    private const string OcrRegionSettingKey = "OcrRegion";

    private void RestoreOcrRegion()
    {
        var parts = GetSettingOrLegacy(OcrRegionSettingKey, string.Empty).Split(',');

        if (parts.Length == 4
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
            && w > 0 && h > 0)
        {
            OcrRegion = new Rect(x, y, w, h);
        }
    }

    private void SaveOcrRegion()
    {
        var r = OcrRegion;

        SetSetting(OcrRegionSettingKey, r.IsEmpty || r.Width <= 0
            ? string.Empty
            : string.Join(",", new[] { r.X, r.Y, r.Width, r.Height }.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture))));
    }

    private void ReleaseOcr()
    {
        IsOcrOn = false;
        _ocr?.Dispose();
        _ocr = null;
    }
}
