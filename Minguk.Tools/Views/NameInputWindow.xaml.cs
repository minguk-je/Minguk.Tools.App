using System;
using System.Windows;

using Minguk.Tools.ViewModels;

namespace Minguk.Tools.Views;

/// <summary>이름 한 칸을 받는 작은 창.</summary>
public partial class NameInputWindow : DevExpress.Xpf.Core.ThemedWindow
{
    public NameInputWindow()
    {
        InitializeComponent();

        // 열리자마자 바로 칠 수 있게 칸에 포커스를 둔다.
        Loaded += (_, _) => NameEdit.Focus();
    }

    /// <summary>
    /// 이름을 묻는다. 그만두면 null.
    /// </summary>
    /// <param name="title">창 제목.</param>
    /// <param name="message">칸 위의 안내.</param>
    /// <param name="validate">이미 쓰는 이름 등 부르는 쪽만 아는 검사. 문제가 있으면 그 글을, 없으면 null.</param>
    public static string? Ask(string title, string message, Func<string, string?>? validate = null)
    {
        var window = new NameInputWindow { Title = title };

        if (Application.Current?.MainWindow is { IsVisible: true } owner) window.Owner = owner;

        if (window.DataContext is not NameInputWindowViewModel vm) return null;

        vm.Message = message;
        vm.Validate = validate;

        window.ShowDialog();

        return vm.Result;
    }
}
