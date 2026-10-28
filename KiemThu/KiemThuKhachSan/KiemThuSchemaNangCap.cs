using System.Data;
using System.Text.RegularExpressions;
using Hotel.Business;
using Hotel.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Hotel.Tests;

public sealed class RoomUpgradeMigrationTests
{
    private const string MigrationMarker = "-- Paid room upgrade migration";

    private static SqlConnectionStringBuilder Connection(string database) => new(
        Environment.GetEnvironmentVariable("HOTEL_TEST_CONNECTION")
        ?? "Server=lpc:.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True")
    {
        InitialCatalog = database
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpgradeMigration_PreservesLegacyBookingsAndInvoices_AndIsRepeatable(bool standaloneScript)
    {
        var databaseName = "HotelManagementBTL_UpgradeMigration_" + Guid.NewGuid().ToString("N")[..10];
        using (var master = new SqlConnection(Connection("master").ConnectionString))
        {
            master.Open();
            var createSql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TaoCoSoDuLieu.sql"));
            using var create = Database.Command(master, null, createSql, ("@DatabaseName", databaseName));
            create.CommandTimeout = 120;
            create.ExecuteNonQuery();
        }

        var db = new Database(Connection(databaseName).ConnectionString);
        var schemaSql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CauTrucDuLieu.sql"));
        var markerAt = schemaSql.IndexOf(MigrationMarker, StringComparison.Ordinal);
        Assert.True(markerAt > 0, "The additive upgrade migration must follow the original schema.");
        using (var legacy = db.Open())
        {
            ExecuteBatches(legacy, schemaSql[..markerAt]);
            // Version 1 already protected issued invoices. Keep those original triggers in the fixture.
            foreach (var batch in Batches(schemaSql).Where(batch =>
                batch.Contains("CREATE OR ALTER TRIGGER dbo.TR_Invoices_Immutable", StringComparison.Ordinal)
                || batch.Contains("CREATE OR ALTER TRIGGER dbo.TR_InvoiceLines_Immutable", StringComparison.Ordinal)))
                Database.Execute(legacy, null, batch);
        }
        var originalSchema = db.Query("SELECT COL_LENGTH(N'dbo.BookingRooms',N'StayId') AS StayIdColumn,OBJECT_ID(N'dbo.RoomUpgrades') AS UpgradeTable").Rows[0];
        Assert.True(originalSchema.IsNull("StayIdColumn"));
        Assert.True(originalSchema.IsNull("UpgradeTable"));

        SeedLegacyData(db);
        var snapshots = SnapshotQueries().ToDictionary(pair => pair.Key, pair => db.Query(pair.Value));
        var service = new HotelService(db);
        DataTable? stableStayIds = null;

        for (var repeat = 0; repeat < 2; repeat++)
        {
            if (standaloneScript)
            {
                using var installer = new SqlConnection(Connection("master").ConnectionString);
                installer.Open();
                var installSql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CaiDat.sql"))
                    .Replace("HotelManagementBTL", databaseName, StringComparison.Ordinal);
                ExecuteBatches(installer, installSql);
            }
            else
                service.Initialize();

            foreach (var (table, sql) in SnapshotQueries())
                AssertSameRows(snapshots[table], db.Query(sql));

            var stayIds = db.Query("SELECT BookingId,RoomId,StayId FROM dbo.BookingRooms ORDER BY BookingId,RoomId");
            Assert.Equal(3, stayIds.Rows.Count);
            Assert.All(stayIds.AsEnumerable(), row => Assert.True(row.Field<int>("StayId") > 0));
            Assert.Equal(stayIds.Rows.Count, stayIds.AsEnumerable().Select(row => row.Field<int>("StayId")).Distinct().Count());
            if (stableStayIds is not null)
                AssertSameRows(stableStayIds, stayIds);
            stableStayIds = stayIds;

            Assert.Equal(1, db.Query("SELECT COLUMNPROPERTY(OBJECT_ID(N'dbo.BookingRooms'),N'StayId','IsIdentity') AS IsIdentity").Rows[0].Field<int>("IsIdentity"));
            Assert.Empty(db.Query("SELECT * FROM dbo.RoomUpgrades").AsEnumerable());
            Assert.Equal(new[] { 1, 2 }, db.Query("SELECT Version FROM dbo.SchemaInfo ORDER BY Version").AsEnumerable().Select(row => row.Field<int>("Version")).ToArray());
        }
    }

    private static void SeedLegacyData(Database db)
    {
        db.Execute("""
            DECLARE @type int,@firstRoom int,@secondRoom int,@customer int,@employee int,@future int,@paid int,@invoice int;
            INSERT dbo.RoomTypes(Name,Capacity,Rate) VALUES(N'Loại phòng cũ cần giữ',6,1250000);
            SET @type=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Rooms(Number,Floor,RoomTypeId) VALUES(N'LEGACY-A',4,@type);
            SET @firstRoom=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Rooms(Number,Floor,RoomTypeId) VALUES(N'LEGACY-B',5,@type);
            SET @secondRoom=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Customers(FullName,Phone,IdentityNumber,Address)
                VALUES(N'Tên khách đã cập nhật',N'0901234567',N'LEGACY-ID',N'Địa chỉ khách hiện tại');
            SET @customer=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Employees(FullName,Phone,Position)
                VALUES(N'Tên nhân viên hiện tại',N'0907654321',N'Quản lý');
            SET @employee=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Users(EmployeeId,Username,PasswordHash,Role)
                VALUES(@employee,N'legacy_admin',@hash,N'Quản lý');

            INSERT dbo.Bookings(CustomerId,EmployeeId,Arrival,Departure,Guests,Deposit,Notes)
                VALUES(@customer,@employee,@futureArrival,@futureDeparture,3,123456,N'Phiếu đặt cũ cần giữ');
            SET @future=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.BookingRooms(BookingId,RoomId,NightlyRate,Capacity)
                VALUES(@future,@firstRoom,375000,2),(@future,@secondRoom,625000,4);

            INSERT dbo.Bookings(CustomerId,EmployeeId,Arrival,Departure,Guests,Deposit,Status,ActualCheckIn,ActualCheckOut,Notes)
                VALUES(@customer,@employee,@pastArrival,@pastDeparture,1,200000,N'Đã trả',@pastArrival,@pastDeparture,N'Lượt ở đã thanh toán cũ');
            SET @paid=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.BookingRooms(BookingId,RoomId,NightlyRate,Capacity)
                VALUES(@paid,@firstRoom,375000,2);
            INSERT dbo.Invoices(BookingId,EmployeeId,CustomerName,CustomerPhone,CustomerIdentity,CustomerAddress,EmployeeName,
                IssuedAt,CheckIn,CheckOut,Surcharge,Discount,Deposit,Total,PaymentMethod,Notes)
                VALUES(@paid,@employee,N'Tên trên hóa đơn cũ',N'0901111222',N'OLD-INVOICE-ID',N'Địa chỉ trên hóa đơn cũ',
                    N'Nhân viên trên hóa đơn cũ',@pastDeparture,@pastArrival,@pastDeparture,0,0,200000,750000,N'Tiền mặt',N'Ảnh chụp hóa đơn cũ');
            SET @invoice=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.InvoiceLines(InvoiceId,Kind,Description,Quantity,Unit,UnitPrice)
                VALUES(@invoice,N'Phòng',N'Phòng LEGACY-A',2,N'Đêm',375000);
            """,
            ("@hash", HotelService.HashPassword("Legacy@123")),
            ("@futureArrival", DateTime.Today.AddDays(3).AddHours(14)),
            ("@futureDeparture", DateTime.Today.AddDays(5).AddHours(12)),
            ("@pastArrival", DateTime.Today.AddDays(-5).AddHours(14)),
            ("@pastDeparture", DateTime.Today.AddDays(-3).AddHours(12)));
    }

    private static Dictionary<string, string> SnapshotQueries() => new()
    {
        ["RoomTypes"] = "SELECT * FROM dbo.RoomTypes ORDER BY Id",
        ["Rooms"] = "SELECT * FROM dbo.Rooms ORDER BY Id",
        ["Customers"] = "SELECT * FROM dbo.Customers ORDER BY Id",
        ["Employees"] = "SELECT * FROM dbo.Employees ORDER BY Id",
        ["Users"] = "SELECT * FROM dbo.Users ORDER BY Id",
        ["Bookings"] = "SELECT * FROM dbo.Bookings ORDER BY Id",
        ["BookingRooms"] = "SELECT BookingId,RoomId,NightlyRate,Capacity FROM dbo.BookingRooms ORDER BY BookingId,RoomId",
        ["Invoices"] = "SELECT * FROM dbo.Invoices ORDER BY Id",
        ["InvoiceLines"] = "SELECT * FROM dbo.InvoiceLines ORDER BY Id"
    };

    private static void AssertSameRows(DataTable expected, DataTable actual)
    {
        Assert.Equal(expected.Columns.Cast<DataColumn>().Select(column => column.ColumnName), actual.Columns.Cast<DataColumn>().Select(column => column.ColumnName));
        Assert.Equal(expected.Rows.Count, actual.Rows.Count);
        for (var i = 0; i < expected.Rows.Count; i++)
            Assert.Equal(expected.Rows[i].ItemArray, actual.Rows[i].ItemArray);
    }

    private static IEnumerable<string> Batches(string sql) => Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
        .Where(batch => !string.IsNullOrWhiteSpace(batch));

    private static void ExecuteBatches(SqlConnection connection, string sql)
    {
        foreach (var batch in Batches(sql))
        {
            using var command = Database.Command(connection, null, batch);
            command.CommandTimeout = 120;
            command.ExecuteNonQuery();
        }
    }
}
