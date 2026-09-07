using Minguk.Tools.Helper;

namespace Minguk.Tools.Views;

public partial class MainView : System.Windows.Controls.UserControl
{
    public MainView()
    {
        StartupTrace.Mark("    MainView 인스턴스화 시작");

        InitializeComponent();

        StartupTrace.Mark("    MainView.InitializeComponent (아코디언 + 도킹)");
    }
}
