using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text;

namespace Minguk.Base.Database;

/*
public class ConnectionHelper : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static string DataSource { get; set; } = String.Empty;
    public static string Database { get; set; } = String.Empty;
    public static string UserID { get; set; } = String.Empty;
    public static string Password { get; set; } = String.Empty;
    public static bool Pooling { get; set; } = true;
    private static int DefaultTimeOut { get; set; } = 900;

    public ConnectionHelper()
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
        }
    }

    public static string ConnectionString
    {
        get
        {
            // Integrated Security=True;    통합연결
            // Persist Security Info=True;  암호연결

            if (string.IsNullOrWhiteSpace(DataSource))
            {
                return string.Empty;
            }

            SqlConnectionStringBuilder sqlBuilder = new SqlConnectionStringBuilder
            {
                DataSource = DataSource,
                InitialCatalog = Database,
                UserID = UserID,
                Password = Password,
                PersistSecurityInfo = true,
                ConnectTimeout = 20,
                Pooling = Pooling
            };

            return sqlBuilder.ToString();
        }
    }

    public static SqlConnection CreateConnection(bool isConnection = false)
    {
        SqlConnection? sqlConnection = null;
        try
        {
            sqlConnection = new SqlConnection(ConnectionString);
            sqlConnection.InfoMessage += static (object obj, SqlInfoMessageEventArgs ex) =>
            {
                // ExceptionViewer.Show(ex, MethodBase.GetCurrentMethod()?.GetDeclaringName());
                Logger.Error(ex.Message);
            };

            if (isConnection)
            {
                sqlConnection.Open();
            }
        }
        catch
        {
            throw;
        }

        return sqlConnection;
    }

    public static SqlCommand CreateCommand(SqlConnection sqlConnection, string commandText = "", CommandType commandType = CommandType.Text)
    {
        SqlCommand command = sqlConnection.CreateCommand();
        command.CommandTimeout = DefaultTimeOut;
        command.CommandType = commandType;
        command.CommandText = commandText;

        return command;
    }

    public static SqlCommand CreateCommand(SqlTransaction sqlTransaction, string commandText = "", CommandType commandType = CommandType.Text)
    {
        SqlCommand command = sqlTransaction.Connection.CreateCommand();
        command.CommandTimeout = DefaultTimeOut;
        command.Transaction = sqlTransaction;
        command.CommandType = commandType;
        command.CommandText = commandText;
        return command;
    }

    public static object? ExecuteScalar(SqlCommand sqlCommand, SqlParameterCollection parameters)
    {
        SqlParameter[] sqlParams = new SqlParameter[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            sqlParams[i] = (SqlParameter)((ICloneable)parameters[i]).Clone();
        }
        return ExecuteScalar(sqlCommand, sqlParams);
    }

    public static object? ExecuteScalar(SqlCommand sqlCommand, SqlParameter[]? sqlParameters = null)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteScalar ");
        StringBuilder sql = new StringBuilder();

        object? result = null;
        try
        {
            using (SqlConnection connection = CreateConnection())
            {
                connection.Open();

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                if (sqlParameters != null)
                {
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }
                }

                stopWatch.Start();
                result = sqlCommand.ExecuteScalar();
                stopWatch.Stop();

                TimeSpan ts = stopWatch.Elapsed;
                message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);

                connection.Close();
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + sqlCommand.CommandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public static object? ExecuteScalar(SqlTransaction transaction, string commandText, SqlParameter[]? sqlParameters, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteScalar ");
        StringBuilder sql = new StringBuilder();

        object? result = null;
        try
        {
            using (SqlCommand sqlCommand = CreateCommand(transaction.Connection))
            {
                sqlCommand.Transaction = transaction;
                sqlCommand.CommandType = commandType;
                sqlCommand.CommandText = commandText;

                if (sqlParameters != null)
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                stopWatch.Start();
                result = sqlCommand.ExecuteScalar();
                stopWatch.Stop();

                TimeSpan ts = stopWatch.Elapsed;
                message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + commandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public static object? ExecuteScalar(string commandText, SqlParameterCollection sqlParameters, CommandType commandType = CommandType.Text)
    {
        SqlParameter[] sqlParams = new SqlParameter[sqlParameters.Count];
        for (int i = 0; i < sqlParameters.Count; i++)
        {
            sqlParams[i] = (SqlParameter)((ICloneable)sqlParameters[i]).Clone();
        }
        return ExecuteScalar(commandText, sqlParams, commandType);
    }

    public static object? ExecuteScalar(string commandText, SqlParameter[]? sqlParameters, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteScalar ");
        StringBuilder sql = new StringBuilder();

        object? result = null;
        try
        {
            using (SqlConnection connection = CreateConnection())
            using (SqlCommand sqlCommand = CreateCommand(connection))
            {
                connection.Open();

                sqlCommand.CommandType = commandType;
                sqlCommand.CommandText = commandText;

                if (sqlParameters != null)
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                stopWatch.Start();
                result = sqlCommand.ExecuteScalar();
                stopWatch.Stop();

                TimeSpan ts = stopWatch.Elapsed;
                message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);

                connection.Close();
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + commandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public static int ExecuteNonQuery(SqlConnection sqlConnection, string commandText, SqlParameterCollection sqlParameters, CommandType commandType = CommandType.Text)
    {
        SqlParameter[] sqlParams = new SqlParameter[sqlParameters.Count];
        for (int i = 0; i < sqlParameters.Count; i++)
        {
            sqlParams[i] = (SqlParameter)((ICloneable)sqlParameters[i]).Clone();
        }
        return ExecuteNonQuery(sqlConnection, commandText, sqlParams, commandType);
    }

    public static int ExecuteNonQuery(SqlTransaction sqlTransaction, string commandText, SqlParameterCollection sqlParameters, CommandType commandType = CommandType.Text)
    {
        SqlParameter[] sqlParams = new SqlParameter[sqlParameters.Count];
        for (int i = 0; i < sqlParameters.Count; i++)
        {
            sqlParams[i] = (SqlParameter)((ICloneable)sqlParameters[i]).Clone();
        }
        return ExecuteNonQuery(sqlTransaction, commandText, sqlParams, commandType);
    }

    public static int ExecuteNonQuery(SqlConnection sqlConnection, string commandText, SqlParameter[]? sqlParameters = null, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteNonQuery ");
        StringBuilder sql = new StringBuilder();

        int result = 0;
        try
        {
            using (SqlCommand sqlCommand = CreateCommand(sqlConnection))
            {
                sqlCommand.CommandType = commandType;
                sqlCommand.CommandText = commandText;

                if (sqlParameters != null)
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                stopWatch.Start();
                result = sqlCommand.ExecuteNonQuery();
                stopWatch.Stop();

                TimeSpan ts = stopWatch.Elapsed;
                message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + commandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public static int ExecuteNonQuery(SqlTransaction sqlTransaction, string commandText, SqlParameter[]? sqlParameters = null, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteNonQuery ");
        StringBuilder sql = new StringBuilder();

        int result = 0;
        try
        {
            using (SqlCommand sqlCommand = CreateCommand(sqlTransaction.Connection))
            {
                sqlCommand.Transaction = sqlTransaction;
                sqlCommand.CommandType = commandType;
                sqlCommand.CommandText = commandText;

                if (sqlParameters != null)
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                stopWatch.Start();
                result = sqlCommand.ExecuteNonQuery();
                stopWatch.Stop();

                TimeSpan ts = stopWatch.Elapsed;
                message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + commandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public static int ExecuteNonQuery(string commandText, SqlParameterCollection sqlParameters, CommandType commandType = CommandType.Text)
    {
        SqlParameter[] sqlParams = new SqlParameter[sqlParameters.Count];
        for (int i = 0; i < sqlParameters.Count; i++)
        {
            sqlParams[i] = (SqlParameter)((ICloneable)sqlParameters[i]).Clone();
        }
        return ExecuteNonQuery(commandText, sqlParams, commandType);
    }

    public static int ExecuteNonQuery(string commandText, SqlParameter[]? sqlParameters = null, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteNonQuery ");
        StringBuilder sql = new StringBuilder();

        int result = 0;
        try
        {
            using (SqlConnection connection = CreateConnection())
            using (SqlCommand sqlCommand = CreateCommand(connection))
            {
                connection.Open();

                sqlCommand.CommandType = commandType;
                sqlCommand.CommandText = commandText;

                if (sqlParameters != null)
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                stopWatch.Start();
                result = sqlCommand.ExecuteNonQuery();
                stopWatch.Stop();

                TimeSpan ts = stopWatch.Elapsed;
                message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);

                connection.Close();
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + commandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public static DataTable ExecuteQuery(SqlConnection sqlConnection, string commandText, SqlParameterCollection sqlParameters, CommandType commandType = CommandType.Text)
    {
        SqlParameter[] sqlParams = new SqlParameter[sqlParameters.Count];
        for (int i = 0; i < sqlParameters.Count; i++)
        {
            sqlParams[i] = (SqlParameter)((ICloneable)sqlParameters[i]).Clone();
        }
        return ExecuteQuery(sqlConnection, commandText, sqlParams, commandType);
    }

    public static DataTable ExecuteQuery(SqlConnection sqlConnection, String sqlCommandText, SqlParameter[]? sqlParameters = null, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteQuery ");
        StringBuilder sql = new StringBuilder();

        DataTable result = new DataTable();
        try
        {
            using (SqlCommand sqlCommand = sqlConnection.CreateCommand())
            {
                sqlCommand.CommandTimeout = DefaultTimeOut;
                sqlCommand.CommandText = sqlCommandText;
                sqlCommand.CommandType = commandType;

                if (sqlParameters != null)
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                stopWatch.Start();
                using (SqlDataReader reader = sqlCommand.ExecuteReader())
                {
                    stopWatch.Stop();

                    TimeSpan ts = stopWatch.Elapsed;
                    message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);

                    result.Load(reader);
                    reader.Close();
                }
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + sqlCommandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public static DataTable ExecuteQuery(SqlTransaction sqlTransaction, string commandText, SqlParameterCollection sqlParameters, CommandType commandType = CommandType.Text)
    {
        SqlParameter[] sqlParams = new SqlParameter[sqlParameters.Count];
        for (int i = 0; i < sqlParameters.Count; i++)
        {
            sqlParams[i] = (SqlParameter)((ICloneable)sqlParameters[i]).Clone();
        }
        return ExecuteQuery(sqlTransaction, commandText, sqlParams, commandType);
    }

    public static DataTable ExecuteQuery(SqlTransaction sqlTransaction, String commandText, SqlParameter[]? sqlParameters = null, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteQuery ");
        StringBuilder sql = new StringBuilder();

        DataTable dataTable = new DataTable();
        try
        {
            using (SqlCommand sqlCommand = sqlTransaction.Connection.CreateCommand())
            {
                sqlCommand.Transaction = sqlTransaction;
                sqlCommand.CommandTimeout = DefaultTimeOut;
                sqlCommand.CommandText = commandText;
                sqlCommand.CommandType = commandType;

                if (sqlParameters != null)
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                stopWatch.Start();
                using (SqlDataReader reader = sqlCommand.ExecuteReader())
                {
                    stopWatch.Stop();

                    TimeSpan ts = stopWatch.Elapsed;
                    message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);

                    dataTable.Load(reader);
                    reader.Close();
                }
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + commandText.TrimStart() + sql.ToString());
        }
        return dataTable;
    }

    public static DataTable ExecuteQuery(string commandText, SqlParameterCollection sqlParameters, CommandType commandType = CommandType.Text)
    {
        SqlParameter[] sqlParams = new SqlParameter[sqlParameters.Count];
        for (int i = 0; i < sqlParameters.Count; i++)
        {
            sqlParams[i] = (SqlParameter)((ICloneable)sqlParameters[i]).Clone();
        }
        return ExecuteQuery(commandText, sqlParams, commandType);
    }

    public static DataTable ExecuteQuery(string commandText, SqlParameter[]? sqlParameters = null, CommandType commandType = CommandType.Text)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteQuery ");
        StringBuilder sql = new StringBuilder();

        DataTable result = new DataTable();
        try
        {
            using (SqlConnection connection = CreateConnection(true))
            using (SqlCommand sqlCommand = CreateCommand(connection))
            {
                sqlCommand.CommandText = commandText;
                sqlCommand.CommandType = commandType;

                if (sqlParameters != null)
                    foreach (SqlParameter parameter in sqlParameters)
                    {
                        sqlCommand.Parameters.Add(parameter);
                    }

                sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

                stopWatch.Start();
                using (SqlDataReader reader = sqlCommand.ExecuteReader(CommandBehavior.CloseConnection))
                {
                    stopWatch.Stop();

                    TimeSpan ts = stopWatch.Elapsed;
                    message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);

                    result.Load(reader);
                    reader.Close();
                }

                connection.Close();
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + commandText.TrimStart() + sql.ToString());
        }
        return result;
    }

    public static DataTable ExecuteQuery(SqlCommand sqlCommand)
    {
        Stopwatch stopWatch = new Stopwatch();
        StringBuilder message = new StringBuilder("/* ExecuteQuery ");
        StringBuilder sql = new StringBuilder();

        DataTable dataTable = new DataTable();
        try
        {
            sql.Append(SqlCommandDumper.GetCommandText(sqlCommand));

            stopWatch.Start();
            using (SqlDataReader reader = sqlCommand.ExecuteReader())
            {
                stopWatch.Stop();
                dataTable.Load(reader);
                reader.Close();
            }

            TimeSpan ts = stopWatch.Elapsed;
            message.AppendFormat(": {0:00}:{1:00}:{2:00}.{3:000} ", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds);
        }
        catch
        {
            throw;
        }
        finally
        {
            message.Append("#1# ");
            Logger.Debug(message.ToString() + sqlCommand.CommandText.TrimStart() + sql.ToString());
        }
        return dataTable;
    }
}
*/
