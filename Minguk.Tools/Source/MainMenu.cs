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
    private static ObservableCollection<MenuItemModel>? _instance;

    public static ObservableCollection<MenuItemModel> Instance => _instance ??= CreateAccordionViewMenu();

    public static ObservableCollection<MenuItemModel> CreateAccordionViewMenu()
    {
        var menuItemList = new ObservableCollection<MenuItemModel>();

        // ── 루트 그룹 ────────────────────────────────────────────────────
        // canSelect: false → 폴더 자체는 선택되지 않는다(클릭하면 펼침/접힘만 한다).
        var rootItem = MenuItemModel.Create(
            menu_cd: "1000",
            menu_nm: "Office Automation",
            dll_nm: string.Empty,
            class_nm: string.Empty,
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: true,
            canSelect: false,
            icon: FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/folder_open.png"));

        menuItemList.Add(rootItem);

        // ── 샘플 화면 1 : 대시보드 ────────────────────────────────────────
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1100",
            menu_nm: "대시보드",
            dll_nm: "Minguk.Tools.dll",
            class_nm: "Minguk.Tools.Views.DashboardView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/business/16x16/business_report.png")));

        // ── 캡처 : 순수하게 잡고 담는다 ───────────────────────────────────
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1200",
            menu_nm: "캡처",
            dll_nm: "Minguk.Tools.dll",
            class_nm: "Minguk.Tools.Views.CaptureMonitorView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/screen.png")));

        // ── 라벨링 ────────────────────────────────────────────────────────
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1400",
            menu_nm: "라벨링",
            dll_nm: "Minguk.Tools.dll",
            class_nm: "Minguk.Tools.Views.LabelingView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            // edit.png 는 없는 파일이었다 - 아이콘이 조용히 비었다. 스모크(--views)가 메뉴 아이콘을 검사한다.
            icon: FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/picture-edit.png")));

        // ── 편집 : 몹 찾기·글자 읽기를 보면서 스크립트를 쓴다 ─────────────
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1500",
            menu_nm: "편집",
            dll_nm: "Minguk.Tools.dll",
            class_nm: "Minguk.Tools.Views.ScriptStudioView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/basic/16x16/document-edit.png")));

        // ── 플레이 : 게임을 연결하고 저장된 스크립트를 돌린다 ─────────────
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1600",
            menu_nm: "플레이",
            dll_nm: "Minguk.Tools.dll",
            class_nm: "Minguk.Tools.Views.PlayView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/multimedia/16x16/button_green_play.png")));

        // ── 입력 자동화 ───────────────────────────────────────────────────
        rootItem.AddChildren(MenuItemModel.Create(
            menu_cd: "1300",
            menu_nm: "입력 자동화",
            dll_nm: "Minguk.Tools.dll",
            class_nm: "Minguk.Tools.Views.InputAutomationView",
            is_enable: true,
            isExpanded: true,
            showInCollapsedMode: false,
            canSelect: true,
            icon: FreeImage.Instance?.CacheByteArray("axialis/hardwarenetwork/16x16/keyboard.png")));

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
