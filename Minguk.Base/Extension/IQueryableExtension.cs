using System.Data;
using System.Reflection;

using DevExpress.Data.Linq.Helpers;

using FastMember;

namespace Minguk.Base.Extension;

public static class IQueryableExtension
{
    // https://forums.asp.net/t/1892104.aspx?how+to+convert+anonymous+object+to+datatable
    public static DataTable ToDataTable(this IQueryable source, string tableName = "Master")
    {
        if (source == null) throw new ArgumentNullException();
        DataTable dataTable = new DataTable();
        if (source.Count() == 0) return dataTable;

        TypeAccessor? accessor = null;
        List<string>? names = null;
        object?[]? values = null;

        foreach (var row in source!)
        {
            if (values == null)
            {
                // blatently assume the list is homogeneous
                Type itemType = row.GetType();
                dataTable.TableName = tableName; // itemType.Name;
                names = new List<string>();
                foreach (var prop in itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    Type colType = prop.PropertyType;
                    if ((colType.IsGenericType) && (colType.GetGenericTypeDefinition() == typeof(Nullable<>)))
                    {
                        colType = colType.GetGenericArguments()[0];

                        names.Add(prop.Name);
                        DataColumn dataColumn = new DataColumn(prop.Name, colType);

                        dataTable.Columns.Add(dataColumn);
                    }
                    else if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                    {
                        names.Add(prop.Name);
                        dataTable.Columns.Add(prop.Name, prop.PropertyType);
                    }
                }
                names.TrimExcess();

                accessor = TypeAccessor.Create(itemType);
                values = new object[names.Count];
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = accessor?[row, names?[i]];
            }
            dataTable.Rows.Add(values);
        }
        return dataTable;
    }

    public static DataTable CopyToDataTable(this IQueryable? source, string tableName = "Master")
    {
        DataTable dt = new DataTable(tableName);
        PropertyInfo[]? columns = null;

        if (source == null) return dt;

        dt.BeginLoadData();
        foreach (var record in source)
        {
            if (columns == null)
            {
                columns = record.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (PropertyInfo GetProperty in columns)
                {
                    Type colType = GetProperty.PropertyType;

                    if ((colType.IsGenericType) && (colType.GetGenericTypeDefinition() == typeof(Nullable<>)))
                    {
                        colType = colType.GetGenericArguments()[0];
                    }

                    DataColumn dataColumn = new DataColumn(GetProperty.Name, colType);
                    dt.Columns.Add(dataColumn);
                }
            }

            DataRow dr = dt.NewRow();

            foreach (PropertyInfo pinfo in columns)
            {
                dr[pinfo.Name] = pinfo.GetValue(record, null) == null ? DBNull.Value : pinfo.GetValue(record, null);
            }

            dt.Rows.Add(dr);
        }
        dt.EndLoadData();

        return dt;
    }

    public static DataTable CopyToDataTable<T>(this IQueryable<T>? Linqlist, string tableName = "Master")
    {
        DataTable dt = new DataTable(tableName);
        PropertyInfo[]? columns = null;

        if (Linqlist == null) return dt;

        dt.BeginLoadData();
        foreach (T record in Linqlist)
        {
            if (columns == null)
            {
                columns = record?.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                if (columns != null)
                    foreach (PropertyInfo GetProperty in columns)
                    {
                        Type colType = GetProperty.PropertyType;

                        if ((colType.IsGenericType) && (colType.GetGenericTypeDefinition() == typeof(Nullable<>)))
                        {
                            colType = colType.GetGenericArguments()[0];
                        }

                        dt.Columns.Add(new DataColumn(GetProperty.Name, colType));
                    }
            }

            DataRow dr = dt.NewRow();

            if (columns != null)
                foreach (PropertyInfo pinfo in columns)
                {
                    dr[pinfo.Name] = pinfo.GetValue(record, null) == null ? DBNull.Value : pinfo.GetValue(record, null);
                }

            dt.Rows.Add(dr);
        }
        dt.EndLoadData();

        return dt;
    }
}
