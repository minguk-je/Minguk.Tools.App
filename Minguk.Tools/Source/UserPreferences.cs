using System;
using System.Windows;

namespace Minguk.Tools.Source;

/// <summary>창 위치·크기·상태를 Properties.Settings 에 담고 꺼낸다.</summary>
public class UserPreferences
{
    public double WindowTop { get; set; }
    public double WindowLeft { get; set; }
    public double WindowHeight { get; set; }
    public double WindowWidth { get; set; }
    public WindowState WindowState { get; set; }

    public UserPreferences()
    {
        Load();
        SizeToFit();

        // 저장해 둔 모니터가 빠졌거나 배치가 바뀌면 창이 화면 밖에 뜬다.
        // 만들어만 두고 부르지 않던 보정을 여기서 건다.
        MoveIntoView();
    }

    /// <summary>저장된 크기가 현재 화면보다 크면 줄인다. 모니터 구성이 바뀐 경우를 위한 것이다.</summary>
    public void SizeToFit()
    {
        if (WindowHeight > SystemParameters.VirtualScreenHeight)
            WindowHeight = SystemParameters.VirtualScreenHeight;

        if (WindowWidth > SystemParameters.VirtualScreenWidth)
            WindowWidth = SystemParameters.VirtualScreenWidth;
    }

    /// <summary>창이 화면 밖으로 절반 이상 나가 있으면 안쪽으로 끌어온다.</summary>
    public void MoveIntoView()
    {
        if (WindowTop + WindowHeight / 2 > SystemParameters.VirtualScreenHeight)
            WindowTop = SystemParameters.VirtualScreenHeight - WindowHeight;

        if (WindowLeft + WindowWidth / 2 > SystemParameters.VirtualScreenWidth)
            WindowLeft = SystemParameters.VirtualScreenWidth - WindowWidth;

        if (WindowTop < 0) WindowTop = 0;
        if (WindowLeft < 0) WindowLeft = 0;
    }

    private void Load()
    {
        try
        {
            WindowTop = Properties.Settings.Default.WindowTop;
            WindowLeft = Properties.Settings.Default.WindowLeft;
            WindowHeight = Properties.Settings.Default.WindowHeight;
            WindowWidth = Properties.Settings.Default.WindowWidth;
            WindowState = (WindowState)Enum.Parse(typeof(WindowState), Properties.Settings.Default.WindowState);
        }
        catch
        {
            // 설정 파일이 깨졌거나 첫 실행이면 기본값으로 시작한다.
            WindowTop = 0;
            WindowLeft = 0;
            WindowHeight = 1024;
            WindowWidth = 1536;
            WindowState = WindowState.Normal;
        }
    }

    public void Save()
    {
        // 최소화 상태를 저장하면 다음 실행이 최소화로 뜬다. 저장하지 않는다.
        if (WindowState == WindowState.Minimized)
            return;

        Properties.Settings.Default.WindowTop = WindowTop;
        Properties.Settings.Default.WindowLeft = WindowLeft;
        Properties.Settings.Default.WindowHeight = WindowHeight;
        Properties.Settings.Default.WindowWidth = WindowWidth;
        Properties.Settings.Default.WindowState = WindowState.ToString();

        Properties.Settings.Default.Save();
    }
}
