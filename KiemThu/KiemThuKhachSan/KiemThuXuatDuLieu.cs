using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Hotel.Core;
using Hotel.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Hotel.Tests;

public sealed class DataExportTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture f = fixture;

    private int UpgradedStay()
    {
        var original = f.Room(500000);
        var upgraded = f.Room(800000);
        var stay = f.Stay(original, 200000);
        f.Service.UpgradeRoom(f.Reception, f.Service.PreviewRoomUpgrade(f.Reception, stay, original, upgraded));
        return stay;
    }

    [Fact]
    public async Task ExportedUpgradeSnapshot_PreservesStableRoomStaysHistoryAndInvoiceFees()
    {
        var stay = UpgradedStay();
        f.Service.AddService(f.Reception, stay, 1, 2);
        var paid = f.Stay(f.Room(450000));
        var oldInvoice = f.Service.Checkout(f.Admin, f.Service.PreviewCheckout(f.Admin, paid, 0, 0), "Tiền mặt", "Export fixture");
        var oldDocument = f.Service.Invoice(f.Admin, oldInvoice);
        var quoteBefore = f.Service.PreviewCheckout(f.Admin, stay, 0, 0);
        var before = Snapshot(f.Db);
        var sql = await Export();
        AssertSnapshot(before, f.Db);
        Assert.Contains("INSERT INTO dbo.[RoomUpgrades]", sql);
        Assert.Contains("SET IDENTITY_INSERT dbo.[BookingRooms] ON", sql);

        var restored = new DatabaseFixture();
        ExecuteImport(restored.Db, sql);
        AssertSnapshot(before, restored.Db);
        var user = restored.Service.Login("admin", "Admin@123");
        var restoredQuote = restored.Service.PreviewCheckout(user, stay, 0, 0);
        Assert.Equal(quoteBefore.Total, restoredQuote.Total);
        Assert.Equal(quoteBefore.Deposit, restoredQuote.Deposit);
        Assert.Equal(quoteBefore.Lines, restoredQuote.Lines);
        Assert.Equal(300000m, Assert.Single(restoredQuote.Lines, line => line.Kind == "Nâng phòng").Amount);
        var restoredDocument = restored.Service.Invoice(user, oldInvoice);
        Assert.Equal(oldDocument.Total, restoredDocument.Total);
        Assert.Equal(oldDocument.Lines, restoredDocument.Lines);
        var invoice = restored.Service.Checkout(user, restoredQuote, "Tiền mặt", "Restored active stay");
        Assert.Equal(restoredQuote.Total, restored.Service.Invoice(user, invoice).Total);
    }

    [Fact]
    public async Task GeneratedImport_RefusesExistingUpgradeHistory_BeforeChangingAnyData()
    {
        var stay = UpgradedStay();
        var paid = f.Stay(f.Room(450000));
        f.Service.Checkout(f.Admin, f.Service.PreviewCheckout(f.Admin, paid, 0, 0), "Thẻ", "Guard fixture");
        var sql = await Export();
        var before = Snapshot(f.Db);
        var error = Assert.Throws<SqlException>(() => ExecuteImport(f.Db, sql));
        Assert.Equal(51012, error.Number);
        AssertSnapshot(before, f.Db);
        Assert.Equal("Đang ở", f.Service.Booking(f.Admin, stay)["Status"]);
    }

    [Fact]
    public void LegacyImport_RefusesExistingUpgradeHistory_BeforeChangingAnyData()
    {
        var stay = UpgradedStay();
        var before = Snapshot(f.Db);
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DuLieuDayDu.sql"));
        var error = Assert.Throws<SqlException>(() => ExecuteImport(f.Db, sql));
        Assert.Equal(51012, error.Number);
        AssertSnapshot(before, f.Db);
        Assert.Equal("Đang ở", f.Service.Booking(f.Admin, stay)["Status"]);
    }

    private async Task<string> Export()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "ExportTestOutput", Guid.NewGuid().ToString("N"), "snapshot with spaces.sql");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(AppContext.BaseDirectory, "XuatDuLieu.ps1"), "-ConnectionString", f.Db.ConnectionString, "-OutputPath", output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the export fixture.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The isolated database export timed out.");
        }
        var result = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, "Export failed: " + result + Environment.NewLine + error);
        Assert.True(File.Exists(output), "Export did not produce the requested test output.");
        return File.ReadAllText(output);
    }

    private static void ExecuteImport(Database target, string sql)
    {
        var name = new SqlConnectionStringBuilder(target.ConnectionString).InitialCatalog;
        Assert.StartsWith("HotelManagementBTL_Test_", name);
        var use = new Regex(@"^USE\s+(?:\[[^\]]+\]|[A-Za-z0-9_]+);[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        sql = use.Replace(sql, "USE [" + name + "];", 1);
        using var connection = target.Open();
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch))
                Database.Execute(connection, null, batch);
    }

    private static Dictionary<string, DataTable> Snapshot(Database db)
    {
        var tables = new[] { "RoomTypes", "Rooms", "Customers", "Employees", "Users", "Services", "Bookings", "BookingRooms", "RoomUpgrades", "ServiceUsages", "Invoices", "InvoiceLines", "AuditLog" };
        var snapshot = tables.ToDictionary(table => table,
            table => db.Query($"SELECT * FROM dbo.[{table}] ORDER BY {(table == "BookingRooms" ? "StayId" : "Id")}"));
        snapshot.Add("Triggers", db.Query("SELECT name,is_disabled FROM sys.triggers WHERE name IN(N'TR_Invoices_Immutable',N'TR_InvoiceLines_Immutable',N'TR_RoomUpgrades_Immutable') ORDER BY name"));
        return snapshot;
    }

    private static void AssertSnapshot(Dictionary<string, DataTable> expected, Database actual)
    {
        var current = Snapshot(actual);
        foreach (var (tableName, prior) in expected)
        {
            var next = current[tableName];
            Assert.Equal(prior.Columns.Cast<DataColumn>().Select(column => column.ColumnName), next.Columns.Cast<DataColumn>().Select(column => column.ColumnName));
            Assert.Equal(prior.Rows.Count, next.Rows.Count);
            for (var row = 0; row < prior.Rows.Count; row++)
                Assert.Equal(prior.Rows[row].ItemArray, next.Rows[row].ItemArray);
        }
    }
}
