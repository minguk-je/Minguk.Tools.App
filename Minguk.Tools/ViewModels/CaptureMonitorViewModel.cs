using System;
using System.Collections.ObjectModel;
using System.Linq;

using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;

using Minguk.Image;

namespace Minguk.Tools.ViewModels;

/// <summary>
/// 캡처 화면. 창/모니터를 잡아 미리보기에 올리고, 한 장을 데이터셋에 담거나 파일로 떨어뜨리고,
/// 1초 통계를 표로 본다. <b>순수하게 잡는 일만 한다</b> - 몹 찾기·글자 읽기·스크립트는 편집 화면이다.
/// </summary>
/// <remarks>
/// 잡는 일은 <see cref="CaptureViewModelBase"/> 에 있다. 이 파일에 남은 것은 이 화면만의 것 -
/// 통계 표(마지막 1초 한 줄)와 그 표의 열 배치 저장·복원이다.
/// </remarks>
public partial class CaptureMonitorViewModel : CaptureViewModelBase
{
    /// <summary>그리드에 남겨 둘 줄 수. 오래 켜 두면 메모리를 먹으니 잘라 낸다.</summary>
    // 마지막 한 줄만 둔다. 600줄을 쌓아 봐야 보는 것은 맨 위 한 줄이었고("마지막 것만 봐도 될 것 같아"),
    // 그 자리를 미리보기에 주는 편이 낫다. 비고에 적히던 알림은 상태 줄과 아래 바로 간다.
    private const int MaxRows = 1;

    /// <summary>통계 그리드의 뷰. 새 줄이 들어올 때 맨 위를 유지하려고 들고 있는다.</summary>
    private DevExpress.Xpf.Grid.TableView? _gridView;

    /// <summary>
    /// 그리드의 열 너비·순서·정렬·필터를 문자열로 뽑고 되돌린다.
    /// View 의 &lt;dxmvvm:LayoutSerializationService x:Name="GridLayoutService" /&gt; 가 실체다.
    /// </summary>
    private ILayoutSerializationService GridLayoutService
        => ServiceContainer.GetService<ILayoutSerializationService>("GridLayoutService");

    /// <summary>
    /// 그리드 열 구성의 판 번호. 열을 추가·삭제·개명하면 올린다.
    ///
    /// 저장된 레이아웃은 그때의 열 구성을 담고 있어서, 열이 바뀐 뒤 그대로 되돌리면
    /// 새 열이 숨겨진 채로 나온다. 판이 다르면 저장본을 버리고 기본 배치로 시작한다.
    /// </summary>
    private const int GridLayoutVersion = 2;

    /// <summary>열 너비를 내용에 맞춘다. 끄면 사용자가 조절한 너비가 유지된다.</summary>
    public bool IsColumnAutoWidth
    {
        get => GetProperty(() => IsColumnAutoWidth);
        set => SetProperty(() => IsColumnAutoWidth, value);
    }

    public DelegateCommand DoClearCommand { get; }

    public virtual ObservableCollection<FrameLogRow> Rows { get; set; } = new();

    public static CaptureMonitorViewModel Create() => ViewModelSource.Create(() => new CaptureMonitorViewModel());

    public CaptureMonitorViewModel()
    {
        Caption = "캡처";
        CaptionImage = FreeImage.Instance?.CacheImageSource("axialis/basic/16x16/screen.png");

        DoClearCommand = new DelegateCommand(() => Rows.Clear(), false);
    }

    protected override void InitializeControls()
    {
        base.InitializeControls();

        _gridView = FindControl<DevExpress.Xpf.Grid.GridControl>("GridObjectService")?.View as DevExpress.Xpf.Grid.TableView;
    }

    protected override void RestoreSettings()
    {
        base.RestoreSettings();

        // 자동 너비와 배치 복원은 반드시 이 순서로, 그리드가 자리를 잡은 뒤에 넣는다.
        //
        // IsColumnAutoWidth 는 첨부 속성을 거쳐 ApplyColumnAutoWidth 를 부르는데,
        // 그게 모든 열의 Width 를 "지금 그려진 너비(ActualWidth)"로 고정해 버린다.
        // 배치를 먼저 복원해도 이게 나중에 돌면 복원한 너비가 그대로 지워진다.
        // ContextIdle 은 Background 보다 낮다. 그리드 로딩과 그에 딸린 바인딩 적용
        // (IsColumnAutoWidth -> ApplyColumnAutoWidth, 이게 열 너비를 다시 쓴다)이
        // 모두 끝난 뒤에 우리 배치를 얹기 위해 이 우선순위를 쓴다.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                IsColumnAutoWidth = GetSetting(nameof(IsColumnAutoWidth), false);
                RestoreGridLayout();
            }));
    }

    protected override void SaveSettings()
    {
        base.SaveSettings();

        SetSetting(nameof(IsColumnAutoWidth), IsColumnAutoWidth);

        try
        {
            SetSetting(nameof(GridLayoutService), GridLayoutService.Serialize());
            SetSetting(nameof(GridLayoutVersion), GridLayoutVersion);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "그리드 상태 저장 실패");
        }
    }

    /// <summary>1초 요약을 표 맨 위에 끼운다. 미리보기 fps 는 바탕이 먼저 채운다.</summary>
    protected override void OnStatisticsRow(FrameLogRow row)
    {
        base.OnStatisticsRow(row);

        Rows.Insert(0, row);

        while (Rows.Count > MaxRows)
            Rows.RemoveAt(Rows.Count - 1);

        KeepGridAtTop();
    }

    /// <summary>
    /// 그리드를 항상 맨 위가 보이게 유지한다.
    ///
    /// 새 줄은 0번 자리에 끼워 넣는다. 그러면 보고 있던 줄이 한 칸씩 아래로 밀리고
    /// 포커스도 그 줄을 따라 내려가서, 가만히 둬도 화면이 계속 흘러내린다.
    /// 최신 줄을 보는 화면이므로 맨 위에 붙여 둔다.
    ///
    /// 값이 이미 0 일 때는 건드리지 않는다. 1초마다 같은 값을 다시 넣으면
    /// 그때마다 포커스 변경이 돌아 사용자가 고른 셀이 풀린다.
    /// </summary>
    private void KeepGridAtTop()
    {
        if (_gridView is null)
            return;

        if (_gridView.TopRowIndex != 0)
            _gridView.TopRowIndex = 0;

        if (_gridView.FocusedRowHandle != 0)
            _gridView.FocusedRowHandle = 0;
    }

    /// <summary>
    /// 지난번 그리드 상태를 되돌린다.
    ///
    /// 저장본이 깨져 있거나 열 구성이 바뀌었으면 예외가 난다. 그때는 그냥 기본 배치로 둔다 —
    /// 그리드 하나 때문에 화면 전체가 안 열리면 곤란하다.
    /// </summary>
    private void RestoreGridLayout()
    {
        if (GetSetting(nameof(GridLayoutVersion), 0) != GridLayoutVersion)
        {
            Logger.Debug("그리드 열 구성이 바뀌었다. 저장된 배치를 버리고 기본으로 시작한다.");
            return;
        }

        var layout = GetSetting(nameof(GridLayoutService), string.Empty);
        if (string.IsNullOrEmpty(layout))
            return;

        try
        {
            // 저장된 배치에는 검색 창이 펼쳐져 있었는지(ActualShowSearchPanel)도 들어 있다. 그대로 복원하면 XAML 의
            // ShowSearchPanelMode=Never 를 무시하고 펼친 채로 굳는다 - 모드를 다시 놓고 HideSearchPanel 을 불러도,
            // DXSerializer.AllowProperty 로 막아도 안 됐다. 그래서 그 항목을 글에서 지우고 복원한다.
            // 마지막 한 줄만 보는 통계 표에 검색 창은 필요 없다.
            layout = System.Text.RegularExpressions.Regex.Replace(layout, "<property name=\"ActualShowSearchPanel\">[^<]*</property>", string.Empty);

            GridLayoutService.Deserialize(layout);

            var grid = FindControl<DevExpress.Xpf.Grid.GridControl>("GridObjectService");

            var widths = grid is null
                ? "(그리드 못 잡음)"
                : string.Join(", ", grid.Columns.Select(column =>
                    $"{column.FieldName}:{column.Width.Value}/{column.Width.UnitType}/실제{column.ActualWidth:n0}"));

            Logger.Debug($"그리드 상태 복원 완료. 너비=[{widths}]");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "그리드 상태 복원 실패. 기본 배치로 시작한다.");
        }
    }
}
