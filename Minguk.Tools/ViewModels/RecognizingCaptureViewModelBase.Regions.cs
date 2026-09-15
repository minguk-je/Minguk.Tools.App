using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using DevExpress.Mvvm;

using Minguk.Tools.Capture.Input;
using Minguk.Tools.Markup.Regions;
using Minguk.Tools.Vision.Labeling;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 이름 붙인 자리 만들기 - 미리보기에서 끌어 사각형을 만들고 이름을 붙인다.
/// </summary>
/// <remarks>
/// <b>글자 영역 지정과 무엇이 다른가</b> - 그쪽은 자리가 <b>하나</b>고 이름이 없다(늘 읽는 자리 하나).
/// 이쪽은 여럿이고 이름이 있어 스크립트가 <c>숫자읽기("탄약")</c> 처럼 부른다. 두 기능이 같은 끌기 손짓을
/// 쓰므로 한 번에 하나만 켜지게 한다 - 둘 다 켜져 있으면 끈 사각형이 어디로 갈지 알 수 없다.
///
/// 여기서 하는 것은 <b>새 자리 그리기</b>뿐이다. 만들어 둔 자리를 옮기고 크기를 바꾸는 것은 미리보기 위의
/// <see cref="RegionCanvas"/>(Thumb·어도너)가 하고, 결과만 <see cref="RegionEditCommand"/> 로 받아 저장한다.
///
/// 목록은 데이터셋 폴더의 <c>regions.json</c> 이다(<see cref="RegionBook"/>).
/// </remarks>
public abstract partial class RecognizingCaptureViewModelBase
{
    private Point? _regionPickStart;
    private RegionBook? _regions;

    /// <summary>만들어 둔 자리들. 화면 목록과 미리보기 캔버스가 이것을 본다.</summary>
    public ObservableCollection<NamedRegion> Regions { get; } = [];

    /// <summary>스크립트가 볼 목록. 부를 때마다 지금 것을 준다.</summary>
    public RegionBook RegionBook => _regions ??= RegionBook.Load(RecognitionRoot);

    public NamedRegion? SelectedRegion
    {
        get => GetProperty(() => SelectedRegion);
        set => SetProperty(() => SelectedRegion, value, RaiseRegionFields);
    }

    /// <summary>켜면 미리보기에서 끈 사각형이 새 자리가 된다. 그동안 클릭은 게임으로 안 나간다.</summary>
    public bool IsRegionPicking
    {
        get => GetProperty(() => IsRegionPicking);
        set => SetProperty(() => IsRegionPicking, value, () =>
        {
            RaisePropertyChanged(nameof(IsRegionVisible));
            RaiseRegionEditingActive();

            if (!IsRegionPicking)
            {
                // 끄는 도중에 모드를 껐으면 잡고 있던 것을 놓는다. 커서도 게임 쪽 모양으로 되돌린다.
                CancelRegionDrag();
                if (_previewSurface is not null) _previewSurface.Cursor = null;
                return;
            }

            // 같은 손짓을 두 기능이 나눠 쓴다. 글자 영역 쪽은 끈다.
            IsOcrRegionPicking = false;

            // 빈 자리는 새로 그리는 곳이다. 자리 위의 커서(옮기기·크기 조절)는 손잡이가 제 것을 보인다.
            if (_previewSurface is not null) _previewSurface.Cursor = Cursors.Cross;

            StatusText = "빈 자리를 끌면 새 자리, 자리 안을 끌면 옮기기, 고른 자리의 손잡이를 끌면 크기 조절입니다. 끝나면 영역 지정을 끄세요.";
        });
    }

    /// <summary>만들어 둔 자리를 미리보기에 겹쳐 그릴지. 지정 중이거나 사람이 켰을 때.</summary>
    public bool IsRegionVisible => IsRegionPicking || ShowRegions;

    public bool ShowRegions
    {
        get => GetProperty(() => ShowRegions);
        set => SetProperty(() => ShowRegions, value, () =>
        {
            RaisePropertyChanged(nameof(IsRegionVisible));
            RaiseRegionEditingActive();
        });
    }

    /// <summary>
    /// 미리보기가 자리 편집기로 도는가 - "영역 지정" 을 켰거나, <b>입력 전달이 꺼져 있고 자리가 보일 때</b>(사용자, 2026-09-15).
    /// 캔버스(<c>RegionCanvas.IsEditing</c>)가 이것에 묶여 고르기·옮기기·크기 조절 어도너가 산다.
    /// </summary>
    /// <remarks>
    /// 전달이 꺼져 있으면 미리보기 클릭이 게임으로 갈 일이 없어, 보이는 자리를 고치려고 영역 지정을 따로 켜는 것이 번거로웠다.
    /// 전달이 켜져 있으면 클릭은 게임 몫이라 영역 지정을 켜야만 된다. 글자 영역을 끄는 중에는 그쪽이 먼저다 - 같은 손짓을 나눠 쓴다.
    /// 자리가 안 보이면(영역 보기 끔) 편집기도 안 뜬다 - 안 보이는 것을 잡으면 헷갈린다.
    /// </remarks>
    public bool IsRegionEditingActive => IsRegionPicking || (EditsRegionsWithoutPicking && !IsInputForwardingEnabled && ShowRegions && !IsOcrRegionPicking);

    /// <summary>
    /// 영역 지정 없이(전달 끔 + 영역 보기) 자리 편집기를 띄우는 화면인가. 스크립트 화면만 - 플레이 화면에는 자리 편집 도구가 없어
    /// 거기서 켜지면 미리보기를 누른 것이 모르는 새 자리가 된다.
    /// </summary>
    protected virtual bool EditsRegionsWithoutPicking => false;

    private bool _wasRegionEditingActive;

    private void RaiseRegionEditingActive()
    {
        RaisePropertyChanged(nameof(IsRegionEditingActive));

        // 꺼지는 순간 잡고 있던 것을 놓는다(전달을 켰는데 끌기가 남으면 그 뒤 클릭이 게임으로 안 간다).
        if (_wasRegionEditingActive && !IsRegionEditingActive) CancelRegionDrag();

        _wasRegionEditingActive = IsRegionEditingActive;
    }

    protected override void OnInputForwardingEnabledChanged()
    {
        base.OnInputForwardingEnabledChanged();
        RaiseRegionEditingActive();
    }

    /// <summary>끄는 중인 사각형. 놓으면 목록에 들어가고 이것은 비운다.</summary>
    public Rect RegionDraft
    {
        get => GetProperty(() => RegionDraft);
        set => SetProperty(() => RegionDraft, value);
    }

    /// <summary>
    /// 새로 만들 자리의 이름. 도구 줄의 칸에 묶인다.
    /// </summary>
    /// <remarks>
    /// 끌고 나서 대화 상자로 묻지 않는다 - 미리보기에서 손을 뗀 순간 창이 뜨면 게임 화면을 가리고, 그 사이
    /// 화면이 바뀐다. 이름을 먼저 적고 끄는 편이 손이 덜 간다. 비워 두면 <c>자리1</c> 처럼 붙인다.
    /// </remarks>
    public string RegionName
    {
        get => GetProperty(() => RegionName);
        set => SetProperty(() => RegionName, value);
    }

    /// <summary>
    /// 고른 자리의 이름. 칸에서 고치면 바로 바뀐다.
    /// </summary>
    /// <remarks>
    /// 처음에는 도구 줄의 "이름" 칸을 써서 바꾸게 했는데, 그 칸은 <b>새로 만드는 자리의 이름</b>이라
    /// 고른 자리의 이름은 그 칸에 안 들어 있어 비어 있었고, 그대로 누르면 상태 줄에만 말이
    /// 떠서 아무 일도 안 일어난 것처럼 보였다. 고르면 그 값이 들어 있는 칸이 있어야 한다.
    /// </remarks>
    public string SelectedRegionName
    {
        get => SelectedRegion?.Name ?? string.Empty;
        set => RenameSelected(value);
    }

    private void RenameSelected(string wanted) => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        var name = (wanted ?? string.Empty).Trim();

        if (name.Length == 0 || string.Equals(name, region.Name, StringComparison.Ordinal)) return;

        // 이름으로 찾으므로 옮기기 전에 옛 이름으로 빼낸다 - 안 그러면 둘이 남는다.
        RegionBook.Remove(region.Name);
        region.Name = name;
        RegionBook.Put(region);

        SaveRegions();
        LoadRegions();

        SelectedRegion = Regions.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        StatusText = $"이름을 「{name}」 으로 바꿨습니다.";
    });

    /// <summary>고른 자리가 바뀌거나 그 값이 바뀌면 이름 칸과 겹그림을 다시 그리게 한다.</summary>
    private void RaiseRegionFields()
    {
        RaisePropertyChanged(nameof(SelectedRegionName));

        // 겹그림·캔버스는 목록이 바뀔 때만 다시 그린다. 안의 값만 바뀌면 모르므로 여기서 알린다.
        RegionsRevision++;
    }

    /// <summary>자리가 한 번 바뀔 때마다 오른다. 겹그림과 캔버스가 이것을 보고 다시 놓는다.</summary>
    public int RegionsRevision
    {
        get => GetProperty(() => RegionsRevision);
        set => SetProperty(() => RegionsRevision, value);
    }

    public ICommand RemoveRegionCommand => new DelegateCommand(DoRemoveRegion, () => SelectedRegion is not null);

    public ICommand RenameRegionCommand => new DelegateCommand(DoRenameRegion, () => SelectedRegion is not null);

    /// <summary>고른 자리를 지금 읽어 본다 - 자리가 맞는지 확인하는 가장 빠른 길.</summary>
    public ICommand TestRegionCommand => new DelegateCommand(DoTestRegion, () => SelectedRegion is not null);

    /// <summary>고른 자리의 전처리(흰 글자만 남기기)를 켜고 끈다.</summary>
    public ICommand ToggleRegionInkCommand => new DelegateCommand(DoToggleRegionInk, () => SelectedRegion is not null);

    /// <summary>
    /// 미리보기 캔버스가 자리를 옮기거나 크기를 바꿀 때마다 준다. 끄는 동안은 자리만 고치고, 놓으면 저장한다.
    /// </summary>
    public ICommand RegionEditCommand => new DelegateCommand<RegionEdit>(ApplyRegionEdit);

    private void ApplyRegionEdit(RegionEdit edit) => Guard(() =>
    {
        if (edit.Rect.Width < MinimumRegionSize || edit.Rect.Height < MinimumRegionSize) return;

        edit.Region.Rect = edit.Rect;

        if (!edit.Completed) return;

        SaveRegions();
        RaiseRegionFields();

        StatusText = $"「{edit.Region.Name}」 자리를 고쳤습니다 - " +
                     $"{edit.Region.X * 100:0.0}%, {edit.Region.Y * 100:0.0}%  {edit.Region.Width * 100:0.0}% x {edit.Region.Height * 100:0.0}%";
    });

    /// <summary>파일에서 목록을 다시 채운다. 고른 것이 없으면 첫 줄을 고른다 - 빈 채로 두면 이름 칸·지금 읽기가 다 죽어 보인다.</summary>
    protected void LoadRegions()
    {
        _regions = RegionBook.Load(RecognitionRoot);

        var keep = SelectedRegion?.Name;

        Regions.Clear();

        foreach (var region in _regions.Regions.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            Regions.Add(region);

        SelectedRegion = Regions.FirstOrDefault(r => string.Equals(r.Name, keep, StringComparison.OrdinalIgnoreCase)) ?? Regions.FirstOrDefault();
    }

    private void SaveRegions() => Guard(() =>
    {
        RegionBook.Save();
        RaisePropertyChanged(nameof(Regions));
    });

    /// <summary>
    /// 지정 모드면 여기서 마우스 다운을 먹는다. true 면 클릭을 게임으로 보내지 않는다.
    /// </summary>
    /// <remarks>
    /// 여기 오는 것은 <b>빈 자리</b>를 누른 것뿐이다. 자리 위의 누름은 캔버스의 손잡이(Thumb)가 먼저 먹어 여기까지 안 온다.
    /// 그래서 무엇을 잡았는지 가릴 것 없이 새로 그리기만 한다.
    /// </remarks>
    private bool TryBeginRegionPick(Point pointInControl)
    {
        if (!IsRegionEditingActive) return false;

        // 영역 지정을 켜지 않은 채(전달 끔)면 요소 검사가 먼저다 - 검사하려고 누른 것이 새 자리가 되면 안 된다.
        if (!IsRegionPicking && IsElementInspectEnabled) return false;

        var (control, source) = PreviewSizes;

        if (!PreviewInputMapper.TryMapToRatio(pointInControl, control, source, clamp: true, out var ratio))
            return true;

        _regionPickStart = ratio;
        RegionDraft = new Rect(ratio, ratio);

        // 끄다가 미리보기 밖으로 나가도 놓는 것을 받아야 한다. 안 그러면 버튼을 뗐는데 계속 끌린다.
        _previewSurface?.CaptureMouse();

        return true;
    }

    private bool TryDragRegion(MouseEventArgs args)
    {
        if (_regionPickStart is not { } start || _previewImage is null) return false;

        var (control, source) = PreviewSizes;

        if (PreviewInputMapper.TryMapToRatio(args.GetPosition(_previewImage), control, source, clamp: true, out var ratio))
            RegionDraft = new Rect(start, ratio);

        return true;
    }

    private bool TryFinishRegionPick(MouseButtonEventArgs args)
    {
        if (_regionPickStart is not { } start || _previewImage is null) return false;

        var (control, source) = PreviewSizes;

        CancelRegionDrag();

        if (!PreviewInputMapper.TryMapToRatio(args.GetPosition(_previewImage), control, source, clamp: true, out var ratio))
            return true;

        var rect = new Rect(start, ratio);

        // 클릭과 끌기를 가른다. 점짜리는 읽을 것이 없다 - 빈 자리를 누른 것은 고른 것을 푸는 뜻으로 받는다.
        if (rect.Width < MinimumRegionSize || rect.Height < MinimumRegionSize)
        {
            if (SelectedRegion is not null) SelectedRegion = null;
            else StatusText = "너무 작습니다 - 읽을 글자가 들어가게 끌어 주세요.";
            return true;
        }

        var name = NextName(RegionName);
        var region = new NamedRegion { Name = name, Rect = rect };

        RegionBook.Put(region);
        SaveRegions();
        LoadRegions();

        SelectedRegion = Regions.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        StatusText = $"「{name}」 자리를 만들었습니다. 스크립트에서 숫자읽기(\"{name}\") 로 읽습니다.";

        return true;
    }

    /// <summary>이보다 작은 자리는 읽을 것이 없다(0~1). <see cref="NamedRegion.IsUsable"/> 과 같다.</summary>
    private const double MinimumRegionSize = 0.004;

    private void CancelRegionDrag()
    {
        _regionPickStart = null;
        RegionDraft = Rect.Empty;

        if (_previewSurface?.IsMouseCaptured == true) _previewSurface.ReleaseMouseCapture();
    }

    private void DoRemoveRegion() => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        RegionBook.Remove(region.Name);
        SaveRegions();
        LoadRegions();

        StatusText = $"「{region.Name}」 자리를 지웠습니다.";
    });

    /// <summary>비었으면 안 겹치는 이름을 지어 준다. 이름 없이 만든 자리는 스크립트가 부를 길이 없다.</summary>
    private string NextName(string wanted)
    {
        var name = (wanted ?? string.Empty).Trim();

        if (name.Length > 0) return name;

        for (var n = 1; ; n++)
        {
            var candidate = $"자리{n}";

            if (RegionBook.Find(candidate) is null) return candidate;
        }
    }

    private void DoRenameRegion() => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        var name = (RegionName ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            StatusText = "바꿀 이름을 도구 줄의 이름 칸에 적고 다시 누르세요.";
            return;
        }

        if (string.Equals(name, region.Name, StringComparison.Ordinal)) return;

        RegionBook.Remove(region.Name);
        region.Name = name;
        RegionBook.Put(region);
        SaveRegions();
        LoadRegions();

        SelectedRegion = Regions.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    });

    /// <summary>
    /// 고른 자리를 지금 한 번 읽어 상태 줄에 적는다.
    /// </summary>
    /// <remarks>
    /// <b>이게 없으면 자리를 못 맞춘다.</b> 끌어 놓고 맞는지 보려면 스크립트를 짜서 돌려야 하는데, 한 번에
    /// 몇십 초가 걸린다. 여기서는 누르는 즉시 읽은 글이 뜬다 - 비면 자리를 조금 넓히거나 전처리를 켜고 끈다.
    /// </remarks>
    private void DoTestRegion() => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        if (!IsRunning)
        {
            StatusText = "먼저 시작(연결)을 눌러 화면을 잡아야 읽을 수 있습니다.";
            return;
        }

        // 프레임 복사를 켜 둬야 허브가 조각을 준다. 한 장 올 때까지 잠깐 기다린다.
        Hub.WantsFrames = true;

        var ocr = _ocr;

        if (ocr is null)
        {
            StatusText = "글자 읽기 엔진이 없습니다 - Windows OCR 언어 팩을 확인하세요.";
            return;
        }

        var deadline = Environment.TickCount64 + 1500;
        System.Windows.Media.Imaging.BitmapSource? crop = null;

        while (Environment.TickCount64 < deadline && (!Hub.TryCropFrame(region.Rect, out crop) || crop is null))
            System.Threading.Thread.Sleep(50);

        if (crop is null)
        {
            StatusText = "프레임이 안 옵니다 - 캡처가 돌고 있는지, CPU 리드백이 켜져 있는지 보세요.";
            return;
        }

        // 네 갈래를 다 해 본다 - 전처리 켜고/끄고 × 쓰던 엔진(대개 한국어)/영문. 사람이 그 자리에 무엇을 담아
        // 뒀는지 모르기 때문이다. 실측: 플레이어 이름 자리를 만들었는데 영문으로만 읽어 빈 글이 나왔다
        // (한국어로는 「상제님」 이 읽혔다). 반대로 숫자는 한국어가 225 를 22512h5 로 낸다.
        var english = Vision.Ocr.OcrEngineFactory.TryCreate("en-US");
        var best = string.Empty;
        var how = string.Empty;

        foreach (var ink in new[] { region.Ink, !region.Ink })
        {
            var prepared = ink ? Vision.Ocr.HudInk.Prepare(crop) : Enlarge(crop);

            foreach (var (engine, label) in new[] { (ocr, ocr.Language), (english, "en-US") })
            {
                if (engine is null) continue;

                var read = engine.RecognizeAsync(prepared).GetAwaiter().GetResult().Text.Replace(Environment.NewLine, " ").Trim();

                if (read.Length <= best.Length) continue;

                best = read;
                how = $"{(ink ? "전처리" : "그대로")}·{label}";
            }
        }

        english?.Dispose();

        var digits = new string([.. best.Where(char.IsDigit)]);

        StatusText = best.Length == 0
            ? $"「{region.Name}」 에서 아무것도 못 읽었습니다. 자리를 조금 넓히거나 전처리를 켜고 꺼 보세요."
            : $"「{region.Name}」 → 「{best}」{(digits.Length > 0 ? $"  (숫자 {digits})" : string.Empty)}  [{how}]";

        // 상태 줄은 다음 갱신이 덮는다. 나중에 "왜 안 읽혔지" 를 되짚으려면 로그에 남아야 한다.
        Logger.Debug($"영역 읽기: 「{region.Name}」 {region.Rect} → 「{best}」 [{how}]");
    });

    /// <summary>작은 글자는 키워야 읽힌다. 전처리를 안 할 때 쓰는 길.</summary>
    private static System.Windows.Media.Imaging.BitmapSource Enlarge(System.Windows.Media.Imaging.BitmapSource crop)
    {
        var scaled = new System.Windows.Media.Imaging.TransformedBitmap(crop, new System.Windows.Media.ScaleTransform(6, 6));

        scaled.Freeze();

        return scaled;
    }

    private void DoToggleRegionInk() => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        region.Ink = !region.Ink;
        SaveRegions();
        LoadRegions();

        SelectedRegion = Regions.FirstOrDefault(r => string.Equals(r.Name, region.Name, StringComparison.OrdinalIgnoreCase));
        StatusText = $"「{region.Name}」 전처리(흰 글자만 남기기)를 {(region.Ink ? "켰습니다" : "껐습니다")}.";
    });
}
