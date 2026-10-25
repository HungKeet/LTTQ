using System.Data;
using Hotel.Business;
using Hotel.Core;
using Hotel.Data;
using Microsoft.Data.SqlClient;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Hotel.Tests;

public sealed class DatabaseFixture
{
    public readonly Database Db;
    public readonly HotelService Service;
    public readonly Session Admin;
    public readonly Session Reception;
    public readonly string DatabaseName = "HotelManagementBTL_Test_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N")[..6];
    public DatabaseFixture()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HOTEL_TEST_CONNECTION") ?? "Server=lpc:.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True") { InitialCatalog = DatabaseName };
        Db = new Database(connection.ConnectionString);
        Service = new HotelService(Db);
        Service.Initialize();
        Admin = Service.Login("admin", "Admin@123");
        Reception = Service.Login("letan", "Letan@123");
    }
    public int Room(decimal rate = 500000, int capacity = 2)
    {
        var token = Guid.NewGuid().ToString("N")[..10];
        var type = Service.SaveCatalog(Admin, CatalogKind.RoomTypes, null, new()
        {
            ["Name"] = "Loại " + token,
            ["Capacity"] = capacity,
            ["Rate"] = rate,
            ["Active"] = true
        });
        return Service.SaveCatalog(Admin, CatalogKind.Rooms, null, new()
        {
            ["Number"] = token,
            ["Floor"] = 1,
            ["RoomTypeId"] = type,
            ["OperationalStatus"] = "Sẵn sàng",
            ["Active"] = true
        });
    }
    public BookingInput Input(int[] rooms, decimal deposit = 0, int day = 0, int nights = 2, int guests = 1) => new(1, DateTime.Today.AddDays(day).AddHours(14), DateTime.Today.AddDays(day + nights).AddHours(12), guests, deposit, "Kiểm thử", rooms);
    public int Stay(int room, decimal deposit = 0)
    {
        var id = Service.SaveBooking(Admin, Input(new[] { room }, deposit));
        Service.CheckIn(Admin, id);
        return id;
    }
}

public sealed class BusinessTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture f = fixture;
    [Fact]
    public void Seed_Is_Idempotent_And_Login_Rejects_BadPassword()
    {
        f.Service.Initialize();
        Assert.Equal("Quản lý", f.Admin.Role);
        Assert.Equal(2, f.Db.Query("SELECT * FROM Users").Rows.Count);
        Assert.Throws<InvalidOperationException>(() => f.Service.Login("admin", "wrong"));
    }
    [Fact]
    public void Password_IsSalted_And_ChangeChecksOldPassword()
    {
        var a = HotelService.HashPassword("a password");
        var b = HotelService.HashPassword("a password");
        Assert.NotEqual(a, b);
        Assert.True(HotelService.VerifyPassword("a password", a));
        Assert.False(HotelService.VerifyPassword("no", a));
        Assert.Throws<ArgumentException>(() => f.Service.ChangePassword(f.Admin, "bad", "NewPass@123"));
    }
    [Fact]
    public void Reception_Cannot_Administer_Or_ReadReports()
    {
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.SaveCatalog(f.Reception, CatalogKind.Services, null, new() { ["Name"] = "Unauthorized", ["Unit"] = "Lần", ["Price"] = 10 }));
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.Revenue(f.Reception, DateTime.Today, DateTime.Today));
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.Catalog(f.Reception, CatalogKind.Users));
    }
    [Fact]
    public void Customer_ParameterizedInput_And_Search()
    {
        var text = "O'Brian; DROP TABLE Rooms; --";
        var id = f.Service.SaveCatalog(f.Reception, CatalogKind.Customers, null, new()
        {
            ["FullName"] = text,
            ["Phone"] = "0901234567",
            ["IdentityNumber"] = Guid.NewGuid().ToString("N")[..20],
            ["Address"] = "Hà Nội"
        });
        Assert.Equal(text, f.Service.CatalogRecord(f.Admin, CatalogKind.Customers, id)["FullName"]);
        Assert.True(f.Service.Catalog(f.Reception, CatalogKind.Customers, "O'Brian").Rows.Count > 0);
        Assert.True(f.Db.Query("SELECT * FROM Rooms").Rows.Count > 0);
    }
    [Fact]
    public void Duplicate_Identity_Rejected_AndUnused_CustomerDeleted()
    {
        var tag = Guid.NewGuid().ToString("N")[..20];
        var input = new Dictionary<string, object?> { ["FullName"] = "Khách kiểm thử", ["Phone"] = "0901234567", ["IdentityNumber"] = tag };
        var id = f.Service.SaveCatalog(f.Admin, CatalogKind.Customers, null, input);
        Assert.Throws<SqlException>(() => f.Service.SaveCatalog(f.Admin, CatalogKind.Customers, null, input));
        f.Service.DeleteCatalog(f.Admin, CatalogKind.Customers, id);
        Assert.Throws<InvalidOperationException>(() => f.Service.CatalogRecord(f.Admin, CatalogKind.Customers, id));
    }
    [Fact]
    public void Overlap_Rejected_ButAdjacent_Allowed()
    {
        var room = f.Room();
        f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, day: 10));
        Assert.Throws<InvalidOperationException>(() => f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, day: 11)));
        var second = f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, day: 12));
        Assert.True(second > 0);
        Assert.DoesNotContain(f.Service.AvailableRooms(f.Admin, DateTime.Today.AddDays(11), DateTime.Today.AddDays(12)).AsEnumerable(), r => r.Field<int>("Id") == room);
    }
    [Fact]
    public async Task Concurrent_Bookings_OnlyOne_Succeeds()
    {
        var room = f.Room();
        var input = f.Input(new[] { room }, day: 15);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => { try { f.Service.SaveBooking(f.Admin, input); return true; } catch (InvalidOperationException) { return false; } })));
        Assert.Single(results, x => x);
    }
    [Fact]
    public void Capacity_AndDates_Validated()
    {
        var room = f.Room(capacity: 2);
        Assert.Throws<ArgumentException>(() => f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, guests: 3)));
        Assert.Throws<ArgumentException>(() => f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, day: -1)));
        Assert.Throws<ArgumentException>(() => f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, nights: 0)));
        Assert.Throws<ArgumentException>(() => f.Service.SaveBooking(f.Admin, f.Input(Array.Empty<int>())));
    }
    [Fact]
    public void MultiRoom_CheckIn_AndRooms_AreBusy()
    {
        var rooms = new[] { f.Room(), f.Room() };
        var b = f.Service.SaveBooking(f.Admin, f.Input(rooms, guests: 4));
        f.Service.CheckIn(f.Admin, b);
        Assert.Equal("Đang ở", f.Service.Booking(f.Admin, b)["Status"]);
        var overview = f.Service.RoomsOverview(f.Admin);
        Assert.Equal(2, overview.AsEnumerable().Count(r => rooms.Contains(r.Field<int>("Id")) && (string)r["Trạng thái"] == "Đang ở"));
        Assert.Throws<InvalidOperationException>(() => f.Service.CheckIn(f.Admin, b));
    }
    [Fact]
    public void FutureCheckIn_AndCancelActive_Rejected()
    {
        var room = f.Room();
        var b = f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, day: 20));
        Assert.Throws<InvalidOperationException>(() => f.Service.CheckIn(f.Admin, b));
        var active = f.Stay(f.Room());
        Assert.Throws<InvalidOperationException>(() => f.Service.CancelBooking(f.Admin, active));
    }
    [Fact]
    public void Cancel_RefundsDeposit_AndReleasesRoom()
    {
        var room = f.Room();
        var input = f.Input(new[] { room }, 350000, day: 8);
        var b = f.Service.SaveBooking(f.Admin, input);
        f.Service.CancelBooking(f.Admin, b);
        var row = f.Service.Booking(f.Admin, b);
        Assert.Equal("Đã hủy", row["Status"]);
        Assert.Equal(350000m, row["RefundedDeposit"]);
        Assert.True(f.Service.SaveBooking(f.Admin, input) > 0);
    }
    [Fact]
    public void EditBooking_PreservesPrice_AndCanChangeDates()
    {
        var room = f.Room(500000);
        var b = f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, day: 3));
        var type = Convert.ToInt32(f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, room)["RoomTypeId"]);
        f.Service.SaveCatalog(f.Admin, CatalogKind.RoomTypes, type, new()
        {
            ["Name"] = "Đổi giá " + Guid.NewGuid().ToString("N")[..8],
            ["Capacity"] = 2,
            ["Rate"] = 800000
        });
        f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, day: 4), b);
        Assert.Equal(500000m, f.Service.BookingRooms(f.Admin, b).Rows[0]["Giá / đêm"]);
    }
    [Fact]
    public void Maintenance_AndActiveRoomChanges_Protected()
    {
        var room = f.Room();
        var data = f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, room);
        Dictionary<string, object?> fields = new()
        {
            ["Number"] = data["Number"],
            ["Floor"] = 1,
            ["RoomTypeId"] = data["RoomTypeId"],
            ["OperationalStatus"] = "Bảo trì",
            ["Active"] = true
        };
        f.Service.SaveCatalog(f.Admin, CatalogKind.Rooms, room, fields);
        Assert.Throws<InvalidOperationException>(() => f.Service.SaveBooking(f.Admin, f.Input(new[] { room })));
        fields["OperationalStatus"] = "Sẵn sàng";
        f.Service.SaveCatalog(f.Admin, CatalogKind.Rooms, room, fields);
        f.Stay(room);
        fields["Active"] = false;
        Assert.Throws<InvalidOperationException>(() => f.Service.SaveCatalog(f.Admin, CatalogKind.Rooms, room, fields));
    }
    [Fact]
    public void ExtendStay_ChecksFutureReservation()
    {
        var room = f.Room();
        var active = f.Stay(room);
        f.Service.SaveBooking(f.Admin, f.Input(new[] { room }, day: 3));
        Assert.Throws<InvalidOperationException>(() => f.Service.ChangeDeparture(f.Admin, active, DateTime.Today.AddDays(5).AddHours(12)));
        f.Service.ChangeDeparture(f.Admin, active, DateTime.Today.AddDays(3).AddHours(12));
        Assert.Equal(DateTime.Today.AddDays(3).AddHours(12), f.Service.Booking(f.Admin, active)["Departure"]);
    }
    [Fact]
    public void Service_QuantitySnapshot_AndCheckout_Lifecycle()
    {
        var room = f.Room(500000);
        var b = f.Stay(room, 300000);
        var s = f.Service.SaveCatalog(f.Admin, CatalogKind.Services, null, new()
        {
            ["Name"] = "Dịch vụ " + Guid.NewGuid().ToString("N")[..6],
            ["Unit"] = "Lần",
            ["Price"] = 75000
        });
        var usage = f.Service.AddService(f.Reception, b, s, 1);
        f.Service.UpdateUsage(f.Reception, usage, 2);
        f.Service.SaveCatalog(f.Admin, CatalogKind.Services, s, new()
        {
            ["Name"] = "Giá mới " + Guid.NewGuid().ToString("N")[..6],
            ["Unit"] = "Lần",
            ["Price"] = 95000
        });
        var q = f.Service.PreviewCheckout(f.Admin, b, 20000, 10000);
        Assert.Equal(660000m, q.Total);
        Assert.Equal(360000m, q.Balance);
        var inv = f.Service.Checkout(f.Reception, q, "Tiền mặt", "Test");
        var document = f.Service.Invoice(f.Admin, inv);
        Assert.Equal(660000m, document.Total);
        Assert.Equal(75000m, document.Lines.Single(x => x.Kind == "Dịch vụ").UnitPrice);
        Assert.Equal("Đã trả", f.Service.Booking(f.Admin, b)["Status"]);
        Assert.Equal("Chờ dọn", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, room)["OperationalStatus"]);
        Assert.Throws<InvalidOperationException>(() => f.Service.AddService(f.Admin, b, s, 1));
        Assert.Throws<InvalidOperationException>(() => f.Service.Checkout(f.Admin, q, "Tiền mặt", ""));
        f.Service.MarkRoomReady(f.Reception, room);
        Assert.Equal("Sẵn sàng", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, room)["OperationalStatus"]);
    }
    [Fact]
    public void DepositGreaterThanTotal_ReturnsRefund()
    {
        var b = f.Stay(f.Room(450000), 700000);
        var q = f.Service.PreviewCheckout(f.Admin, b, 0, 0);
        Assert.Equal(-250000m, q.Balance);
        var id = f.Service.Checkout(f.Admin, q, "Chuyển khoản", "");
        Assert.Equal(-250000m, f.Service.Invoice(f.Admin, id).Balance);
    }
    [Fact]
    public void ExcessDiscount_IsRejected()
    {
        var b = f.Stay(f.Room());
        Assert.Throws<ArgumentException>(() => f.Service.PreviewCheckout(f.Admin, b, 0, 900000));
    }
    [Fact]
    public void StaleCheckout_RequiresNewPreview()
    {
        var b = f.Stay(f.Room());
        var q = f.Service.PreviewCheckout(f.Admin, b, 0, 0);
        f.Service.AddService(f.Admin, b, 1, 1);
        Assert.Throws<InvalidOperationException>(() => f.Service.Checkout(f.Admin, q, "Tiền mặt", ""));
        Assert.Equal("Đang ở", f.Service.Booking(f.Admin, b)["Status"]);
    }
    [Fact]
    public void ImmutableInvoice_AndCustomerSnapshot()
    {
        var customer = f.Service.SaveCatalog(f.Admin, CatalogKind.Customers, null, new()
        {
            ["FullName"] = "Tên gốc",
            ["Phone"] = "0901234567"
        });
        var b = f.Service.SaveBooking(f.Admin, f.Input(new[] { f.Room() }) with
        {
            CustomerId = customer
        });
        f.Service.CheckIn(f.Admin, b);
        var id = f.Service.Checkout(f.Admin, f.Service.PreviewCheckout(f.Admin, b, 0, 0), "Tiền mặt", "");
        f.Service.SaveCatalog(f.Admin, CatalogKind.Customers, customer, new()
        {
            ["FullName"] = "Tên đã đổi",
            ["Phone"] = "0901234567"
        });
        Assert.Equal("Tên gốc", f.Service.Invoice(f.Admin, id).Customer);
        Assert.Throws<SqlException>(() => f.Db.Execute("UPDATE Invoices SET Total=0 WHERE Id=@id", ("@id", id)));
        Assert.Throws<SqlException>(() => f.Db.Execute("DELETE InvoiceLines WHERE InvoiceId=@id", ("@id", id)));
    }
    [Fact]
    public void Checkout_RollsBackAllWrites_OnDatabaseFailure()
    {
        var room = f.Room();
        var b = f.Stay(room);
        var q = f.Service.PreviewCheckout(f.Admin, b, 0, 0);
        f.Db.Execute("CREATE TRIGGER dbo.TR_TestFail ON dbo.InvoiceLines AFTER INSERT AS BEGIN THROW 51999, 'Simulated failure', 1; END");
        try
        {
            Assert.Throws<SqlException>(() => f.Service.Checkout(f.Admin, q, "Tiền mặt", ""));
        }
        finally { f.Db.Execute("DROP TRIGGER dbo.TR_TestFail"); }
        Assert.Equal("Đang ở", f.Service.Booking(f.Admin, b)["Status"]);
        Assert.Empty(f.Db.Query("SELECT * FROM Invoices WHERE BookingId=@id", ("@id", b)).Rows.Cast<DataRow>());
        Assert.Equal("Sẵn sàng", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, room)["OperationalStatus"]);
    }
    [Fact]
    public void Revenue_EqualsInvoiceTotals_WithoutDoubleCountingDeposit()
    {
        var b = f.Stay(f.Room(500000), 200000);
        f.Service.Checkout(f.Admin, f.Service.PreviewCheckout(f.Admin, b, 0, 0), "Thẻ", "");
        var daily = f.Service.Revenue(f.Admin, DateTime.Today, DateTime.Today);
        var expected = f.Db.Query("SELECT COALESCE(SUM(Total),0) Total FROM Invoices WHERE IssuedAt>=@d", ("@d", DateTime.Today)).Rows[0].Field<decimal>("Total");
        Assert.Equal(expected, daily.AsEnumerable().Sum(r => r.Field<decimal>("Doanh thu")));
        Assert.NotEmpty(f.Service.Invoices(f.Admin, DateTime.Today, DateTime.Today).Rows.Cast<DataRow>());
    }
    [Fact]
    public void UsedCustomer_CannotBeDeleted()
    {
        var b = f.Service.SaveBooking(f.Admin, f.Input(new[] { f.Room() }, day: 10));
        Assert.Throws<SqlException>(() => f.Service.DeleteCatalog(f.Admin, CatalogKind.Customers, 1));
        Assert.True(b > 0);
    }
    [Fact]
    public void OwnAccount_CannotBeDisabled()
    {
        Assert.Throws<InvalidOperationException>(() => f.Service.SaveCatalog(f.Admin, CatalogKind.Users, f.Admin.UserId, new() { ["Username"] = "admin", ["EmployeeId"] = f.Admin.EmployeeId, ["Role"] = "Quản lý", ["Active"] = false }));
    }
}

public sealed class MoneyTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(30, 30)]
    public void Nights_UsesCalendarDays_MinimumOne(int days, int expected)
    {
        var start = new DateTime(2026, 10, 1, 14, 0, 0);
        Assert.Equal(expected, Money.Nights(start, start.AddDays(days)));
    }
    [Fact] public void NegativeDuration_Rejected() => Assert.Throws<ArgumentException>(() => Money.Nights(DateTime.Today, DateTime.Today.AddMinutes(-1)));
}

public sealed class InstallationTests
{
    [Fact]
    public void StandaloneSqlScript_IsRepeatable_AndCreatesValidVietnameseSeed()
    {
        var name = "HotelManagementBTL_InstallTest_" + Guid.NewGuid().ToString("N")[..10];
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CaiDat.sql")).Replace("HotelManagementBTL", name);
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HOTEL_TEST_CONNECTION") ?? "Server=lpc:.\\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True") { InitialCatalog = "master" };
        using (var connection = new SqlConnection(builder.ConnectionString))
        {
            connection.Open();
            for (var repeat = 0; repeat < 2; repeat++)
                foreach (var batch in System.Text.RegularExpressions.Regex.Split(script, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    if (!string.IsNullOrWhiteSpace(batch))
                    {
                        using var command = new SqlCommand(batch, connection);
                        command.ExecuteNonQuery();
                    }
        }
        builder.InitialCatalog = name;
        var service = new HotelService(new Database(builder.ConnectionString));
        var admin = service.Login("admin", "Admin@123");
        var reception = service.Login("letan", "Letan@123");
        Assert.Equal("Lễ tân", reception.Role);
        Assert.Equal("Phòng đơn", service.CatalogRecord(admin, CatalogKind.RoomTypes, 1)["Name"]);
        Assert.Equal(8, service.RoomsOverview(admin).Rows.Count);
    }
}
