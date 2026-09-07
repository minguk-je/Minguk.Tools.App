using DevExpress.DataAccess.Native.Data;
using DevExpress.Mvvm;

using Minguk.Base.Extension;
using Minguk.Base.Views;

using Newtonsoft.Json;

using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using DataTable = System.Data.DataTable;

namespace Minguk.Base.Database;

/*
public partial class DataProcess : ViewModelBase, IDisposable
{
    public SqlConnection? SqlConnection { get => GetProperty(() => SqlConnection); set => SetProperty(() => SqlConnection, value); }
    public SqlDataAdapter SqlDataAdapter { get => GetProperty(() => SqlDataAdapter); set => SetProperty(() => SqlDataAdapter, value); }
    public SqlTransaction? SqlTransaction { get => GetProperty(() => SqlTransaction); set => SetProperty(() => SqlTransaction, value); }

    public SqlConnection? SaveSqlConnection { get => GetProperty(() => SaveSqlConnection); set => SetProperty(() => SaveSqlConnection, value); }
    public SqlTransaction? SaveSqlTransaction { get => GetProperty(() => SaveSqlTransaction); set => SetProperty(() => SaveSqlTransaction, value); }

    // 지정된 DataTable 의 변경 행마다 INSERT/UPDATE/DELETE 실행
    public int Update(bool isAcceptChanges = true)
    {
        int updateCount = 0;
        try
        {
            updateCount = SqlDataAdapter.Update(DataTable);
            if (isAcceptChanges)
                DataTable.AcceptChanges();
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return updateCount;
    }

    void SqlDataAdapter_RowUpdating(object sender, SqlRowUpdatingEventArgs e)
    {
        try
        {
            string message = $"/* {Name} : {Enum.GetName(typeof(StatementType), e.StatementType)} / {e.Row.Table.TableName} / {e.Row.RowState} #1#";

            Logger.Debug(message + SqlCommandDumper.GetCommandText(e.Command));
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void AcceptChanges() => DataTable.AcceptChanges();

    public void FillSchema() => SqlDataAdapter.FillSchema(DataTable, SchemaType.Source);

    public void Fill()
    {
        try
        {
            SetState(DataProcess.States.Query);
            DataTable.Clear();

            foreach (var dataRowProcess in DetailDataRowProcess)
                dataRowProcess.SetState(DataProcess.States.None);

            string message = $"/* {Name} - Fill #1#";
            string elapsedTime = "";
            try
            {
                Logger.Debug("{0}.FillSchema()", Name);
                SqlDataAdapter.FillSchema(DataTable, SchemaType.Source);

                Logger.Debug("{0}.Fill()", Name);
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();
                SqlDataAdapter.Fill(DataTable);
                stopWatch.Stop();

                TimeSpan ts = stopWatch.Elapsed;
                elapsedTime = $": {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000} #1#";
            }
            catch
            {
                throw;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(elapsedTime))
                    message = message.Replace("#1#", elapsedTime);

                string sql = SqlCommandDumper.GetCommandText(SqlDataAdapter.SelectCommand);
                Logger.Debug(message, sql);

                SetState(DataProcess.States.Browse);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void SetPermission(bool[] permissionArray)
    {
        if (permissionArray.Length == 4)
            SetPermission(permissionArray[0], permissionArray[1], permissionArray[2], permissionArray[3]);
    }

    public void SetPermission(bool isInsert, bool isUpdate, bool isDelete, bool isPrint)
    {
        CanInsert = isInsert;
        CanUpdate = isUpdate;
        CanDelete = isDelete;
        CanPrint = isPrint;
    }

    public bool IsTransaction() => SqlTransaction != null;

    public SqlTransaction? BeginTransaction()
    {
        try
        {
            if (SqlConnection != null)
            {
                if (SqlConnection.State != ConnectionState.Open)
                    SqlConnection.Open();

                SqlTransaction = SqlConnection.BeginTransaction();
                SqlDataAdapter.SelectCommand.Transaction = SqlTransaction;
                SqlDataAdapter.InsertCommand.Transaction = SqlTransaction;
                SqlDataAdapter.UpdateCommand.Transaction = SqlTransaction;
                SqlDataAdapter.DeleteCommand.Transaction = SqlTransaction;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        return SqlTransaction;
    }

    public void CommitTransaction()
    {
        try
        {
            if (SqlTransaction != null && SqlConnection != null)
            {
                SqlTransaction.Commit();
                SqlTransaction.Dispose();
                SqlTransaction = null;

                SqlDataAdapter.SelectCommand.Transaction = null;
                SqlDataAdapter.InsertCommand.Transaction = null;
                SqlDataAdapter.UpdateCommand.Transaction = null;
                SqlDataAdapter.DeleteCommand.Transaction = null;

                if (SqlConnection.State == ConnectionState.Open)
                    SqlConnection.Close();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void Rollback()
    {
        try
        {
            if (SqlTransaction != null && SqlConnection != null)
            {
                SqlTransaction.Rollback();
                SqlTransaction.Dispose();
                SqlTransaction = null;

                SqlDataAdapter.SelectCommand.Transaction = null;
                SqlDataAdapter.InsertCommand.Transaction = null;
                SqlDataAdapter.UpdateCommand.Transaction = null;
                SqlDataAdapter.DeleteCommand.Transaction = null;

                if (SqlConnection.State == ConnectionState.Open)
                    SqlConnection.Close();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void ConnectTransaction(DataProcess dataRowProcess)
    {
        try
        {
            SaveSqlConnection = SqlConnection;
            SaveSqlTransaction = SqlTransaction;

            SqlConnection = dataRowProcess.SqlConnection;
            SqlTransaction = dataRowProcess.SqlTransaction;

            SqlDataAdapter.SelectCommand.Connection = SqlConnection;
            SqlDataAdapter.InsertCommand.Connection = SqlConnection;
            SqlDataAdapter.UpdateCommand.Connection = SqlConnection;
            SqlDataAdapter.DeleteCommand.Connection = SqlConnection;

            SqlDataAdapter.SelectCommand.Transaction = SqlTransaction;
            SqlDataAdapter.InsertCommand.Transaction = SqlTransaction;
            SqlDataAdapter.UpdateCommand.Transaction = SqlTransaction;
            SqlDataAdapter.DeleteCommand.Transaction = SqlTransaction;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public void DisconnectTransaction()
    {
        try
        {
            SqlConnection = SaveSqlConnection;
            SqlTransaction = SaveSqlTransaction;

            SaveSqlConnection = null;
            SaveSqlTransaction = null;

            SqlDataAdapter.SelectCommand.Connection = SqlConnection;
            SqlDataAdapter.InsertCommand.Connection = SqlConnection;
            SqlDataAdapter.UpdateCommand.Connection = SqlConnection;
            SqlDataAdapter.DeleteCommand.Connection = SqlConnection;

            SqlDataAdapter.SelectCommand.Transaction = SqlTransaction;
            SqlDataAdapter.InsertCommand.Transaction = SqlTransaction;
            SqlDataAdapter.UpdateCommand.Transaction = SqlTransaction;
            SqlDataAdapter.DeleteCommand.Transaction = SqlTransaction;
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
    }

    public object? ExecuteScalar(string commandText, SqlParameter[]? parameters, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteScalar ");
        StringBuilder sql = new StringBuilder();

        object? obj = null;
        try
        {
            if (SqlConnection == null)
                throw new InvalidOperationException("SqlConnection is null.");

            bool isCustomOpen = false;
            using SqlCommand sqlCommand = SqlConnection.CreateCommand();

            sqlCommand.Transaction = SqlTransaction;
            sqlCommand.CommandType = commandType;
            sqlCommand.CommandText = commandText;

            if (parameters != null)
                foreach (SqlParameter parameter in parameters)
                {
                    sqlCommand.Parameters.Add(parameter);
                }

            sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

            if (sqlCommand.Connection.State != ConnectionState.Open)
            {
                sqlCommand.Connection.Open();
                isCustomOpen = true;
                Logger.Debug("ExecuteScalar : Custom Open");
            }

            stopWatch.Start();
            obj = sqlCommand.ExecuteScalar();
            stopWatch.Stop();

            if (isCustomOpen)
            {
                sqlCommand.Connection.Close();
                Logger.Debug("ExecuteScalar : Custom Close");
            }

            TimeSpan ts = stopWatch.Elapsed;
            message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message + commandText.TrimStart() + sql.ToString());
        }
        return obj;
    }

    public int ExecuteNonQuery(string commandText, SqlParameter[]? parameters, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteNonQuery ");
        StringBuilder sql = new StringBuilder();

        int result = 0;
        try
        {
            if (SqlConnection == null)
                throw new InvalidOperationException("SqlConnection is null.");

            bool isCustomOpen = false;
            using SqlCommand sqlCommand = SqlConnection.CreateCommand();

            sqlCommand.Transaction = SqlTransaction;
            sqlCommand.CommandType = commandType;
            sqlCommand.CommandText = commandText;

            if (parameters != null)
                foreach (SqlParameter parameter in parameters)
                {
                    sqlCommand.Parameters.Add(parameter);
                }

            sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

            if (sqlCommand.Connection.State != ConnectionState.Open)
            {
                sqlCommand.Connection.Open();
                isCustomOpen = true;
                Logger.Debug("ExecuteNonQuery : Custom Open");
            }

            stopWatch.Start();
            result = sqlCommand.ExecuteNonQuery();
            stopWatch.Stop();

            if (isCustomOpen)
            {
                sqlCommand.Connection.Close();
                Logger.Debug("ExecuteNonQuery : Custom Close");
            }

            TimeSpan ts = stopWatch.Elapsed;
            message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message + commandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public DataTable ExecuteQuery(string commandText, SqlParameter[]? parameters, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteQuery ");
        StringBuilder sql = new StringBuilder();

        DataTable result = new DataTable();
        try
        {
            if (SqlConnection == null)
                throw new InvalidOperationException("SqlConnection is null.");

            bool isCustomOpen = false;
            using SqlCommand sqlCommand = SqlConnection.CreateCommand();

            sqlCommand.Transaction = SqlTransaction;
            sqlCommand.CommandType = commandType;
            sqlCommand.CommandText = commandText;

            if (parameters != null)
                foreach (SqlParameter parameter in parameters)
                {
                    sqlCommand.Parameters.Add(parameter);
                }

            sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

            if (sqlCommand.Connection.State != ConnectionState.Open)
            {
                sqlCommand.Connection.Open();
                isCustomOpen = true;
                Logger.Debug("ExecuteQuery : Custom Open");
            }

            stopWatch.Start();
            using (SqlDataReader reader = sqlCommand.ExecuteReader(CommandBehavior.Default))
            {
                stopWatch.Stop();

                TimeSpan ts = stopWatch.Elapsed;
                message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);

                result.Load(reader);
                reader.Close();
            }

            if (isCustomOpen)
            {
                sqlCommand.Connection.Close();
                Logger.Debug("ExecuteQuery : Custom Close");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(JsonConvert.SerializeObject(ex));
            ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message + commandText.TrimStart() + sql.ToString());
        }
        return result;
    }
}
*/
