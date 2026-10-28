using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Hotel.Data;

/// <summary>DAL: only parameterized values; identifiers are internal constants.</summary>
public sealed class Database(string connectionString)
{
    public string ConnectionString { get; } = connectionString;
    public SqlConnection Open()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }
    public DataTable Query(string sql, params (string Name, object? Value)[] values)
    {
        using var connection = Open();
        return Query(connection, null, sql, values);
    }
    public int Execute(string sql, params (string Name, object? Value)[] values)
    {
        using var connection = Open();
        return Execute(connection, null, sql, values);
    }
    public T Transaction<T>(Func<SqlConnection, SqlTransaction, T> action)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Serialize conflicting hotel writes, including availability and catalog changes.
            Execute(connection, transaction, "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'HotelBTL.Write', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r<0 THROW 51000, N'Hệ thống đang bận, vui lòng thử lại.', 1;");
            var result = action(connection, transaction);
            transaction.Commit();
            return result;
        }
        catch { transaction.Rollback(); throw; }
    }
    public static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        foreach (var (name, value) in values)
        {
            if (value is DateTime timestamp)
            {
                // Match the schema's datetime2 columns without rounding DateTime to SQL datetime's 3 ms increments.
                var parameter = command.Parameters.Add(name, SqlDbType.DateTime2);
                parameter.Scale = 7;
                parameter.Value = timestamp;
            }
            else
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }
    public static DataTable Query(SqlConnection connection, SqlTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(connection, transaction, sql, values);
        using var reader = command.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }
    public static int Execute(SqlConnection connection, SqlTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(connection, transaction, sql, values);
        return command.ExecuteNonQuery();
    }
    public static T Scalar<T>(SqlConnection connection, SqlTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(connection, transaction, sql, values);
        return (T)Convert.ChangeType(command.ExecuteScalar() ?? throw new InvalidOperationException("Không tìm thấy dữ liệu."), typeof(T));
    }
    public void Initialize()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString);
        var name = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]{0,100}$"))
            throw new ArgumentException("Tên cơ sở dữ liệu chỉ gồm chữ, số và dấu gạch dưới.");
        builder.InitialCatalog = "master";
        using (var master = new SqlConnection(builder.ConnectionString))
        {
            master.Open();
            var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TaoCoSoDuLieu.sql"));
            using var create = Command(master, null, script, ("@DatabaseName", name));
            create.CommandTimeout = 120;
            create.ExecuteNonQuery();
        }
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            // Two first launches must not create the same tables at the same time.
            using var schemaLock = Command(connection, transaction, "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'HotelBTL.Schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000; IF @r<0 THROW 51000, N'Không thể khóa khởi tạo bảng dữ liệu. Vui lòng thử lại.', 1;");
            schemaLock.CommandTimeout = 120;
            schemaLock.ExecuteNonQuery();
            var path = Path.Combine(AppContext.BaseDirectory, "CauTrucDuLieu.sql");
            foreach (var batch in Regex.Split(File.ReadAllText(path), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch))
                    Execute(connection, transaction, batch);
            Execute(connection, transaction, "DECLARE @uq sysname; SELECT @uq = kc.name FROM sys.key_constraints kc JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id WHERE kc.parent_object_id = OBJECT_ID('dbo.Users') AND c.name = 'EmployeeId'; IF @uq IS NOT NULL EXEC('ALTER TABLE dbo.Users DROP CONSTRAINT [' + @uq + ']');");
            Execute(connection, transaction, "DECLARE @chk sysname; SELECT @chk = name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.Users') AND definition LIKE '%Role%'; IF @chk IS NOT NULL EXEC('ALTER TABLE dbo.Users DROP CONSTRAINT [' + @chk + ']'); ALTER TABLE dbo.Users ALTER COLUMN Role nvarchar(50) NOT NULL;");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }
}
