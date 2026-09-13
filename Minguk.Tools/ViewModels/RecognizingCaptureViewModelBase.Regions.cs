using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using DevExpress.Mvvm;

using Minguk.Tools.Capture.Input;
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
/// 목록은 데이터셋 폴더의 <c>regions.json</c> 이다(<see cref="RegionBook"/>).
/// </remarks>
public abstract partial class RecognizingCaptureViewModelBase
{
    /// <summary>미리보기에서 자리를 끄는 중에 무엇을 하는지.</summary>
    private enum RegionDragMode
    {
        /// <summary>빈 자리를 눌렀다 - 새 자리를 그린다.</summary>
        Draw,

        /// <summary>자리 안쪽을 눌렀다 - 통째로 옮긴다.</summary>
        Move,

        /// <summary>고른 자리의 모서리·변 손잡이를 눌렀다 - 크기를 바꾼다.</summary>
        Resize
    }

    /// <summary>손잡이를 잡는 반지름(화면 픽셀). 라벨링 캔버스와 같다.</summary>
    private const double RegionGripPixels = 8d;

    /// <summary>
    /// 옮기기로 치는 최소 거리(화면 픽셀).
    /// </summary>
    /// <remarks>
    /// 고르려고 누를 때마다 손이 떨려 1~2px 씩 흘러 저장할 때마다 자리가 바뀌면 안 된다(라벨링 캔버스에서 겪었다).
    /// </remarks>
    private const double RegionMoveDeadPixels = 4d;

    private RegionDragMode _regionDragMode;
    private Point? _regionPickStart;
    private Point _regionPickStartInControl;
    private BoxHandle _regionDragHandle;
    private Rect _regionDragOrigin;
    private NamedRegion? _regionDragTarget;
    private bool _regionDragMoved;

    private RegionBook? _regions;

    /// <summary>만들어 둔 자리들. 화면 목록이 이것을 본다.</summary>
    public ObservableCollection<NamedRegion> Regions { get; } = [];

    /// <summary>스크립트가 볼 목록. 부를 때마다 지금 것을 준다.</summary>
    public RegionBook RegionBook => _regions ??= RegionBook.Load(LabelDataset.ConfiguredRoot);

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

            if (!IsRegionPicking)
            {
                // 끄는 도중에 모드를 껐으면 잡고 있던 것을 놓는다. 커서도 게임 쪽 모양으로 되돌린다.
                CancelRegionDrag();
                if (_previewSurface is not null) _previewSurface.Cursor = null;
                return;
            }

            // 같은 손짓을 두 기능이 나눠 쓴다. 글자 영역 쪽은 끈다.
            IsOcrRegionPicking = false;
            StatusText = "빈 자리를 끌면 새 자리, 자리 안을 끌면 옮기기, 고른 자리의 손잡이를 끌면 크기 조절입니다. 끝나면 영역 지정을 끄세요.";
        });
    }

    /// <summary>만들어 둔 자리를 미리보기에 겹쳐 그릴지. 지정 중이거나 사람이 켰을 때.</summary>
    public bool IsRegionVisible => IsRegionPicking || ShowRegions;

    public bool ShowRegions
    {
        get => GetProperty(() => ShowRegions);
        set => SetProperty(() => ShowRegions, value, () => RaisePropertyChanged(nameof(IsRegionVisible)));
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

    /// <summary>고른 자리의 가로 자리(%). 수자로 고치면 그대로 옮겨간다.</summary>
    /// <remarks>
    /// 0~1 비율을 그대로 칸에 묶으면 0.001 씩 오르내리는 스핀이 되어 만지기 나쁘다. % 로 보이고
    /// 소수점 한 자리까지 둔다 - 1920 화면에서 0.1% 가 2px 이라 그것이면 충분하다.
    /// </remarks>
    public double SelectedRegionX
    {
        get => Percent(SelectedRegion?.X);
        set => MoveSelected(x: value / 100.0);
    }

    public double SelectedRegionY
    {
        get => Percent(SelectedRegion?.Y);
        set => MoveSelected(y: value / 100.0);
    }

    public double SelectedRegionWidth
    {
        get => Percent(SelectedRegion?.Width);
        set => MoveSelected(width: value / 100.0);
    }

    public double SelectedRegionHeight
    {
        get => Percent(SelectedRegion?.Height);
        set => MoveSelected(height: value / 100.0);
    }

    private static double Percent(double? ratio) => Math.Round((ratio ?? 0) * 100, 1);

    /// <summary>자리를 옮기거나 크기를 바꿔 저장한다. 안 준 것은 그대로 둔다.</summary>
    private void MoveSelected(double? x = null, double? y = null, double? width = null, double? height = null) => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        // 화면 밖으로 나가면 읽을 것이 없다. 0~1 안에 가둔다.
        var nx = Math.Clamp(x ?? region.X, 0, 0.999);
        var ny = Math.Clamp(y ?? region.Y, 0, 0.999);
        var nw = Math.Clamp(width ?? region.Width, 0.004, 1 - nx);
        var nh = Math.Clamp(height ?? region.Height, 0.004, 1 - ny);

        region.Rect = new Rect(nx, ny, nw, nh);

        SaveRegions();

        // 고치는 칸에는 되돌려 알리지 않는다. 사람이 치는 도중에 같은 칸의 값을 되쏘으면 글자가 지워지고
        // 캐럿이 앞으로 튀어 "고쳐지지 않는다" 로 보인다. 겹그림만 다시 그리게 하면 된다.
        RegionsRevision++;
        RaisePropertyChanged(nameof(Regions));

        // 막힌 것은 말해 준다 - 조용히 안 바뀌면 칸이 고장 난 줄 안다.
        if (width is { } wanted && Math.Abs(wanted - nw) > 0.0005)
            StatusText = $"너비는 여기서 {nw * 100:0.0}% 까지다 - 가로 자리를 왼쪽으로 옮기면 더 넓혀진다.";
        else if (height is { } tall && Math.Abs(tall - nh) > 0.0005)
            StatusText = $"높이는 여기서 {nh * 100:0.0}% 까지다 - 세로 자리를 위로 옮기면 더 늘어난다.";
    });

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
        StatusText = $"이름을 「{name}」 으로 바꿠습니다.";
    });

    /// <summary>고른 자리가 바뀌거나 그 값이 바뀌면 칸들과 겹그림을 다시 그리게 한다.</summary>
    private void RaiseRegionFields()
    {
        RaisePropertyChanged(nameof(SelectedRegionName));
        RaisePropertyChanged(nameof(SelectedRegionX));
        RaisePropertyChanged(nameof(SelectedRegionY));
        RaisePropertyChanged(nameof(SelectedRegionWidth));
        RaisePropertyChanged(nameof(SelectedRegionHeight));

        // 겹그림은 목록이 바뀔 때만 다시 그린다. 안의 값만 바뀌면 모르므로 여기서 알린다.
        RegionsRevision++;
    }

    /// <summary>자리가 한 번 바뀔 때마다 오른다. 겹그림이 이것을 보고 다시 그린다.</summary>
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

    protected void LoadRegions()
    {
        _regions = RegionBook.Load(LabelDataset.ConfiguredRoot);

        Regions.Clear();

        foreach (var region in _regions.Regions.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            Regions.Add(region);
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
    /// 누른 자리로 할 일을 정한다 - 라벨링 캔버스와 같은 규칙이다.
    ///   고른 자리의 손잡이(모서리·변 가운데) → 크기 조절
    ///   어느 자리의 안쪽 → 그것을 고르고 옮기기
    ///   빈 자리 → 새로 그리기
    /// 손잡이는 <b>고른 자리에서만</b> 잡힌다. 안 고른 것의 모서리까지 잡으면 붙어 있는 자리 사이에서
    /// 새로 그리려다 엉뚱한 것이 늘어난다.
    ///
    /// Adorner·Thumb 컨트롤을 얹지 않는 이유 - 겹그림은 클릭을 게임으로 흘려보내야 해서 히트 테스트를 끈다.
    /// 거기에 Thumb 을 얹으면 모드를 꺼도 그 자리의 클릭이 게임으로 안 간다. 여기서는 모드가 켜져 있을 때만
    /// 마우스를 먹고, 그리는 것은 겹그림이 한다.
    /// </remarks>
    private bool TryBeginRegionPick(Point pointInControl)
    {
        if (!IsRegionPicking) return false;

        var (control, source) = PreviewSizes;

        if (!PreviewInputMapper.TryMapToRatio(pointInControl, control, source, clamp: true, out var ratio))
            return true;

        _regionPickStart = ratio;
        _regionPickStartInControl = pointInControl;
        _regionDragMoved = false;

        if (SelectedRegion is { } selected && RegionHandleAt(selected.Rect, pointInControl) is var handle && LabelBoxEdit.IsResizeHandle(handle))
        {
            BeginRegionEdit(RegionDragMode.Resize, selected, handle);
        }
        else if (RegionAt(pointInControl) is { } hit)
        {
            if (!ReferenceEquals(hit, SelectedRegion)) SelectedRegion = hit;

            BeginRegionEdit(RegionDragMode.Move, hit, BoxHandle.Inside);
        }
        else
        {
            _regionDragMode = RegionDragMode.Draw;
            _regionDragTarget = null;
            RegionDraft = new Rect(ratio, ratio);
        }

        // 끄다가 미리보기 밖으로 나가도 놓는 것을 받아야 한다. 안 그러면 버튼을 뗐는데 계속 끌린다.
        _previewSurface?.CaptureMouse();

        return true;
    }

    private void BeginRegionEdit(RegionDragMode mode, NamedRegion region, BoxHandle handle)
    {
        _regionDragMode = mode;
        _regionDragTarget = region;
        _regionDragHandle = handle;
        _regionDragOrigin = region.Rect;
    }

    private bool TryDragRegion(MouseEventArgs args)
    {
        if (_previewImage is null) return false;

        var point = args.GetPosition(_previewImage);

        if (_regionPickStart is not { } start)
        {
            // 끌고 있지 않을 때는 커서로 무엇을 잡을지 알려 준다.
            if (IsRegionPicking && _previewSurface is not null) _previewSurface.Cursor = RegionCursorAt(point);
            return false;
        }

        var (control, source) = PreviewSizes;

        if (!PreviewInputMapper.TryMapToRatio(point, control, source, clamp: true, out var ratio))
            return true;

        switch (_regionDragMode)
        {
            case RegionDragMode.Draw:
                RegionDraft = new Rect(start, ratio);
                break;

            case RegionDragMode.Move when _regionDragTarget is { } region:
                if (!_regionDragMoved && (point - _regionPickStartInControl).Length < RegionMoveDeadPixels / Zoom) break;

                _regionDragMoved = true;

                // 시작 자리에 변위를 더한다. 직전 자리에 더하면 반올림이 쌓인다.
                var moved = LabelBoxEdit.Move(ToLabelBox(_regionDragOrigin), ratio.X - start.X, ratio.Y - start.Y);
                ApplyRegionDrag(region, moved);
                break;

            case RegionDragMode.Resize when _regionDragTarget is { } region:
                _regionDragMoved = true;

                var resized = LabelBoxEdit.Resize(ToLabelBox(_regionDragOrigin), _regionDragHandle, ratio.X, ratio.Y);

                // 읽을 수 있는 크기 아래로는 안 줄인다 - 줄던 자리에서 멈춘다.
                if (resized.Width >= MinimumRegionSize && resized.Height >= MinimumRegionSize)
                    ApplyRegionDrag(region, resized);
                break;
        }

        return true;
    }

    /// <summary>끄는 동안은 저장하지 않고 그리기만 한다. 놓을 때 한 번 저장한다.</summary>
    private void ApplyRegionDrag(NamedRegion region, LabelBox box)
    {
        region.Rect = new Rect(box.Left, box.Top, box.Width, box.Height);
        RaiseRegionFields();
    }

    private bool TryFinishRegionPick(MouseButtonEventArgs args)
    {
        if (_regionPickStart is not { } start || _previewImage is null) return false;

        var (control, source) = PreviewSizes;
        var mode = _regionDragMode;
        var target = _regionDragTarget;
        var moved = _regionDragMoved;

        CancelRegionDrag();

        if (mode != RegionDragMode.Draw)
        {
            if (target is not null && moved)
            {
                SaveRegions();
                StatusText = $"「{target.Name}」 자리를 {(mode == RegionDragMode.Move ? "옮겼습니다" : "고쳤습니다")} - " +
                             $"{target.X * 100:0.0}%, {target.Y * 100:0.0}%  {target.Width * 100:0.0}% x {target.Height * 100:0.0}%";
            }

            if (_previewSurface is not null) _previewSurface.Cursor = RegionCursorAt(args.GetPosition(_previewImage));
            return true;
        }

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

    /// <summary>이보다 작은 자리는 읽을 것이 없다(0~1). 칸의 최솟값(0.4%)과 같다.</summary>
    private const double MinimumRegionSize = 0.004;

    /// <summary>미리보기 확대. 손잡이 크기를 화면에서 늘 같게 보이게 나눈다 - 좌표는 확대 전 것이라서.</summary>
    private double Zoom => PreviewZoom > 0 ? PreviewZoom : 1;

    private void CancelRegionDrag()
    {
        _regionPickStart = null;
        _regionDragTarget = null;
        _regionDragMoved = false;
        RegionDraft = Rect.Empty;

        if (_previewSurface?.IsMouseCaptured == true) _previewSurface.ReleaseMouseCapture();
    }

    private static LabelBox ToLabelBox(Rect rect) => LabelBox.FromCorners(0, rect.Left, rect.Top, rect.Right, rect.Bottom);

    /// <summary>0~1 자리를 미리보기 Image 좌표로. <see cref="PreviewInputMapper.TryMapToRatio"/> 의 반대다.</summary>
    private Rect? RegionToControl(Rect ratio)
    {
        if (_previewImage is null) return null;

        var (control, source) = PreviewSizes;

        if (control.Width <= 0 || control.Height <= 0 || source.Width <= 0 || source.Height <= 0) return null;

        var scale = Math.Min(control.Width / source.Width, control.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        var offsetX = (control.Width - width) / 2;
        var offsetY = (control.Height - height) / 2;

        return new Rect(offsetX + (ratio.X * width), offsetY + (ratio.Y * height), ratio.Width * width, ratio.Height * height);
    }

    private BoxHandle RegionHandleAt(Rect ratio, Point point)
    {
        if (RegionToControl(ratio) is not { } rect) return BoxHandle.None;

        return LabelBoxEdit.HitHandle(rect.Left, rect.Top, rect.Right, rect.Bottom, point.X, point.Y, RegionGripPixels / Zoom);
    }

    /// <summary>
    /// 그 점을 안에 둔 자리. 고른 것을 먼저 보고, 겹치면 작은 것을 준다.
    /// </summary>
    /// <remarks>
    /// 큰 자리 안에 작은 자리를 두는 일이 있다(체력 칸 안의 숫자). 큰 것을 주면 작은 것은 영영 못 잡는다.
    /// </remarks>
    private NamedRegion? RegionAt(Point point)
    {
        if (SelectedRegion is { } selected && RegionToControl(selected.Rect) is { } own && own.Contains(point))
            return selected;

        return Regions
            .Select(region => (region, rect: RegionToControl(region.Rect)))
            .Where(each => each.rect is { } rect && rect.Contains(point))
            .OrderBy(each => each.region.Width * each.region.Height)
            .Select(each => each.region)
            .FirstOrDefault();
    }

    private Cursor RegionCursorAt(Point point)
    {
        if (SelectedRegion is { } selected)
        {
            var handle = RegionHandleAt(selected.Rect, point);

            if (LabelBoxEdit.IsResizeHandle(handle))
            {
                return handle switch
                {
                    BoxHandle.TopLeft or BoxHandle.BottomRight => Cursors.SizeNWSE,
                    BoxHandle.TopRight or BoxHandle.BottomLeft => Cursors.SizeNESW,
                    BoxHandle.Top or BoxHandle.Bottom => Cursors.SizeNS,
                    _ => Cursors.SizeWE
                };
            }
        }

        return RegionAt(point) is not null ? Cursors.SizeAll : Cursors.Cross;
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
