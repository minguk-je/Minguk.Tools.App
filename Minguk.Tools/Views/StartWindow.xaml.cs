using Minguk.Tools.Projects;
using Minguk.Tools.ViewModels;

namespace Minguk.Tools.Views;

/// <summary>
/// 시작 창. 앱이 뜨기 전에 한 번 띄우고, 고른 솔루션을 <see cref="Chosen"/> 로 돌려준다.
/// </summary>
/// <remarks>
/// 대화 상자로 띄운다(<c>ShowDialog</c>). 여기서 그만두면 <see cref="Chosen"/> 가 null 이고 앱은 닫힌다 -
/// 솔루션 없이 열리는 화면이 없다.
/// </remarks>
public partial class StartWindow : DevExpress.Xpf.Core.ThemedWindow
{
    public StartWindow()
    {
        InitializeComponent();
    }

    /// <summary>만들기 칸을 펴 둔 채 연다(Automation 화면의 "새 솔루션").</summary>
    public static StartWindow ForCreate()
    {
        var window = new StartWindow();

        if (window.DataContext is StartWindowViewModel vm) vm.StartInCreateMode = true;

        return window;
    }

    /// <summary>사람이 고른 솔루션. 그만뒀으면 null.</summary>
    public Solution? Chosen => (DataContext as StartWindowViewModel)?.Result;
}
