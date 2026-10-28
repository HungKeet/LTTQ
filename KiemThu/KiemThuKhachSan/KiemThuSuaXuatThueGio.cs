using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Hotel.Core;
using Hotel.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Hotel.Tests;

public sealed class HourlyExportMigrationTests
{
    [Fact]
    public async Task ExportAndRestore_PreservesHourlyTypeAndCheckoutPrice()
    {
        var source = new DatabaseFixture();
        var room = source.Room(500000);
        var now = DateTime.Now;
        var booking = source.Service.WalkIn(source.Reception,
            new BookingInput(1, now, now.AddHours(3), 1, 50000, "Kiểm tra xuất dữ liệu", [room], "Theo giờ"));
        source.Db.Execute("UPDATE Bookings SET ActualCheckIn=@at WHERE Id=@id", ("@at", now.AddMinutes(-170)), ("@id", booking));
        var before = source.Service.PreviewCheckout(source.Reception, booking, 0, 0);
        var output = Path.Combine(AppContext.BaseDirectory, "ExportTestOutput", Guid.NewGuid().ToString("N"), "hourly.sql");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "XuatDuLieu.ps1"), "-ConnectionString", source.Db.ConnectionString, "-OutputPath", output })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start exporter.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, (await stdout) + Environment.NewLine + (await stderr));
        var sql = File.ReadAllText(output);
        Assert.Contains("[RentalType]", sql);
        var target = new DatabaseFixture();
        var name = new SqlConnectionStringBuilder(target.Db.ConnectionString).InitialCatalog;
        Assert.StartsWith("HotelManagementBTL_Test_", name);
        sql = Regex.Replace(sql, @"^USE\s+(?:\[[^\]]+\]|[A-Za-z0-9_]+);[ \t]*\r?$", "USE [" + name + "];", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        ExecuteBatches(target.Db, sql);
        var user = target.Service.Login("admin", "Admin@123");
        Assert.Equal("Theo giờ", target.Service.Booking(user, booking).Field<string>("RentalType"));
        var after = target.Service.PreviewCheckout(user, booking, 0, 0);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.Deposit, after.Deposit);
        Assert.Equal(before.Lines, after.Lines);
    }

    [Fact]
    public void StandaloneInstaller_MigratesLegacyBookingsWithoutLosingData_AndIsIdempotent()
    {
        var fixture = new DatabaseFixture();
        var booking = fixture.Service.SaveBooking(fixture.Admin, fixture.Input([fixture.Room()], day: 4));
        var original = fixture.Service.Booking(fixture.Admin, booking);
        var arrival = original.Field<DateTime>("Arrival");
        var departure = original.Field<DateTime>("Departure");
        fixture.Db.Execute("""
            DECLARE @drop nvarchar(max)=N'';
            SELECT @drop=@drop+N'ALTER TABLE dbo.Bookings DROP CONSTRAINT '+QUOTENAME(name)+N';'
              FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.Bookings') AND definition LIKE N'%RentalType%';
            SELECT @drop=@drop+N'ALTER TABLE dbo.Bookings DROP CONSTRAINT '+QUOTENAME(dc.name)+N';'
              FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
             WHERE dc.parent_object_id=OBJECT_ID(N'dbo.Bookings') AND c.name=N'RentalType';
            EXEC sys.sp_executesql @drop;
            ALTER TABLE dbo.Bookings DROP COLUMN RentalType;
            """);
        var name = new SqlConnectionStringBuilder(fixture.Db.ConnectionString).InitialCatalog;
        Assert.StartsWith("HotelManagementBTL_Test_", name);
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CaiDat.sql")).Replace("HotelManagementBTL", name);
        ExecuteBatches(fixture.Db, sql);
        ExecuteBatches(fixture.Db, sql);
        var restored = fixture.Service.Booking(fixture.Admin, booking);
        Assert.Equal("Theo ngày", restored.Field<string>("RentalType"));
        Assert.Equal(arrival, restored.Field<DateTime>("Arrival"));
        Assert.Equal(departure, restored.Field<DateTime>("Departure"));
    }

    private static void ExecuteBatches(Database database, string sql)
    {
        Assert.StartsWith("HotelManagementBTL_Test_", new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog);
        using var connection = database.Open();
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch))
                Database.Execute(connection, null, batch);
    }
}
