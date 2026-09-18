using System;
using System.Linq;
using System.Windows;

namespace Minguk.Tools.Projects;

/// <summary>
/// 솔루션이 있어야 여는 화면 앞의 문. 없으면 시작 창을 띄우고, 사람이 고르면 연다.
/// </summary>
/// <remarks>
/// <b>왜 앱을 켤 때가 아닌가</b>(사용자 결정 2026-09-14) - Minguk Tools 는 기능 프로젝트를 여럿 담는 셸이고,
/// 솔루션은 그중 Automation(게임 자동화)의 것이다. 켤 때 무조건 시작 창을 띄웠더니 입력 테스트나 학습환경을
/// 보려 해도 솔루션부터 골라야 했다. 그래서 솔루션이 필요한 메뉴(<c>MenuItemModel.REQUIRES_SOLUTION</c>)를
/// 처음 누를 때 한 번만 묻는다. 한 번 고르면 이 실행 동안은 다시 안 묻는다.
///
/// 옛 자리에 데이터가 남아 있으면(설정에 솔루션이 없고 Datasets·captures 가 있으면) 시작 창보다 먼저 옮길지 묻는다 -
/// 사진 수백 장이 안 보이면 사람은 잃어버린 줄 안다. 옮기면 그 솔루션이 바로 열려 시작 창은 건너뛴다.
/// </remarks>
public static class SolutionGate
{
    /// <summary>솔루션이 열려 있게 한다. 사람이 그만두면 false - 화면을 열지 않는다.</summary>
    public static bool EnsureOpen()
    {
        if (SolutionWorkspace.Current is not null) return true;

        if (!TryMigrate()) return false;

        if (SolutionWorkspace.Current is not null) return true;

        if (TryOpenLast()) return true;

        var start = new Views.StartWindow();

        if (Application.Current?.MainWindow is { IsVisible: true } owner) start.Owner = owner;

        start.ShowDialog();

        return start.Chosen is not null;
    }

    /// <summary>
    /// 마지막에 열었던 솔루션이 아직 있으면 묻지 않고 그대로 연다(사용자, 2026-09-19 - 화면을 복구할 때마다 솔루션을 다시 골라야 했다).
    /// 다른 솔루션은 위 칸 콤보로 바꾼다. 파일이 없어졌거나 못 읽으면 시작 창으로 간다.
    /// </summary>
    private static bool TryOpenLast()
    {
        var last = Minguk.Base.Utilities.AppSettingUtility.Get(SolutionWorkspace.LastSolutionKey, string.Empty);

        if (string.IsNullOrWhiteSpace(last) || !System.IO.File.Exists(last)) return false;

        try
        {
            SolutionWorkspace.Open(last);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>옛 자리를 첫 솔루션으로 옮긴다. 취소면 false, 아니오면 옮기지 않고 시작 창으로 간다.</summary>
    private static bool TryMigrate()
    {
        if (!SolutionMigration.IsNeeded()) return true;

        var plan = SolutionMigration.Plan();

        var newLine = Environment.NewLine;
        var what = string.Join(newLine, plan.Steps.Select(step => "  · " + step.What));

        var message = "예전 자리에 있던 것을 솔루션 하나로 옮깁니다." + newLine + newLine
                      + what + newLine + newLine
                      + "→ " + plan.ProjectDirectory + newLine + newLine
                      + "옮길까요?";

        var answer = DevExpress.Xpf.Core.DXMessageBox.Show(
            message, "솔루션", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel) return false;
        if (answer != MessageBoxResult.Yes) return true;

        SolutionMigration.Apply(plan);

        return true;
    }
}
