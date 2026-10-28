using System.Data;
using Hotel.Core;
using Xunit;

namespace Hotel.Tests;

public sealed class HourlyBillingFixTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture f = fixture;

    private (int Booking, int Room) HourlyStay(decimal rate = 500000m)
    {
        var room = f.Room(rate);
        var now = DateTime.Now;
        var booking = f.Service.WalkIn(f.Admin, f.Input([room]) with
        {
            Arrival = now, Departure = now.AddHours(2), RentalType = "Theo giờ"
        });
        return (booking, room);
    }

    [Theory]
    [InlineData(24, 500000)]
    [InlineData(25, 675000)]
    [InlineData(27, 725000)]
    [InlineData(48, 1000000)]
    [InlineData(49, 1175000)]
    public void HourlyCharge_BeyondOneDay_ChargesFullDaysAndRemainingHours(int hours, decimal expected)
        => Assert.Equal(expected, Money.HourlyCharge(500000m, hours));

    [Fact]
    public void HourlyCheckout_Overstay_ChargesBothFullDays()
    {
        var stay = HourlyStay();
        var start = DateTime.Now.AddHours(-48).AddMinutes(10);
        f.Db.Execute("UPDATE Bookings SET Arrival=@at,ActualCheckIn=@at WHERE Id=@id", ("@at", start), ("@id", stay.Booking));
        var quote = f.Service.PreviewCheckout(f.Admin, stay.Booking, 0, 0);
        Assert.Equal(1000000m, quote.Total);
        var invoice = f.Service.Checkout(f.Admin, quote, "Tiền mặt", "");
        Assert.Equal(1000000m, f.Service.Invoice(f.Admin, invoice).Total);
    }

    [Fact]
    public void HourlyCheckout_RejectsClockEarlierThanCheckIn()
    {
        var stay = HourlyStay();
        f.Db.Execute("UPDATE Bookings SET ActualCheckIn=@at WHERE Id=@id", ("@at", DateTime.Now.AddHours(1)), ("@id", stay.Booking));
        Assert.Throws<ArgumentException>(() => f.Service.PreviewCheckout(f.Admin, stay.Booking, 0, 0));
        Assert.Empty(f.Db.Query("SELECT Id FROM Invoices WHERE BookingId=@id", ("@id", stay.Booking)).AsEnumerable());
    }

    [Fact]
    public void HourlyCheckout_OneDongRate_CanBePaid()
    {
        var stay = HourlyStay(1m);
        var quote = f.Service.PreviewCheckout(f.Admin, stay.Booking, 0, 0);
        Assert.Equal(1m, quote.Total);
        var invoice = f.Service.Checkout(f.Admin, quote, "Tiền mặt", "");
        Assert.Equal(1m, f.Service.Invoice(f.Admin, invoice).Total);
    }

    [Fact]
    public void HourlyUpgrade_UsesSamePolicyForPreviewAndSettlement()
    {
        var stay = HourlyStay();
        var target = f.Room(800000m);
        var preview = f.Service.PreviewRoomUpgrade(f.Admin, stay.Booking, stay.Room, target);
        Assert.Equal("Theo giờ", preview.RentalType);
        Assert.Equal(2, preview.Hours);
        Assert.Equal(105000m, preview.Fee);
        f.Service.UpgradeRoom(f.Admin, preview);
        var quote = f.Service.PreviewCheckout(f.Admin, stay.Booking, 0, 0);
        Assert.Equal(105000m, Assert.Single(quote.Lines, line => line.Kind == "Nâng phòng").Amount);
    }

    [Fact]
    public void HourlyUpgrade_OneDongDifference_CanBePaid()
    {
        var stay = HourlyStay();
        var target = f.Room(500001m);
        f.Service.UpgradeRoom(f.Admin, f.Service.PreviewRoomUpgrade(f.Admin, stay.Booking, stay.Room, target));
        var quote = f.Service.PreviewCheckout(f.Admin, stay.Booking, 0, 0);
        Assert.Equal(1m, Assert.Single(quote.Lines, line => line.Kind == "Nâng phòng").Amount);
        f.Service.Checkout(f.Admin, quote, "Tiền mặt", "");
    }
}
