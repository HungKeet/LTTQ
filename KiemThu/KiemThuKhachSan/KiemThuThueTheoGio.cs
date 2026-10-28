using System.Data;
using Hotel.Business;
using Hotel.Core;
using Xunit;

namespace Hotel.Tests;

public sealed class HourlyRentalTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture f = fixture;

    [Theory]
    [InlineData(1, 175000)] // 35% của 500,000
    [InlineData(2, 175000)] // 35% của 500,000
    [InlineData(3, 225000)] // 45% của 500,000 (35% + 10%)
    [InlineData(4, 275000)] // 55% của 500,000 (35% + 20%)
    [InlineData(5, 325000)] // 65% của 500,000 (35% + 30%)
    [InlineData(6, 500000)] // Từ 6h trở lên: tính trần 100% giá ngày (500,000)
    [InlineData(8, 500000)] // Từ 6h trở lên: tính trần 100% giá ngày (500,000)
    [InlineData(9, 500000)] // Tối đa 100% của 500,000
    [InlineData(24, 500000)] // 24h: tối đa 100% của 500,000
    public void HourlyRate_Calculation_FollowsPolicy(int hours, decimal expectedCharge)
    {
        const decimal dailyRate = 500000m;
        var charge = Money.HourlyCharge(dailyRate, hours);
        Assert.Equal(expectedCharge, charge);
    }

    [Fact]
    public void SaveBooking_WithHourlyRental_SavesRentalTypeAndEnforcesMax24Hours()
    {
        var room = f.Room(rate: 600000m);
        var start = DateTime.Today.AddDays(5).AddHours(10);
        
        // Thuê 3 tiếng hợp lệ
        var inputHourly = f.Input([room]) with
        {
            Arrival = start,
            Departure = start.AddHours(3),
            RentalType = "Theo giờ"
        };
        var bookingId = f.Service.SaveBooking(f.Admin, inputHourly);
        Assert.True(bookingId > 0);

        var b = f.Service.Booking(f.Admin, bookingId);
        Assert.Equal("Theo giờ", b.Field<string>("RentalType"));

        // Thuê theo giờ vượt quá 24h phải bị từ chối
        var inputOver24 = inputHourly with
        {
            Departure = start.AddHours(25)
        };
        Assert.Throws<ArgumentException>(() => f.Service.SaveBooking(f.Admin, inputOver24));
    }

    [Fact]
    public void WalkIn_HourlyRental_SavesRentalTypeAndUpdatesRoom()
    {
        var room = f.Room(rate: 400000m);
        var now = DateTime.Now;
        var input = new BookingInput(
            CustomerId: 0,
            Arrival: now,
            Departure: now.AddHours(2),
            Guests: 1,
            Deposit: 0,
            Notes: "Khách vãng lai thuê theo giờ",
            RoomIds: [room],
            RentalType: "Theo giờ");

        var walkInId = f.Service.WalkIn(f.Reception, input, new()
        {
            ["FullName"] = "Khách thuê giờ",
            ["Phone"] = "0987654321",
            ["IdentityNumber"] = Guid.NewGuid().ToString("N")[..12],
            ["Address"] = "TP.HCM"
        });

        Assert.True(walkInId > 0);
        var booking = f.Service.Booking(f.Reception, walkInId);
        Assert.Equal("Theo giờ", booking.Field<string>("RentalType"));
        Assert.Equal("Đang ở", booking.Field<string>("Status"));

        // Kiểm tra RoomsOverview có trường Hình thức là 'Theo giờ'
        var overview = f.Service.RoomsOverview(f.Reception);
        var roomRow = overview.AsEnumerable().Single(r => r.Field<int>("Id") == room);
        Assert.Equal("Đang ở", roomRow.Field<string>("Trạng thái"));
        Assert.Equal("Theo giờ", roomRow.Field<string>("Hình thức"));
    }

    [Fact]
    public void BookingsFilter_ByRentalType_FiltersCorrectly()
    {
        var room1 = f.Room();
        var room2 = f.Room();
        var day = 8;
        var start1 = DateTime.Today.AddDays(day).AddHours(14);
        var start2 = DateTime.Today.AddDays(day).AddHours(8);

        var idDaily = f.Service.SaveBooking(f.Admin, f.Input([room1], day: day, nights: 1) with { RentalType = "Theo ngày" });
        var idHourly = f.Service.SaveBooking(f.Admin, f.Input([room2], day: day) with
        {
            Arrival = start2,
            Departure = start2.AddHours(4),
            RentalType = "Theo giờ"
        });

        // Lọc "Tất cả"
        var all = f.Service.Bookings(f.Admin, rentalType: "Tất cả");
        Assert.Contains(all.AsEnumerable(), r => r.Field<int>("Id") == idDaily);
        Assert.Contains(all.AsEnumerable(), r => r.Field<int>("Id") == idHourly);

        // Lọc "Theo ngày"
        var dailyOnly = f.Service.Bookings(f.Admin, rentalType: "Theo ngày");
        Assert.Contains(dailyOnly.AsEnumerable(), r => r.Field<int>("Id") == idDaily);
        Assert.DoesNotContain(dailyOnly.AsEnumerable(), r => r.Field<int>("Id") == idHourly);

        // Lọc "Theo giờ"
        var hourlyOnly = f.Service.Bookings(f.Admin, rentalType: "Theo giờ");
        Assert.DoesNotContain(hourlyOnly.AsEnumerable(), r => r.Field<int>("Id") == idDaily);
        Assert.Contains(hourlyOnly.AsEnumerable(), r => r.Field<int>("Id") == idHourly);
    }

    [Fact]
    public void Quote_HourlyRental_CalculatesCorrectAmountAndItemDescription()
    {
        const decimal rate = 500000m;
        var room = f.Room(rate: rate);
        var now = DateTime.Now;
        var input = new BookingInput(
            CustomerId: 0,
            Arrival: now,
            Departure: now.AddHours(3),
            Guests: 1,
            Deposit: 50000m,
            Notes: "Kiểm thử hóa đơn theo giờ",
            RoomIds: [room],
            RentalType: "Theo giờ");

        var bookingId = f.Service.WalkIn(f.Reception, input, new()
        {
            ["FullName"] = "Khách tính tiền giờ",
            ["Phone"] = "0912345678",
            ["IdentityNumber"] = Guid.NewGuid().ToString("N")[..12],
            ["Address"] = "Đà Nẵng"
        });

        // Đặt ActualCheckIn trước thời điểm hiện tại 2 giờ 50 phút (~170 phút) -> làm tròn lên đúng 3 giờ
        f.Db.Execute("UPDATE Bookings SET ActualCheckIn=@checkIn WHERE Id=@id",
            ("@checkIn", DateTime.Now.AddHours(-2).AddMinutes(-50)), ("@id", bookingId));

        var quote = f.Service.PreviewCheckout(f.Reception, bookingId, 0m, 0m);
        // Với 3 giờ: 45% của 500,000 = 225,000đ
        var expectedRoomCharge = Money.HourlyCharge(rate, 3);
        Assert.Equal(225000m, expectedRoomCharge);
        Assert.Equal(expectedRoomCharge, quote.Subtotal);
        Assert.Equal(50000m, quote.Deposit);
        Assert.Equal(expectedRoomCharge - 50000m, quote.Balance);

        // Kiểm tra chi tiết dòng hóa đơn
        var item = Assert.Single(quote.Lines);
        Assert.Contains("(theo giờ: 3h)", item.Description);
        Assert.Equal(1, item.Quantity);
        Assert.Equal("Lượt", item.Unit);
        Assert.Equal(expectedRoomCharge, item.UnitPrice);
    }
}
