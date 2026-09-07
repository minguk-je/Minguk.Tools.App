using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using DevExpress.Mvvm.UI;
using DevExpress.Xpf.Accordion;
using DevExpress.Xpf.Docking;
using Minguk.Tools.Models;
using Minguk.Tools.Source;
using Newtonsoft.Json;
using DevExpress.Xpf.Core.Native;
using Minguk.Base;
using Minguk.Base.Enums;
using Minguk.Base.Extension;
using Minguk.Base.Utilities;
using Minguk.Base.Views;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 셸 본체. 네비게이션 메뉴와 문서(탭) 영역을 묶는다.
///
/// 화면을 여는 흐름:
///   메뉴 선택/더블클릭/Enter
///     → MessengerUtility.SendShowWindow(CLASS_NM)
///     → OnMessenger 가 받아 DoShowWindow(CLASS_NM)
///     → IDocumentManagerService.CreateDocument(뷰이름)
///     → MainViewLocator 가 DI 에서 View 인스턴스를 꺼내 문서 탭에 얹음
///
/// 메신저를 한 번 거치는 이유는, 나중에 다른 화면에서도 "이 화면 열어줘" 를
/// MainViewModel 참조 없이 보낼 수 있게 하기 위함이다.
/// </summary>
public class MainViewModel : ViewModelBase, ISupportLogicalLayout
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public bool CanSerialize => true;
    public IEnumerable<object>? LookupViewModels => null;

    public IDocumentManagerService DocumentManagerService => this.GetService<IDocumentManagerService>();
    public ILayoutSerializationService LayoutSerializationService => this.GetService<ILayoutSerializationService>();
    protected IDispatcherService DispatcherService => this.GetService<IDispatcherService>();

    // ── XAML 의 UIObjectService 로 실제 컨트롤을 잡아 온다 ─────────────────
    // 메뉴 접기·전체화면처럼 레이아웃을 직접 만져야 하는 동작 때문에 필요하다.
    public IUIObjectService AccordionControlObjectService => ServiceContainer.GetService<IUIObjectService>("AccordionControlObjectService");
    public AccordionControl? AccordionControl { get; set; }

    public IUIObjectService LayoutGroupObjectService => ServiceContainer.GetService<IUIObjectService>("LayoutGroupObjectService");
    public LayoutGroup? LayoutGroup { get; set; }

    public IUIObjectService GridSplitterObjectService => ServiceContainer.GetService<IUIObjectService>("GridSplitterObjectService");
    public GridSplitter? GridSplitter { get; set; }

    public IUIObjectService DocumentGroupObjectService => ServiceContainer.GetService<IUIObjectService>("DocumentGroupObjectService");
    public DocumentGroup? DocumentGroup { get; set; }

    public IUIObjectService DockPanelGridObjectService => ServiceContainer.GetService<IUIObjectService>("DockPanelGridObjectService");
    public Grid? DockPanelGrid { get; set; }

    // ── Command ─────────────────────────────────────────────────────────
    public ICommand OnInitializedCommand { get; set; }
    public ICommand OnClosingCommand { get; set; }
    public ICommand OnMouseDoubleClickCommand { get; set; }
    public ICommand OnPreviewKeyDownCommand { get; set; }
    public ICommand OnAccordionSelectedItemChangedCommand { get; set; }

    public virtual ObservableCollection<MenuItemModel>? AccordionControlMenuItemSource { get; set; }

    /// <summary>
    /// 문서 Id → 뷰 클래스 이름. 활성 탭이 바뀌었을 때 어느 메뉴 항목인지 되짚기 위한 것이다.
    /// 문서 Id 는 CustomID 로 다듬어져 원래 이름을 알 수 없으므로 만들 때 기록해 둔다.
    /// </summary>
    private readonly Dictionary<string, string> _documentClassNames = new();

    public string? MainMessage { get => GetProperty(() => MainMessage); set => SetProperty(() => MainMessage, value); }
    public string? SubMessage { get => GetProperty(() => SubMessage); set => SetProperty(() => SubMessage, value); }

    public static MainViewModel Create() => ViewModelSource.Create(() => new MainViewModel());

    public MainViewModel()
    {
        OnInitializedCommand = new DelegateCommand(OnInitialized, false);
        OnClosingCommand = new DelegateCommand<CancelEventArgs>(OnClosing, false);
        OnMouseDoubleClickCommand = new DelegateCommand<MouseButtonEventArgs>(OnMouseDoubleClick, false);
        OnPreviewKeyDownCommand = new DelegateCommand<KeyEventArgs>(OnPreviewKeyDown, false);
        OnAccordionSelectedItemChangedCommand = new DelegateCommand<AccordionSelectedItemChangedEventArgs>(OnSelectedItemChanged, false);

        MainMessage = string.Empty;
        SubMessage = string.Empty;
    }

    public void OnInitialized()
    {
        if (IsInDesignMode)
            return;

        try
        {
            Logger.Trace(string.Empty);

            Messenger.Default.Register<MessengerUtility>(this, OnMessenger);
            DocumentManagerService.ActiveDocumentChanged += OnActiveDocumentChanged;

            InitializeUiObjectService();
            InitializeMenu();
            RestoreDocument();

            SubMessage = Minguk.Tools.Helper.AppVersionHelper.DisplayTitle;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void InitializeUiObjectService()
    {
        try
        {
            AccordionControl = AccordionControlObjectService.Object as AccordionControl;
            DocumentGroup = DocumentGroupObjectService.Object as DocumentGroup;
            LayoutGroup = LayoutGroupObjectService.Object as LayoutGroup;
            GridSplitter = GridSplitterObjectService.Object as GridSplitter;
            DockPanelGrid = DockPanelGridObjectService.Object as Grid;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void InitializeMenu()
    {
        try
        {
            AccordionControlMenuItemSource = MainMenu.Instance;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    /// <summary>활성 탭이 바뀌면 해당 메뉴 항목을 굵게(IS_ACTIVE) 표시한다.</summary>
    private void OnActiveDocumentChanged(object sender, ActiveDocumentChangedEventArgs e)
    {
        try
        {
            foreach (var root in AccordionControlMenuItemSource ?? new ObservableCollection<MenuItemModel>())
                ResetActive(root);

            if (e.NewDocument?.Id is not string documentId)
                return;

            if (_documentClassNames.TryGetValue(documentId, out var className) &&
                MainMenu.FindMenuItem(className) is { } menuItem)
            {
                menuItem.IS_ACTIVE = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }

        static void ResetActive(MenuItemModel item)
        {
            item.IS_ACTIVE = false;
            foreach (var child in item.Children)
                ResetActive(child);
        }
    }

    private void OnClosing(CancelEventArgs e)
    {
        try
        {
            SaveLayout();
            SaveNavigationWidth();

            Messenger.Default.Unregister<MessengerUtility>(this, OnMessenger);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    // ── 메뉴 조작 ────────────────────────────────────────────────────────

    public void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        try
        {
            // 폴더 항목(CanSelect=false)에서의 더블클릭은 무시한다.
            var accordionItem = LayoutTreeHelper.GetVisualParents(e.OriginalSource as DependencyObject)
                                                .OfType<AccordionItem>()
                                                .FirstOrDefault();
            if (accordionItem is { CanSelect: false })
                return;

            ShowSelectedMenu();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void OnPreviewKeyDown(KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Return)
                ShowSelectedMenu();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void OnSelectedItemChanged(AccordionSelectedItemChangedEventArgs e)
    {
        try
        {
            if (e.NewItem is MenuItemModel menuItemModel && !string.IsNullOrEmpty(menuItemModel.CLASS_NM))
                MessengerUtility.SendShowWindow(typeof(MainViewModel), menuItemModel.CLASS_NM);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void ShowSelectedMenu()
    {
        if (AccordionControl?.SelectedItem is MenuItemModel menuItemModel && !string.IsNullOrEmpty(menuItemModel.CLASS_NM))
            MessengerUtility.SendShowWindow(typeof(MainViewModel), menuItemModel.CLASS_NM);
    }

    // ── 문서 열기 ────────────────────────────────────────────────────────

    /// <summary>
    /// 뷰 이름으로 문서 탭을 연다. 이미 열려 있으면 그 탭을 앞으로 가져온다.
    ///
    /// Id 를 CustomID 로 다듬는 이유는 DevExpress Docking 이 XamlName 규칙을 강제하기 때문이다.
    /// 네임스페이스의 '.' 을 그대로 넣으면 레이아웃 직렬화에서 예외가 난다.
    /// </summary>
    public void DoShowWindow(object? parameter)
    {
        if (parameter is not string viewName)
            return;

        try
        {
            IDocument document = DocumentManagerService.FindDocument(viewName, this);
            if (document == null)
            {
                // 화면 생성이 무거울 수 있어 대기 표시를 띄운다. 반드시 finally 에서 닫는다.
                Utility.ShowWaitSplashScreen(SplashMessageTypes.Loading);
                try
                {
                    document = DocumentManagerService.CreateDocument(viewName, viewName, this);
                    document.Id = new CustomID($"DocId_{viewName}").ToString();
                    document.Title = MainMenu.FindMenuItem(viewName)?.MENU_NM ?? viewName;
                    document.DestroyOnClose = true;

                    if (document.Id is string documentId)
                        _documentClassNames[documentId] = viewName;
                }
                finally
                {
                    Utility.CloseSplashScreen();
                }
            }

            document?.Show();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    // ── 레이아웃 저장/복원 ────────────────────────────────────────────────

    /// <summary>
    /// 지난번에 열려 있던 문서 탭과 도킹 배치를 되살린다.
    ///
    /// 두 가지를 반드시 같이, 이 순서로 해야 한다.
    ///   ① LogicalLayout : 문서(탭) 자체를 다시 만든다
    ///   ② RootLayout    : 만들어진 문서를 어디에 어떻게 놓을지 배치한다
    /// ②만 따로 미루면 배치할 문서가 없어서 저장된 배치가 그냥 버려진다.
    ///
    /// 시작 시간의 대부분이 여기서 나온다. 얼마나 걸렸는지 로그로 남겨 두면
    /// 느려졌을 때 어디를 봐야 하는지 바로 알 수 있다.
    /// </summary>
    public void RestoreDocument()
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            if (!string.IsNullOrEmpty(Minguk.Tools.Properties.Settings.Default.LogicalLayout))
                this.RestoreDocumentManagerService(Minguk.Tools.Properties.Settings.Default.LogicalLayout);

            var documentElapsed = stopwatch.ElapsedMilliseconds;

            if (!string.IsNullOrEmpty(Minguk.Tools.Properties.Settings.Default.RootLayout))
                LayoutSerializationService.Deserialize(Minguk.Tools.Properties.Settings.Default.RootLayout);

            stopwatch.Stop();

            Logger.Debug($"탭 복원 {stopwatch.ElapsedMilliseconds}ms " +
                         $"(문서 {documentElapsed}ms + 배치 {stopwatch.ElapsedMilliseconds - documentElapsed}ms, " +
                         $"탭 {DocumentManagerService.Documents.Count()}개)");
        }
        catch (Exception ex)
        {
            // 레이아웃이 깨져도 앱은 떠야 한다. 저장본을 버리고 기본 배치로 시작한다.
            Logger.Warn(ex, "저장된 레이아웃 복원 실패. 기본 배치로 시작한다.");
            DeleteLayout();
        }
    }

    public void SaveLayout()
    {
        try
        {
            Minguk.Tools.Properties.Settings.Default.LogicalLayout = this.SerializeDocumentManagerService();
            Minguk.Tools.Properties.Settings.Default.RootLayout = LayoutSerializationService.Serialize();
            Minguk.Tools.Properties.Settings.Default.Save();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }
    }

    public void DeleteLayout()
    {
        try
        {
            Minguk.Tools.Properties.Settings.Default.LogicalLayout = string.Empty;
            Minguk.Tools.Properties.Settings.Default.RootLayout = string.Empty;
            Minguk.Tools.Properties.Settings.Default.Save();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }
    }

    private void SaveNavigationWidth()
    {
        try
        {
            if (DockPanelGrid != null && LayoutGroup is { Visibility: Visibility.Visible })
                AppSettingUtility.Set("NavigationWidth", JsonConvert.SerializeObject(DockPanelGrid.ColumnDefinitions[0].Width));
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
        }
    }

    // ── 메신저 ──────────────────────────────────────────────────────────

    private void OnMessenger(MessengerUtility messenger)
    {
        try
        {
            if (messenger.Success || !messenger.IsTarget(this))
                return;

            Logger.Trace("{0} : {1}", messenger.MessageType, messenger.Message);

            switch (messenger.MessageType)
            {
                case MessengerMessageType.ShowWindow:
                    DoShowWindow(messenger.Message);
                    messenger.Success = true;
                    break;

                case MessengerMessageType.Action:
                    HandleAction(messenger);
                    break;

                case MessengerMessageType.Message:
                    MainMessage = messenger.Message;
                    break;

                case MessengerMessageType.SubMessage:
                    SubMessage = messenger.Message;
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    private void HandleAction(MessengerUtility messenger)
    {
        switch (messenger.Message)
        {
            case "IsMenuVisible":
                {
                    // 네비게이션을 접을 때 스플리터도 같이 숨겨야 한다.
                    // 스플리터만 남으면 잡히지 않는 2px 짜리 띠가 화면에 남는다.
                    var visible = Convert.ToBoolean(messenger.Value);
                    if (LayoutGroup != null)
                        LayoutGroup.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    if (GridSplitter != null)
                        GridSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    break;
                }

            case "IsFullMode":
                {
                    // 전체화면에서는 네비게이션과 상태바를 감춘다.
                    var full = Convert.ToBoolean(messenger.Value);
                    if (Application.Current.MainWindow is { } window)
                    {
                        window.WindowState = full ? WindowState.Maximized : WindowState.Normal;
                        window.WindowStyle = full ? WindowStyle.None : WindowStyle.SingleBorderWindow;
                    }
                    break;
                }

            case "DoSaveLayout":
                SaveLayout();
                MainMessage = "레이아웃을 저장했습니다.";
                break;

            case "DoDeleteLayout":
                DeleteLayout();
                MainMessage = "레이아웃을 초기화했습니다. 재시작하면 반영됩니다.";
                break;

            case "DoCloseAll":
                CloseAllDocuments();
                break;
        }
    }

    private void CloseAllDocuments()
    {
        foreach (var document in DocumentManagerService.Documents.ToList())
        {
            // 이미 닫힌 문서를 다시 닫으면 DevExpress 내부에서 NRE 가 난다. 하나씩 감싼다.
            try
            {
                document.Close();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "문서 닫기 실패");
            }
        }
    }
}
