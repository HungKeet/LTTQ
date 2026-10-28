using System.Data;
using Hotel.Business;
using Hotel.Core;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Hotel.Tests;

public sealed class RoomUpgradeTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture f = fixture;

    private DataRow[] Upgrades(int bookingId) => f.Db.Query("SELECT u.* FROM RoomUpgrades u JOIN BookingRooms br ON br.StayId=u.StayId WHERE br.BookingId=@id ORDER BY u.Id", ("@id", bookingId)).AsEnumerable().ToArray();
    private int StayId(int bookingId, int roomId) => f.Db.Query("SELECT StayId FROM BookingRooms WHERE BookingId=@b AND RoomId=@r", ("@b", bookingId), ("@r", roomId)).Rows[0].Field<int>("StayId");
    private decimal CurrentRate(int bookingId, int roomId) => f.Service.BookingRooms(f.Admin, bookingId).AsEnumerable().Single(r => r.Field<int>("Id") == roomId).Field<decimal>("Giá / đêm");
    private static void Reject(Action action)
    {
        var error = Record.Exception(action);
        Assert.NotNull(error);
        Assert.True(error is ArgumentException or InvalidOperationException, $"Expected a business validation error, got {error.GetType().Name}: {error.Message}");
    }
    private void ChangeCatalogPrice(int roomId, decimal rate)
    {
        var room = f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, roomId);
        var typeId = room.Field<int>("RoomTypeId");
        var type = f.Service.CatalogRecord(f.Admin, CatalogKind.RoomTypes, typeId);
        f.Service.SaveCatalog(f.Admin, CatalogKind.RoomTypes, typeId, new()
        {
            ["Name"] = type["Name"], ["Capacity"] = type["Capacity"], ["Rate"] = rate, ["Active"] = true
        });
    }

    [Fact]
    public void Preview_UsesAgreedRateAndRemainingNights_WithoutMovingOrCharging()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source, 200000);
        ChangeCatalogPrice(source, 950000);
        var preview = f.Service.PreviewRoomUpgrade(f.Reception, stay, source, target);
        Assert.Equal(500000m, preview.OldRate);
        Assert.Equal(800000m, preview.NewRate);
        Assert.Equal(300000m, preview.Difference);
        Assert.Equal(Money.Nights(preview.QuotedAt, preview.Departure), preview.Nights);
        Assert.Equal(300000m * preview.Nights, preview.Fee);
        Assert.Equal(source, Assert.Single(f.Service.BookingRooms(f.Admin, stay).AsEnumerable()).Field<int>("Id"));
        Assert.Empty(Upgrades(stay));
        Assert.Empty(f.Db.Query("SELECT Id FROM Invoices WHERE BookingId=@id", ("@id", stay)).AsEnumerable());
    }

    [Fact]
    public void Upgrade_PreservesPastNightPricesAndDeposit_ChargesSeparateInvoiceLine()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source, 200000);
        var checkIn = DateTime.Today.AddDays(-2).AddHours(14);
        f.Db.Execute("UPDATE Bookings SET Arrival=@at,ActualCheckIn=@at WHERE Id=@id", ("@at", checkIn), ("@id", stay));
        var slot = StayId(stay, source);
        f.Service.AddService(f.Reception, stay, 1, 2);
        f.Service.UpgradeRoom(f.Reception, f.Service.PreviewRoomUpgrade(f.Reception, stay, source, target));
        Assert.Equal(slot, StayId(stay, target));
        Assert.Equal(800000m, CurrentRate(stay, target));
        Assert.Equal(500000m, f.Db.Query("SELECT NightlyRate FROM BookingRooms WHERE StayId=@id", ("@id", slot)).Rows[0].Field<decimal>("NightlyRate"));
        Assert.Equal("Chờ dọn", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, source)["OperationalStatus"]);
        Assert.Single(Upgrades(stay));
        var quote = f.Service.PreviewCheckout(f.Reception, stay, 25000, 5000);
        var roomLine = Assert.Single(quote.Lines, x => x.Kind == "Phòng");
        var upgradeLine = Assert.Single(quote.Lines, x => x.Kind == "Nâng phòng");
        Assert.Equal(500000m, roomLine.UnitPrice);
        Assert.Equal(Money.Nights(checkIn, quote.CheckoutAt), roomLine.Quantity);
        Assert.Equal(300000m, upgradeLine.UnitPrice);
        Assert.Equal(1m, upgradeLine.Quantity);
        Assert.Equal(25000m, quote.Surcharge);
        Assert.Equal(200000m, quote.Deposit);
        Assert.Equal(quote.Subtotal + 25000m - 5000m, quote.Total);
        var invoice = f.Service.Checkout(f.Reception, quote, "Tiền mặt", "Nâng phòng trả phí");
        var document = f.Service.Invoice(f.Admin, invoice);
        Assert.Equal(quote.Total, document.Total);
        Assert.Equal(upgradeLine, Assert.Single(document.Lines, x => x.Kind == "Nâng phòng"));
        Assert.Equal("Chờ dọn", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, target)["OperationalStatus"]);
        Assert.Throws<InvalidOperationException>(() => f.Service.PreviewRoomUpgrade(f.Admin, stay, target, f.Room(1000000)));
    }

    [Fact]
    public void MultipleUpgrades_UseIncrementalDifferences_WithoutDoubleCharging()
    {
        var first = f.Room(500000);
        var second = f.Room(800000);
        var third = f.Room(1000000);
        var stay = f.Stay(first);
        var slot = StayId(stay, first);
        f.Service.UpgradeRoom(f.Admin, f.Service.PreviewRoomUpgrade(f.Admin, stay, first, second));
        var next = f.Service.PreviewRoomUpgrade(f.Admin, stay, second, third);
        Assert.Equal(800000m, next.OldRate);
        Assert.Equal(200000m, next.Difference);
        f.Service.UpgradeRoom(f.Reception, next);
        Assert.Equal(slot, StayId(stay, third));
        Assert.Equal(1000000m, CurrentRate(stay, third));
        var lines = f.Service.PreviewCheckout(f.Admin, stay, 0, 0).Lines;
        Assert.Equal(2, lines.Count(x => x.Kind == "Nâng phòng"));
        Assert.Equal(new[] { 200000m, 300000m }, lines.Where(x => x.Kind == "Nâng phòng").Select(x => x.UnitPrice).Order().ToArray());
        Assert.Equal(1000000m, lines.Sum(x => x.Amount));
    }

    [Fact]
    public void ShortUpgrade_UsesMinimumOneNight_AndDoesNotChargeBeforeConfirmation()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var now = DateTime.Now;
        var stay = f.Service.SaveBooking(f.Admin, f.Input([source]) with { Arrival = now, Departure = now.AddMinutes(20) });
        f.Service.CheckIn(f.Admin, stay);
        var preview = f.Service.PreviewRoomUpgrade(f.Reception, stay, source, target);
        Assert.Equal(1, preview.Nights);
        Assert.Equal(300000m, preview.Fee);
        Assert.Empty(Upgrades(stay));
        f.Service.UpgradeRoom(f.Reception, preview);
        Assert.Equal(800000m, f.Service.PreviewCheckout(f.Reception, stay, 0, 0).Total);
    }

    [Fact]
    public void ExtendingAfterHistoricalUpgrade_ChargesAllActualUpgradeNights()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source);
        var slot = StayId(stay, source);
        // Represent a previously confirmed upgrade; history itself cannot be edited.
        f.Db.Execute("UPDATE Bookings SET Arrival=@a,ActualCheckIn=@a,Departure=@d WHERE Id=@id",
            ("@a", DateTime.Today.AddDays(-5).AddHours(14)), ("@d", DateTime.Today.AddDays(-1).AddHours(12)), ("@id", stay));
        f.Db.Execute("UPDATE BookingRooms SET RoomId=@new WHERE StayId=@stay", ("@new", target), ("@stay", slot));
        f.Db.Execute("""
            INSERT RoomUpgrades(StayId,FromRoomId,ToRoomId,FromRoomNumber,ToRoomNumber,OldNightlyRate,NewNightlyRate,UpgradedAt,EmployeeId)
            SELECT @stay,@old,@new,o.Number,n.Number,500000,800000,@at,@employee
            FROM Rooms o CROSS JOIN Rooms n WHERE o.Id=@old AND n.Id=@new
            """, ("@stay", slot), ("@old", source), ("@new", target),
            ("@at", DateTime.Today.AddDays(-2).AddHours(14)), ("@employee", f.Admin.EmployeeId));
        f.Service.ChangeDeparture(f.Reception, stay, DateTime.Today.AddDays(2).AddHours(12));
        var quote = f.Service.PreviewCheckout(f.Reception, stay, 0, 0);
        var fee = Assert.Single(quote.Lines, line => line.Kind == "Nâng phòng");
        Assert.Equal(2m, fee.Quantity);
        Assert.Equal(600000m, fee.Amount);
        Assert.Equal(3100000m, quote.Total);
    }

    [Fact]
    public void OrdinaryRoomChangeAfterUpgrade_KeepsAgreedRateAndExistingFees()
    {
        var source = f.Room(500000);
        var upgraded = f.Room(800000);
        var replacement = f.Room(1200000);
        var stay = f.Stay(source);
        f.Service.UpgradeRoom(f.Admin, f.Service.PreviewRoomUpgrade(f.Admin, stay, source, upgraded));
        var before = f.Service.PreviewCheckout(f.Admin, stay, 0, 0);
        var slot = StayId(stay, upgraded);
        f.Service.ChangeRoom(f.Reception, stay, upgraded, replacement);
        Assert.Equal(slot, StayId(stay, replacement));
        Assert.Equal(800000m, CurrentRate(stay, replacement));
        var after = f.Service.PreviewCheckout(f.Admin, stay, 0, 0);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.Lines.Where(x => x.Kind == "Nâng phòng"), after.Lines.Where(x => x.Kind == "Nâng phòng"));
        Assert.Single(Upgrades(stay));
        var next = f.Service.PreviewRoomUpgrade(f.Admin, stay, replacement, f.Room(1000000));
        Assert.Equal(800000m, next.OldRate);
        Assert.Equal(200000m, next.Difference);
    }

    [Theory]
    [InlineData("Chờ dọn")]
    [InlineData("Bảo trì")]
    [InlineData("Ngừng hoạt động")]
    [InlineData("Loại ngừng hoạt động")]
    [InlineData("Đang ở")]
    [InlineData("Trùng lịch")]
    public void Upgrade_RejectsUnavailableTarget_WithoutMoving(string reason)
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source);
        switch (reason)
        {
            case "Ngừng hoạt động": f.Db.Execute("UPDATE Rooms SET Active=0 WHERE Id=@id", ("@id", target)); break;
            case "Loại ngừng hoạt động": f.Db.Execute("UPDATE RoomTypes SET Active=0 WHERE Id=(SELECT RoomTypeId FROM Rooms WHERE Id=@id)", ("@id", target)); break;
            case "Đang ở": f.Stay(target); break;
            case "Trùng lịch": f.Service.SaveBooking(f.Admin, f.Input([target], day: 1)); break;
            default: f.Db.Execute("UPDATE Rooms SET OperationalStatus=@s WHERE Id=@id", ("@s", reason), ("@id", target)); break;
        }
        Reject(() => f.Service.PreviewRoomUpgrade(f.Admin, stay, source, target));
        Assert.Equal(source, Assert.Single(f.Service.BookingRooms(f.Admin, stay).AsEnumerable()).Field<int>("Id"));
        Assert.Empty(Upgrades(stay));
    }

    [Theory]
    [InlineData(500000)]
    [InlineData(450000)]
    public void Upgrade_RejectsEqualOrLowerPrice(int rate)
    {
        var source = f.Room(500000);
        var stay = f.Stay(source);
        Reject(() => f.Service.PreviewRoomUpgrade(f.Admin, stay, source, f.Room(rate)));
        Reject(() => f.Service.PreviewRoomUpgrade(f.Admin, stay, source, source));
        Assert.Empty(Upgrades(stay));
    }

    [Fact]
    public void Upgrade_ValidatesCapacityAcrossAllBookedRooms()
    {
        var source = f.Room(500000, 3);
        var other = f.Room(400000, 1);
        var stay = f.Service.SaveBooking(f.Admin, f.Input([source, other], guests: 4));
        f.Service.CheckIn(f.Admin, stay);
        Reject(() => f.Service.PreviewRoomUpgrade(f.Admin, stay, source, f.Room(800000, 2)));
        var target = f.Room(800000, 3);
        f.Service.UpgradeRoom(f.Reception, f.Service.PreviewRoomUpgrade(f.Reception, stay, source, target));
        Assert.Equal(2, f.Service.BookingRooms(f.Admin, stay).Rows.Count);
        Assert.Equal(800000m, CurrentRate(stay, target));
        Assert.Equal(400000m, CurrentRate(stay, other));
        Assert.Single(Upgrades(stay));
    }

    [Fact]
    public void ChangedTargetPrice_RejectsStaleQuoteAndAcceptsNewAgreement()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source);
        var preview = f.Service.PreviewRoomUpgrade(f.Admin, stay, source, target);
        ChangeCatalogPrice(target, 900000);
        Reject(() => f.Service.UpgradeRoom(f.Reception, preview));
        Assert.Empty(Upgrades(stay));
        Assert.Equal(source, Assert.Single(f.Service.BookingRooms(f.Admin, stay).AsEnumerable()).Field<int>("Id"));
        var updated = f.Service.PreviewRoomUpgrade(f.Reception, stay, source, target);
        Assert.Equal(400000m, updated.Difference);
        f.Service.UpgradeRoom(f.Reception, updated);
        Assert.Equal(900000m, CurrentRate(stay, target));
    }

    [Fact]
    public void ChangedDepartureAndForgedFee_RequireNewPreview()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source);
        var preview = f.Service.PreviewRoomUpgrade(f.Admin, stay, source, target);
        Reject(() => f.Service.UpgradeRoom(f.Admin, preview with { OldRate = 799999m }));
        f.Service.ChangeDeparture(f.Admin, stay, preview.Departure.AddDays(2));
        Reject(() => f.Service.UpgradeRoom(f.Admin, preview));
        var updated = f.Service.PreviewRoomUpgrade(f.Admin, stay, source, target);
        Assert.Equal(preview.Nights + 2, updated.Nights);
        Assert.Equal(preview.Fee + 600000m, updated.Fee);
        Assert.Empty(Upgrades(stay));
    }

    [Fact]
    public void Upgrade_RequiresActiveStayAndAuthorizedSession()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var pending = f.Service.SaveBooking(f.Admin, f.Input([source], day: 3));
        Reject(() => f.Service.PreviewRoomUpgrade(f.Admin, pending, source, target));
        var activeSource = f.Room(500000);
        var active = f.Stay(activeSource);
        var preview = f.Service.PreviewRoomUpgrade(f.Admin, active, activeSource, target);
        var fake = f.Admin with { UserId = int.MaxValue };
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.PreviewRoomUpgrade(fake, active, activeSource, target));
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.UpgradeRoom(fake, preview));
        f.Db.Execute("UPDATE Bookings SET Arrival=@a,ActualCheckIn=@a,Departure=@d WHERE Id=@id", ("@a", DateTime.Today.AddDays(-1)), ("@d", DateTime.Now.AddMinutes(-1)), ("@id", active));
        Reject(() => f.Service.PreviewRoomUpgrade(f.Admin, active, activeSource, target));
        Assert.Empty(Upgrades(active));
    }

    [Theory]
    [InlineData("RoomUpgrades")]
    [InlineData("Rooms")]
    public void Upgrade_RollsBackRoomAndFee_WhenAnyWriteFails(string table)
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source);
        var preview = f.Service.PreviewRoomUpgrade(f.Admin, stay, source, target);
        f.Db.Execute($"CREATE TRIGGER dbo.TR_TestUpgradeFail ON dbo.{table} AFTER {(table == "Rooms" ? "UPDATE" : "INSERT")} AS BEGIN THROW 51999, 'Simulated upgrade failure', 1; END");
        try { Assert.Throws<SqlException>(() => f.Service.UpgradeRoom(f.Admin, preview)); }
        finally { f.Db.Execute("DROP TRIGGER dbo.TR_TestUpgradeFail"); }
        Assert.Equal(source, Assert.Single(f.Service.BookingRooms(f.Admin, stay).AsEnumerable()).Field<int>("Id"));
        Assert.Equal(500000m, CurrentRate(stay, source));
        Assert.Equal("Sẵn sàng", f.Service.CatalogRecord(f.Admin, CatalogKind.Rooms, source)["OperationalStatus"]);
        Assert.Empty(Upgrades(stay));
    }

    [Fact]
    public async Task ConcurrentUpgrades_ToOneTarget_OnlyOneRoomAndFeeCommit()
    {
        var sourceA = f.Room(500000);
        var sourceB = f.Room(500000);
        var target = f.Room(800000);
        var a = f.Stay(sourceA);
        var b = f.Stay(sourceB);
        var previews = new[] { f.Service.PreviewRoomUpgrade(f.Admin, a, sourceA, target), f.Service.PreviewRoomUpgrade(f.Admin, b, sourceB, target) };
        var results = await Task.WhenAll(previews.Select(preview => Task.Run(() =>
        {
            try { f.Service.UpgradeRoom(f.Reception, preview); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.Single(results, result => result);
        Assert.Equal(1, Upgrades(a).Length + Upgrades(b).Length);
    }

    [Fact]
    public void Upgrade_RejectsRepeatedConfirmation_AndOldCheckoutPreview()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source);
        var checkoutBefore = f.Service.PreviewCheckout(f.Admin, stay, 0, 0);
        var preview = f.Service.PreviewRoomUpgrade(f.Admin, stay, source, target);
        f.Service.UpgradeRoom(f.Admin, preview);
        Reject(() => f.Service.UpgradeRoom(f.Admin, preview));
        Assert.Throws<InvalidOperationException>(() => f.Service.Checkout(f.Admin, checkoutBefore, "Tiền mặt", ""));
        Assert.Single(Upgrades(stay));
    }

    [Fact]
    public void UpgradeHistoryIsImmutable_AndCatalogChangesDoNotRepriceInvoices()
    {
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source);
        f.Service.UpgradeRoom(f.Admin, f.Service.PreviewRoomUpgrade(f.Admin, stay, source, target));
        var record = Assert.Single(Upgrades(stay));
        Assert.Throws<SqlException>(() => f.Db.Execute("UPDATE RoomUpgrades SET UpgradedAt=@at WHERE Id=@id", ("@at", DateTime.Today.AddDays(-1)), ("@id", record["Id"])));
        Assert.Throws<SqlException>(() => f.Db.Execute("DELETE RoomUpgrades WHERE Id=@id", ("@id", record["Id"])));
        ChangeCatalogPrice(target, 1200000);
        var quote = f.Service.PreviewCheckout(f.Admin, stay, 0, 0);
        Assert.Equal(300000m, Assert.Single(quote.Lines, x => x.Kind == "Nâng phòng").UnitPrice);
        var invoice = f.Service.Checkout(f.Admin, quote, "Thẻ", "");
        Assert.Equal(800000m, f.Service.Invoice(f.Admin, invoice).Total);
    }

    [Fact]
    public void UpgradeFees_AreIncludedInDailyAndMonthlyRoomRevenue()
    {
        decimal RoomRevenue(bool monthly) => (monthly ? f.Service.MonthlyRevenue(f.Admin, DateTime.Today, DateTime.Today) : f.Service.Revenue(f.Admin, DateTime.Today, DateTime.Today)).AsEnumerable().Sum(r => r.Field<decimal>("Tiền phòng"));
        var dailyBefore = RoomRevenue(false);
        var monthlyBefore = RoomRevenue(true);
        var source = f.Room(500000);
        var target = f.Room(800000);
        var stay = f.Stay(source, 200000);
        f.Service.UpgradeRoom(f.Admin, f.Service.PreviewRoomUpgrade(f.Admin, stay, source, target));
        f.Service.Checkout(f.Admin, f.Service.PreviewCheckout(f.Admin, stay, 0, 0), "Chuyển khoản", "");
        Assert.Equal(800000m, RoomRevenue(false) - dailyBefore);
        Assert.Equal(800000m, RoomRevenue(true) - monthlyBefore);
    }
}
