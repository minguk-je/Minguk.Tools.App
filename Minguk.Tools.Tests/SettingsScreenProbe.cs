using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.LayoutControl;

using Minguk.Tools.Helper;
using Minguk.Tools.Markup.Settings;
using Minguk.Tools.Projects;
using Minguk.Tools.Projects.Settings;
using Minguk.Tools.ViewModels;
using Minguk.Tools.ViewModels.Settings;
using Minguk.Tools.Views;

namespace Minguk.Tools.Tests;

/// <summary>
/// 설정 탭(<c>docs/솔루션-설정.md</c>)을 화면 밖 창에 띄운다 - <c>--settings-screen [--out=폴더]</c>.
/// </summary>
/// <remarks>
/// 임시 솔루션에 칸 종류를 모두 놓고: 미리보기가 칸을 다 그리는지, 편집기로 바꾼 값이 층에 들어가는지, 스크립트가(다른 스레드에서) 쓴 값이
/// 편집기에 보이는지, 디자인에서 칸을 놓고 속성으로 고치면 양식 파일에 저장되는지, 겹치는 이름은 막는지, 누른 자리의 칸을 고르는지.
/// 바인딩 오류·세로 정렬을 재고 미리보기·디자인 두 장을 찍는다. 커서는 안 가져간다.
/// </remarks>
public static class SettingsScreenProbe
{
    public static int Run(string[] args)
    {
        var outputFolder = Program.ArgValue(args, "--out=") ?? Path.GetTempPath();
        var failures = 0;
        var bindingErrors = new List<string>();

        CompatibilitySettings.UseLightweightThemes = true;
        LightweightThemeManager.AllowStandardControlsTheming = false;
        DevExpress.Xpf.Grid.DataControlBase.AllowInfiniteGridSize = true;

        _ = new System.Threading.Timer(_ =>
        {
            Console.WriteLine("[FAIL] 설정 화면 - 90초 안에 끝나지 않았다.");
            Environment.Exit(2);
        }, null, 90_000, System.Threading.Timeout.Infinite);

        UserPreferencesHelper.EnsureDefaults();
        UserPreferencesHelper.ApplyTheme();

        var root = Path.Combine(Path.GetTempPath(), "minguk-settings-screen-" + Guid.NewGuid().ToString("N")[..8]);
        var project = MakeSolution(root);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new CollectingListener(bindingErrors));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

        void Expect(bool ok, string name, string detail)
        {
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] 설정 화면: {name} — {detail}");
            if (!ok) failures++;
        }

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            Window? window = null;

            try
            {
                var view = new SolutionSettingsView();
                window = new Window
                {
                    Width = 1200, Height = 720, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                    Content = view
                };
                window.Show();

                await Pump(800);

                if (view.DataContext is DevExpress.Mvvm.ISupportParentViewModel child) child.ParentViewModel = new object();

                await Pump(1000);

                var vm = (SolutionSettingsViewModel)view.DataContext;
                vm.OpenProject(project);
                vm.SelectedLayer = vm.Layers.First(l => l.Kind == SettingsLayerKind.Solution);

                await Pump(1200);

                var canvas = Descendants<SettingsFormCanvas>(window).Single();
                var items = Descendants<LayoutItem>(canvas).Where(i => i.Tag is SettingsItem).ToList();
                var names = items.Select(i => ((SettingsItem)i.Tag).Name).ToList();

                Expect(names.SequenceEqual(["물약HP", "자동줍기", "난이도", "속도", "보스이름", "물약목록", "보스"]),
                    "미리보기가 솔루션 공통 칸 다음에 프로젝트 칸을 그린다", string.Join(", ", names));

                // ── 편집기로 값 바꾸기 ──
                var settings = SolutionSettings.ForProject(project);
                var spin = Descendants<SpinEdit>(ItemOf(items, "물약HP")).Single();
                spin.EditValue = 77m;
                await Pump(300);
                var written = settings.Find("물약HP")!;
                Expect(written.Source == SettingsLayerKind.Solution && Num(written) == 77, "숫자 칸을 바꾸면 편집 대상 층(솔루션 공통)에 들어간다", $"{written.Source} · {written.Value?.ToJsonString()}");

                // 이 프로젝트로 바꿔 덮어쓰기 - 라벨이 굵어진다. 칸마다 붙던 ↺ 는 뺐다(사용자, 2026-09-17).
                vm.SelectedLayer = vm.Layers.First(l => l.Kind == SettingsLayerKind.Project);
                await Pump(800);
                items = [.. Descendants<LayoutItem>(canvas).Where(i => i.Tag is SettingsItem)];
                Descendants<SpinEdit>(ItemOf(items, "물약HP")).Single().EditValue = 12m;
                await Pump(300);
                var overridden = ItemOf(items, "물약HP");
                var noButtons = !Descendants<SimpleButton>(canvas).Any();
                Expect(settings.Find("물약HP")!.IsProjectValue && overridden.LabelStyle is not null && noButtons,
                    "이 프로젝트에서 바꾸면 덮어쓰기 - 라벨이 굵고, 칸에 되돌리기 단추가 없다", $"프로젝트 값 {settings.Find("물약HP")!.IsProjectValue} · 굵게 {overridden.LabelStyle is not null} · 단추 없음 {noButtons}");

                // [초기값] - 편집 대상 층의 덮어쓴 값을 모두 뺀다(단추는 묻고 부르는 것 - 여기서는 묻지 않는 쪽을 부른다).
                var removedCount = vm.ResetOverriddenValues();
                await Pump(300);
                Expect(removedCount >= 1 && Num(settings.Find("물약HP")!) == 77 && overridden.LabelStyle is null && vm.ResetValuesCommand.CanExecute(null),
                    "[초기값] 을 누르면 덮어쓴 값이 빠지고 솔루션 값으로 돌아간다", $"뺀 {removedCount}개 · {settings.Find("물약HP")!.Value?.ToJsonString()} · 굵게 {overridden.LabelStyle is not null}");

                // ── 스크립트(다른 스레드)가 쓴 값이 편집기에 보인다 ──
                await System.Threading.Tasks.Task.Run(() => SolutionSettings.ForProject(project).SetValue("자동줍기", JsonValue.Create(false)));
                await Pump(400);
                var check = Descendants<CheckEdit>(ItemOf(items, "자동줍기")).Single();
                Expect(check.IsChecked == false, "스크립트가 다른 스레드에서 쓴 값이 편집기에 곧바로 보인다", $"체크 {check.IsChecked}");

                // 목록 칸 - 행이 그려진다.
                var grid = Descendants<DevExpress.Xpf.Grid.GridControl>(ItemOf(items, "물약목록")).Single();
                var dataRows = grid.ItemsSource is System.Data.DataView table ? table.Count : -1;
                Expect(dataRows == 2 && grid.Columns.Count == 3, "목록 칸이 열·행을 그린다(맨 아래는 새 행 줄)", $"열 {grid.Columns.Count} · 행 {dataRows}");

                var narrowest = grid.Columns.Min(c => c.ActualWidth);
                Expect(narrowest >= 60 - 0.5, "목록 칸 열은 짧아도 60 보다 좁지 않다", string.Join(", ", grid.Columns.Select(c => $"{c.FieldName} {c.ActualWidth:0}")));

                var mine = bindingErrors.Where(e => e.Contains("Settings")).Distinct().ToList();
                Expect(mine.Count == 0, "바인딩 오류 없음", string.Join(" / ", mine.Take(5)));

                failures += VerticalAlignmentCheck.Report((FrameworkElement)window.Content, "설정 화면(미리보기)");

                Render(window, Path.Combine(outputFolder, "minguk-settings-preview.png"));

                // ── 디자인 ──
                // 도구 상자 아이콘(DevExpress.Images) - 경로가 틀리면 null 이라 글자만 보인다.
                var missingGlyphs = vm.Toolbox.Where(t => t.Glyph is not System.Windows.Media.Imaging.BitmapSource { PixelWidth: 16 }).Select(t => t.Title).ToList();
                Expect(missingGlyphs.Count == 0, "도구 상자 칸마다 DevExpress 아이콘(16x16)이 읽힌다", missingGlyphs.Count == 0 ? $"{vm.Toolbox.Count}개" : "못 읽음: " + string.Join(", ", missingGlyphs));

                vm.SelectedLayer = vm.Layers.First(l => l.Kind == SettingsLayerKind.Solution);
                vm.IsDesign = true;
                await Pump(1200);

                vm.AddItemCommand.Execute(vm.Toolbox.First(t => t.Kind == SettingsItemKind.Text));
                await Pump(800);

                var savedForm = SettingsForm.Load(Path.Combine(Path.GetDirectoryName(project)!, SolutionSettingsFiles.FormFile));
                Expect(savedForm.Find("글자1") is not null && vm.SelectedEditor is not null, "도구 상자에서 놓은 칸이 양식 파일에 저장되고 속성 창에 뜬다", $"{string.Join(", ", savedForm.ValueItems().Select(i => i.Name))}");

                if (vm.SelectedEditor is Minguk.Tools.ViewModels.Settings.ValueItemEditor editor)
                {
                    editor.Label = "캐릭터 이름";
                    await Pump(500);
                    savedForm = SettingsForm.Load(Path.Combine(Path.GetDirectoryName(project)!, SolutionSettingsFiles.FormFile));
                    Expect(savedForm.Find("글자1")?.Label == "캐릭터 이름", "속성 창에서 고친 라벨이 저장된다", savedForm.Find("글자1")?.Label ?? "(없음)");

                    // 프로젝트에 있는 이름으로 바꾸려 하면 막는다.
                    editor.Name = "보스";
                    await Pump(300);
                    Expect(editor.Name == "글자1" && vm.IsStatusError && vm.StatusText?.Contains("테스트런") == true, "프로젝트에 있는 이름으로는 못 바꾼다", vm.StatusText ?? "");
                }

                // 누른 자리의 칸을 고른다.
                var target = Descendants<LayoutItem>(canvas).First(i => i.Tag is SettingsItem { Name: "난이도" });
                var center = target.TransformToAncestor((Visual)canvas.Content).Transform(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                var hit = canvas.HitItem(center);
                Expect(hit?.Name == "난이도", "디자인에서 누른 자리의 칸을 고른다", hit?.Name ?? "(없음)");

                Render(window, Path.Combine(outputFolder, "minguk-settings-design.png"));

                // 속성 창 - 긴 글(목록 열)은 잘려 보이므로 누르면 넓게 펼쳐 고친다(MemoEdit 팝업).
                {
                    var restore = vm.SelectedEditor?.Item;
                    var listItem = Descendants<LayoutItem>(canvas).First(i => i.Tag is SettingsItem { Name: "물약목록" });
                    vm.SelectedFormItem = (SettingsItem)listItem.Tag; // 판을 누른 것과 같다(판이 SelectedItem 에 쓴다)
                    await Pump(500);

                    var propertyGrid = Descendants<DevExpress.Xpf.PropertyGrid.PropertyGridControl>(window).Single();
                    propertyGrid.Focus(); // 초점이 없으면 편집기가 안 켜진다(InplaceInactive)
                    propertyGrid.SelectedPropertyPath = "Columns";
                    await Pump(200);
                    propertyGrid.ShowEditor(false);
                    await Pump(400);
                    var memo = Descendants<MemoEdit>(propertyGrid).FirstOrDefault();
                    memo?.ShowPopup();
                    await Pump(400);
                    Expect(memo is { IsPopupOpen: true } && memo.EditValue is string text && text.Contains("켜기"), "속성 창: 목록 열은 넓게 펼쳐 고친다",
                        memo is null ? "편집기 없음" : $"펼침 {memo.IsPopupOpen} · {memo.EditValue}");
                    if (memo is not null) memo.IsPopupOpen = false;
                    propertyGrid.HideEditor();
                    await Pump(300);

                    // ── 목록 처음 행 ── 속성 창의 … 이 표 대화 상자를 띄운다. 대화 상자는 모달이라 화면만 따로 띄워 고치고, 확인 뒤 하는 일(SetDefaultRows)을 부른다.
                    var rowsButton = Descendants<ButtonEdit>(propertyGrid).FirstOrDefault(b => b is not MemoEdit && Equals(b.EditValue, "없음"));
                    Expect(rowsButton is not null && vm.EditListRowsCommand.CanExecute(null), "속성 창: 목록 칸에 「처음 행」 … 이 있다",
                        rowsButton is null ? "줄 없음" : $"{rowsButton.EditValue} · 명령 {vm.EditListRowsCommand.CanExecute(null)}");

                    var listEditor = (Minguk.Tools.ViewModels.Settings.ListEditor)vm.SelectedEditor!;
                    var rowsDialog = new Minguk.Tools.ViewModels.Settings.SettingsListRowsViewModel(listEditor.Item.Columns!, listEditor.DefaultRowsValue);
                    var rowsView = new SettingsListRowsView { DataContext = rowsDialog };
                    var rowsWindow = new Window { Left = -20000, Top = -20000, SizeToContent = SizeToContent.WidthAndHeight, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = rowsView };
                    rowsWindow.Show();
                    await Pump(600);

                    var rowsEditor = Descendants<SettingsListRowsEditor>(rowsView).Single();
                    rowsDialog.Rows = System.Text.Json.Nodes.JsonNode.Parse("""[{"키":"1","HP":30,"켜기":true},{"키":"2","HP":60,"켜기":false}]""")!.AsArray();
                    await Pump(200);
                    rowsEditor.ListGrid!.Grid.SetCellValue(1, "HP", 75.0);
                    await Pump(200);
                    Expect(rowsEditor.ListGrid.RowCount == 2 && rowsDialog.Rows.Count == 2 && rowsDialog.Rows[1]?["HP"]?.GetValue<long>() == 75,
                        "처음 행 대화 상자: 표를 고치면 행 값에 들어간다", rowsDialog.Rows.ToJsonString());
                    Render(rowsWindow, Path.Combine(outputFolder, "minguk-settings-list-rows.png"));
                    rowsWindow.Close();

                    listEditor.SetDefaultRows(rowsDialog.Rows);
                    await Pump(500);
                    var savedDefault = SettingsForm.Load(Path.Combine(Path.GetDirectoryName(project)!, SolutionSettingsFiles.FormFile)).Find("물약목록")?.Default;
                    Expect(savedDefault is System.Text.Json.Nodes.JsonArray { Count: 2 } && (vm.SelectedEditor as Minguk.Tools.ViewModels.Settings.ListEditor)?.DefaultRows == "2행",
                        "처음 행: 확인하면 양식 파일에 저장되고 속성 창에 「2행」", $"{savedDefault?.ToJsonString()} · {(vm.SelectedEditor as Minguk.Tools.ViewModels.Settings.ListEditor)?.DefaultRows}");

                    Render(window, Path.Combine(outputFolder, "minguk-settings-design-list.png"));

                    // 값이 없는 층에서는 처음 행이 보인다 - 솔루션 값을 빼고 미리보기에서 확인.
                    var fallback = settings.Find("물약목록");
                    settings.ResetValue("물약목록", SettingsLayerKind.Solution);
                    await Pump(300);
                    Expect(settings.Find("물약목록")?.Value is System.Text.Json.Nodes.JsonArray { Count: 2 } seen && seen[1]?["HP"]?.GetValue<long>() == 75 && settings.Find("물약목록")?.Source is null,
                        "처음 행: 값을 안 바꾼 칸은 처음 행을 읽는다", settings.Find("물약목록")?.Value?.ToJsonString() ?? "(없음)");
                    if (fallback?.Value is { } previous) settings.SetValue("물약목록", previous.DeepClone(), SettingsLayerKind.Solution);
                    await Pump(300);

                    vm.SelectedFormItem = restore;
                    await Pump(300);
                }

                // 판에 초점을 두고 Delete 키 - 지우기 명령과 같다(사용자, 2026-09-17 "Delete 키 안 먹어").
                canvas.Focus();
                await Pump(200);
                var source = PresentationSource.FromVisual(canvas)!;
                canvas.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Delete)
                    { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                await Pump(500);
                savedForm = SettingsForm.Load(Path.Combine(Path.GetDirectoryName(project)!, SolutionSettingsFiles.FormFile));
                Expect(savedForm.Find("글자1") is null, "판에서 Delete 를 누르면 고른 칸이 양식에서 빠진다", string.Join(", ", savedForm.ValueItems().Select(i => i.Name)));

                // ── 도구 상자에서 끌어 놓기 ── 실제 끌기(DoDragDrop)는 마우스가 있어야 해서 판의 놓기(Drop)를 자리로 부른다.
                Point CenterOf(string name, double yRatio = 0.5)
                {
                    var element = Descendants<FrameworkElement>(canvas).First(e => e.Tag is SettingsItem item && (item.Name == name || item.Label == name) && e is LayoutItem or LayoutGroup);
                    return element.TransformToAncestor((Visual)canvas.Content).Transform(new Point(element.ActualWidth / 2, element.ActualHeight * yRatio));
                }

                SettingsForm Saved() => SettingsForm.Load(Path.Combine(Path.GetDirectoryName(project)!, SolutionSettingsFiles.FormFile));

                var before = canvas.DropTargetAt(SettingsItemKind.Check, CenterOf("난이도", 0.25));
                var after = canvas.DropTargetAt(SettingsItemKind.Check, CenterOf("난이도", 0.75));
                var inside = canvas.DropTargetAt(SettingsItemKind.Check, CenterOf("물약", 0.6));
                Expect(before is { Placement: SettingsDropPlacement.Before, Anchor.Name: "난이도" } && after.Placement == SettingsDropPlacement.After
                       && inside is { Placement: SettingsDropPlacement.Inside, Anchor.Label: "물약" },
                    "끌어 놓을 자리 - 칸 위 반은 앞, 아래 반은 뒤, 구역 안쪽은 그 안", $"{before.Placement}·{after.Placement}·{inside.Placement}");

                canvas.Drop(SettingsItemKind.Check, CenterOf("난이도", 0.25));
                await Pump(800);
                var order = Saved().Root.Children!.Select(c => c.Name.Length > 0 ? c.Name : c.Label).ToList();
                Expect(order.IndexOf("체크1") >= 0 && order.IndexOf("체크1") + 1 == order.IndexOf("난이도") && vm.SelectedEditor?.Item.Name == "체크1",
                    "칸 위에 놓으면 그 앞에 들어가고 고른 칸이 된다", string.Join(", ", order));

                canvas.Drop(SettingsItemKind.Number, CenterOf("물약", 0.6));
                await Pump(800);
                var potionChildren = Saved().Root.Children!.First(c => c.Label == "물약").Children!.Select(c => c.Name).ToList();
                Expect(potionChildren.LastOrDefault() == "숫자1", "구역 안에 놓으면 그 구역 끝에 들어간다", string.Join(", ", potionChildren));

                var layoutRoot = (FrameworkElement)canvas.Content;
                canvas.Drop(SettingsItemKind.Slider, new Point(layoutRoot.ActualWidth - 3, layoutRoot.ActualHeight - 3));
                await Pump(800);
                Expect(Saved().Root.Children!.Last().Name == "슬라이더1", "빈 자리에 놓으면 맨 끝에 들어간다", string.Join(", ", Saved().Root.Children!.Select(c => c.Name)));

                // ── 나누기 ── 세로 구역 「물약」 의 「물약HP」 뒤에 놓으면 저장되고, 미리보기에서 물약HP 에 상하 크기 조절이 켜진다(나누기 자체는 안 그린다).
                {
                    // 판에 그려진 칸(고치는 양식 그 객체) - 파일에서 새로 읽은 객체로는 화면 모델이 자리를 못 찾는다.
                    var potionHp = (SettingsItem)Descendants<LayoutItem>(canvas).First(i => i.Tag is SettingsItem { Name: "물약HP" }).Tag;
                    vm.SelectedFormItem = potionHp;
                    await Pump(300);
                    vm.AddItemCommand.Execute(vm.Toolbox.First(t => t.Kind == SettingsItemKind.Splitter));
                    await Pump(800);

                    var potionKids = Saved().Root.Children!.First(c => c.Label == "물약").Children!.Select(c => c.Kind.ToString()).ToList();
                    Expect(potionKids.Count >= 2 && potionKids[1] == nameof(SettingsItemKind.Splitter), "나누기를 칸 뒤에 놓으면 양식에 저장된다", string.Join(", ", potionKids));

                    vm.IsDesign = false;
                    await Pump(800);

                    var hpItem = Descendants<LayoutItem>(canvas).FirstOrDefault(i => i.Tag is SettingsItem { Name: "물약HP" });
                    var drawnSplitter = Descendants<LayoutItem>(canvas).Any(i => i.Tag is SettingsItem { Kind: SettingsItemKind.Splitter });
                    var sizing = hpItem is not null && LayoutControl.GetAllowVerticalSizing(hpItem) && LayoutControl.GetAllowHorizontalSizing(hpItem);
                    Expect(sizing && !drawnSplitter, "미리보기: 나누기 앞 칸에 상하·좌우 크기 조절이 둘 다 켜지고(세로 구역이어도), 나누기 자체는 안 그린다", $"상하·좌우 조절 {sizing} · 나누기 그림 {drawnSplitter}");

                    // 막대를 끈 것처럼 - LayoutControl 이 끄는 동안 칸 Height 를 고치고, 손을 떼면 판이 알린다. 양식에 저장되고 다시 그려도 그 크기다.
                    if (hpItem is not null)
                    {
                        hpItem.Height = 120;
                        hpItem.Width = 260;
                        ((UIElement)canvas.Content).RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                        await Pump(500);

                        var savedHeight = Saved().Find("물약HP")?.Height;
                        var savedWidth = Saved().Find("물약HP")?.Width;

                        vm.IsDesign = true;
                        await Pump(800);
                        vm.IsDesign = false;
                        await Pump(800);

                        var redrawnItem = Descendants<LayoutItem>(canvas).FirstOrDefault(i => i.Tag is SettingsItem { Name: "물약HP" });
                        Expect(savedHeight == 120 && savedWidth == 260 && redrawnItem is { Height: 120, Width: 260 },
                            "나누기로 끌어 바꾼 높이·너비가 양식에 저장되고, 다시 그려도 그 크기다", $"저장 {savedWidth}x{savedHeight} · 다시 그림 {redrawnItem?.Width}x{redrawnItem?.Height}");
                    }

                    vm.IsDesign = true;
                    await Pump(800);
                }

                vm.IsDesign = false;
                await Pump(500);

                // ── 플레이 설정 값 창 ── 플레이 화면 [설정] 과 같이 WindowService 로 띄운다(값만, 완성품의 프로젝트 폴더).
                {
                    var host = new System.Windows.Controls.Grid();
                    var windows = new DevExpress.Mvvm.UI.WindowService
                    {
                        ViewTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PlaySettingsView)) },
                        WindowShowMode = DevExpress.Mvvm.UI.WindowShowMode.Default
                    };
                    DevExpress.Mvvm.UI.Interactivity.Interaction.GetBehaviors(host).Add(windows);
                    var hostWindow = new Window { Width = 200, Height = 100, Left = -3000, Top = -3000, ShowInTaskbar = false, Content = host };
                    hostWindow.Show();
                    await Pump(300);

                    var playVm = SolutionSettingsViewModel.CreateForPlay(project);
                    ((DevExpress.Mvvm.IWindowService)windows).Title = playVm.Caption;
                    DevExpress.Mvvm.WindowServiceExtensions.Show(windows, playVm);
                    await Pump(1200);

                    var playWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Content is PlaySettingsView || Descendants<PlaySettingsView>(w).Any());
                    var playCanvas = playWindow is null ? null : Descendants<SettingsFormCanvas>(playWindow).SingleOrDefault();
                    var playNames = playCanvas is null ? [] : Descendants<LayoutItem>(playCanvas).Where(i => i.Tag is SettingsItem).Select(i => ((SettingsItem)i.Tag).Name).ToList();
                    Expect(playWindow is not null && playNames.Contains("물약HP") && playNames.Contains("보스") && playWindow.Title == "설정 - 설정검사 / 테스트런"
                           && !Descendants<DevExpress.Xpf.PropertyGrid.PropertyGridControl>(playWindow).Any(),
                        "플레이 설정 창: 값만 보이는 판이 완성품 프로젝트의 칸을 그린다(디자인·속성 없음)",
                        $"창 {playWindow?.Title ?? "없음"} · 칸 {string.Join(", ", playNames)}");

                    if (playCanvas is not null && playWindow is not null)
                    {
                        var playItems = Descendants<LayoutItem>(playCanvas).Where(i => i.Tag is SettingsItem).ToList();
                        Descendants<SpinEdit>(ItemOf(playItems, "물약HP")).Single().EditValue = 55m;
                        await Pump(300);
                        Expect(Num(settings.Find("물약HP")!) == 55, "플레이 설정 창: 값을 바꾸면 스크립트가 읽는 값이 바뀐다", settings.Find("물약HP")!.Value?.ToJsonString() ?? "(없음)");

                        await System.Threading.Tasks.Task.Run(() => SolutionSettings.ForProject(project).SetValue("자동줍기", JsonValue.Create(true)));
                        await Pump(400);
                        var playCheck = Descendants<CheckEdit>(ItemOf(playItems, "자동줍기")).Single();
                        Expect(playCheck.IsChecked == true, "플레이 설정 창: 스크립트가 쓴 값이 곧바로 보인다", $"체크 {playCheck.IsChecked}");

                        // 창 자리·크기를 옮기면 화면 모델에 적힌다(닫을 때 저장, 다음에 그 자리로 뜬다).
                        playWindow.Left = -21000; playWindow.Top = -21000; playWindow.Width = 520; playWindow.Height = 640;
                        await Pump(300);
                        Expect(playVm.WindowBounds == "-21000,-21000,520,640", "플레이 설정 창: 창을 옮기고 키우면 자리·크기가 적힌다", playVm.WindowBounds ?? "(없음)");

                        playWindow.Close();
                        await Pump(300);
                        Expect(!((DevExpress.Mvvm.IWindowService)windows).IsWindowAlive, "플레이 설정 창: 닫으면 창이 사라진다", $"살아 있음 {((DevExpress.Mvvm.IWindowService)windows).IsWindowAlive}");
                    }

                    hostWindow.Close();
                }

                Console.WriteLine($"[INFO] 화면을 찍었다: {Path.Combine(outputFolder, "minguk-settings-preview.png")} · {Path.Combine(outputFolder, "minguk-settings-design.png")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 설정 화면 - {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                failures++;
            }
            finally
            {
                try { window?.Close(); } catch (Exception) { }

                SettingsLayer.ResetCache();

                try { Directory.Delete(root, true); } catch (Exception) { }

                app.Shutdown();
            }
        });

        app.Run();

        Console.WriteLine(failures == 0 ? "설정 화면 검사 통과" : $"설정 화면 검사 실패 {failures}건");

        return failures;
    }

    /// <summary>솔루션 「설정검사」 + 프로젝트 「테스트런」. 공통에 칸 종류 전부, 프로젝트에 하나. 프로젝트 폴더를 돌려준다.</summary>
    private static string MakeSolution(string root)
    {
        var solution = Solution.Create(root, "설정검사");
        var folder = Path.Combine(solution.Directory, "테스트런");

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "테스트런.mtsproj"), "{}");
        solution.Projects.Add(new SolutionProjectEntry { Path = "테스트런/테스트런.mtsproj" });
        solution.Save();

        var form = new SettingsForm();
        var potion = new SettingsItem { Kind = SettingsItemKind.Group, Label = "물약", Orientation = SettingsOrientation.Vertical, Children = [] };
        potion.Children.Add(new SettingsItem { Kind = SettingsItemKind.Number, Name = "물약HP", Label = "먹을 HP(%)", Default = 40, Min = 0, Max = 100 });
        potion.Children.Add(new SettingsItem { Kind = SettingsItemKind.Check, Name = "자동줍기", Label = "자동 줍기", Default = true });
        form.Root.Children!.Add(potion);
        form.Root.Children.Add(new SettingsItem { Kind = SettingsItemKind.Combo, Name = "난이도", Items = ["보통", "악몽", "지옥"], Default = "지옥" });
        form.Root.Children.Add(new SettingsItem { Kind = SettingsItemKind.Slider, Name = "속도", Min = 1, Max = 10, Default = 5 });
        form.Root.Children.Add(new SettingsItem { Kind = SettingsItemKind.Text, Name = "보스이름", Label = "보스 이름", Default = "안다리엘" });
        form.Root.Children.Add(new SettingsItem
        {
            Kind = SettingsItemKind.List, Name = "물약목록", Label = "물약 목록",
            Columns = [new() { Name = "키" }, new() { Name = "HP", Kind = SettingsItemKind.Number }, new() { Name = "켜기", Kind = SettingsItemKind.Check }]
        });

        SettingsLayer.ResetCache();

        var settings = SolutionSettings.ForProject(folder);
        settings.Solution!.SaveForm(form);

        var projectForm = new SettingsForm();
        projectForm.Root.Children!.Add(new SettingsItem { Kind = SettingsItemKind.Text, Name = "보스", Default = "안다리엘" });
        settings.Project.SaveForm(projectForm);

        settings.SetValue("물약목록", SettingsValue.FromObject(new[]
        {
            new Dictionary<string, object?> { ["키"] = "1", ["HP"] = 30, ["켜기"] = true },
            new Dictionary<string, object?> { ["키"] = "2", ["HP"] = 60, ["켜기"] = false }
        }), SettingsLayerKind.Solution);

        return folder;
    }

    private static LayoutItem ItemOf(IEnumerable<LayoutItem> items, string name) => items.First(i => i.Tag is SettingsItem item && item.Name == name);

    private static double Num(SettingsEntry entry) => entry.Value is JsonValue v ? SettingsValue.ToDouble(v) : double.NaN;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var inner in Descendants<T>(child)) yield return inner;
        }
    }

    private static async System.Threading.Tasks.Task Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < milliseconds)
            await Dispatcher.Yield(DispatcherPriority.Background);
    }

    private static void Render(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
        }

        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class CollectingListener(List<string> errors) : TraceListener
    {
        private string _pending = string.Empty;

        public override void Write(string? message) => _pending += message;

        public override void WriteLine(string? message)
        {
            errors.Add(_pending + message);
            _pending = string.Empty;
        }
    }
}
