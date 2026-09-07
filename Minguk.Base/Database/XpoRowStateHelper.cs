using DevExpress.Xpo;

namespace Minguk.Base.Database;

/// <summary>
/// XPO 편집 화면의 행 표시기(A/U/D) 관리.
///
/// 표시 값은 <see cref="IEditableRow.RowState"/> 이고, 공용 행 표시기 템플릿이 이 값을 보고 그린다.
/// 삭제는 그 자리에서 지우지 않고 D 로 표시만 해 두었다가 저장 시점에 실제로 지운다.
///
/// 저장/취소 여부 판정도 UnitOfWork 가 아니라 이 RowState 를 기준으로 한다.
/// UnitOfWork.GetObjectsToSave 로 물어보면 커밋 직후 ItemsSource 를 다시 붙이는 과정에서
/// 그리드가 포커스 행 값을 되쓰며 다시 Dirty 로 잡혀, 저장 후에도 참을 돌려줄 때가 있다.
/// </summary>
public static class XpoRowStateHelper
{
    /// <summary>A/U/D 표시가 붙은 행이 하나라도 있는지.</summary>
    public static bool HasChangedRows<T>(XPCollection<T>? collection) where T : class, IEditableRow
    {
        return collection != null && collection.Any(row => row.RowState != RowStates.None);
    }

    /// <summary>
    /// 값이 바뀐 행 하나만 A/U 로 표시한다.
    /// 삭제 표시는 저장/취소 전까지 유지해야 하므로 덮어쓰지 않는다.
    /// (UnitOfWork.ObjectChanged 구독에서 호출한다)
    /// </summary>
    public static void MarkRowChanged(UnitOfWork? unitOfWork, object? changed)
    {
        if (unitOfWork == null || changed is not IEditableRow row)
            return;

        if (row.RowState == RowStates.Deleted)
            return;

        row.RowState = unitOfWork.IsNewObject(changed) ? RowStates.Added : RowStates.Modified;
    }

    /// <summary>
    /// 행을 삭제 표시한다. 실제 삭제는 저장 시점에 이뤄진다.
    /// 아직 저장되지 않은 신규 행은 DB 에 지울 대상이 없으므로 그 자리에서 버린다.
    /// </summary>
    public static void MarkRowDeleted<T>(UnitOfWork? unitOfWork, T row, XPCollection<T>? collection) where T : class, IEditableRow
    {
        if (row is not XPBaseObject persistent)
            return;

        if (unitOfWork != null && unitOfWork.IsNewObject(persistent))
        {
            persistent.Delete();

            if (collection != null && collection.Contains(row))
                collection.Remove(row);

            return;
        }

        row.RowState = RowStates.Deleted;
    }

    /// <summary>삭제 표시된 행을 실제로 삭제한다. (저장 직전에 호출)</summary>
    public static void DeleteMarkedRows<T>(XPCollection<T>? collection) where T : class, IEditableRow
    {
        if (collection == null)
            return;

        foreach (var row in collection.ToList())
        {
            if (row.RowState == RowStates.Deleted && row is XPBaseObject persistent)
                persistent.Delete();
        }
    }

    /// <summary>
    /// 저장/취소 직후처럼 '모두 깨끗하다'가 확정인 시점에 행 표시를 일괄로 지운다.
    ///
    /// 여기서 IsObjectToSave 로 다시 판정하면 안 된다.
    /// RowState 를 바꾸면서 발생한 변경 알림 때문에 해당 행이 다시 Dirty 로 잡혀
    /// A 였던 행이 U 로 남는다. (복사 → 저장 에서 재현)
    /// </summary>
    public static void ResetRowStates<T>(XPCollection<T>? collection) where T : class, IEditableRow
    {
        if (collection == null)
            return;

        foreach (var row in collection)
            row.RowState = RowStates.None;
    }
}
