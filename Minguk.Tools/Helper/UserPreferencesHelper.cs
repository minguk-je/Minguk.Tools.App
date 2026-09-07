using Minguk.Base.Extension;
using Minguk.Base.Utilities;
using Minguk.Base.Views;

using Newtonsoft.Json;

using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

using Minguk.Tools.Source;

namespace Minguk.Tools.Helper;

/// <summary>
/// 테마 · 폰트 · 창 위치처럼 "사용자가 바꾸는 것"을 저장하고 되살린다.
/// 값은 AppSettingUtility(%AppData% JSON)와 Properties.Settings 두 곳에 나눠 둔다.
/// </summary>
public static class UserPreferencesHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 처음 실행일 때의 기본값을 심는다. 설정을 읽는 어떤 코드보다 먼저 불러야 한다.
    /// </summary>
    public static void EnsureDefaults()
    {
        try
        {
            if (AppSettingUtility.Exists("Config.AppSettings") == false)
                AppSettingUtility.Set("Config.AppSettings", "Production");
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }
    }

    /// <summary>
    /// 저장된 테마를 적용한다.
    ///
    /// 반드시 창이 하나라도 만들어지기 전에 부를 것. 화면이 뜬 뒤에 테마를 바꾸면
    /// DevExpress 가 테마 리소스를 통째로 다시 만들고 시각 트리를 전부 다시 그린다 —
    /// 시작할 때 눈에 띄게 버벅이던 원인이 이것이었다.
    ///
    /// 리소스 사전을 건드리지 않고 DevExpress 정적 설정만 바꾸므로
    /// Application.InitializeComponent() 보다 먼저 불러도 안전하다.
    /// </summary>
    public static void ApplyTheme()
    {
        try
        {
            string themeName = AppSettingUtility.Get("Config.ThemeName", "Win11System");

            DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName = themeName;
            DevExpress.Xpf.Core.ApplicationThemeHelper.UpdateApplicationThemeName();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }
    }

    /// <summary>
    /// 저장된 폰트를 App 리소스에 끼운다.
    ///
    /// App.xaml 이 로드된 뒤(InitializeComponent 이후), 그리고 MainWindow 가 만들어지기
    /// 전에 불러야 한다. 화면이 뜬 뒤에 바꾸면 DynamicResource 를 물고 있는 모든 요소가
    /// 다시 측정·배치된다.
    /// </summary>
    public static void ApplyFonts()
    {
        try
        {
            string fontFamilyName = AppSettingUtility.Get("Config.FontFamily", "D2Coding");
            FontFamily fontFamily = new FontFamily(fontFamilyName);

            double fontSize = Convert.ToDouble(AppSettingUtility.Get("Config.FontSize", "12"));
            if (fontSize < 10)
                fontSize = 10;

            // XAML 은 DynamicResource 로 참조해야 반영된다.
            Application.Current.Resources["BaseFontFamily"] = fontFamily;
            Application.Current.Resources["BaseFontSize"] = fontSize;
            Application.Current.Resources["EditorFontSize"] = fontSize + 1;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    /// <summary>
    /// 저장된 창 위치·크기·상태를 되살린다.
    ///
    /// 반드시 첫 렌더 전 — Window.SourceInitialized — 에 부를 것.
    /// 창이 보인 뒤에 부르면 XAML 의 기본 크기로 한 번 그려진 다음 저장된 자리로 튀어간다.
    /// 그 두 번째 배치가 도킹·아코디언 전체를 다시 계산하게 만든다.
    /// </summary>
    public static void ApplyWindowPlacement(Window window)
    {
        try
        {
            var userPrefs = new UserPreferences();

            // 첫 실행이라 저장된 값이 없으면 XAML 의 기본 크기를 그대로 둔다.
            if (userPrefs.WindowWidth <= 0 || userPrefs.WindowHeight <= 0)
                return;

            window.WindowStartupLocation = WindowStartupLocation.Manual;

            window.Left = userPrefs.WindowLeft;
            window.Top = userPrefs.WindowTop;
            window.Width = userPrefs.WindowWidth;
            window.Height = userPrefs.WindowHeight;

            // 최대화는 위치·크기를 먼저 넣은 뒤에 건다. 그래야 복원 크기가 살아 있다.
            if (userPrefs.WindowState == WindowState.Maximized)
                window.WindowState = WindowState.Maximized;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public static void SaveUserPreferences()
    {
        try
        {
            var userPrefs = new UserPreferences();
            if (Application.Current?.MainWindow is { } window)
            {
                userPrefs.WindowLeft = window.Left;
                userPrefs.WindowTop = window.Top;
                userPrefs.WindowWidth = window.Width;
                userPrefs.WindowHeight = window.Height;
                userPrefs.WindowState = window.WindowState;
                userPrefs.Save();
            }

            SaveThemeName();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public static void SaveThemeName()
    {
        try
        {
            AppSettingUtility.Set("Config.ThemeName", DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }
    }

    public static void ChangeFontFamily()
    {
        try
        {
            string fontFamilyName = AppSettingUtility.Get("Config.FontFamily", "D2Coding");
            Application.Current.Resources["BaseFontFamily"] = new FontFamily(fontFamilyName);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public static void ChangeFontSize()
    {
        try
        {
            double fontSize = Convert.ToDouble(AppSettingUtility.Get("Config.FontSize", "12"));
            Application.Current.Resources["BaseFontSize"] = fontSize;
            Application.Current.Resources["EditorFontSize"] = fontSize + 1;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }
}
