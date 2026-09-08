using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using DevExpress.Xpf.Core;

using Minguk.Base.Extension;
using Minguk.Base.Utilities;
using Minguk.Base.Views;

using Minguk.Tools.Helper;

using Newtonsoft.Json;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 설정 대화상자. 문서 탭이 아니라 <see cref="IDialogService"/> 로 띄우는 창이라
/// DocumentViewModelBase 를 쓰지 않는다.
///
/// 값은 화면에서 바로 반영하지 않는다. "확인" 을 눌러야 저장하고, "취소" 면 아무것도 안 한다.
/// 폰트는 저장하는 즉시 앱 전체에 적용된다(App 리소스를 갈아 끼우므로).
/// </summary>
public class ConfigViewModel : ViewModelBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    protected ICurrentDialogService CurrentDialogService => this.GetService<ICurrentDialogService>();
    protected IMessageBoxService MessageBoxService => this.GetService<IMessageBoxService>();

    public ICommand OnInitializedCommand { get; set; }
    public ICommand DoYesCommand { get; set; }
    public ICommand DoNoCommand { get; set; }
    public ICommand DoInitializeSettingCommand { get; set; }

    /// <summary>실행 환경. AppSettings.{Development|Production}.json 중 무엇을 읽을지 정한다.</summary>
    public string? AppSettings { get => GetProperty(() => AppSettings); set => SetProperty(() => AppSettings, value); }

    public string? FontFamily { get => GetProperty(() => FontFamily); set => SetProperty(() => FontFamily, value); }

    public double FontSize { get => GetProperty(() => FontSize); set => SetProperty(() => FontSize, value); }

    /// <summary>
    /// "초기화" 를 눌렀는지. 대화상자를 띄운 쪽이 이걸 보고 앱을 다시 시작한다.
    /// 여기서 직접 재시작하면 대화상자가 닫히기 전에 프로세스가 죽어서 지저분해진다.
    /// </summary>
    public bool IsResetRequested { get; private set; }

    public static ConfigViewModel Create() => ViewModelSource.Create(() => new ConfigViewModel());

    public ConfigViewModel()
    {
        OnInitializedCommand = new DelegateCommand(OnInitialized, false);
        DoYesCommand = new DelegateCommand(DoYes, false);
        DoNoCommand = new DelegateCommand(DoNo, false);
        DoInitializeSettingCommand = new DelegateCommand(DoInitializeSetting, false);
    }

    private void OnInitialized()
    {
        if (IsInDesignMode)
            return;

        try
        {
            Logger.Trace(string.Empty);

            AppSettings = AppSettingUtility.Get("Config.AppSettings", "Production");
            FontFamily = AppSettingUtility.Get("Config.FontFamily", "D2Coding");
            FontSize = Convert.ToDouble(AppSettingUtility.Get("Config.FontSize", "12"));
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    /// <summary>확인. 바뀐 값만 저장하고, 폰트는 그 자리에서 앱에 반영한다.</summary>
    private void DoYes()
    {
        try
        {
            Logger.Trace(string.Empty);

            // 실행 환경 — 다음 실행부터 적용된다. 지금 바꿔 봐야 이미 읽어 들인 뒤다.
            if (AppSettingUtility.Get("Config.AppSettings", "Production") != AppSettings)
                AppSettingUtility.Set("Config.AppSettings", AppSettings);

            // 글꼴 — App 리소스를 갈아 끼우므로 즉시 반영된다.
            if (AppSettingUtility.Get("Config.FontFamily", "D2Coding") != FontFamily)
            {
                AppSettingUtility.Set("Config.FontFamily", FontFamily);
                UserPreferencesHelper.ChangeFontFamily();
            }

            var savedFontSize = Convert.ToDouble(AppSettingUtility.Get("Config.FontSize", "12"));
            if (Math.Abs(savedFontSize - FontSize) > 0.01)
            {
                AppSettingUtility.Set("Config.FontSize", FontSize);
                UserPreferencesHelper.ChangeFontSize();
            }

            CurrentDialogService?.Close(MessageResult.Yes);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void DoNo()
    {
        try
        {
            Logger.Trace(string.Empty);

            CurrentDialogService?.Close(MessageResult.No);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    /// <summary>
    /// 설정 초기화. 저장된 값을 전부 지우고 기본값으로 되돌린 뒤 재시작을 요청한다.
    ///
    /// 지우는 대상은 AppSettingUtility 가 관리하는 것(%AppData% 의 JSON)이다.
    /// 창 위치·크기와 탭 목록은 Properties.Settings 에 따로 있어서 여기서 같이 지운다 —
    /// 안 그러면 "초기화했는데 창은 그대로" 가 되어 초기화한 것 같지 않다.
    /// </summary>
    private void DoInitializeSetting()
    {
        try
        {
            Logger.Trace(string.Empty);

            var result = MessageBoxService.ShowMessage(
                "저장된 설정을 모두 지우고 기본값으로 되돌립니다.\n창 위치와 열려 있던 탭도 초기화됩니다.\n\n계속할까요?",
                "설정 초기화",
                MessageButton.YesNo,
                MessageIcon.Warning,
                MessageResult.No);

            if (result != MessageResult.Yes)
                return;

            AppSettingUtility.Clear();

            Properties.Settings.Default.Reset();
            Properties.Settings.Default.Save();

            ApplicationThemeHelper.ApplicationThemeName = "Win11System";

            IsResetRequested = true;

            Logger.Info("설정을 초기화했다. 재시작을 요청한다.");

            CurrentDialogService?.Close(MessageResult.Yes);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }
}
