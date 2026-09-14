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
using System.Xml.Linq;

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
public class MainViewModel : ViewModelBase, ISupportLogicalLayout, Modules.IMainShell
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

            // 공용 단축키는 마지막에 본 화면이 받는다. 내용이 뷰면 그 DataContext 가 뷰모델이다.
            var content = e.NewDocument?.Content;
            var activated = (content as FrameworkElement)?.DataContext as DocumentViewModelBase ?? content as DocumentViewModelBase;
            activated?.NotifyActivated();

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
            SaveOpenDocumentSettings();
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
            // 솔루션이 있어야 여는 항목(Automation)은 문을 거친다. 없으면 시작 창을 먼저 띄우고, 그만두면 안 연다.
            // 다른 기능 화면은 그냥 연다 - 셸은 여러 기능을 담는다.
            if (MainMenu.FindMenuItem(viewName) is { REQUIRES_SOLUTION: true } &&
                !Minguk.Tools.Projects.SolutionGate.EnsureOpen())
                return;

            IDocument document = DocumentManagerService.FindDocument(viewName, this);
            if (document == null)
            {
                // 화면 생성이 무거울 수 있어 대기 표시를 띄운다. 반드시 finally 에서 닫는다.
                Utility.ShowWaitSplashScreen(SplashMessageTypes.Loading);
                try
                {
                    document = CreateDocumentCore(viewName);
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

    /// <summary>
    /// 문서 하나를 만든다. 대기 표시는 붙이지 않는다 — 부르는 쪽이 정한다.
    ///
    /// Id 를 CustomID 로 다듬는 이유는 DevExpress Docking 이 XamlName 규칙을 강제하기 때문이다.
    /// 네임스페이스의 '.' 을 그대로 넣으면 레이아웃 직렬화에서 예외가 난다.
    /// </summary>
    private IDocument CreateDocumentCore(string viewName)
    {
        var document = DocumentManagerService.CreateDocument(viewName, viewName, this);

        document.Id = new CustomID($"DocId_{viewName}").ToString();
        document.Title = MainMenu.FindMenuItem(viewName)?.MENU_NM ?? viewName;
        document.DestroyOnClose = true;

        if (document.Id is string documentId)
            _documentClassNames[documentId] = viewName;

        return document;
    }

    // ── 레이아웃 저장/복원 ────────────────────────────────────────────────

    /// <summary>
    /// 지난번 도킹 배치만 되살린다. <b>열려 있던 탭은 되살리지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 예전에는 지난번 탭들을 전부 다시 열었다. 탭 하나에 수백 ms 라 켤 때 1초 넘게 걸렸고, 정작 그 탭들이
    /// 필요한 날은 드물었다("시간만 지연되고 별루"). 메뉴에서 한 번 누르는 것이 더 빠르다.
    /// 어떤 탭이 열려 있었는지도 더는 적지 않는다 - 되살리지 않을 것을 저장할 이유가 없다.
    ///
    /// 첫 화면이 그려진 뒤로 미룬다. Background 는 Render/Loaded 보다 낮아서 첫 프레임이 나온 뒤에 돈다.
    /// </remarks>
    public void RestoreDocument()
    {
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(RestoreDocumentCore));
    }

    /// <summary>배치에서 되살리지 않을 문서 이름(Id 에 들어가는 조각).</summary>
    private static readonly string[] OldDocumentNames =
    [
        "AutomationMainView", "ProjectWorkspaceView", "DocId_Project_",
        "CaptureMonitorView", "LabelingView", "ScriptStudioView"
    ];

    private void RestoreDocumentCore()
    {
        try
        {
            var saved = Minguk.Tools.Properties.Settings.Default.RootLayout;

            // Automation 화면과 예전 최상위 화면 탭은 되살리지 않는다. 되살리면 시작 창(솔루션 고르기)을 안 거친 채 열리고,
            // 예전 화면들은 이제 Automation 화면 아래 탭이다(2026-09-14).
            if (!string.IsNullOrEmpty(saved) && OldDocumentNames.Any(name => saved.Contains(name, StringComparison.Ordinal)))
            {
                Logger.Info("저장된 배치에 프로젝트 탭이나 옛 화면 탭이 있어 버리고 기본 배치로 시작한다.");
                DeleteLayout();
                return;
            }

            if (!string.IsNullOrEmpty(saved))
                LayoutSerializationService.Deserialize(StripWindowGeometry(saved));
        }
        catch (Exception ex)
        {
            // 레이아웃이 깨져도 앱은 떠야 한다. 저장본을 버리고 기본 배치로 시작한다.
            Logger.Warn(ex, "저장된 레이아웃 복원 실패. 기본 배치로 시작한다.");
            DeleteLayout();
        }
    }

    /// <summary>
    /// 저장된 도킹 배치에서 창 크기 항목($activeWindowId)을 떼어 낸다.
    ///
    /// DevExpress 는 도킹 배치를 직렬화할 때 창의 크기·상태까지 같이 담는다.
    /// 그걸 그대로 되돌리면 복원 도중 창이 한 번 접혔다가(1422 -> 252) 다시 펴진다.
    /// 100ms 남짓이지만 창이 두 번 열리는 것처럼 보인다.
    ///
    /// 창 위치·크기는 UserPreferences 가 첫 렌더 전에 이미 넣어 두었다.
    /// 여기서는 패널 배치만 되돌리면 된다.
    /// </summary>
    private static string StripWindowGeometry(string layout)
    {
        try
        {
            var document = XDocument.Parse(layout);

            document.Descendants("property")
                    .Where(element => (string?)element.Attribute("name") == "$activeWindowId")
                    .Remove();

            return document.ToString(SaveOptions.DisableFormatting);
        }
        catch (Exception ex)
        {
            // 저장본 형식이 바뀌었더라도 복원 자체는 시도해 본다.
            Logger.Warn(ex, "레이아웃에서 창 크기 항목을 떼어 내지 못했다. 원본 그대로 쓴다.");
            return layout;
        }
    }

    /// <summary>
    /// 열려 있는 문서들에게 "지금 설정을 저장하라"고 알린다.
    ///
    /// 탭을 닫으면 문서가 스스로 저장하지만, 앱을 그냥 닫으면 문서는 파기되지 않는다.
    /// 그래서 셸이 종료할 때 한 번 대신 불러 준다.
    /// </summary>
    private void SaveOpenDocumentSettings()
    {
        foreach (var document in DocumentManagerService.Documents)
        {
            // Content 가 ViewModel 인 경우와 View 인 경우 둘 다 대응한다.
            var viewModel = document.Content as DocumentViewModelBase
                            ?? (document.Content as FrameworkElement)?.DataContext as DocumentViewModelBase;

            viewModel?.SaveSettingsNow();
        }
    }

    /// <summary>
    /// 도킹 배치를 저장한다. 열려 있는 탭 목록은 저장하지 않는다 - 켤 때 되살리지 않는다(<see cref="RestoreDocument"/>).
    /// </summary>
    public void SaveLayout()
    {
        try
        {
            // 예전에 적어 둔 탭 목록은 비운다. 남겨 두면 옛 판이 그것을 읽어 되살린다.
            Minguk.Tools.Properties.Settings.Default.OpenDocuments = string.Empty;
            Minguk.Tools.Properties.Settings.Default.ActiveDocument = string.Empty;
            Minguk.Tools.Properties.Settings.Default.RootLayout = LayoutSerializationService.Serialize();
            Minguk.Tools.Properties.Settings.Default.Save();

            Logger.Debug("도킹 배치 저장");
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
            Minguk.Tools.Properties.Settings.Default.OpenDocuments = string.Empty;
            Minguk.Tools.Properties.Settings.Default.ActiveDocument = string.Empty;
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
