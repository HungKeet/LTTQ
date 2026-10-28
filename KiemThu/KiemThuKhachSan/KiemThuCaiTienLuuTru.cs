using System.Data;
using Hotel.Business;
using Hotel.Core;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Hotel.Tests;

public sealed class StayImprovementsTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture f = fixture;

    private Dictionary<string, object?> Customer(string? identity = null) => new()
    {
        ["FullName"] = "Khách tại quầy",
        ["Phone"] = "0901234567",
        ["IdentityNumber"] = identity ?? Guid.NewGuid().ToString("N")[..20],
        ["Address"] = "Hà Nội"
    };

    private void RoomStatus(int room, string status) => f.Db.Execute(
        "UPDATE Rooms SET OperationalStatus=@status WHERE Id=@id", ("@status", status), ("@id", room));

    [Fact]
    public void OverdueStay_BlocksNow_ButAllowsFutureReservation()
    {
        var room = f.Room();
        var stay = f.Stay(room);
        f.Db.Execute("UPDATE Bookings SET Arrival=@a,ActualCheckIn=@a,Departure=@d WHERE Id=@id",
            ("@a", DateTime.Today.AddDays(-2)), ("@d", DateTime.Today.AddHours(-1)), ("@id", stay));
        var now = DateTime.Now;
        Assert.DoesNotContain(f.Service.AvailableRooms(f.Admin, now, now.AddHours(2)).AsEnumerable(), r => r.Field<int>("Id") == room);
        Assert.Throws<InvalidOperationException>(() => f.Service.SaveBooking(f.Admin,
            f.Input([room]) with { Arrival = now, Departure = now.AddHours(2) }));
        Assert.Contains(f.Service.AvailableRooms(f.Admin, DateTime.Today.AddDays(4), DateTime.Today.AddDays(5)).AsEnumerable(), r => r.Field<int>("Id") == room);
        var future = f.Service.SaveBooking(f.Admin, f.Input([room], day: 4));
        Assert.True(future > 0);
        Assert.Throws<InvalidOperationException>(() => f.Service.ChangeDeparture(f.Admin, stay, DateTime.Today.AddDays(5)));
    }

    [Fact]
    public void ImmediateOverdueSearch_RemainsBlockedAcrossRepeatedFastQueries()
    {
        var room = f.Room();
        var stay = f.Stay(room);
        f.Db.Execute("UPDATE Bookings SET Arrival=@a,ActualCheckIn=@a,Departure=@d WHERE Id=@id",
            ("@a", DateTime.Today.AddDays(-2)), ("@d", DateTime.Now.AddHours(-1)), ("@id", stay));
        for (var i = 0; i < 20; i++)
        {
            var now = DateTime.Now;
            Assert.DoesNotContain(f.Service.AvailableRooms(f.Admin, now, now.AddHours(2)).AsEnumerable(), r => r.Field<int>("Id") == room);
            Assert.Throws<InvalidOperationException>(() => f.Service.SaveBooking(f.Admin,
                f.Input([room]) with { Arrival = now, Departure = now.AddHours(2) }));
        }
        var future = DateTime.Today.AddDays(3);
        Assert.Contains(f.Service.AvailableRooms(f.Admin, future, future.AddDays(1)).AsEnumerable(), r => r.Field<int>("Id") == room);
    }

    [Fact]
    public void DateTimeParameters_PreserveTicksAndMatchSqlDateTime2Columns()
    {
        var moment = new DateTime(2026, 10, 27, 19, 1, 2).AddTicks(12345);
        var row = f.Db.Query("SELECT @at AS Moment,CONVERT(nvarchar(20),SQL_VARIANT_PROPERTY(@at,'BaseType')) AS BaseType,@count AS CountValue",
            ("@at", moment), ("@count", 7)).Rows[0];
        Assert.Equal(moment, row.Field<DateTime>("Moment"));
        Assert.Equal("datetime2", row.Field<string>("BaseType"));
        Assert.Equal(7, row.Field<int>("CountValue"));
    }

    [Fact]
    public void SameDayReservations_UseExactTimes_AndAllowAdjacentBookings()
    {
        var room = f.Room();
        var start = DateTime.Today.AddDays(7).AddHours(9);
        var input = f.Input([room]) with { Arrival = start, Departure = start.AddHours(2) };
        var id = f.Service.SaveBooking(f.Admin, input);
        Assert.True(id > 0);
        Assert.Throws<InvalidOperationException>(() => f.Service.SaveBooking(f.Admin, input with { Arrival = start.AddHours(1), Departure = start.AddHours(3) }));
        Assert.True(f.Service.SaveBooking(f.Admin, input with { Arrival = start.AddHours(2), Departure = start.AddHours(3) }) > 0);
        Assert.Throws<ArgumentException>(() => f.Service.SaveBooking(f.Admin, input with { Departure = start }));
        Assert.Throws<ArgumentException>(() => f.Service.SaveBooking(f.Admin, input with { Departure = start.AddMinutes(-1) }));
    }

    [Fact]
    public void EarlyActualCheckIn_BlocksReservationsBeforePlannedArrival()
    {
        var room = f.Room();
        var stay = f.Stay(room);
        var now = DateTime.Now;
        var scheduled = now.AddHours(3);
        f.Db.Execute("UPDATE Bookings SET Arrival=@scheduled,ActualCheckIn=@actual,Departure=@departure WHERE Id=@id",
            ("@scheduled", scheduled), ("@actual", now.AddMinutes(-5)),
            ("@departure", now.AddDays(1)), ("@id", stay));
        var input = f.Input([room]) with { Arrival = now.AddMinutes(10), Departure = now.AddMinutes(30) };
        Assert.DoesNotContain(f.Service.AvailableRooms(f.Admin, input.Arrival, input.Departure).AsEnumerable(), r => r.Field<int>("Id") == room);
        Assert.Throws<InvalidOperationException>(() => f.Service.SaveBooking(f.Admin, input));
    }

    [Fact]
    public void ReadyRoomSearch_ExcludesDirtyAndOccupiedRooms_AndKeepsFutureReservationsAvailable()
    {
        var ready = f.Room();
        var dirty = f.Room();
        var occupied = f.Room();
        RoomStatus(dirty, "Chờ dọn");
        f.Stay(occupied);
        var now = DateTime.Now;
        var available = f.Service.AvailableRooms(f.Reception, now, now.AddHours(2), readyOnly: true).AsEnumerable().Select(r => r.Field<int>("Id")).ToArray();
        Assert.Contains(ready, available);
        Assert.DoesNotContain(dirty, available);
        Assert.DoesNotContain(occupied, available);
        Assert.Contains(f.Service.AvailableRooms(f.Admin, now.AddDays(4), now.AddDays(5)).AsEnumerable(), r => r.Field<int>("Id") == occupied);
    }

    [Fact]
    public void ChangeRoom_PreservesBookingMoneyAndServices_AndReleasesOldRoomForCleaning()
    {
        var oldRoom = f.Room(500000);
        var newRoom = f.Room(800000);
        var stay = f.Stay(oldRoom, 200000);
        f.Service.AddService(f.Reception, stay, 1, 2);
        var before = f.Service.PreviewCheckout(f.Admin, stay, 0, 0);
        f.Service.ChangeRoom(f.Reception, stay, oldRoom, newRoom);
        var selected = Assert.Single(f.Service.BookingRooms(f.Admin, stay).AsEnumerable());
        Assert.Equal(newRoom, selected.Field<int>("Id"));
        Assert.Equal(500000m, selected.Field<decimal>("Giá / đêm"));
        Assert.Equal("Chờ dọn", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, oldRoom)["OperationalStatus"]);
        Assert.Equal("Đang ở", f.Service.Booking(f.Admin, stay)["Status"]);
        var after = f.Service.PreviewCheckout(f.Admin, stay, 0, 0);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.Deposit, after.Deposit);
        Assert.Equal(before.Lines.Where(x => x.Kind == "Dịch vụ"), after.Lines.Where(x => x.Kind == "Dịch vụ"));
        Assert.Throws<InvalidOperationException>(() => f.Service.Checkout(f.Admin, before, "Tiền mặt", ""));
        Assert.NotEmpty(f.Db.Query("SELECT Id FROM AuditLog WHERE Action=N'Đổi phòng' AND Detail LIKE @detail", ("@detail", "%#" + stay + "%")).Rows.Cast<DataRow>());
        f.Service.Checkout(f.Admin, after, "Tiền mặt", "");
        Assert.Equal("Chờ dọn", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, newRoom)["OperationalStatus"]);
    }

    [Theory]
    [InlineData("Chờ dọn")]
    [InlineData("Bảo trì")]
    [InlineData("Ngừng hoạt động")]
    [InlineData("Loại ngừng hoạt động")]
    [InlineData("Đang ở")]
    [InlineData("Trùng lịch")]
    public void ChangeRoom_RejectsUnavailableTarget_WithoutChangingCurrentRoom(string reason)
    {
        var oldRoom = f.Room();
        var target = f.Room();
        var stay = f.Stay(oldRoom);
        switch (reason)
        {
            case "Ngừng hoạt động": f.Db.Execute("UPDATE Rooms SET Active=0 WHERE Id=@id", ("@id", target)); break;
            case "Loại ngừng hoạt động": f.Db.Execute("UPDATE RoomTypes SET Active=0 WHERE Id=(SELECT RoomTypeId FROM Rooms WHERE Id=@id)", ("@id", target)); break;
            case "Đang ở": f.Stay(target); break;
            case "Trùng lịch": f.Service.SaveBooking(f.Admin, f.Input([target], day: 1)); break;
            default: RoomStatus(target, reason); break;
        }
        Assert.ThrowsAny<InvalidOperationException>(() => f.Service.ChangeRoom(f.Admin, stay, oldRoom, target));
        Assert.Equal(oldRoom, Assert.Single(f.Service.BookingRooms(f.Admin, stay).AsEnumerable()).Field<int>("Id"));
        Assert.Equal("Sẵn sàng", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, oldRoom)["OperationalStatus"]);
    }

    [Fact]
    public void ChangeRoom_ValidatesStatusMembershipAndTotalCapacity()
    {
        var oldRoom = f.Room(capacity: 3);
        var otherRoom = f.Room(capacity: 1);
        var target = f.Room(capacity: 2);
        var stay = f.Service.SaveBooking(f.Admin, f.Input([oldRoom, otherRoom], guests: 4));
        f.Service.CheckIn(f.Admin, stay);
        Assert.ThrowsAny<ArgumentException>(() => f.Service.ChangeRoom(f.Admin, stay, oldRoom, oldRoom));
        Assert.ThrowsAny<InvalidOperationException>(() => f.Service.ChangeRoom(f.Admin, stay, target, f.Room()));
        Assert.ThrowsAny<InvalidOperationException>(() => f.Service.ChangeRoom(f.Admin, stay, oldRoom, otherRoom));
        Assert.ThrowsAny<ArgumentException>(() => f.Service.ChangeRoom(f.Admin, stay, oldRoom, target));
        var largeTarget = f.Room(capacity: 4);
        f.Service.ChangeRoom(f.Admin, stay, oldRoom, largeTarget);
        Assert.Equal(2, f.Service.BookingRooms(f.Admin, stay).Rows.Count);
        var pendingRoom = f.Room();
        var pending = f.Service.SaveBooking(f.Admin, f.Input([pendingRoom], day: 3));
        Assert.Throws<InvalidOperationException>(() => f.Service.ChangeRoom(f.Admin, pending, pendingRoom, target));
    }

    [Fact]
    public void ChangeRoom_RollsBackAllWrites_WhenCleaningUpdateFails()
    {
        var oldRoom = f.Room();
        var target = f.Room();
        var stay = f.Stay(oldRoom);
        f.Db.Execute("CREATE TRIGGER dbo.TR_TestChangeRoomFail ON dbo.Rooms AFTER UPDATE AS BEGIN THROW 51999, 'Simulated cleaning failure', 1; END");
        try { Assert.Throws<SqlException>(() => f.Service.ChangeRoom(f.Admin, stay, oldRoom, target)); }
        finally { f.Db.Execute("DROP TRIGGER dbo.TR_TestChangeRoomFail"); }
        Assert.Equal(oldRoom, Assert.Single(f.Service.BookingRooms(f.Admin, stay).AsEnumerable()).Field<int>("Id"));
        Assert.Equal("Sẵn sàng", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, oldRoom)["OperationalStatus"]);
    }

    [Fact]
    public async Task ConcurrentRoomChanges_OnlyOneStayGetsTarget()
    {
        var firstRoom = f.Room();
        var secondRoom = f.Room();
        var first = f.Stay(firstRoom);
        var second = f.Stay(secondRoom);
        var target = f.Room();
        var results = await Task.WhenAll(new[] { (first, firstRoom), (second, secondRoom) }.Select(pair => Task.Run(() =>
        {
            try { f.Service.ChangeRoom(f.Admin, pair.Item1, pair.Item2, target); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.Single(results, x => x);
    }

    [Fact]
    public void WalkIn_CreatesCustomerBookingAndCheckInTogether_AndAllowsSameDay()
    {
        var room = f.Room();
        var customer = Customer();
        var now = DateTime.Now;
        var id = f.Service.WalkIn(f.Reception, f.Input([room], deposit: 100000) with { Departure = now.AddMinutes(20) }, customer);
        var booking = f.Service.Booking(f.Admin, id);
        Assert.Equal("Đang ở", booking["Status"]);
        Assert.InRange(booking.Field<DateTime>("ActualCheckIn"), now, DateTime.Now);
        Assert.Equal(customer["FullName"], f.Service.CatalogRecord(f.Admin, CatalogKind.Customers, booking.Field<int>("CustomerId"))["FullName"]);
        var quote = f.Service.PreviewCheckout(f.Admin, id, 0, 0);
        Assert.Equal(1m, Assert.Single(quote.Lines).Quantity);
        Assert.Equal(400000m, quote.Balance);
    }

    [Fact]
    public void WalkIn_UsesExistingCustomer_AndRejectsPastDeparture()
    {
        var room = f.Room();
        var id = f.Service.WalkIn(f.Reception, f.Input([room]));
        Assert.Equal(1, f.Service.Booking(f.Admin, id).Field<int>("CustomerId"));
        Assert.Throws<ArgumentException>(() => f.Service.WalkIn(f.Admin, f.Input([f.Room()]) with { Departure = DateTime.Now.AddMinutes(-1) }));
    }

    [Theory]
    [InlineData("Chờ dọn")]
    [InlineData("Bảo trì")]
    [InlineData("Đang ở")]
    [InlineData("Trùng lịch")]
    [InlineData("Vượt sức chứa")]
    public void WalkIn_FailureDoesNotLeaveNewCustomerOrBooking(string reason)
    {
        var target = f.Room(capacity: 2);
        if (reason == "Đang ở") f.Stay(target);
        else if (reason == "Trùng lịch") f.Service.SaveBooking(f.Admin, f.Input([target], day: 1));
        else if (reason is "Chờ dọn" or "Bảo trì") RoomStatus(target, reason);
        var customer = Customer();
        var bookings = f.Db.Query("SELECT Id FROM Bookings").Rows.Count;
        var input = f.Input([target], guests: reason == "Vượt sức chứa" ? 3 : 1);
        if (reason == "Vượt sức chứa")
            Assert.Throws<ArgumentException>(() => f.Service.WalkIn(f.Reception, input, customer));
        else
            Assert.Throws<InvalidOperationException>(() => f.Service.WalkIn(f.Reception, input, customer));
        Assert.Equal(bookings, f.Db.Query("SELECT Id FROM Bookings").Rows.Count);
        Assert.Empty(f.Db.Query("SELECT Id FROM Customers WHERE IdentityNumber=@identity", ("@identity", customer["IdentityNumber"])).Rows.Cast<DataRow>());
    }

    [Fact]
    public void WalkIn_RejectsInvalidOrDuplicateCustomer_WithoutLeavingBooking()
    {
        var customer = Customer();
        f.Service.SaveCatalog(f.Reception, CatalogKind.Customers, null, customer);
        var count = f.Db.Query("SELECT Id FROM Bookings").Rows.Count;
        var duplicate = Assert.Throws<InvalidOperationException>(() => f.Service.WalkIn(f.Reception, f.Input([f.Room()]), customer));
        Assert.Contains("chọn khách hàng", duplicate.Message);
        customer["Phone"] = "abc";
        Assert.Throws<ArgumentException>(() => f.Service.WalkIn(f.Reception, f.Input([f.Room()]), customer));
        Assert.Equal(count, f.Db.Query("SELECT Id FROM Bookings").Rows.Count);
    }

    [Fact]
    public async Task ConcurrentWalkIns_OnlyOneSucceeds_AndLosingCustomerIsRolledBack()
    {
        var target = f.Room();
        var customers = new[] { Customer(), Customer() };
        var results = await Task.WhenAll(customers.Select(customer => Task.Run(() =>
        {
            try { f.Service.WalkIn(f.Reception, f.Input([target]), customer); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.Single(results, x => x);
        Assert.Single(f.Db.Query("SELECT Id FROM Customers WHERE IdentityNumber IN(@first,@second)",
            ("@first", customers[0]["IdentityNumber"]), ("@second", customers[1]["IdentityNumber"])).AsEnumerable());
    }

    [Fact]
    public void InvoiceServices_GroupRepeatedUses_AndPreservePriceSnapshots()
    {
        var stay = f.Stay(f.Room());
        var name = "Gộp dịch vụ " + Guid.NewGuid().ToString("N")[..8];
        var service = f.Service.SaveCatalog(f.Admin, CatalogKind.Services, null,
            new() { ["Name"] = name, ["Unit"] = "Lần", ["Price"] = 10000m });
        f.Service.AddService(f.Reception, stay, service, 1);
        f.Service.AddService(f.Reception, stay, service, 2);
        f.Service.SaveCatalog(f.Admin, CatalogKind.Services, service,
            new() { ["Name"] = name, ["Unit"] = "Lần", ["Price"] = 15000m });
        f.Service.AddService(f.Reception, stay, service, 4);
        var quote = f.Service.PreviewCheckout(f.Admin, stay, 0, 0);
        var lines = quote.Lines.Where(x => x.Kind == "Dịch vụ").OrderBy(x => x.UnitPrice).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(3m, lines[0].Quantity);
        Assert.Equal(4m, lines[1].Quantity);
        Assert.Equal(90000m, lines.Sum(x => x.Amount));
        Assert.Equal(3, f.Service.Usages(f.Admin, stay).Rows.Count);
        var invoice = f.Service.Checkout(f.Reception, quote, "Tiền mặt", "");
        Assert.Equal(lines, f.Service.Invoice(f.Admin, invoice).Lines.Where(x => x.Kind == "Dịch vụ").OrderBy(x => x.UnitPrice));
    }
}
