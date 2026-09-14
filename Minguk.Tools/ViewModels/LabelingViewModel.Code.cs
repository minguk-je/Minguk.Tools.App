using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using DevExpress.Mvvm;

using Minguk.Base.Utilities;

using Minguk.Tools.Markup;
using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.ViewModels;

public partial class LabelingViewModel
{
    private LabelDataset? _dataset;
    private LabelClasses _classes = new();

    /// <summary>몹 색(사람이 고른 것). 데이터셋 폴더의 class-colors.json. 안 고른 번호는 기본 색이다.</summary>
    private LabelPalette _palette = new();

    /// <summary>지금 화면에 뜬 그림. 저장할 자리를 알기 위해 따로 든다.</summary>
    /// <remarks>
    /// <see cref="SelectedItem"/> 은 <b>새로 고른 것</b>으로 이미 바뀐 뒤에 콜백이 돈다.
    /// 앞 그림에 저장하려면 그 전 것을 여기 들고 있어야 한다.
    /// </remarks>
    private LabelingRow? _loaded;

    /// <summary>목록을 우리가 건드리는 중인지. 그동안의 선택 변경은 저장을 부르지 않는다.</summary>
    private bool _isReloading;

    // ── 데이터셋 ─────────────────────────────────────────────────────────

    /// <summary>폴더를 다시 훑는다. 캡처 화면에서 새로 담은 그림도 이때 들어온다.</summary>
    private void DoReload() => Guard(() =>
    {
        SaveCurrentIfDirty();

        _dataset = new LabelDataset(DatasetRoot ?? LabelDataset.DefaultRoot);
        _dataset.EnsureCreated();

        _classes = _dataset.LoadClasses();
        _palette = _dataset.LoadPalette();
        RefreshClassNames();

        var previous = SelectedItem?.ImagePath;

        _isReloading = true;

        try
        {
            Items.Clear();
            foreach (var item in _dataset.EnumerateItems()) Items.Add(new LabelingRow(item));
        }
        finally
        {
            _isReloading = false;
        }

        // 보던 그림이 아직 있으면 그 자리로 돌아간다. 다시 고르게 하면 찍던 자리를 잃는다.
        SelectedItem = Items.FirstOrDefault(i => i.ImagePath == previous) is { ImagePath.Length: > 0 } found
            ? found
            : Items.FirstOrDefault();

        UpdateProgress();
        RefreshModelSummary();

        // 이름만 다르고 확장자가 같은 그림은 라벨 하나를 나눠 갖는다. 조용히 두면
        // 한쪽에 찍은 것이 다른 쪽에도 붙은 것처럼 보인다.
        if (_dataset.FindDuplicateStems() is { Count: > 0 } duplicates)
        {
            StatusText = $"이름이 겹치는 그림이 {duplicates.Count}건 있습니다 " +
                         $"({string.Join(", ", duplicates.Take(3))}). 라벨 파일을 나눠 갖게 됩니다.";
        }
        else
        {
            StatusText = Items.Count == 0
                ? $"담긴 그림이 없습니다. 캡처 화면에서 담거나 {_dataset.ImageDirectory} 에 넣으세요."
                : $"{Items.Count}장을 읽었습니다.";
        }

        RaiseListCommands();
    });

    private void DoOpenFolder() => Guard(() =>
    {
        var path = _dataset?.ImageDirectory;

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            MessengerUtility.SendMainMessage("아직 폴더가 없습니다.");
            return;
        }

        // 탐색기로 연다. UseShellExecute 가 아니면 폴더 경로를 못 넘긴다.
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    });

    private void DoChooseFolder() => Guard(() =>
    {
        if (FolderBrowserDialogService is not { } dialog)
        {
            MessengerUtility.SendMainMessage("폴더 고르기 서비스를 찾지 못했습니다.");
            return;
        }

        dialog.StartPath = Directory.Exists(DatasetRoot) ? DatasetRoot : LabelDataset.DefaultRoot;

        if (!dialog.ShowDialog()) return;

        DatasetRoot = dialog.ResultPath;

        // 바로 저장한다. 캡처 화면이 이 값을 보고 담으므로, 화면을 닫을 때까지
        // 미뤄 두면 그동안 담은 그림이 옛 폴더로 간다.
        LabelDataset.ConfiguredRoot = DatasetRoot;

        DoReload();
    });

    // ── 그림 넘기기 ──────────────────────────────────────────────────────

    private void OnSelectedItemChanged() => Guard(() =>
    {
        // 목록을 우리가 비웠다 채우는 중이면 저장하지 않는다 - 그 사이의 선택 변경은
        // 사람이 한 것이 아니고, 빈 Boxes 를 앞 그림에 덮어쓰게 된다.
        if (!_isReloading) SaveCurrentIfDirty();

        _loaded = SelectedItem;

        // 앞 그림에서 찾은 점선을 지운다. 안 지우면 다음 그림 위에 그대로 남아
        // 엉뚱한 자리에 몹이 있는 것처럼 보인다.
        ClearPredictionsForNewImage();

        Boxes.Clear();
        SelectedBoxIndex = -1;

        if (SelectedItem is not { } item)
        {
            CurrentImage = null;
            IsDirty = false;
            RaiseListCommands();
            return;
        }

        CurrentImage = LoadImage(item.ImagePath);

        var boxes = LabelFile.Load(item.LabelPath, out var skipped);

        foreach (var box in boxes) Boxes.Add(box);

        // 읽어 넣는 것으로는 안 고친 것이다. Boxes 를 채우면 CollectionChanged 가 돌아
        // IsDirty 가 서므로 여기서 다시 내린다.
        IsDirty = false;

        if (skipped > 0)
            StatusText = $"{item.Name} - 읽을 수 없는 줄 {skipped}개를 건너뛰었습니다.";

        RaiseListCommands();
    });

    /// <summary>
    /// 그림을 읽는다. 파일을 붙들지 않는다.
    /// </summary>
    /// <remarks>
    /// <see cref="BitmapCacheOption.OnLoad"/> 가 아니면 BitmapImage 가 파일을 계속 열어 둔다.
    /// 그러면 탐색기에서 그 그림을 지우거나 옮길 수 없고, 앱을 껐다 켜야 풀린다.
    /// 스트림을 직접 열어 넘기는 것도 같은 이유다 - UriSource 만 주면 늦게 읽으면서 붙든다.
    /// </remarks>
    private ImageSource? LoadImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            var image = new BitmapImage();

            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            return image;
        }
        catch (Exception ex)
        {
            // 한 장이 깨졌다고 화면을 세우지 않는다. 그 장만 건너뛰고 다음으로 갈 수 있어야 한다.
            Logger.Warn(ex, $"그림을 읽지 못했다: {path}");
            StatusText = $"{Path.GetFileName(path)} 를 읽지 못했습니다: {ex.Message}";

            return null;
        }
    }

    private void DoPrevious() => Move(-1);

    private void DoNext() => Move(1);

    private void Move(int delta) => Guard(() =>
    {
        if (Items.Count == 0) return;

        var index = SelectedItem is { } item ? Items.IndexOf(item) : -1;

        // 끝에서 멈춘다. 돌아 나오게 하면 수백 장을 넘기다가 처음으로 돌아온 줄 모르고
        // 이미 찍은 것을 또 찍는다.
        var next = Math.Clamp(index + delta, 0, Items.Count - 1);

        SelectedItem = Items[next];
    });

    /// <summary>
    /// 아직 안 찍은 다음 그림으로 건너뛴다.
    /// </summary>
    /// <remarks>
    /// 수백 장을 찍다 보면 중간에 몇 장이 빈다. 한 장씩 넘겨 찾게 하지 않는다.
    /// 지금 자리 뒤부터 보고, 없으면 앞에서 다시 찾는다.
    /// </remarks>
    private void DoNextUnlabeled() => Guard(() =>
    {
        SaveCurrentIfDirty();

        var start = SelectedItem is { } item ? Items.IndexOf(item) : -1;

        for (var step = 1; step <= Items.Count; step++)
        {
            var candidate = Items[(start + step + Items.Count) % Items.Count];

            if (candidate.HasLabel) continue;

            SelectedItem = candidate;
            StatusText = $"안 찍은 그림으로 갑니다: {candidate.Name}";

            return;
        }

        StatusText = "안 찍은 그림이 없습니다.";
    });

    /// <summary>
    /// 앞에서 가장 가까운, 라벨이 있는 그림의 사각형을 이 그림에 더한다.
    /// </summary>
    /// <remarks>
    /// 연달아 담은 그림은 몹이 몇 픽셀만 움직인다. 매 장 처음부터 그리게 하면 같은 사각형을
    /// 수십 번 그린다 - 가져와서 옮기기·크기 조절로 맞추는 것이 빠르다.
    /// 바로 앞 장이 아니라 <b>라벨이 있는</b> 가장 가까운 앞 장이다. 앞 장을 건너뛰었으면
    /// 빈 것을 가져와 봐야 아무 일도 안 일어난다.
    /// 지금 있는 사각형은 지우지 않고 더한다. 지우고 싶으면 모두 지우기가 있다.
    /// </remarks>
    private void DoCopyPrevious() => Guard(() =>
    {
        if (SelectedItem is not { } current) return;

        var index = Items.IndexOf(current);

        for (var i = index - 1; i >= 0; i--)
        {
            var source = Items[i];

            if (!source.HasLabel) continue;

            var boxes = LabelFile.Load(source.LabelPath, out _);

            if (boxes.Count == 0) continue;

            foreach (var box in boxes) Boxes.Add(box);

            SelectedBoxIndex = Boxes.Count - 1;
            StatusText = $"{source.Name} 의 사각형 {boxes.Count}개를 가져왔습니다. 자리가 다르면 끌어서 맞추세요.";

            return;
        }

        StatusText = "앞에 라벨이 있는 그림이 없습니다.";
    });

    // ── 저장 ─────────────────────────────────────────────────────────────

    private void DoSave() => Guard(() =>
    {
        if (SaveCurrentIfDirty()) return;

        // 안 바뀌었어도 누른 이상 무슨 일이 있었는지는 말해 준다.
        StatusText = "바뀐 것이 없습니다.";
    });

    /// <summary>
    /// 바뀐 것이 있으면 저장한다. 저장했으면 true.
    /// </summary>
    /// <remarks>
    /// 여러 곳에서 부른다(그림 넘기기·다시 읽기·화면 닫기). 안 바뀌었을 때 아무 일도 안 하는
    /// 것이 중요하다 - 그냥 넘겨 보기만 해도 파일 시각이 바뀌면 무엇을 실제로 고쳤는지 모른다.
    /// </remarks>
    private bool SaveCurrentIfDirty()
    {
        if (!IsDirty || _loaded is not { } item) return false;

        try
        {
            LabelFile.Save(item.LabelPath, Boxes);

            // 목록의 표시를 바로 켠다. 저장했는데 ● 이 안 켜지면 안 된 줄 알고 또 찍는다.
            item.HasLabel = Boxes.Count > 0;

            IsDirty = false;

            StatusText = Boxes.Count == 0
                ? $"{item.Name} - 라벨을 모두 지웠습니다."
                : $"{item.Name} - 사각형 {Boxes.Count}개를 저장했습니다.";

            UpdateProgress();

            return true;
        }
        catch (Exception ex)
        {
            // 여기서 Guard 를 쓰지 않는다. 그림을 넘기는 도중에 불리므로 예외 창을 띄우면
            // 넘기기가 반쯤 된 채로 멈춘다. 대신 안 저장했다는 표시를 남긴다.
            Logger.Error(ex, $"라벨을 저장하지 못했다: {item.LabelPath}");
            StatusText = $"저장하지 못했습니다: {ex.Message}";

            return false;
        }
    }

    private void OnBoxesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        IsDirty = true;

        DoClearBoxesCommand.RaiseCanExecuteChanged();
        DoDeleteBoxCommand.RaiseCanExecuteChanged();
    }

    private void DoDeleteBox() => Guard(() =>
    {
        if (SelectedBoxIndex < 0 || SelectedBoxIndex >= Boxes.Count) return;

        Boxes.RemoveAt(SelectedBoxIndex);
        SelectedBoxIndex = -1;
    });

    private void DoClearBoxes() => Guard(() =>
    {
        Boxes.Clear();
        SelectedBoxIndex = -1;
    });

    // ── 몹 이름 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 몹을 하나 더하고 바로 이름 칸을 연다.
    /// </summary>
    /// <remarks>
    /// 이름을 따로 적는 칸이 없다(2026-09-14). VS 솔루션 탐색기의 새 항목처럼 임시 이름("몹 3")으로 넣고
    /// 그리드 안에서 고쳐 쓰게 한다 - 같은 일을 두 곳(칸·그리드)에서 하면 하나는 안 쓰인다.
    /// </remarks>
    private void DoAddClass() => Guard(() =>
    {
        var n = _classes.Count + 1;
        string name;

        do name = $"몹 {n++}";
        while (_classes.IndexOf(name) >= 0);

        var index = _classes.Add(name);

        SaveClasses();
        RefreshClassNames();

        SelectedClassIndex = index;

        StatusText = $"몹을 더했습니다: {name} ({index}번) - 이름을 고쳐 쓰세요.";

        BeginRename();
    });

    /// <summary>
    /// 고른 몹을 지운다. 아무 라벨에도 안 쓰인 몹만이다.
    /// </summary>
    /// <remarks>
    /// 라벨에는 번호가 들어 있어 중간을 지우면 뒤가 당겨진다 - <see cref="LabelDataset.RemoveClass"/> 가 라벨 파일의
    /// 번호와 색까지 같이 당긴다. 쓰인 몹은 못 지운다(사각형을 잃거나 다른 몹을 가리키게 된다) - 어느 파일에 쓰였는지
    /// 상태 줄에 적어 사람이 그 사각형을 먼저 지우게 한다. 시험으로 넣은 몹처럼 안 찍은 것은 잃을 것이 없다.
    /// </remarks>
    private void DoDeleteClass() => Guard(() =>
    {
        if (_dataset is not { } dataset) return;

        // 그리드에서 여러 줄을 골랐으면 다 지운다. 하나만 지우면 "멀티 선택이 되는데 하나만 지워진다" 가 된다(실제 그랬다).
        var indices = (_classGrid?.SelectedItems.OfType<LabelClassRow>().Select(r => r.Index) ?? [])
            .Distinct()
            .OrderBy(i => i)
            .ToArray();

        if (indices.Length == 0 && SelectedClassIndex >= 0) indices = [SelectedClassIndex];
        if (indices.Length == 0) return;

        // 지금 그림의 사각형도 파일에 있어야 "쓰였는지" 를 제대로 센다.
        SaveCurrentIfDirty();

        var names = indices.Select(_classes.NameOf).ToArray();

        foreach (var index in indices)
        {
            var used = dataset.FindLabelsUsing(index);

            if (used.Count == 0) continue;

            StatusText = $"'{_classes.NameOf(index)}' 은 라벨 {used.Count}장에 쓰여 못 지웁니다. 그 사각형을 먼저 지우세요: {string.Join(", ", used.Take(3))}{(used.Count > 3 ? " …" : "")}";
            MessengerUtility.SendMainMessage(StatusText);
            return;
        }

        var answer = MessageBoxService.ShowMessage(
            $"몹 {indices.Length}개를 지웁니다: {string.Join(", ", names)}\n아직 아무 라벨에도 안 쓰였습니다.\n뒤 번호는 당겨지고 라벨 파일도 같이 고쳐집니다.\n\n계속할까요?",
            "몹 지우기",
            MessageButton.OKCancel,
            MessageIcon.Question);

        if (answer != MessageResult.OK) return;

        // 뒤에서부터 지운다 - 앞을 먼저 지우면 뒤 번호가 당겨져 다른 몹을 지운다.
        var rewritten = 0;
        foreach (var index in indices.Reverse()) rewritten += dataset.RemoveClass(_classes, index);

        // 번호가 당겨졌으니 목록과 지금 그림의 사각형을 파일에서 다시 읽는다.
        DoReload();
        SelectedClassIndex = _classes.Count == 0 ? -1 : Math.Min(indices[0], _classes.Count - 1);

        StatusText = rewritten > 0
            ? $"몹을 지웠습니다: {string.Join(", ", names)}. 라벨 파일 {rewritten}장의 번호를 당겼습니다."
            : $"몹을 지웠습니다: {string.Join(", ", names)}.";
    });

    // ── 그림 목록 패널 너비 ──────────────────────────────────────────────

    /// <summary>
    /// 그리드 배치가 끝날 때마다 열 너비를 합쳐 패널 너비로 되돌린다.
    /// </summary>
    /// <remarks>
    /// <c>LayoutUpdated</c> 는 자주 오지만 더하는 것이 열 대여섯 개라 값이 싸다. 1px 넘게 다를 때만 쓴다 - 패널 너비를
    /// 바꾸면 다시 배치가 오는데, 열은 내용 너비라 그리드 너비와 무관해 같은 값이 나오고 거기서 멈춘다(되먹임 없음).
    /// 세로 스크롤 막대와 테두리 몫(24px)은 고정으로 더한다 - 막대가 나타났다 사라지며 패널이 떨리지 않게.
    /// </remarks>
    private void OnImagesGridLayoutUpdated(object? sender, EventArgs e)
    {
        if (_imagesGrid is not { View: DevExpress.Xpf.Grid.TableView view } grid) return;

        var columns = grid.Columns.Where(c => c.Visible).Sum(c => c.ActualWidth);
        if (columns <= 0) return;

        var indicator = double.IsNaN(view.IndicatorWidth) ? 0 : view.IndicatorWidth;
        var width = Math.Ceiling(columns + indicator + 24);

        if (Math.Abs(width - ImagesPanelWidth) > 1) ImagesPanelWidth = width;
    }

    /// <summary>더하기 직후 새 줄의 이름 칸을 연다.</summary>
    private void BeginRename() => BeginClassEdit(nameof(LabelClassRow.Name));

    /// <summary>
    /// 그리드의 고른 줄에서 그 칸을 연다.
    /// </summary>
    /// <remarks>
    /// 한 박자 뒤에 연다 - 더하기는 줄을 통째로 갈아 끼운 직후라 바로 열면 그리드가 아직 옛 줄을 보고 있다.
    /// 칸은 <see cref="OnClassEditorShowing"/> 이 막고 있어 <c>_allowClassEdit</c> 을 먼저 세운다.
    /// </remarks>
    private void BeginClassEdit(string fieldName)
    {
        if (SelectedClassIndex < 0 || _classGrid is not { View: DevExpress.Xpf.Grid.TableView view } grid) return;

        DispatcherService.BeginInvoke(() =>
        {
            _allowClassEdit = true;
            grid.CurrentColumn = grid.Columns[fieldName] ?? grid.CurrentColumn;
            view.ShowEditor(selectAll: true);
        });
    }

    /// <summary>처음에는 어느 칸도 안 열린다. 더블 클릭·더하기 직후에만 연다 - 한 번 누를 때마다 열리면 줄을 고르려다 편집이 된다.</summary>
    private void OnClassEditorShowing(object sender, DevExpress.Xpf.Grid.ShowingEditorEventArgs e)
        => e.Cancel = !_allowClassEdit;

    private void OnClassEditorHidden(object sender, DevExpress.Xpf.Grid.EditorEventArgs e) => _allowClassEdit = false;

    /// <summary>칸을 더블 클릭하면 그 칸(이름·색)을 고친다. 행 번호 자리를 더블 클릭하면 이름 칸이다.</summary>
    private void OnClassRowDoubleClick(object sender, DevExpress.Xpf.Grid.RowDoubleClickEventArgs e)
    {
        if (_classGrid?.GetRow(e.HitInfo.RowHandle) is not LabelClassRow row) return;

        var field = e.HitInfo.Column?.FieldName is nameof(LabelClassRow.Color) ? nameof(LabelClassRow.Color) : nameof(LabelClassRow.Name);

        SelectedClassIndex = row.Index;
        BeginClassEdit(field);
        e.Handled = true;
    }

    /// <summary>
    /// 그리드 안에서 이름이나 색을 고치면 여기로 온다.
    /// </summary>
    /// <remarks>
    /// 빈 이름과 이미 있는 이름은 되돌린다 - 되돌리는 세터가 다시 여기로 오지만 같은 이름이라 곧바로 끝난다.
    /// 목록을 다시 채우지 않는다(줄을 갈아 끼우면 고른 줄이 풀린다). 캔버스에 주는 복사본만 새로 만든다.
    /// </remarks>
    private void OnClassRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Guard(() =>
    {
        if (sender is not LabelClassRow row) return;

        if (e.PropertyName == nameof(LabelClassRow.Color))
        {
            if (_palette.ColorOf(row.Index) == row.Color) return;

            _palette.Set(row.Index, row.Color);
            _dataset?.SavePalette(_palette);

            ClassColors = _palette.Snapshot(_classes.Count);
            if (row.Index == SelectedClassIndex) OnSelectedClassChanged();

            return;
        }

        if (e.PropertyName != nameof(LabelClassRow.Name)) return;

        var before = _classes.NameOf(row.Index);
        var wanted = row.Name?.Trim() ?? string.Empty;

        if (string.Equals(row.Name, before, StringComparison.Ordinal)) return;

        if (wanted.Length == 0)
        {
            row.Name = before;
            StatusText = "몹 이름이 비어 되돌렸습니다.";
            return;
        }

        var duplicate = _classes.IndexOf(wanted);

        if (duplicate >= 0 && duplicate != row.Index)
        {
            row.Name = before;
            StatusText = $"이미 있는 이름입니다: {wanted} ({duplicate}번)";
            return;
        }

        _classes.Rename(row.Index, wanted);
        SaveClasses();

        if (!string.Equals(row.Name, wanted, StringComparison.Ordinal)) row.Name = wanted;   // 앞뒤 빈칸을 뗀 것

        ClassNameSnapshot = _classes.Names.ToArray();

        StatusText = $"이름을 바꿨습니다: {before} → {wanted} ({row.Index}번 그대로)";
    });

    private void SaveClasses()
    {
        if (_dataset is not { } dataset) return;

        dataset.SaveClasses(_classes);
    }

    /// <summary>
    /// 목록을 다시 채운다. 캔버스에 넘길 복사본도 여기서 새로 만든다.
    /// </summary>
    private void RefreshClassNames()
    {
        var index = SelectedClassIndex;

        foreach (var old in Classes) old.PropertyChanged -= OnClassRowChanged;
        Classes.Clear();

        for (var i = 0; i < _classes.Count; i++)
        {
            var row = new LabelClassRow(i, _classes.NameOf(i), _palette.ColorOf(i));
            row.PropertyChanged += OnClassRowChanged;
            Classes.Add(row);
        }

        ClassNameSnapshot = _classes.Names.ToArray();
        ClassColors = _palette.Snapshot(_classes.Count);

        // 아직 몹이 하나도 없으면 고를 것이 없다. 0번을 고른 척하면 없는 몹으로 찍힌다.
        SelectedClassIndex = Classes.Count == 0
            ? -1
            : Math.Clamp(index < 0 ? 0 : index, 0, Classes.Count - 1);

        // 목록을 비우는 순간 그리드가 고른 것을 풀었다(SelectedClass = null). 번호가 그대로면 세터 콜백이
        // 안 돌아 되살아나지 않으므로 여기서 줄을 다시 맞춘다.
        SyncSelectedClass();

        DoDeleteClassCommand.RaiseCanExecuteChanged();
    }

    private void OnSelectedClassChanged()
    {
        // 캔버스가 쓰는 색과 같은 것을 보여 준다. 다른 색을 보여 주면 어느 몹을 찍는 중인지
        // 화면과 그림이 어긋난다. 사람이 고른 색이 있으면 그것이다.
        var color = _palette.ColorOf(Math.Max(SelectedClassIndex, 0));
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        CurrentClassBrush = brush;

        SyncSelectedClass();

        DoDeleteClassCommand.RaiseCanExecuteChanged();
    }

    /// <summary>번호에 맞는 줄을 그리드의 고른 줄로. 같으면 안 건드린다 - 서로 되부르지 않게.</summary>
    private void SyncSelectedClass()
    {
        var row = SelectedClassIndex >= 0 && SelectedClassIndex < Classes.Count ? Classes[SelectedClassIndex] : null;

        if (!ReferenceEquals(SelectedClass, row)) SelectedClass = row;
    }

    /// <summary>
    /// 그리드에서 고른 줄을 번호로.
    /// </summary>
    /// <remarks>
    /// 고른 것을 풀면(null) 번호는 그대로 둔다 - 캔버스는 늘 찍을 몹이 있어야 하고, 목록을 다시 채울 때
    /// 잠깐 비는 순간에도 번호를 잃으면 안 된다.
    /// </remarks>
    private void OnSelectedClassRowChanged()
    {
        if (SelectedClass is not { } row) return;

        if (row.Index != SelectedClassIndex) SelectedClassIndex = row.Index;
    }

    // ── 알림 ─────────────────────────────────────────────────────────────

    private void UpdateProgress()
    {
        if (_dataset is null)
        {
            ProgressText = null;
            return;
        }

        // 줄이 제 상태를 들고 있어 디스크를 다시 보지 않는다. 밖에서 라벨 파일을 지웠다면
        // "다시 읽기" 로 맞춘다.
        var done = Items.Count(item => item.HasLabel);

        ProgressText = $"{Items.Count}장 중 {done}장 찍음";
    }

    private void RaiseListCommands()
    {
        DoPreviousCommand.RaiseCanExecuteChanged();
        DoNextCommand.RaiseCanExecuteChanged();
        DoNextUnlabeledCommand.RaiseCanExecuteChanged();
        DoSaveCommand.RaiseCanExecuteChanged();
        DoClearBoxesCommand.RaiseCanExecuteChanged();
        DoDeleteBoxCommand.RaiseCanExecuteChanged();
        DoDetectCommand.RaiseCanExecuteChanged();
        DoClearPredictionsCommand.RaiseCanExecuteChanged();
        DoAdoptPredictionsCommand.RaiseCanExecuteChanged();
        DoCopyPreviousCommand.RaiseCanExecuteChanged();
    }

    private void OnPredictionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DoClearPredictionsCommand.RaiseCanExecuteChanged();
        DoAdoptPredictionsCommand.RaiseCanExecuteChanged();
    }
}
