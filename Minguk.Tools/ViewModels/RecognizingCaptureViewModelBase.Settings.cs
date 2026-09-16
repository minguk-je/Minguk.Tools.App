using System;
using System.IO;

using DevExpress.Mvvm;

namespace Minguk.Tools.ViewModels;

public abstract partial class RecognizingCaptureViewModelBase
{
    private DelegateCommand? _showSettingsCommand;

    /// <summary>
    /// 이 화면이 보는 프로젝트의 설정 값 창을 띄운다(스크립트·플레이 도구 줄의 [설정]) - 도는 중에도 바꾼다.
    /// </summary>
    public DelegateCommand ShowSettingsCommand => _showSettingsCommand ??= new DelegateCommand(DoShowSettings);

    /// <summary>설정 값 창. View 의 <c>SettingsWindowService</c> - 없는 화면이면 단추가 아무 일도 안 한다.</summary>
    protected IWindowService? SettingsWindowService => GetService<IWindowService>("SettingsWindowService");

    /// <summary>설정 창이 보고 있는 프로젝트 폴더.</summary>
    private string? _settingsProject;

    /// <summary>
    /// 설정 값 창을 띄운다 - <see cref="RecognitionRoot"/>(스크립트의 <c>설정()</c> 이 읽는 자리와 같다. 스크립트 화면은 고른 프로젝트, 플레이는 완성품의 프로젝트).
    /// </summary>
    /// <remarks>
    /// 문서 탭이 아니라 창이다 - 돌리는 동안에는 게임이 앞에 있어 게임 옆·다른 모니터에 두고 본다. 이미 떠 있으면 앞으로만 가져온다.
    /// 값은 앱 안 한 벌(<c>SettingsLayer.For</c>)이라 도는 스크립트가 다음 호출부터 바뀐 값을 읽고, 스크립트가 쓴 값도 창에 곧바로 보인다.
    /// </remarks>
    private void DoShowSettings() => Guard(() =>
    {
        if (SettingsWindowService is not { } windows) return;

        var project = RecognitionRoot;

        if (string.IsNullOrWhiteSpace(project) || !Directory.Exists(project))
        {
            StatusText = "설정을 볼 프로젝트가 없습니다. 솔루션에서 프로젝트를 고르거나 완성품을 먼저 고르세요.";
            return;
        }

        if (windows.IsWindowAlive && string.Equals(_settingsProject, project, StringComparison.OrdinalIgnoreCase))
        {
            windows.Restore();
            windows.Activate();
            return;
        }

        if (windows.IsWindowAlive) windows.Close();

        var settings = SolutionSettingsViewModel.CreateForPlay(project);
        _settingsProject = project;
        windows.Title = settings.Caption;
        windows.Show(settings);
    });

    /// <summary>보는 프로젝트가 바뀌었다(플레이에서 완성품을 바꿈) - 설정 창이 떠 있으면 새 프로젝트로 다시 띄운다.</summary>
    protected void FollowSettingsWindow()
    {
        if (SettingsWindowService is not { IsWindowAlive: true }) return;
        if (string.Equals(_settingsProject, RecognitionRoot, StringComparison.OrdinalIgnoreCase)) return;

        DoShowSettings();
    }

    /// <summary>화면이 닫히면 설정 창도 닫는다(창이 저장하고 구독을 푼다).</summary>
    private void CloseSettingsWindow()
    {
        if (SettingsWindowService is { IsWindowAlive: true } windows) windows.Close();
    }
}
