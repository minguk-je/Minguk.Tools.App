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

    public static void Initialize()
    {
        try
        {
            // 처음 실행이면 기본값을 심는다.
            if (AppSettingUtility.Exists("Config.AppSettings") == false)
                AppSettingUtility.Set("Config.AppSettings", "Production");

            // 테마
            string themeName = AppSettingUtility.Get("Config.ThemeName", "Win11System");
            DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName = themeName;
            DevExpress.Xpf.Core.ApplicationThemeHelper.UpdateApplicationThemeName();

            // 폰트
            string fontFamilyName = AppSettingUtility.Get("Config.FontFamily", "D2Coding");
            FontFamily fontFamily = new FontFamily(fontFamilyName);

            double fontSize = Convert.ToDouble(AppSettingUtility.Get("Config.FontSize", "12"));
            if (fontSize < 10)
                fontSize = 10;

            // App.xaml 의 리소스를 갈아 끼운다. XAML 은 DynamicResource 로 참조해야 반영된다.
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

    public static void LoadUserPreferences()
    {
        try
        {
            var userPrefs = new UserPreferences();
            if (Application.Current?.MainWindow is { } window)
            {
                window.Height = userPrefs.WindowHeight;
                window.Width = userPrefs.WindowWidth;
                window.Top = userPrefs.WindowTop;
                window.Left = userPrefs.WindowLeft;
                window.WindowState = userPrefs.WindowState;
            }
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
