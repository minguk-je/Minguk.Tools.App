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

    /// <summary>설정 대화상자. MainWindow.xaml 의 ConfigDialogService 가 실체다.</summary>
    protected IDialogService ConfigDialogService => this.GetService<IDialogService>("ConfigDialogService");

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

    /// <summary>
    /// 창 제목. 솔루션이 열려 있으면 VS 처럼 "사격장 - 오버워치 - Minguk Tools v1.0.0", 없으면 "Minguk Tools v1.0.0".
    /// </summary>
    /// <remarks>
    /// 메뉴창은 접히고 솔루션 탭은 다른 탭 뒤로 갈 수 있다 - 그러면 지금 어느 프로젝트(모델·스크립트가 통째로 다르다)인지 안 보인다(docs/프로젝트-설계.md).
    /// </remarks>
    public string Title { get => GetProperty(() => Title); private set => SetProperty(() => Title, value); }

    private static string MakeTitle()
    {
        if (Minguk.Tools.Projects.SolutionWorkspace.Current is not { } solution) return AppVersionHelper.DisplayTitle;

        var startup = solution.Startup();

        return startup is null
            ? $"{solution.Name} - {AppVersionHelper.DisplayTitle}"
            : $"{Minguk.Tools.Projects.Solution.NameOf(startup)} - {solution.Name} - {AppVersionHelper.DisplayTitle}";
    }

    private void OnSolutionChanged(object? sender, EventArgs e)
    {
        var title = MakeTitle();
        var startup = MakeStartupName();

        void Apply()
        {
            Title = title;
            StartupName = startup;
        }

        // 솔루션은 UI 에서 바뀌지만, 혹시 다른 스레드에서 오면 창 스레드로 넘긴다.
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(Apply);
        else Apply();
    }

    // ── 셸 실행 : ▶ 시작 프로젝트 · ■ 중지 ──────────────────────────────────

    /// <summary>
    /// ▶ 옆에 보이는 시작 프로젝트 이름(VS 2026 의 "▶ 프로젝트"). 솔루션이 없으면 "시작 프로젝트 없음".
    /// </summary>
    /// <remarks>
    /// 고르는 것은 솔루션 탭 위 칸에서만 한다 - 여기서도 바꾸면 솔루션 탭 아래 화면이 옛 프로젝트를 들고 있어 위 칸과 어긋난다.
    /// </remarks>
    public string StartupName { get => GetProperty(() => StartupName); private set => SetProperty(() => StartupName, value); }

    /// <summary>▶ - 시작 프로젝트의 완성품(bin\*.mtsx)을 플레이 화면에서 한 번 돌린다.</summary>
    public ICommand DoRunStartupCommand { get; }

    /// <summary>■ - 플레이 화면의 실행을 멈춘다.</summary>
    public ICommand DoStopCommand { get; }

    private static string MakeStartupName()
        => Minguk.Tools.Projects.SolutionWorkspace.Current?.Startup() is { } startup
            ? Minguk.Tools.Projects.Solution.NameOf(startup)
            : "시작 프로젝트 없음";

    /// <summary>
    /// 완성품을 찾고, 플레이 화면을 열어(없으면 만들어) 돌린다. VS 가 F5 로 디버그 창을 띄우는 것과 같다.
    /// </summary>
    /// <remarks>
    /// 소스가 아니라 빌드한 완성품을 돌린다 - 플레이 화면이 완성품만 돌리기 때문이다(고치는 중의 시험은 스크립트 화면 F5).
    /// 없으면 빌드하라고만 말한다. 몰래 빌드하지 않는다 - 빌드가 실패하면 왜 안 도는지 한 번 더 헤매게 된다.
    /// </remarks>
    private void DoRunStartup()
    {
        try
        {
            var (path, reason) = PlayViewModel.FindStartupBuild();

            if (path is null)
            {
                MessengerUtility.SendMainMessage(reason ?? "돌릴 완성품이 없습니다.");
                return;
            }

            MessengerUtility.SendShowWindow(typeof(MainViewModel), "Minguk.Tools.Views.PlayView");
            PlayViewModel.RequestRun(path);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

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

        DoRunStartupCommand = new DelegateCommand(DoRunStartup, false);
        DoStopCommand = new DelegateCommand(PlayViewModel.RequestStop, false);

        Title = MakeTitle();
        StartupName = MakeStartupName();

        // 정적 이벤트다 - 창이 닫힐 때 푼다(Disposables). 창은 앱과 수명이 같지만 재시작·하네스에서 새로 만들 수 있다.
        Minguk.Tools.Projects.SolutionWorkspace.Changed += OnSolutionChanged;
        Disposables.Add(Disposable.Create(() => Minguk.Tools.Projects.SolutionWorkspace.Changed -= OnSolutionChanged));
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

            if (!e.Cancel) Disposables.Dispose();
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

            var viewModel = ConfigViewModel.Create();

            ConfigDialogService.ShowDialog(dialogButtons: MessageButton.OK, title: "설정", viewModel: viewModel);

            // 초기화는 대화상자가 닫힌 뒤에 처리한다.
            // 대화상자 안에서 바로 재시작하면 창이 닫히기 전에 프로세스가 죽는다.
            if (viewModel.IsResetRequested)
                DoRestart();
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
