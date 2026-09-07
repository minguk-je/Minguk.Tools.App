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
    /// 지난번에 열려 있던 문서 탭과 도킹 배치를 되살린다.
    ///
    /// 어떤 탭이 열려 있었는지는 우리가 직접 적어 둔다(<see cref="SaveLayout"/>).
    /// DevExpress 의 논리 레이아웃 직렬화(SerializeDocumentManagerService)는 이 구조에서
    /// 문서를 하나도 담지 못했다 — 저장본에 DocumentType 이 전부 비어 있었고,
    /// 그래서 여태 탭이 복원된 적이 없다.
    ///
    /// 순서가 중요하다.
    ///   ① 탭을 다시 만든다        - 활성이던 탭을 마지막에 Show 해서 그게 선택되게 한다
    ///   ② RootLayout 을 되돌린다  - 만들어진 탭을 어디에 어떻게 놓을지
    /// ②를 먼저 하면 배치할 대상이 없어서 저장된 배치가 그냥 버려진다.
    /// </summary>
    public void RestoreDocument()
    {
        // 첫 화면이 그려진 뒤로 미룬다.
        // 탭을 되살리는 데 1초쯤 걸리는데, 그 시간을 여기서 붙잡으면 그동안 창이 멎어 있다.
        // 셸(메뉴 + 빈 문서 영역)을 먼저 보여 주고 탭은 곧이어 채운다.
        //
        // 우선순위가 핵심이다. IDispatcherService.BeginInvoke 는 Normal 로 넣는데
        // Normal 은 Render 보다 높아서 결국 그리기 전에 실행된다 - 미룬 게 아니게 된다.
        // Background 는 Render/Loaded 보다 낮아서 첫 프레임이 나온 뒤에 돈다.
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(RestoreDocumentCore));
    }

    private bool _documentsRestored;

    private async void RestoreDocumentCore()
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var openDocuments = (Minguk.Tools.Properties.Settings.Default.OpenDocuments ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries);

            var activeDocument = Minguk.Tools.Properties.Settings.Default.ActiveDocument;

            // 활성이던 탭은 맨 뒤로 돌린다. 마지막에 Show 한 문서가 선택된 채로 남는다.
            foreach (var viewName in openDocuments.Where(x => x != activeDocument).Concat(
                         openDocuments.Where(x => x == activeDocument)))
            {
                // 메뉴에 없는 화면(이름이 바뀌었거나 지워진 경우)은 조용히 건너뛴다.
                if (MainMenu.FindMenuItem(viewName) is null)
                {
                    Logger.Debug($"복원 건너뜀 - 메뉴에 없는 화면: {viewName}");
                    continue;
                }

                CreateDocumentCore(viewName).Show();

                // 탭 하나를 만들 때마다 디스패처에 자리를 내준다.
                // 한 번에 몰아서 만들면 그 시간 내내 창이 멎어 보인다.
                await System.Windows.Threading.Dispatcher.Yield(
                    System.Windows.Threading.DispatcherPriority.Background);
            }

            var documentElapsed = stopwatch.ElapsedMilliseconds;

            if (!string.IsNullOrEmpty(Minguk.Tools.Properties.Settings.Default.RootLayout))
                LayoutSerializationService.Deserialize(
                    StripWindowGeometry(Minguk.Tools.Properties.Settings.Default.RootLayout));

            stopwatch.Stop();

            _documentsRestored = true;

            Logger.Debug($"탭 복원 {stopwatch.ElapsedMilliseconds}ms " +
                         $"(문서 {documentElapsed}ms + 배치 {stopwatch.ElapsedMilliseconds - documentElapsed}ms, " +
                         $"탭 {openDocuments.Length}개)");
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
    /// 열려 있는 탭 목록과 도킹 배치를 저장한다.
    ///
    /// 탭은 View 전체 타입 이름으로 적는다. 문서 Id 는 CustomID 로 다듬어져 있어서
    /// 원래 이름을 되짚을 수 없기 때문에, 만들 때 기록해 둔 _documentClassNames 를 쓴다.
    /// </summary>
    public void SaveLayout()
    {
        try
        {
            // 복원이 끝나기 전에 창을 닫으면 문서가 아직 없다.
            // 그대로 저장하면 지난번 탭 목록을 빈 값으로 지워 버린다.
            if (!_documentsRestored)
            {
                Logger.Debug("탭 복원 전에 종료됨. 저장된 탭 목록을 그대로 둔다.");
                Minguk.Tools.Properties.Settings.Default.Save();
                return;
            }

            var openDocuments = DocumentManagerService.Documents
                .Select(document => document.Id as string)
                .Where(id => id is not null && _documentClassNames.ContainsKey(id))
                .Select(id => _documentClassNames[id!])
                .Distinct()
                .ToArray();

            var activeId = DocumentManagerService.ActiveDocument?.Id as string;
            var activeDocument = activeId is not null && _documentClassNames.TryGetValue(activeId, out var name)
                ? name
                : string.Empty;

            Minguk.Tools.Properties.Settings.Default.OpenDocuments = string.Join('|', openDocuments);
            Minguk.Tools.Properties.Settings.Default.ActiveDocument = activeDocument;
            Minguk.Tools.Properties.Settings.Default.RootLayout = LayoutSerializationService.Serialize();
            Minguk.Tools.Properties.Settings.Default.Save();

            Logger.Debug($"탭 저장 {openDocuments.Length}개 (활성: {activeDocument})");
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
