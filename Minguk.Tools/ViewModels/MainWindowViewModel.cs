using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using Minguk.Tools.Helper;
using Newtonsoft.Json;
using Minguk.Base;
using Minguk.Base.Enums;
using Minguk.Base.Extension;
using Minguk.Base.Utilities;
using Minguk.Base.Views;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 창(Chrome) 담당. 캡션 영역 버튼과 창 상태(최상위·전체화면)만 다룬다.
///
/// 화면 내용은 MainViewModel 이 맡는다. 두 ViewModel 은 서로를 직접 참조하지 않고
/// MessengerUtility 로만 이야기한다 — 창 껍데기와 내용의 수명이 다르기 때문이다.
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public IDispatcherService DispatcherService => this.GetService<IDispatcherService>();

    public ICommand OnInitializedCommand { get; set; }
    public ICommand OnLoadedCommand { get; set; }
    public ICommand OnActivatedCommand { get; set; }
    public ICommand OnClosingCommand { get; set; }

    public ICommand OnMenuVisibleCommand { get; set; }
    public ICommand DoPreferencesCommand { get; set; }
    public ICommand DoRestartCommand { get; set; }
    public ICommand OnExitCommand { get; set; }
    public ICommand OnFullModeCommand { get; set; }
    public ICommand OnCloseAllCommand { get; set; }
    public ICommand DoSaveLayoutCommand { get; set; }
    public ICommand DoDeleteLayoutCommand { get; set; }

    /// <summary>창 제목. 예: "Minguk Tools v1.0.0"</summary>
    public string Title { get; } = AppVersionHelper.DisplayTitle;

    public bool IsTopMost { get => GetProperty(() => IsTopMost); set => SetProperty(() => IsTopMost, value); }
    public bool IsMenuVisible { get => GetProperty(() => IsMenuVisible); set => SetProperty(() => IsMenuVisible, value); }
    public bool IsFullMode { get => GetProperty(() => IsFullMode); set => SetProperty(() => IsFullMode, value); }

    public CompositeDisposable Disposables { get; } = new();

    public static MainWindowViewModel Create() => ViewModelSource.Create(() => new MainWindowViewModel());

    public MainWindowViewModel()
    {
        // DelegateCommand 의 두 번째 인자 false 는 "CanExecute 를 자동 평가하지 마라" 는 뜻이다.
        // 이 화면의 명령들은 항상 실행 가능하므로 불필요한 재평가를 껐다.
        OnInitializedCommand = new DelegateCommand(OnInitialized, false);
        OnLoadedCommand = new DelegateCommand<RoutedEventArgs>(OnLoaded, false);
        OnActivatedCommand = new DelegateCommand(OnActivated, false);
        OnClosingCommand = new DelegateCommand<CancelEventArgs>(OnClosing, false);

        OnMenuVisibleCommand = new DelegateCommand(OnMenuVisible, false);
        DoPreferencesCommand = new DelegateCommand(DoPreferences, false);
        DoRestartCommand = new DelegateCommand(DoRestart, false);
        OnExitCommand = new DelegateCommand(OnExit, false);
        OnFullModeCommand = new DelegateCommand(OnFullMode, false);
        OnCloseAllCommand = new DelegateCommand(OnCloseAll, false);
        DoSaveLayoutCommand = new DelegateCommand(DoSaveLayout, false);
        DoDeleteLayoutCommand = new DelegateCommand(DoDeleteLayout, false);
    }

    private void OnInitialized()
    {
        if (IsInDesignMode)
            return;

        try
        {
            Logger.Trace(string.Empty);

            // 저장된 상태를 되살린다. 값이 없으면 메뉴는 펼친 상태로 시작한다.
            IsMenuVisible = Convert.ToBoolean(AppSettingUtility.Get(nameof(IsMenuVisible), bool.TrueString));
            IsFullMode = Convert.ToBoolean(AppSettingUtility.Get(nameof(IsFullMode), bool.FalseString));
            IsTopMost = Convert.ToBoolean(AppSettingUtility.Get(nameof(IsTopMost), bool.FalseString));
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void OnLoaded(RoutedEventArgs args)
    {
        try
        {
            Logger.Trace(string.Empty);

            // MainViewModel 이 Initialized 를 마친 뒤에 현재 상태를 한 번 밀어 넣는다.
            // 이 순서를 지키지 않으면 메뉴 접힘 상태가 화면에 반영되지 않는다.
            MessengerUtility.SendAction(typeof(MainViewModel), nameof(IsMenuVisible), string.Empty, IsMenuVisible);
            MessengerUtility.SendAction(typeof(MainViewModel), nameof(IsFullMode), string.Empty, IsFullMode);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void OnActivated()
    {
        // 창이 활성화될 때 할 일이 있으면 여기에 (예: 외부 변경 감지 후 새로고침)
    }

    private void OnClosing(CancelEventArgs e)
    {
        try
        {
            Logger.Trace(string.Empty);

            AppSettingUtility.Set(nameof(IsTopMost), IsTopMost);
            UserPreferencesHelper.SaveUserPreferences();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void OnMenuVisible()
    {
        try
        {
            IsMenuVisible = !IsMenuVisible;
            AppSettingUtility.Set(nameof(IsMenuVisible), IsMenuVisible);

            MessengerUtility.SendAction(typeof(MainViewModel), nameof(IsMenuVisible), string.Empty, IsMenuVisible);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void OnFullMode()
    {
        try
        {
            IsFullMode = !IsFullMode;
            AppSettingUtility.Set(nameof(IsFullMode), IsFullMode);

            MessengerUtility.SendAction(typeof(MainViewModel), nameof(IsFullMode), string.Empty, IsFullMode);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void OnCloseAll() => MessengerUtility.SendAction(typeof(MainViewModel), "DoCloseAll");

    private void DoSaveLayout() => MessengerUtility.SendAction(typeof(MainViewModel), "DoSaveLayout");

    private void DoDeleteLayout() => MessengerUtility.SendAction(typeof(MainViewModel), "DoDeleteLayout");

    private void DoPreferences()
    {
        try
        {
            Logger.Trace(string.Empty);

            // TODO: 설정 화면(ConfigView)을 만들면 여기서 IDialogService 로 띄운다.
            //       TamsTools 는 ThemedWindow 스타일을 지정한 dx:DialogService 를
            //       MainWindow.xaml 의 Interaction.Behaviors 에 두고 이 자리에서 ShowDialog 를 호출한다.
            Minguk.Base.Utility.MessageBox("설정 화면은 아직 만들지 않았습니다.", "설정");
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void DoRestart()
    {
        try
        {
            Logger.Trace(string.Empty);

            UserPreferencesHelper.SaveUserPreferences();

            System.Windows.Forms.Application.Restart();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void OnExit()
    {
        try
        {
            Logger.Trace(string.Empty);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }
}
