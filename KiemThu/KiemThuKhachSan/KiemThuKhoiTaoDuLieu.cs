using System.Data;
using System.Text.RegularExpressions;
using Hotel.Business;
using Hotel.Core;
using Hotel.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Hotel.Tests;

public sealed class DatabaseInitializationTests
{
    private static SqlConnectionStringBuilder Connection(string database) => new(
        Environment.GetEnvironmentVariable("HOTEL_TEST_CONNECTION")
        ?? "Server=lpc:.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True")
    {
        InitialCatalog = database
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingPhysicalFile_DoesNotBlockNewDatabase_OrOverwriteOldData(bool standaloneScript)
    {
        var name = "HotelManagementBTL_Collision_" + Guid.NewGuid().ToString("N")[..10];
        var ownerName = name + "_Owner";
        using var master = new SqlConnection(Connection("master").ConnectionString);
        master.Open();
        var folder = Database.Scalar<string>(master, null, "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath'))");
        // Keep another database in the path CREATE DATABASE <name> would normally use.
        var occupiedPath = Path.Combine(folder, name + ".mdf");
        var logPath = Path.Combine(folder, name + "_log.ldf");
        Database.Execute(master, null, $"CREATE DATABASE [{ownerName}] ON (NAME=N'{ownerName}', FILENAME=N'{occupiedPath.Replace("'", "''")}') LOG ON (NAME=N'{ownerName}_log', FILENAME=N'{logPath.Replace("'", "''")}')");
        var owner = new Database(Connection(ownerName).ConnectionString);
        owner.Execute("CREATE TABLE dbo.PreservedData(Value int NOT NULL); INSERT dbo.PreservedData VALUES(123456)");
        var collision = Assert.Throws<SqlException>(() => Database.Execute(master, null, $"CREATE DATABASE [{name}]"));
        Assert.Contains(collision.Errors.Cast<SqlError>(), error => error.Number == 5170);
        var db = new Database(Connection(name).ConnectionString);
        var service = new HotelService(db);

        void Initialize()
        {
            if (!standaloneScript)
            {
                service.Initialize();
                return;
            }
            using var installer = new SqlConnection(Connection("master").ConnectionString);
            installer.Open();
            var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CaiDat.sql")).Replace("HotelManagementBTL", name);
            foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch))
                    Database.Execute(installer, null, batch);
        }

        Initialize();
        var admin = service.Login("admin", "Admin@123");
        var customer = service.SaveCatalog(admin, CatalogKind.Customers, null,
            new()
            {
                ["FullName"] = "Dữ liệu cần giữ",
                ["Phone"] = "0909999888"
            });
        var createdPath = db.Query("SELECT physical_name FROM sys.database_files WHERE type=0").Rows[0].Field<string>("physical_name");
        Assert.False(string.Equals(occupiedPath, createdPath, StringComparison.OrdinalIgnoreCase));
        Initialize();
        Assert.Equal("Dữ liệu cần giữ", service.CatalogRecord(admin, CatalogKind.Customers, customer)["FullName"]);
        Assert.Equal(123456, owner.Query("SELECT Value FROM dbo.PreservedData").Rows[0].Field<int>("Value"));
        Assert.Equal(createdPath, db.Query("SELECT physical_name FROM sys.database_files WHERE type=0").Rows[0].Field<string>("physical_name"));
    }

    [Fact]
    public async Task ConcurrentFirstLaunch_CreatesOneDatabaseAndOneSeed()
    {
        var name = "HotelManagementBTL_Concurrent_" + Guid.NewGuid().ToString("N")[..10];
        var db = new Database(Connection(name).ConnectionString);
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            start.Wait();
            new HotelService(db).Initialize();
        })).ToArray();
        start.Set();
        await Task.WhenAll(tasks);
        Assert.Equal(2, db.Query("SELECT * FROM dbo.Users").Rows.Count);
        Assert.Equal(8, db.Query("SELECT * FROM dbo.Rooms").Rows.Count);
        Assert.Equal("Quản lý", new HotelService(db).Login("admin", "Admin@123").Role);
    }
}
