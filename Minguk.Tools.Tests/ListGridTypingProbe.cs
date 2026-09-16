using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;

using Minguk.Tools.Helper;
using Minguk.Tools.Markup.Settings;
using Minguk.Tools.Projects.Settings;
using Minguk.Tools.Views;

namespace Minguk.Tools.Tests;

/// <summary>
/// 설정 목록 칸 표에 <b>실제 키보드</b>로 새 행을 적는다 - <c>--list-typing</c>. 커서·키보드를 몇 초 가져간다.
/// </summary>
internal static class ListGridTypingProbe
{
    private static int _failures;

    public static int Run()
    {
        CompatibilitySettings.UseLightweightThemes = true;
        LightweightThemeManager.AllowStandardControlsTheming = false;
        DataControlBase.AllowInfiniteGridSize = true;
        UserPreferencesHelper.EnsureDefaults();
        UserPreferencesHelper.ApplyTheme();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            GetCursorPos(out var original);

            try
            {
                await BareGridAsync(rows: null);
                await BareGridAsync(rows: JsonNode.Parse("""[{"키":"1","HP":30,"켜기":true}]""")!.AsArray());
                await DialogAsync();
                await DialogServiceAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] 목록 표 입력 — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                _failures++;
            }
            finally
            {
                SetCursorPos(original.X, original.Y);
            }

            app.Shutdown();
        });

        app.Run();

        Console.WriteLine(_failures == 0 ? "== 목록 표 입력 통과 ==" : $"== 목록 표 입력 실패 {_failures}건 ==");
        return _failures == 0 ? 0 : 1;
    }

    private static IReadOnlyList<SettingsColumn> Columns() =>
    [
        new SettingsColumn { Name = "키", Kind = SettingsItemKind.Text },
        new SettingsColumn { Name = "HP", Kind = SettingsItemKind.Number },
        new SettingsColumn { Name = "켜기", Kind = SettingsItemKind.Check }
    ];

    private static async Task BareGridAsync(JsonArray? rows)
    {
        var changes = 0;
        var list = new SettingsListGrid(Columns(), editable: true, () => changes++);
        list.Push(rows);

        var window = Host(list.Grid);
        await Task.Delay(1200);
        window.Activate();

        var before = list.RowCount;
        await TypeNewRowAsync(list.Grid, "ab", "42");

        Check($"맨 표(행 {before}개에서): 새 행 줄에 적으면 행이 는다", list.RowCount == before + 1,
            $"행 {list.RowCount} · 바뀜 {changes} · {list.Read().ToJsonString()}");

        window.Close();
    }

    private static async Task DialogAsync()
    {
        var vm = new Minguk.Tools.ViewModels.Settings.SettingsListRowsViewModel(Columns(), []);
        var view = new SettingsListRowsView { DataContext = vm };
        var window = Host(view);
        await Task.Delay(800);

        var grid = Descendants<GridControl>(view).Single();
        await TypeNewRowAsync(grid, "ab", "42");

        Check("처음 행 대화 상자(빈 표): 새 행 줄에 적으면 행이 는다", vm.Rows.Count == 1,
            vm.Rows.ToJsonString());

        await TypeNewRowAsync(grid, "cd", "7");

        Check("처음 행 대화 상자: 두 번째 행도 적힌다", vm.Rows.Count == 2, vm.Rows.ToJsonString());

        window.Close();
    }

    /// <summary>앱처럼 DialogService 창(확인 = Enter 기본)으로 띄운다 - 표의 Enter 가 대화 상자를 닫지 않는지.</summary>
    private static async Task DialogServiceAsync()
    {
        var host = new System.Windows.Controls.Grid();
        var service = new DialogService { ViewTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(SettingsListRowsView)) } };
        DevExpress.Mvvm.UI.Interactivity.Interaction.GetBehaviors(host).Add(service);
        var owner = Host(host);
        await Task.Delay(800);

        var vm = new Minguk.Tools.ViewModels.Settings.SettingsListRowsViewModel(Columns(), []);
        var ok = new DevExpress.Mvvm.UICommand { Caption = "확인", IsDefault = true, Id = DevExpress.Mvvm.MessageResult.OK };
        var cancel = new DevExpress.Mvvm.UICommand { Caption = "취소", IsCancel = true, Id = DevExpress.Mvvm.MessageResult.Cancel };
        var closedEarly = false;
        var typed = false;

        _ = owner.Dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            await Task.Delay(1200);
            var dialog = Application.Current.Windows.OfType<Window>().LastOrDefault(w => w != owner && w.IsVisible);
            if (dialog is null) { closedEarly = true; return; }
            dialog.Activate();
            await Task.Delay(300);

            var grid = Descendants<GridControl>(dialog).Single();
            await TypeNewRowAsync(grid, "ab", "42");
            closedEarly = !dialog.IsVisible;
            typed = true;
            if (dialog.IsVisible) dialog.Close();
        });

        var result = DevExpress.Mvvm.DialogServiceExtensions.ShowDialog(service, [ok, cancel], "처음 행", vm);
        while (!typed && !closedEarly) await Task.Delay(100);

        Check("DialogService 창: 표에서 Enter 를 쳐도 대화 상자가 안 닫히고 행이 적힌다", !closedEarly && vm.Rows.Count == 1,
            $"일찍 닫힘 {closedEarly} · 결과 {result?.Caption ?? "없음"} · {vm.Rows.ToJsonString()}");

        owner.Close();
    }

    private static Window Host(FrameworkElement content)
    {
        var window = new Window
        {
            Width = 560, Height = 380, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = content, Title = "목록 표 입력 검사"
        };
        window.Show();
        window.Activate();
        return window;
    }

    private static async Task TypeNewRowAsync(GridControl grid, string key, string hp)
    {
        var view = (TableView)grid.View;
        var column = grid.Columns["키"];
        var cell = view.GetCellElementByRowHandleAndColumn(DataControlBase.NewItemRowHandle, column) as FrameworkElement;

        if (cell is null)
        {
            Console.WriteLine("[INFO] 새 행 줄 칸을 못 찾음");
            return;
        }

        var center = cell.PointToScreen(new Point(cell.ActualWidth / 2, cell.ActualHeight / 2));
        SetCursorPos((int)center.X, (int)center.Y);
        await Task.Delay(100);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(300);

        Console.WriteLine($"[INFO] 누른 뒤: 초점 행 {view.FocusedRowHandle} · 열 {grid.CurrentColumn?.FieldName} · 편집기 {view.ActiveEditor?.GetType().Name ?? "없음"}");

        await TypeTextAsync(key);
        Console.WriteLine($"[INFO] 키 적은 뒤: 초점 행 {view.FocusedRowHandle} · 편집기 {view.ActiveEditor?.GetType().Name ?? "없음"} · 값 {view.ActiveEditor?.EditValue}");
        await KeyAsync(0x09); // Tab
        await TypeTextAsync(hp);
        Console.WriteLine($"[INFO] HP 적은 뒤: 초점 행 {view.FocusedRowHandle} · 열 {grid.CurrentColumn?.FieldName} · 편집기 {view.ActiveEditor?.GetType().Name ?? "없음"} · 값 {view.ActiveEditor?.EditValue}");
        await KeyAsync(0x0D); // Enter
        await Task.Delay(300);
        Console.WriteLine($"[INFO] Enter 뒤: 초점 행 {view.FocusedRowHandle} · 열 {grid.CurrentColumn?.FieldName} · 편집기 {view.ActiveEditor?.GetType().Name ?? "없음"} · 표 행 {grid.VisibleRowCount}");
        await KeyAsync(0x0D); // Enter
        await Task.Delay(300);
        Console.WriteLine($"[INFO] Enter 두 번 뒤: 초점 행 {view.FocusedRowHandle} · 열 {grid.CurrentColumn?.FieldName} · 편집기 {view.ActiveEditor?.GetType().Name ?? "없음"} · 표 행 {grid.VisibleRowCount}");
    }

    private static async Task TypeTextAsync(string text)
    {
        foreach (var c in text)
        {
            SendUnicode(c, up: false);
            SendUnicode(c, up: true);
            await Task.Delay(80);
        }
    }

    private static async Task KeyAsync(ushort vk)
    {
        SendVk(vk, up: false);
        SendVk(vk, up: true);
        await Task.Delay(200);
    }

    private static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] 목록 표 입력: {name} — {detail}");
        if (!ok) _failures++;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void SendUnicode(char c, bool up) => Send(new KeybdInput { wScan = c, dwFlags = 0x0004 | (up ? 0x0002u : 0) });

    private static void SendVk(ushort vk, bool up) => Send(new KeybdInput { wVk = vk, dwFlags = up ? 0x0002u : 0 });

    private static void Send(KeybdInput ki)
    {
        var input = new Input { type = 1, ki = ki };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
