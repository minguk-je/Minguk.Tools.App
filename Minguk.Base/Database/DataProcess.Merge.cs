using Microsoft.EntityFrameworkCore;

using System.Collections.ObjectModel;

namespace Minguk.Base.Database;

public partial class DataProcess
{
    /*
    public void MergeItemsSource<TEntity, TKey>(
        Microsoft.EntityFrameworkCore.DbContext dbContext,
        ObservableCollection<TEntity> itemsSource,
        IList<TEntity> freshItems,
        Func<TEntity, TKey> keySelector)
        where TEntity : class
        where TKey : notnull
    {
        void UpdateValues(TEntity existing, TEntity fresh)
        {
            var entry = dbContext.Entry(existing);
            if (entry.State == EntityState.Detached)
                dbContext.Attach(existing);

            // CLR 프로퍼티 setter를 직접 호출하여 값 복사
            // → SetProperty를 거치므로 PropertyChanged가 확실히 발생해 그리드가 갱신됨
            var changed = false;
            foreach (var property in entry.Metadata.GetProperties())
            {
                // shadow property 등 CLR 프로퍼티가 없는 경우는 건너뜀
                if (property.PropertyInfo == null)
                    continue;

                var freshValue = property.PropertyInfo.GetValue(fresh);
                var currentValue = property.PropertyInfo.GetValue(existing);

                if (Equals(currentValue, freshValue))
                    continue;

                property.PropertyInfo.SetValue(existing, freshValue);
                changed = true;
            }

            if (changed)
            {
                // 원본 스냅샷도 최신 값으로 동기화 → DetectChanges가 Modified로 오인하지 않음
                entry.OriginalValues.SetValues(fresh);
                entry.State = EntityState.Unchanged;
            }
        }

        MergeItemsSource(
            itemsSource,
            freshItems,
            keySelector,
            updateValues: UpdateValues,
            attachNew: fresh =>
            {
                // ItemsSource에는 없지만 tracker에 같은 키의 인스턴스가 남아있으면
                // (예: 목록만 Clear된 경우) Attach 충돌이 나므로 그 인스턴스를 재사용
                var freshKey = keySelector(fresh);
                var tracked = dbContext.ChangeTracker.Entries<TEntity>()
                    .FirstOrDefault(e => keySelector(e.Entity).Equals(freshKey));

                if (tracked != null)
                {
                    if (tracked.State == EntityState.Deleted)
                        tracked.State = EntityState.Unchanged; // 재조회 = DB 기준으로 리셋

                    UpdateValues(tracked.Entity, fresh);
                    return tracked.Entity;
                }

                dbContext.Attach(fresh); // 이후 그리드 편집/저장을 위해 추적 시작
                return fresh;
            },
            detachRemoved: removed =>
            {
                var entry = dbContext.Entry(removed);
                if (entry.State != EntityState.Detached)
                    entry.State = EntityState.Detached;
            });

        // 재조회 완료 → 행 편집 상태 초기화
        foreach (var item in itemsSource)
        {
            if (item is EditableRowBase { RowState: not RowStates.None } row)
                row.RowState = RowStates.None;
        }
    }

    public void MergeItemsSource<TEntity, TKey>(
        ObservableCollection<TEntity> itemsSource,
        IList<TEntity> freshItems,
        Func<TEntity, TKey> keySelector,
        Action<TEntity, TEntity> updateValues,
        Func<TEntity, TEntity>? attachNew = null,
        Action<TEntity>? detachRemoved = null)
        where TEntity : class
        where TKey : notnull
    {
        // DB에서 삭제된 항목 제거
        var freshKeys = freshItems.Select(keySelector).ToHashSet();
        foreach (var removed in itemsSource.Where(x => freshKeys.Contains(keySelector(x)) == false).ToList())
        {
            itemsSource.Remove(removed);
            detachRemoved?.Invoke(removed);
        }

        // 추가/수정 항목 반영
        var existingByKey = itemsSource.ToDictionary(keySelector);
        for (var i = 0; i < freshItems.Count; i++)
        {
            var fresh = freshItems[i];
            if (existingByKey.TryGetValue(keySelector(fresh), out var existing))
            {
                // 기존 인스턴스에 최신 값 복사 (인스턴스 보존)
                updateValues(existing, fresh);

                // 조회 순서에 맞게 위치 조정
                var currentIndex = itemsSource.IndexOf(existing);
                if (currentIndex != i)
                    itemsSource.Move(currentIndex, i);
            }
            else
            {
                // attachNew가 기존 추적 인스턴스를 돌려줄 수 있으므로 반환값을 삽입
                var toInsert = attachNew != null ? attachNew(fresh) : fresh;
                itemsSource.Insert(i, toInsert);
            }
        }
    }
    */
}
