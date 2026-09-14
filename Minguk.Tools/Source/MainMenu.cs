using System.Collections.ObjectModel;

using Minguk.Image;

using Minguk.Tools.Models;

namespace Minguk.Tools.Source;

/// <summary>
/// 네비게이션 메뉴 정의.
///
/// 화면을 하나 추가하려면 두 곳만 손대면 된다.
///   1) App.xaml.cs 의 builder.Services.AddTransient&lt;새View&gt;()
///   2) 여기에 MenuItemModel.Create(...) 로 항목 추가 (CLASS_NM 은 View 의 전체 타입 이름)
///
/// 아이콘은 두 갈래를 쓸 수 있다.
///   - Axialis 아이콘 팩   : FreeImage.Instance?.CacheByteArray("axialis/...")   ← 경로 소문자
///   - DevExpress 내장 SVG : Minguk.Base.Utility.GetConvertDevExpressSvgImageToImageSource("SvgImages/...")
/// 여기서는 Axialis 쪽만 쓴다. DXImageHelper 는 없는 경로를 주면 런타임에 터지므로,
/// 실제 파일이 있는지 눈으로 확인할 수 있는 Axialis 가 안전하다.
/// </summary>
public class MainMenu : ObservableCollection<MenuItemModel>
{
    /// <summary>Automation 항목이 여는 뷰. 위에서 솔루션·프로젝트를 고르고 아래 탭이 따라가는 화면이다(TamsTools StreamMode 와 같은 짜임).</summary>
    public const string AutomationClassName = "Minguk.Tools.Views.AutomationMainView";

    private static ObservableCollection<MenuItemModel>? _instance;

    public static ObservableCollection<MenuItemModel> Instance => _instance ??= CreateAccordionViewMenu();

    public static ObservableCollection<MenuItemModel> CreateAccordionViewMenu()
    {
        var menuItemList = new ObservableCollection<MenuItemModel>();

        // ── 루트 그룹 : Office Automation ────────────────────────────────
        // 짜임(사용자, 2026-09-14): Minguk Tools(본 창) → Office Automation → Automation → 화면들.
        // Office Automation 은 기능 여럿을 담는 뿌리고, 그 아래 Automation 이 지금 만드는 게임 자동화다.
        // canSelect: false → 폴더 자체는 선택되지 않는다(클릭하면 펼침/접힘만 한다).
        var rootItem = MenuItemModel.Create(
            menu_cd: "1000",
            menu_nm: "자동화",
            dll_nm: string.Empty,
            class_nm: string.Empty,
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: true,
            canSelect: false,
            icon: FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/folder_open.png"));

        menuItemList.Add(rootItem);

        // ── 환경 : 모듈이 들고 온 항목(맨 위) ────────────────────────────
        // 전체 환경이 먼저고 그 안에서 빌더·플레이가 돈다(사용자, 2026-09-14). 학습 도구와 Workspace 를 여기서 정하는데 빌더는 그 Workspace 에서
        // 솔루션을 고르므로 빌더 탭 안에 두면 솔루션부터 골라야 Workspace 를 바꿀 수 있었다. 솔루션과 상관없는 도구라 문을 안 거친다.
        // 셸은 무엇이 붙는지 모른다 - 모듈이 제 항목을 들고 온다.
        foreach (var module in Modules.ToolModules.All)
        {
            foreach (var item in module.CreateMenuItems())
                rootItem.AddChildren(item);
        }

        // ── Automation Builder : 만드는 쪽 ───────────────────────────────
        // 폴더가 아니라 누르는 항목이다(사용자, 2026-09-14). 누르면 솔루션이 없을 때 시작 창을 띄우고 Builder 화면을 연다.
        // 학습환경·화면캡처·라벨링·스크립트는 메뉴 항목이 아니라 그 화면 아래의 탭이다(AutomationScreens).
        // 플레이(돌리는 쪽)와 입력 테스트(점검 도구)는 역할이 달라 따로 뺐다 - 만드는 일과 섞이면 한 탭 줄에 셋이 뒤섞였다.
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1100",
            menu_nm: "빌더",
            dll_nm: "Minguk.Tools.dll",
            class_nm: AutomationClassName,
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/document-edit.png")).RequireSolution());

        // ── 플레이 : 돌리는 쪽 ──────────────────────────────────────────
        // 빌드한 완성품(Player 폴더)만 고른다 - 솔루션을 고를 필요가 없어 문을 안 거친다.
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1200",
            menu_nm: "플레이",
            dll_nm: "Minguk.Tools.dll",
            class_nm: "Minguk.Tools.Views.PlayView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/multimedia/16x16/button_green_play.png")));

        // ── 입력 테스트 : 점검 도구 ──────────────────────────────────────
        // 입력 경로(SendInput·PostMessage·Interception)가 대상에 먹는지 본다. 솔루션과 상관없다.
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1300",
            menu_nm: "입력 테스트",
            dll_nm: "Minguk.Tools.dll",
            class_nm: "Minguk.Tools.Views.InputAutomationView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/hardwarenetwork/16x16/keyboard.png")));

        // ── 샘플 화면 : 대시보드 - 메뉴에서 뺐다(2026-09-14, 지금 안 쓴다) ───────────────
        // 화면(DashboardView·DashboardViewModel)은 ViewModel 작성의 최소 예시로 그대로 두고, DI 등록도 남겨 둔다.
        // 다시 보이려면 아래 주석을 풀면 된다.
        //
        // rootItem.AddChildren(MenuItemModel.Create(
        //     menu_cd: "1900",
        //     menu_nm: "대시보드",
        //     dll_nm: "Minguk.Tools.dll",
        //     class_nm: "Minguk.Tools.Views.DashboardView",
        //     is_enable: true,
        //     isExpanded: true,
        //     showInCollapsedMode: false,
        //     canSelect: true,
        //     icon: FreeImage.Instance?.CacheByteArray("axialis/business/16x16/business_report.png")));

        return menuItemList;
    }

    /// <summary>CLASS_NM 으로 메뉴 항목을 찾는다. 문서 탭 제목을 메뉴에서 가져올 때 쓴다.</summary>
    public static MenuItemModel? FindMenuItem(string? className)
    {
        if (_instance == null || string.IsNullOrEmpty(className))
            return null;

        foreach (var menuItemModel in _instance)
        {
            var result = FindMenuItem(menuItemModel, className);
            if (result != null)
                return result;
        }

        return null;
    }

    public static MenuItemModel? FindMenuItem(MenuItemModel menuItemModel, string? className)
    {
        if (menuItemModel.CLASS_NM == className)
            return menuItemModel;

        foreach (var child in menuItemModel.Children)
        {
            var result = FindMenuItem(child, className);
            if (result != null)
                return result;
        }

        return null;
    }
}
