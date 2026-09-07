using DevExpress.Xpf.Grid;

namespace Minguk.Base.Extension;

public static class GridControlExtension
{
    public static List<int> GetDataRowHandles(this GridControl grid)
    {
        List<int> rowHandles = new List<int>();
        for (int i = 0; i < grid.VisibleRowCount; i++)
        {
            int rowHandle = grid.GetRowHandleByVisibleIndex(i);
            if (grid.IsGroupRowHandle(rowHandle))
            {
                if (!grid.IsGroupRowExpanded(rowHandle))
                {
                    rowHandles.AddRange(GetDataRowHandlesInGroup(grid, rowHandle));
                }
            }
            else
                rowHandles.Add(rowHandle);
        }
        return rowHandles;
    }

    private static List<int> GetDataRowHandlesInGroup(this GridControl grid, int groupRowHandle)
    {
        List<int> rowHandles = new List<int>();
        for (int i = 0; i < grid.GetChildRowCount(groupRowHandle); i++)
        {
            int rowHandle = grid.GetChildRowHandle(groupRowHandle, i);
            if (grid.IsGroupRowHandle(rowHandle))
            {
                rowHandles.AddRange(GetDataRowHandlesInGroup(grid, rowHandle));
            }
            else
                rowHandles.Add(rowHandle);
        }
        return rowHandles;
    }

    public static int GetVisibleGroupRowCount(this GridControl grid)
    {
        int count = 0;
        for (int i = 0; i < grid.VisibleRowCount; i++)
        {
            int rowHandle = grid.GetRowHandleByVisibleIndex(i);
            if (grid.IsGroupRowHandle(rowHandle))
                count++;
        }
        return count;
    }

    public static int GetTotalGroupRowCount(GridControl grid)
    {
        int groupRowCount = 0;
        for (int i = -1; grid.GetGroupRowValue(i) != null; i--)
            groupRowCount++;
        return groupRowCount;
    }

    public static List<int> GetVisibleGroupRowHandles(this GridControl grid)
    {
        List<int> rowHandles = new List<int>();
        for (int i = 0; i < grid.VisibleRowCount; i++)
        {
            int rowHandle = grid.GetRowHandleByVisibleIndex(i);
            if (grid.IsGroupRowHandle(rowHandle))
                rowHandles.Add(rowHandle);
        }
        return rowHandles;
    }
}