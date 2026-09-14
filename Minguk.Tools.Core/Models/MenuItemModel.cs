using System.Collections.ObjectModel;

using DevExpress.Mvvm;
using DevExpress.Mvvm.DataAnnotations;
using DevExpress.Mvvm.POCO;

namespace Minguk.Tools.Models;

/// <summary>
/// 네비게이션(AccordionControl) 한 항목.
///
/// CLASS_NM 이 비어 있으면 폴더(그룹), 채워져 있으면 문서로 열리는 화면이다.
/// ViewModelSource.Factory 로 만드는 이유는 virtual 프로퍼티에 변경 알림을 자동으로 넣기 위함이다.
/// new MenuItemModel(...) 로 직접 만들면 IS_ACTIVE 같은 값이 UI 에 반영되지 않는다.
/// </summary>
public class MenuItemModel : ViewModelBase
{
    public static MenuItemModel Create(
        string menu_cd, string menu_nm, string dll_nm, string class_nm,
        bool is_enable, bool isExpanded, bool showInCollapsedMode, bool canSelect, object? icon)
    {
        var factory = ViewModelSource.Factory<string, string, string, string, bool, bool, bool, bool, object?, MenuItemModel>(
            (_menu_cd, _menu_nm, _dll_nm, _class_nm, _is_enable, _isExpanded, _showInCollapsedMode, _canSelect, _icon)
                => new MenuItemModel(_menu_cd, _menu_nm, _dll_nm, _class_nm, _is_enable, _isExpanded, _showInCollapsedMode, _canSelect, _icon));

        return factory(menu_cd, menu_nm, dll_nm, class_nm, is_enable, isExpanded, showInCollapsedMode, canSelect, icon);
    }

    protected MenuItemModel(
        string menu_cd, string menu_nm, string dll_nm, string class_nm,
        bool is_enable, bool isExpanded, bool showInCollapsedMode, bool canSelect, object? icon)
    {
        MENU_CD = menu_cd;
        MENU_NM = menu_nm;
        DLL_NM = dll_nm;
        CLASS_NM = class_nm;
        IS_ACTIVE = false;
        IS_ENABLE = is_enable;

        IsExpanded = isExpanded;
        ShowInCollapsedMode = showInCollapsedMode;
        CanSelect = canSelect;
        Icon = icon;
    }

    /// <summary>메뉴 코드. 정렬·식별용.</summary>
    public string MENU_CD { get; set; }

    /// <summary>화면에 보이는 이름. 문서 탭 제목으로도 쓰인다.</summary>
    public string MENU_NM { get; set; }

    /// <summary>어셈블리 이름. 현재는 표시용이며 실제 로딩은 DI 가 한다.</summary>
    public string DLL_NM { get; set; }

    /// <summary>열 View 의 전체 타입 이름. 비어 있으면 폴더 항목이다.</summary>
    public string CLASS_NM { get; set; }

    /// <summary>현재 활성 문서인지. true 면 메뉴에서 굵게 표시된다.</summary>
    public virtual bool IS_ACTIVE { get; set; }

    public bool IS_ENABLE { get; set; }

    /// <summary>
    /// 열기 전에 솔루션이 있어야 하는 화면인지. 없으면 셸이 시작 창을 먼저 띄운다.
    /// </summary>
    /// <remarks>
    /// <b>앱 전체가 아니라 화면마다 정한다</b>(사용자 결정 2026-09-14) - Minguk Tools 는 기능 프로젝트를 여럿 담는 셸이라,
    /// 게임 자동화(캡처·라벨링·스크립트·플레이)가 아닌 기능까지 솔루션을 고르게 할 이유가 없다. 켤 때 무조건 시작 창을
    /// 띄웠더니 입력 테스트나 학습환경을 보려 해도 솔루션부터 골라야 했다.
    /// </remarks>
    public bool REQUIRES_SOLUTION { get; set; }

    /// <summary>솔루션이 있어야 여는 화면으로 표시한다. 메뉴를 만드는 자리에서 이어 쓴다.</summary>
    public MenuItemModel RequireSolution()
    {
        REQUIRES_SOLUTION = true;
        return this;
    }

    // ── AccordionControl Item 용 ─────────────────────────────────────────
    public virtual bool IsExpanded { get; set; }
    public virtual bool ShowInCollapsedMode { get; set; }

    [BindableProperty]
    public virtual bool CanSelect { get; set; }

    public virtual object? Icon { get; set; }

    public override string ToString() => MENU_NM;

    private ObservableCollection<MenuItemModel>? _children;
    public ObservableCollection<MenuItemModel> Children => _children ??= new ObservableCollection<MenuItemModel>();

    public void AddChildren(MenuItemModel child) => Children.Add(child);
}
