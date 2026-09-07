using Minguk.Base.Controls;

using Newtonsoft.Json;

using NLog;

using System.Collections;
using System.Windows.Threading;

namespace Minguk.Base.Extension;

/// <summary>
/// 편집 그리드에서 저장/취소 흐름에 함께 쓰이는 공통 처리.
/// (ItemsSource 를 갈아끼우는 화면이면 ORM 종류와 무관하게 필요하다)
/// </summary>
public static class BaseTableViewExtension
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 열려 있는 셀 편집기를 값까지 반영하고 닫는다.
    ///
    /// CommitEditing 은 값을 행에 밀어 넣을 뿐 편집기를 닫지는 않는다.
    /// 편집기가 열린 채로 ItemsSource 를 갈아끼우면 그리드가 다시 붙는 과정에서
    /// 편집기 값이 행에 한 번 더 쓰이고, 그 순간 방금 저장한 객체가 다시 Dirty 로 잡힌다.
    /// 그러면 행 표시기가 U 로 남는다. (복사 → 저장 에서 잘 재현된다)
    /// </summary>
    public static void CloseOpenEditor(this BaseTableView? view)
    {
        if (view?.ActiveEditor == null)
            return;

        view.CommitEditing();
        view.HideEditor();
    }

    /// <summary>
    /// 컬렉션을 새로 만든 뒤, 보고 있던 행/열로 포커스를 되돌린다.
    ///
    /// ItemsSource 를 갈아끼운 직후에는 그리드가 아직 새 행을 만들지 않아 RowHandle 이 유효하지 않다.
    /// 그래서 행 배치가 끝나는 Render 이후로 미뤄서 포커스를 잡는다.
    /// FocusedRowHandle 을 지정하면 해당 행이 화면에 보이도록 스크롤도 함께 이뤄진다.
    /// </summary>
    public static void RestoreFocusedCell(
        this BaseTableView? view,
        BaseGridControl? grid,
        IList? collection,
        object? row,
        string? columnFieldName)
    {
        if (grid == null || view == null || collection == null || row == null)
            return;

        grid.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    var listIndex = collection.IndexOf(row);
                    if (listIndex < 0)
                        return;

                    int rowHandle = grid.GetRowHandleByListIndex(listIndex);
                    view.FocusedRowHandle = rowHandle;
                    view.ScrollIntoView(rowHandle);

                    if (string.IsNullOrEmpty(columnFieldName))
                        return;

                    var column = grid.Columns[columnFieldName];
                    if (column != null)
                        grid.CurrentColumn = column;
                }
                catch (Exception ex)
                {
                    Logger.Error(JsonConvert.SerializeObject(ex));
                }
            }), DispatcherPriority.Render);
    }

    /// <summary>
    /// 지정한 행으로 포커스를 옮기고 첫 편집 가능 컬럼의 편집기를 연다. (신규/복사 행 입력용)
    ///
    /// ShowEditor 는 행/열 배치가 끝난 뒤에 호출해야 셀 편집기가 뜬다.
    /// </summary>
    public static void FocusRowAndShowEditor(
        this BaseTableView? view,
        BaseGridControl? grid,
        IList? collection,
        object? row)
    {
        if (grid == null || view == null || collection == null || row == null)
            return;

        var listIndex = collection.IndexOf(row);
        if (listIndex < 0)
            return;

        view.FocusedRowHandle = grid.GetRowHandleByListIndex(listIndex);

        var firstEditable = view.VisibleColumns.FirstOrDefault(c => !c.ReadOnly);
        if (firstEditable != null)
            grid.CurrentColumn = firstEditable;

        grid.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    grid.Focus();
                    view.ShowEditor();
                }
                catch (Exception ex)
                {
                    Logger.Error(JsonConvert.SerializeObject(ex));
                }
            }), DispatcherPriority.Render);
    }
}
