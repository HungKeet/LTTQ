using System.Data;
using Hotel.Core;
using Xunit;

namespace Hotel.Tests;

public sealed class HourlyBookingFixTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public void ExtensionBeyond24HoursIsRejectedWithoutChangingBooking()
    {
        var room = fixture.Room();
        var now = DateTime.Now;
        var id = fixture.Service.WalkIn(fixture.Admin, fixture.Input([room]) with
        {
            Arrival = now, Departure = now.AddHours(2), RentalType = "Theo giờ"
        });
        var before = fixture.Service.Booking(fixture.Admin, id).Field<DateTime>("Departure");
        Assert.Throws<ArgumentException>(() => fixture.Service.ChangeDeparture(fixture.Admin, id, now.AddHours(72)));
        Assert.Equal(before, fixture.Service.Booking(fixture.Admin, id).Field<DateTime>("Departure"));
        fixture.Service.ChangeDeparture(fixture.Admin, id, now.AddHours(3));
        Assert.Equal("Theo giờ", fixture.Service.Booking(fixture.Admin, id).Field<string>("RentalType"));
    }
}
