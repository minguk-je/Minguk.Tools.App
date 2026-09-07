using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using Minguk.Base.Database;

namespace Minguk.Base.Extension;

public static class DbContextRowStateExtensions
{
    public static void EnableRowStateTracking(this DbContext dbContext, Action<EditableRowBase>? onRowStateChanged = null)
    {
        dbContext.ChangeTracker.Tracked += (_, e) => Apply(e.Entry, e.Entry.State, onRowStateChanged);
        dbContext.ChangeTracker.StateChanged += (_, e) => Apply(e.Entry, e.NewState, onRowStateChanged);
    }

    private static void Apply(EntityEntry entry, EntityState state, Action<EditableRowBase>? onRowStateChanged)
    {
        if (entry.Entity is not EditableRowBase row)
            return;

        var rowState = state switch
        {
            EntityState.Added => RowStates.Added,
            EntityState.Modified => RowStates.Modified,
            EntityState.Deleted => RowStates.Deleted,
            _ => RowStates.None
        };

        if (row.RowState == rowState)
            return;

        row.RowState = rowState;
        onRowStateChanged?.Invoke(row);
    }
}
