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

                // 이 프로젝트로 바꿔 덮어쓰기 - 라벨이 굵어지고 ↺ 가 보인다.
                vm.SelectedLayer = vm.Layers.First(l => l.Kind == SettingsLayerKind.Project);
                await Pump(800);
                items = [.. Descendants<LayoutItem>(canvas).Where(i => i.Tag is SettingsItem)];
                Descendants<SpinEdit>(ItemOf(items, "물약HP")).Single().EditValue = 12m;
                await Pump(300);
                var overridden = ItemOf(items, "물약HP");
                var resetVisible = Descendants<SimpleButton>(overridden).Any(b => b.IsVisible);
                Expect(settings.Find("물약HP")!.IsProjectValue && overridden.LabelStyle is not null && resetVisible,
                    "이 프로젝트에서 바꾸면 덮어쓰기 - 라벨이 굵고 ↺ 가 보인다", $"프로젝트 값 {settings.Find("물약HP")!.IsProjectValue} · 굵게 {overridden.LabelStyle is not null} · ↺ {resetVisible}");

                Descendants<SimpleButton>(overridden).First(b => b.IsVisible).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await Pump(300);
                Expect(Num(settings.Find("물약HP")!) == 77 && !Descendants<SimpleButton>(overridden).Any(b => b.IsVisible), "↺ 를 누르면 솔루션 값으로 돌아간다", $"{settings.Find("물약HP")!.Value?.ToJsonString()}");

                // ── 스크립트(다른 스레드)가 쓴 값이 편집기에 보인다 ──
                await System.Threading.Tasks.Task.Run(() => SolutionSettings.ForProject(project).SetValue("자동줍기", JsonValue.Create(false)));
                await Pump(400);
                var check = Descendants<CheckEdit>(ItemOf(items, "자동줍기")).Single();
                Expect(check.IsChecked == false, "스크립트가 다른 스레드에서 쓴 값이 편집기에 곧바로 보인다", $"체크 {check.IsChecked}");

                // 목록 칸 - 행이 그려진다.
                var grid = Descendants<DevExpress.Xpf.Grid.GridControl>(ItemOf(items, "물약목록")).Single();
                var dataRows = grid.ItemsSource is System.Data.DataView table ? table.Count : -1;
                Expect(dataRows == 2 && grid.Columns.Count == 3, "목록 칸이 열·행을 그린다(맨 아래는 새 행 줄)", $"열 {grid.Columns.Count} · 행 {dataRows}");

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

                vm.DeleteItemCommand.Execute(null);
                await Pump(500);
                savedForm = SettingsForm.Load(Path.Combine(Path.GetDirectoryName(project)!, SolutionSettingsFiles.FormFile));
                Expect(savedForm.Find("글자1") is null, "고른 칸을 지우면 양식에서 빠진다", string.Join(", ", savedForm.ValueItems().Select(i => i.Name)));

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

                vm.IsDesign = false;
                await Pump(500);

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
