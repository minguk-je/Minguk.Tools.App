using System.IO;
using System.Text;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Core.Native;

using Minguk.Base.Enums;

using System.Windows;
using System.Windows.Media;
using Minguk.Base.SplashScreen;
using Size = System.Windows.Size;

namespace Minguk.Base;

public class Utility
{
    public static double PercentOfMaximum(double current, double maximum)
    {
        return (current / maximum * 100);
    }

    public static double CurrentOfMaximum(double maximum, double percent)
    {
        return (maximum * percent / 100);
    }

    public static double Percent(double current, double maximum)
    {
        return (current / maximum * 100);
    }


    public static Visibility IsDebugVisible
    {
#if DEBUG
        get { return Visibility.Visible; }
#else
        get { return Visibility.Collapsed; }
#endif
    }

    public static bool IsDebug
    {
#if DEBUG
        get { return true; }
#else
        get { return false; }
#endif
    }

    public static bool IsRelease
    {
#if DEBUG
        get { return false; }
#else
        get { return true; }
#endif
    }

    public static SplashScreenManager CreateWaitIndicator(SplashMessageTypes splashMessageType, string state = "")
    {
        string status = splashMessageType == SplashMessageTypes.Custom ? state : GetSplashScreenMessage(splashMessageType);
        return SplashScreenManager.CreateWaitIndicator(new DXSplashScreenViewModel { IsIndeterminate = true, Status = status }, true);

        /*
        SplashScreenManager splashScreenManager = SplashScreenManager.CreateWaitIndicator();
        splashScreenManager.ViewModel.Status = status;
        splashScreenManager.ViewModel.IsIndeterminate = true;
        return splashScreenManager;
        */
    }

    public static string GetSplashScreenMessage(SplashMessageTypes splashMessageTypes)
    {
        string result = string.Empty;

        if (SplashMessageTypes.Loading == splashMessageTypes)
        {
            result = "불러오는 중 입니다";
        }
        else if (SplashMessageTypes.Processing == splashMessageTypes)
        {
            result = "처리 중 입니다";
        }
        else if (SplashMessageTypes.Stopping == splashMessageTypes)
        {
            result = "중지하는 중 입니다";
        }
        else if (SplashMessageTypes.Saveing == splashMessageTypes)
        {
            result = "저장 중 입니다";
        }
        else if (SplashMessageTypes.Analysis == splashMessageTypes)
        {
            result = "분석 중 입니다";
        }
        else if (SplashMessageTypes.Connecting == splashMessageTypes)
        {
            result = "연결 중 입니다";
        }
        else if (SplashMessageTypes.Disconnecting == splashMessageTypes)
        {
            result = "연결 해제 중 입니다";
        }
        else if (SplashMessageTypes.Waiting == splashMessageTypes)
        {
            result = "잠시만 기다려 주세요";
        }

        return result;
    }

    public static SplashScreenManager? ActiveSplashScreenManager;

    public static SplashScreenManager ShowWaitSplashScreen(SplashMessageTypes splashMessageTypes, string customMessage = "")
    {
        if (ActiveSplashScreenManager == null)
        {
            ActiveSplashScreenManager = SplashScreenManager.CreateWaitIndicator(new DXSplashScreenViewModel());
            ActiveSplashScreenManager.ViewModel.Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
            ActiveSplashScreenManager.Show(System.Windows.Application.Current.MainWindow);
        }
        else
        {
            ActiveSplashScreenManager.ViewModel.Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
        }

        return ActiveSplashScreenManager;
    }


    public static SplashScreenManager ShowProgressSplashScreen(SplashMessageTypes splashMessageTypes, string customMessage = "")
    {
        if (ActiveSplashScreenManager == null)
        {
            ActiveSplashScreenManager = SplashScreenManager.Create(() => new ProgressSplashScreenWindow(), new DXSplashScreenViewModel() { IsIndeterminate = false });
            ActiveSplashScreenManager.ViewModel.Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
            ActiveSplashScreenManager.Show(System.Windows.Application.Current.MainWindow);
        }
        else
        {
            ActiveSplashScreenManager.ViewModel.Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
        }

        return ActiveSplashScreenManager;
    }


    public static void UpdateProgressSplashScreen(double progress, SplashMessageTypes splashMessageTypes, string customMessage = "")
    {
        if (ActiveSplashScreenManager == null)
        {
            ShowProgressSplashScreen(splashMessageTypes, customMessage);
        }

        if (ActiveSplashScreenManager != null)
        {
            ActiveSplashScreenManager.ViewModel.Progress = progress;
            ActiveSplashScreenManager.ViewModel.Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
        }
    }


    public static void CloseSplashScreen()
    {
        ActiveSplashScreenManager?.Close();
        ActiveSplashScreenManager = null;
    }


    public static SplashScreenManager? ActiveRootSplashScreenManager;

    public static SplashScreenManager ShowRootWaitSplashScreen(SplashMessageTypes splashMessageTypes, string customMessage = "")
    {
        if (ActiveRootSplashScreenManager == null)
        {
            ActiveRootSplashScreenManager = SplashScreenManager.CreateWaitIndicator(new DXSplashScreenViewModel());
            ActiveRootSplashScreenManager.ViewModel.Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
            ActiveRootSplashScreenManager.Show(System.Windows.Application.Current.MainWindow);
        }
        else
        {
            ActiveRootSplashScreenManager.ViewModel.Status = SplashMessageTypes.Custom == splashMessageTypes ? customMessage : GetSplashScreenMessage(splashMessageTypes);
        }

        return ActiveRootSplashScreenManager;
    }

    public static void CloseRootSplashScreen()
    {
        ActiveRootSplashScreenManager?.Close();
        ActiveRootSplashScreenManager = null;
    }

    public static MessageBoxResult MessageBox(string message, string caption, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
    {
        if (DXSplashScreen.IsActive)
            DXSplashScreen.Close();

        return DXMessageBox.Show(message, caption, button, icon);
    }

    public static ImageSource GetConvertDevExpressSvgImageToImageSource(string path, int width = 16, int height = 16)
    {
        return WpfSvgRenderer.CreateImageSource(DXImageHelper.GetImageUri(path), null, new System.Windows.Size(width, height));
    }

    public static ImageSource GetConvertSvgImageToImageSource(string svgXmlString, int width = 16, int height = 16)
    {
        ImageSource result;
        var array = Encoding.UTF8.GetBytes(svgXmlString);
        using (var stream = new MemoryStream(array))
        {
            object? unused = null;
            var image = SvgImageHelper.GetOrCreateSvgImage(stream, ref unused);
            result = WpfSvgRenderer.CreateImageSource(image, new Size(width, height));
        }
        return result;
    }
}
