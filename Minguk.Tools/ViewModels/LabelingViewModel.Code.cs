using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Minguk.Base.Utilities;

using Minguk.Tools.Markup;
using Minguk.Tools.Vision.Labeling;

namespace Minguk.Tools.ViewModels;

public partial class LabelingViewModel
{
    private LabelDataset? _dataset;
    private LabelClasses _classes = new();

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

    private void DoAddClass() => Guard(() =>
    {
        if (string.IsNullOrWhiteSpace(NewClassName))
        {
            MessengerUtility.SendMainMessage("몹 이름을 적어 주세요.");
            return;
        }

        var index = _classes.Add(NewClassName);

        SaveClasses();
        RefreshClassNames();

        SelectedClassIndex = index;
        NewClassName = null;

        StatusText = $"몹을 더했습니다: {_classes.NameOf(index)} ({index}번)";
    });

    /// <summary>
    /// 고른 몹의 이름을 바꾼다. 번호는 그대로라 찍어 둔 라벨은 안 흔들린다.
    /// </summary>
    private void DoRenameClass() => Guard(() =>
    {
        if (SelectedClassIndex < 0) return;

        if (string.IsNullOrWhiteSpace(NewClassName))
        {
            MessengerUtility.SendMainMessage("바꿀 이름을 적어 주세요.");
            return;
        }

        var index = SelectedClassIndex;
        var before = _classes.NameOf(index);

        _classes.Rename(index, NewClassName);

        SaveClasses();
        RefreshClassNames();

        SelectedClassIndex = index;
        NewClassName = null;

        StatusText = $"이름을 바꿨습니다: {before} → {_classes.NameOf(index)} ({index}번 그대로)";
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

        ClassNames.Clear();
        foreach (var name in _classes.Names) ClassNames.Add(name);

        ClassNameSnapshot = _classes.Names.ToArray();

        // 아직 몹이 하나도 없으면 고를 것이 없다. 0번을 고른 척하면 없는 몹으로 찍힌다.
        SelectedClassIndex = ClassNames.Count == 0
            ? -1
            : Math.Clamp(index < 0 ? 0 : index, 0, ClassNames.Count - 1);

        DoRenameClassCommand.RaiseCanExecuteChanged();
    }

    private void OnSelectedClassChanged()
    {
        // 캔버스가 쓰는 색과 같은 것을 보여 준다. 다른 색을 보여 주면 어느 몹을 찍는 중인지
        // 화면과 그림이 어긋난다.
        var color = LabelCanvas.ColorOf(Math.Max(SelectedClassIndex, 0));
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        CurrentClassBrush = brush;

        DoRenameClassCommand.RaiseCanExecuteChanged();
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
