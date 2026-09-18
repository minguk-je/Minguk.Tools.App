using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using DevExpress.Mvvm;

using Minguk.Tools.Capture.Input;
using Minguk.Tools.Markup.Regions;
using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.ViewModels;

/// <summary>밝기 기준 미리보기 한 칸 - 어느 구역인지(이름)와 손질을 입힌 그림.</summary>
public sealed record RegionPreviewItem(string Label, System.Windows.Media.Imaging.BitmapSource Image);

/// <summary>
/// 이름 붙인 자리 - 미리보기에서 끌어 만들고, 그리드에서 이름·계속 읽기·전처리를 고친다.
/// </summary>
/// <remarks>
/// <b>한 곳으로 정리했다</b>(사용자, 2026-09-15 "영역 기능 사용법이 좀 이상해"):
/// - 옛 "글자 영역"(자리 하나, 이름 없음, 설정에 저장)을 없애고 자리마다 <see cref="NamedRegion.KeepReading"/> 로 합쳤다.
///   예전에 저장된 글자 영역은 처음 한 번 「글자」 자리로 옮긴다(<see cref="MigrateOldOcrRegion"/>).
/// - "영역 지정" 체크를 없앴다 - 편집기는 <b>영역 보기가 켜져 있고 입력 전달이 꺼져 있으면</b> 돈다(<see cref="IsRegionEditingActive"/>).
/// - 새 자리 이름 칸을 없앴다 - 새 자리는 「자리N」 으로 생기고 그리드의 이름 칸이 바로 편집 상태가 된다(<see cref="OnRegionCreated"/>).
/// - 버튼은 영역 패널 도구 줄 한 곳(새 자리 · 지금 읽기 · 지우기 · 영역 보기), 같은 것을 그리드 오른쪽 버튼 메뉴에도.
///
/// 만들어 둔 자리를 옮기고 크기를 바꾸는 것은 미리보기 위의 <see cref="RegionCanvas"/>(Thumb·어도너)가 하고, 결과만
/// <see cref="RegionEditCommand"/> 로 받아 저장한다. 목록은 데이터셋 폴더의 <c>regions.json</c> 이다(<see cref="RegionBook"/>).
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
        set => SetProperty(() => SelectedRegion, value, () =>
        {
            // 다른 자리를 고르면 칸 선택은 풀린다 - 칸은 늘 고른 자리 안의 것이다.
            if (SelectedCell is { } cell && (value is null || !value.Cells.Contains(cell))) SelectedCell = null;
            RaisePropertyChanged(nameof(SelectedRegionNode));
            RegionsRevision++;
            WatchPreviewRegion(value);
        });
    }

    /// <summary>고른 칸. 없으면 자리 전체를 고른 것이다. 칸을 고르면 그 칸의 자리가 <see cref="SelectedRegion"/> 이다.</summary>
    public RegionCell? SelectedCell
    {
        get => GetProperty(() => SelectedCell);
        set => SetProperty(() => SelectedCell, value, () =>
        {
            if (value is not null && Regions.FirstOrDefault(r => r.Cells.Contains(value)) is { } owner && !ReferenceEquals(owner, SelectedRegion))
                SelectedRegion = owner;

            RaisePropertyChanged(nameof(SelectedRegionNode));
            RegionsRevision++;

            // 칸이 바뀌면 자리는 그대로라도 자를 자리가 달라져 캐시해 둔 미리보기 원본이 안 맞는다.
            ClearRegionPreview();
        });
    }

    /// <summary>
    /// 영역 패널 트리에서 고른 줄 - 자리(<see cref="NamedRegion"/>)나 칸(<see cref="RegionCell"/>). 고르면 <see cref="SelectedRegion"/>·<see cref="SelectedCell"/> 로 나눠 넣는다.
    /// </summary>
    public object? SelectedRegionNode
    {
        get => (object?)SelectedCell ?? SelectedRegion;
        set
        {
            switch (value)
            {
                case RegionCell cell:
                    SelectedCell = cell;
                    break;

                case NamedRegion region:
                    SelectedCell = null;
                    SelectedRegion = region;
                    break;

                case null when SelectedRegion is not null || SelectedCell is not null:
                    SelectedCell = null;
                    SelectedRegion = null;
                    break;
            }
        }
    }

    /// <summary>만들어 둔 자리를 미리보기에 겹쳐 그릴지. 켜 두고 입력 전달을 끄면 미리보기에서 바로 고친다.</summary>
    public bool ShowRegions
    {
        get => GetProperty(() => ShowRegions);
        set => SetProperty(() => ShowRegions, value, () =>
        {
            RaisePropertyChanged(nameof(IsRegionVisible));
            RaiseRegionEditingActive();
        });
    }

    /// <summary>겹그림이 자리를 그릴지(옛 이름을 XAML 이 묶고 있어 남겼다). <see cref="ShowRegions"/> 와 같다.</summary>
    public bool IsRegionVisible => ShowRegions;

    /// <summary>
    /// 미리보기가 자리 편집기로 도는가 - <b>영역 보기가 켜져 있고 입력 전달이 꺼져 있을 때</b>(스크립트 화면만).
    /// 캔버스(<c>RegionCanvas.IsEditing</c>)가 이것에 묶여 고르기·옮기기·크기 조절 어도너가 산다. 빈 자리를 끌면 새 자리.
    /// </summary>
    /// <remarks>
    /// 전달이 켜져 있으면 클릭은 게임 몫이라 편집기가 안 뜬다 - 고치려면 전달을 끈다. 자리가 안 보이면(영역 보기 끔) 편집기도 안 뜬다.
    /// </remarks>
    public bool IsRegionEditingActive => EditsRegionsWithoutPicking && ShowRegions && !IsInputForwardingEnabled;

    /// <summary>
    /// 미리보기에서 자리를 고치는 화면인가. 스크립트 화면만 - 플레이 화면에는 자리 편집 도구가 없어
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

    /// <summary>자리가 한 번 바뀔 때마다 오른다. 겹그림과 캔버스가 이것을 보고 다시 놓는다.</summary>
    public int RegionsRevision
    {
        get => GetProperty(() => RegionsRevision);
        set => SetProperty(() => RegionsRevision, value);
    }

    /// <summary>화면 가운데에 새 자리를 하나 놓는다 - 미리보기에서 끌어 옮기고 손잡이로 크기를 맞춘다. 이름 칸이 바로 편집 상태가 된다.</summary>
    public ICommand NewRegionCommand => new DelegateCommand(DoNewRegion);

    public ICommand RemoveRegionCommand => new DelegateCommand(DoRemoveRegion, () => SelectedRegion is not null);

    /// <summary>
    /// 고른 자리에 칸을 하나 더한다 - 자리(또는 그 칸)를 골랐을 때만 켜진다(사용자 2026-09-16 「AdornerLayer 가 선택되어 있으면 Adorner 추가 버튼 활성화」).
    /// </summary>
    public ICommand AddCellCommand => new DelegateCommand(DoAddCell, () => SelectedRegion is not null);

    /// <summary>고른 자리를 지금 읽어 본다 - 자리가 맞는지 확인하는 가장 빠른 길.</summary>
    public ICommand TestRegionCommand => new DelegateCommand(DoTestRegion, () => SelectedRegion is not null);

    /// <summary>고른 자리를 영역 이미지(PNG)로 저장한다 - 스크립트의 <c>그림찾기</c>·<c>그림누르기</c> 가 쓴다.</summary>
    public ICommand SaveTemplateCommand => new DelegateCommand(DoSaveTemplate, () => SelectedRegion is not null);

    /// <summary>
    /// 미리보기 캔버스가 자리를 옮기거나 크기를 바꿀 때마다 준다. 끄는 동안은 자리만 고치고, 놓으면 저장한다.
    /// </summary>
    public ICommand RegionEditCommand => new DelegateCommand<RegionEdit>(ApplyRegionEdit);

    /// <summary>미리보기 캔버스가 칸을 옮기거나 크기·각도를 바꿀 때마다 준다. 끄는 동안은 칸만 고치고, 놓으면 저장한다.</summary>
    public ICommand CellEditCommand => new DelegateCommand<CellEdit>(ApplyCellEdit);

    private void ApplyCellEdit(CellEdit edit) => Guard(() =>
    {
        edit.Cell.Rect = edit.Rect;
        edit.Cell.Angle = edit.Angle;

        if (!edit.Completed) return;

        SaveRegions();
        RegionsRevision++;

        StatusText = $"「{edit.Region.Name}.{edit.Cell.Name}」 구역을 고쳤습니다 - " +
                     $"{edit.Cell.X * 100:0.0}%, {edit.Cell.Y * 100:0.0}%  {edit.Cell.Width * 100:0.0}% x {edit.Cell.Height * 100:0.0}%" +
                     (Math.Abs(edit.Cell.Angle) < 0.01 ? string.Empty : $"  {edit.Cell.Angle:0.#}°");
    });

    private void ApplyRegionEdit(RegionEdit edit) => Guard(() =>
    {
        if (edit.Rect.Width < MinimumRegionSize || edit.Rect.Height < MinimumRegionSize) return;

        edit.Region.Rect = edit.Rect;

        if (!edit.Completed) return;

        SaveRegions();
        RegionsRevision++;

        StatusText = $"「{edit.Region.Name}」 영역을 고쳤습니다 - " +
                     $"{edit.Region.X * 100:0.0}%, {edit.Region.Y * 100:0.0}%  {edit.Region.Width * 100:0.0}% x {edit.Region.Height * 100:0.0}%";
    });

    // ── 목록 ─────────────────────────────────────────────────────────────

    /// <summary>자리마다 저장된 이름. 그리드에서 이름을 고치면 옛 이름과 견줘 겹치거나 비었으면 되돌린다.</summary>
    private readonly Dictionary<NamedRegion, string> _savedNames = new(ReferenceEqualityComparer.Instance);

    private bool _isRevertingName;

    /// <summary>파일에서 목록을 다시 채운다. 고른 것이 없으면 첫 줄을 고른다 - 빈 채로 두면 지금 읽기·지우기가 다 죽어 보인다.</summary>
    protected void LoadRegions()
    {
        foreach (var old in _savedNames.Keys) old.PropertyChanged -= OnRegionPropertyChanged;
        foreach (var old in _savedCellNames.Keys) old.PropertyChanged -= OnCellPropertyChanged;
        _savedNames.Clear();
        _savedCellNames.Clear();

        _regions = RegionBook.Load(RecognitionRoot);

        var keep = SelectedRegion?.Name;
        var keepCell = SelectedCell?.Name;

        Regions.Clear();

        foreach (var region in _regions.Regions.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            Regions.Add(region);
            _savedNames[region] = region.Name;
            region.PropertyChanged += OnRegionPropertyChanged;

            foreach (var cell in region.Cells) WatchCell(cell);
        }

        SelectedRegion = Regions.FirstOrDefault(r => string.Equals(r.Name, keep, StringComparison.OrdinalIgnoreCase)) ?? Regions.FirstOrDefault();
        SelectedCell = keepCell is null ? null : SelectedRegion?.FindCell(keepCell);
        UpdateLiveRegions();
    }

    /// <summary>칸마다 저장된 이름. 트리에서 칸 이름을 고치면 옛 이름과 견줘 겹치거나 틀리면 되돌린다.</summary>
    private readonly Dictionary<RegionCell, string> _savedCellNames = new(ReferenceEqualityComparer.Instance);

    private void WatchCell(RegionCell cell)
    {
        _savedCellNames[cell] = cell.Name;
        cell.PropertyChanged += OnCellPropertyChanged;
    }

    private void OnCellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RegionCell cell || _isRevertingName) return;

        var region = Regions.FirstOrDefault(r => r.Cells.Contains(cell));

        // 숫자만 - 저장하고, 이미 읽어 둔 글도 그 눈으로 다시 보인다.
        if (e.PropertyName == nameof(RegionCell.NumbersOnly))
        {
            SaveRegions();
            if (region is not null) cell.LastText = NamedRegion.Shown(region, cell, cell.LastText);
            RegionsRevision++;
            return;
        }

        if (e.PropertyName != nameof(RegionCell.Name)) return;

        var old = _savedCellNames.TryGetValue(cell, out var saved) ? saved : cell.Name;
        var name = cell.Name.Trim();

        if (region is null || string.Equals(name, old, StringComparison.Ordinal)) return;

        var clash = region.Cells.Any(other => !ReferenceEquals(other, cell) && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!RegionBook.IsValidName(name) || clash)
        {
            _isRevertingName = true;
            try { cell.Name = old; }
            finally { _isRevertingName = false; }

            StatusText = name.Length == 0 ? "구역 이름을 비울 수 없습니다 - 스크립트가 「영역.구역」 으로 부릅니다."
                : clash ? $"「{region.Name}」 영역에 「{name}」 구역이 이미 있습니다."
                : "이름에 점(.)은 못 씁니다 - 스크립트가 「영역.구역」 으로 가릅니다.";
            return;
        }

        if (!string.Equals(cell.Name, name, StringComparison.Ordinal))
        {
            _isRevertingName = true;
            try { cell.Name = name; }
            finally { _isRevertingName = false; }
        }

        _savedCellNames[cell] = name;
        SaveRegions();
        RegionsRevision++;

        StatusText = $"구역 이름을 「{region.Name}.{old}」 → 「{region.Name}.{name}」 으로 바꿨습니다. 스크립트에서 부르던 곳이 있으면 같이 고치세요.";
    }

    private void SaveRegions() => Guard(() =>
    {
        RegionBook.Save();
        RaisePropertyChanged(nameof(Regions));
    });

    /// <summary>그리드 칸에서 고친 것 - 이름은 검사해 저장하고, 계속 읽기는 바로 저장한다.</summary>
    private void OnRegionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NamedRegion region || _isRevertingName) return;

        switch (e.PropertyName)
        {
            case nameof(NamedRegion.Name):
                CommitRename(region);
                break;

            case nameof(NamedRegion.NumbersOnly):
                SaveRegions();
                region.LastText = NamedRegion.Shown(region, null, region.LastText);
                foreach (var cell in region.Cells) cell.LastText = NamedRegion.Shown(region, cell, cell.LastText);
                RegionsRevision++;
                break;

            case nameof(NamedRegion.KeepReading):
                SaveRegions();
                if (!region.KeepReading) region.LastText = string.Empty;
                UpdateLiveRegions();
                StatusText = region.KeepReading
                    ? $"「{region.Name}」 을(를) 0.5초마다 읽습니다{(IsRunning ? string.Empty : " - 캡처를 시작하면 읽기 시작합니다")}."
                    : $"「{region.Name}」 계속 읽기를 껐습니다.";
                break;
        }
    }

    /// <summary>
    /// 고른 자리를 지금 화면에서 잘라 프로젝트 <c>Resources</c> 에 PNG 로 저장한다. 스크립트는 <c>그림누르기("이름.png")</c> 로 부른다.
    /// </summary>
    /// <remarks>
    /// 사용자(2026-09-18) "사격장 글이 아니고 큰 이미지인데" - 그림으로 된 메뉴 버튼은 영역 이미지 한 장과 견줘 찾는다(<see cref="Vision.Matching.TemplateMatch"/>).
    /// 영역 이미지를 만드는 길이 이것뿐이다 - 자리를 미리보기에서 맞추고 이 단추를 누른다. 같은 이름이면 덮어쓴다(다시 맞춰 저장하는 일이 잦다).
    /// </remarks>
    private void DoSaveTemplate() => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        if (!IsRunning)
        {
            StatusText = "먼저 캡처를 시작해 화면을 잡아야 영역 이미지를 만들 수 있습니다.";
            return;
        }

        Hub.WantsFrames = true;

        var cell = SelectedCell;
        var target = RegionTargets.Of(region, cell)[0];
        var deadline = Environment.TickCount64 + 1500;
        System.Windows.Media.Imaging.BitmapSource? crop = null;

        while (Environment.TickCount64 < deadline && (!RegionTargets.TryCrop(Hub, target, out crop) || crop is null))
            System.Threading.Thread.Sleep(50);

        if (crop is null)
        {
            StatusText = "프레임이 안 옵니다 - 캡처가 돌고 있는지, CPU 리드백이 켜져 있는지 보세요.";
            return;
        }

        var name = (cell is null ? region.Name : $"{region.Name}.{cell.Name}") + ".png";
        var folder = System.IO.Path.Combine(RecognitionRoot, Input.Scripting.Projects.ScriptProject.ResourceFolder);

        System.IO.Directory.CreateDirectory(folder);

        var path = System.IO.Path.Combine(folder, name);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();

        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));

        using (var file = System.IO.File.Create(path)) encoder.Save(file);

        StatusText = $"영역 이미지를 저장했습니다 - {name} ({crop.PixelWidth}x{crop.PixelHeight}). 스크립트에서 그림누르기(\"{name}\") 로 부릅니다.";
        Logger.Info($"영역 이미지 저장: {path} ({crop.PixelWidth}x{crop.PixelHeight})");
    });

    private void CommitRename(NamedRegion region)
    {
        var old = _savedNames.TryGetValue(region, out var saved) ? saved : region.Name;
        var name = region.Name.Trim();

        if (string.Equals(name, old, StringComparison.Ordinal))
            return;

        var clash = Regions.Any(other => !ReferenceEquals(other, region) && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!RegionBook.IsValidName(name) || clash)
        {
            _isRevertingName = true;
            try { region.Name = old; }
            finally { _isRevertingName = false; }

            StatusText = name.Length == 0
                ? "이름을 비울 수 없습니다 - 스크립트가 이 이름으로 부릅니다."
                : clash ? $"「{name}」 은(는) 이미 있는 이름입니다 - 스크립트가 어느 쪽을 볼지 모르게 됩니다."
                : "이름에 점(.)은 못 씁니다 - 스크립트가 「영역.구역」 으로 가릅니다.";
            return;
        }

        if (!string.Equals(region.Name, name, StringComparison.Ordinal))
        {
            _isRevertingName = true;
            try { region.Name = name; }
            finally { _isRevertingName = false; }
        }

        _savedNames[region] = name;
        SaveRegions();
        RegionsRevision++;

        StatusText = $"이름을 「{old}」 → 「{name}」 으로 바꿨습니다. 스크립트에서 읽기(\"{old}\") 로 부르던 곳이 있으면 같이 고치세요.";
    }

    // ── 만들기 · 지우기 ───────────────────────────────────────────────────

    private void DoNewRegion() => Guard(() =>
    {
        // 화면 가운데에 탄약 숫자 만한 크기로. 옮기고 크기를 바꾸는 것은 미리보기의 손잡이로.
        AddRegion(new Rect(0.45, 0.45, 0.1, 0.06), "가운데에 놓았습니다 - 미리보기에서 끌어 옮기고 손잡이로 크기를 맞추세요");
    });

    /// <summary>자리를 넣고 고른 뒤 이름 칸을 편집 상태로 연다. 새 자리가 보이게 영역 보기를 켠다.</summary>
    private void AddRegion(Rect rect, string how)
    {
        var name = NextName();
        var region = new NamedRegion { Name = name, Rect = rect };

        RegionBook.Put(region);
        SaveRegions();
        LoadRegions();

        SelectedRegion = Regions.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        ShowRegions = true;

        StatusText = $"「{name}」 영역을 만들었습니다({how}). 이름을 바로 고치고 Enter." +
                     (IsInputForwardingEnabled ? " 미리보기에서 옮기려면 입력 전달을 끄세요." : string.Empty);

        if (SelectedRegion is { } created) OnRegionCreated(created);
    }

    /// <summary>새 자리가 생겼다. 화면은 영역 목록을 앞으로 띄우고 이름 칸을 편집 상태로 연다.</summary>
    protected virtual void OnRegionCreated(NamedRegion region) { }

    /// <summary>새 칸이 생겼다. 화면은 영역 목록의 그 칸 줄 이름을 편집 상태로 연다.</summary>
    protected virtual void OnCellCreated(RegionCell cell) { }

    private void DoAddCell() => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        var name = string.Empty;
        // 구역1 부터 비어 있는 번호(기본 구역은 「전체」 라 겹치지 않는다). 옛 파일의 「칸1」 은 NamedRegion 이 그대로 읽는다.
        for (var n = 1; name.Length == 0 || region.FindCell(name) is not null; n++) name = $"구역{n}";

        // 자리 가운데에 폭 절반 - 손잡이로 맞춘다.
        var cell = new RegionCell { Name = name, Rect = new Rect(0.25, 0, 0.5, 1) };

        region.Cells.Add(cell);
        WatchCell(cell);
        SaveRegions();

        SelectedCell = cell;
        ShowRegions = true;
        RegionsRevision++;

        StatusText = $"「{region.Name}.{name}」 구역을 더했습니다. 미리보기에서 끌어 맞추고, 이름을 고치세요." +
                     (IsInputForwardingEnabled ? " 미리보기에서 옮기려면 입력 전달을 끄세요." : string.Empty);

        OnCellCreated(cell);
    });

    private void DoRemoveRegion() => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        // 칸을 골랐으면 칸을 지운다. 자리에는 칸이 늘 하나 이상이다.
        if (SelectedCell is { } cell && region.Cells.Contains(cell))
        {
            if (region.Cells.Count <= 1)
            {
                StatusText = $"「{region.Name}」 영역에는 구역이 하나뿐이라 지울 수 없습니다 - 영역을 지우려면 영역 줄을 고르세요.";
                return;
            }

            region.Cells.Remove(cell);
            cell.PropertyChanged -= OnCellPropertyChanged;
            _savedCellNames.Remove(cell);
            SelectedCell = null;
            SaveRegions();
            RegionsRevision++;

            StatusText = $"「{region.Name}.{cell.Name}」 구역을 지웠습니다.";
            return;
        }

        RegionBook.Remove(region.Name);
        SaveRegions();
        LoadRegions();

        StatusText = $"「{region.Name}」 영역을 지웠습니다.";
    });

    /// <summary>안 겹치는 새 이름. 「영역1」「영역2」…(사용자, 2026-09-16 - 화면 말은 영역·구역).</summary>
    private string NextName()
    {
        for (var n = 1; ; n++)
        {
            var candidate = $"영역{n}";

            if (RegionBook.Find(candidate) is null) return candidate;
        }
    }

    /// <summary>
    /// 예전 "글자 영역"(설정에 저장된 한 곳)을 한 번 「글자」 자리로 옮긴다. 이미 같은 이름이 있으면 「글자2」….
    /// </summary>
    protected void MigrateOldOcrRegion(Rect old)
    {
        if (old.IsEmpty || old.Width < MinimumRegionSize || old.Height < MinimumRegionSize) return;

        var name = "글자";
        for (var n = 2; RegionBook.Find(name) is not null; n++) name = $"글자{n}";

        RegionBook.Put(new NamedRegion { Name = name, Rect = old });
        SaveRegions();
        LoadRegions();

        Logger.Info($"옛 글자 영역을 자리 「{name}」 으로 옮겼다: {old}");
    }

    // ── 미리보기에서 새 자리 끌기 ─────────────────────────────────────────

    /// <summary>
    /// 편집기가 돌면 여기서 마우스 다운을 먹는다. true 면 클릭을 게임으로 보내지 않는다.
    /// </summary>
    /// <remarks>
    /// 여기 오는 것은 <b>빈 자리</b>를 누른 것뿐이다. 자리 위의 누름은 캔버스의 손잡이(Thumb)가 먼저 먹어 여기까지 안 온다.
    /// 요소 검사가 켜져 있으면 검사가 먼저다 - 검사하려고 누른 것이 새 자리가 되면 안 된다.
    /// </remarks>
    private bool TryBeginRegionPick(Point pointInControl)
    {
        if (!IsRegionEditingActive || IsElementInspectEnabled) return false;

        // 빈 자리를 누르면 고른 것을 바로 푼다(VS 디자이너와 같다, 사용자 2026-09-15). 끌어서 새 자리를 만들면 그것이 골라진다.
        // 뗄 때만 풀면 좌표를 못 푼 누름(그림 크기를 모를 때)에서 안 풀렸다.
        if (SelectedRegion is not null) SelectedRegion = null;

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
            return true;
        }

        AddRegion(rect, "미리보기에서 끌어 만듦");
        return true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs args) => TryDragRegion(args);

    protected override void OnPreviewMouseUp(MouseButtonEventArgs args) => Guard(() => TryFinishRegionPick(args));

    /// <summary>이보다 작은 자리는 읽을 것이 없다(0~1). <see cref="NamedRegion.IsUsable"/> 과 같다.</summary>
    private const double MinimumRegionSize = 0.004;

    private void CancelRegionDrag()
    {
        _regionPickStart = null;
        RegionDraft = Rect.Empty;

        if (_previewSurface?.IsMouseCaptured == true) _previewSurface.ReleaseMouseCapture();
    }

    // ── 밝기 기준 미리보기 ──────────────────────────────────────────────────

    /// <summary>
    /// 「지금 읽기」가 칸마다 자른 원본(손질 전) 그림 - 밝기 기준·반전·확대를 바꿀 때마다 화면을 또 안 잘라도 되게 칸별로 들고 있는다.
    /// </summary>
    private readonly Dictionary<RegionCell, System.Windows.Media.Imaging.BitmapSource> _previewRawCrops = new(ReferenceEqualityComparer.Instance);

    private NamedRegion? _previewWatched;

    /// <summary>
    /// 「지금 읽기」로 자른 칸마다 지금 밝기 기준·반전·확대를 입힌 모습(사용자, 2026-09-18 "최대 이미지는 안 보여" - 자리를 골랐으면
    /// 그 안 칸 전부를 보여야 한다). 숫자만 바꿔서는 눈에 안 보이니 값을 바꿀 때마다(그리드 편집을 마치면) 캐시해 둔 원본에 다시 입혀 보여 준다.
    /// </summary>
    public ObservableCollection<RegionPreviewItem> RegionPreviewImages { get; } = [];

    /// <summary>미리보기 칸을 보일지(하나라도 있을 때만). 화면이 <c>BooleanToVisibilityConverter</c> 로 쓴다.</summary>
    public bool HasRegionPreview => RegionPreviewImages.Count > 0;

    /// <summary>고른 자리가 바뀌면 그 자리를 지켜본다(밝기 기준·반전·확대가 바뀔 때마다 미리보기를 새로 입히려고) - 캐시해 둔 원본은 다른 자리 것이라 비운다.</summary>
    private void WatchPreviewRegion(NamedRegion? region)
    {
        if (_previewWatched is { } old) old.PropertyChanged -= OnPreviewRegionPropertyChanged;

        _previewWatched = region;

        if (region is not null) region.PropertyChanged += OnPreviewRegionPropertyChanged;

        ClearRegionPreview();
    }

    private void OnPreviewRegionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NamedRegion.Threshold) or nameof(NamedRegion.Invert) or nameof(NamedRegion.Scale)) ReapplyRegionPreview();
    }

    private void ClearRegionPreview()
    {
        _previewRawCrops.Clear();
        RegionPreviewImages.Clear();
        RaisePropertyChanged(nameof(HasRegionPreview));
    }

    private void ReapplyRegionPreview()
    {
        if (SelectedRegion is not { } region) return;

        RegionPreviewImages.Clear();

        foreach (var (cell, raw) in _previewRawCrops)
            RegionPreviewImages.Add(new RegionPreviewItem(cell.Name, Vision.Ocr.RegionPreprocess.Apply(raw, region)));

        RaisePropertyChanged(nameof(HasRegionPreview));
    }

    // ── 지금 읽기 ────────────────────────────────────────────────────────

    /// <summary>
    /// 고른 자리를 지금 한 번 읽어 상태 줄과 그리드의 읽은 글자에 적는다.
    /// </summary>
    /// <remarks>
    /// <b>이게 없으면 자리를 못 맞춘다.</b> 끌어 놓고 맞는지 보려면 스크립트를 짜서 돌려야 하는데, 한 번에
    /// 몇십 초가 걸린다. 여기서는 누르는 즉시 읽은 글이 뜬다 - 비면 자리가 글자를 덮고 있는지 본다.
    /// </remarks>
    private void DoTestRegion() => Guard(() =>
    {
        if (SelectedRegion is not { } region) return;

        if (!IsRunning)
        {
            StatusText = "먼저 캡처를 시작해 화면을 잡아야 읽을 수 있습니다.";
            return;
        }

        // 프레임 복사를 켜 둬야 허브가 조각을 준다. 한 장 올 때까지 잠깐 기다린다.
        Hub.WantsFrames = true;

        if (!EnsureOcrEngine(out var problem) || _ocr is not { } ocr)
        {
            StatusText = $"글자 읽기 엔진을 열지 못했습니다. {problem}";
            return;
        }

        // 칸을 골랐으면 그 칸, 자리를 골랐으면 칸들을 차례로.
        var cell = SelectedCell is { } picked && region.Cells.Contains(picked) ? picked : null;
        var label = cell is null ? region.Name : $"{region.Name}.{cell.Name}";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var texts = new List<string>();

        // 다시 누르면 이 자리의 캐시를 지금 화면으로 새로 채운다(구역을 하나만 골랐으면 그 구역 것만 남는다).
        _previewRawCrops.Clear();

        foreach (var target in RegionTargets.Of(region, cell))
        {
            var deadline = Environment.TickCount64 + 1500;
            System.Windows.Media.Imaging.BitmapSource? crop = null;

            while (Environment.TickCount64 < deadline && (!RegionTargets.TryCrop(Hub, target, out crop) || crop is null))
                System.Threading.Thread.Sleep(50);

            if (crop is null)
            {
                StatusText = "프레임이 안 옵니다 - 캡처가 돌고 있는지, CPU 리드백이 켜져 있는지 보세요.";
                return;
            }

            // 자리를 골랐으면(구역을 안 골랐으면) 구역마다 미리보기로 캐시한다(사용자, 2026-09-18 "최대 이미지는 안 보여").
            _previewRawCrops[target.Cell] = crop;

            var read = ocr.RecognizeAsync(Vision.Ocr.RegionPreprocess.Apply(crop, region)).GetAwaiter().GetResult().Text.Replace(Environment.NewLine, " ").Trim();

            target.Cell.LastText = NamedRegion.Shown(region, target.Cell, read);
            texts.Add(read);
        }

        ReapplyRegionPreview();

        var (text, numbers) = RegionTargets.Combine(texts);

        if (cell is null) region.LastText = NamedRegion.Shown(region, null, text);

        StatusText = text.Length == 0
            ? $"「{label}」 에서 아무것도 못 읽었습니다. 구역이 글자를 덮고 있는지 보세요."
            : $"「{label}」 → 「{text}」{(numbers.Length > 0 ? $"  (숫자 {string.Join(", ", numbers)})" : string.Empty)}  [{ocr.Name} {watch.Elapsed.TotalMilliseconds:0}ms]";

        // 상태 줄은 다음 갱신이 덮는다. 나중에 "왜 안 읽혔지" 를 되짚으려면 로그에 남아야 한다.
        Logger.Debug($"영역 읽기: 「{label}」 {region.Rect} → 「{text}」 [{ocr.Name}]");
    });
}
