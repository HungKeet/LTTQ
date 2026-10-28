using System.Data;
using Hotel.Business;
using Hotel.Core;
using Xunit;

namespace Hotel.Tests;

public sealed class CustomerValidationTests
{
    [Theory]
    [InlineData("  Nguyễn  Thị Lan  ", "Nguyễn Thị Lan")]
    [InlineData("Jean-Luc", "Jean-Luc")]
    [InlineData("O'Connor", "O'Connor")]
    [InlineData("Đặng An", "Đặng An")]
    [InlineData("François D’Artagnan", "François D’Artagnan")]
    public void FullName_NormalizesWhitespaceAndAcceptsInternationalNames(string input, string expected)
        => Assert.Equal(expected, CustomerValidation.FullName(input));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("123456")]
    [InlineData("Nguyễn 123")]
    [InlineData("---")]
    [InlineData("<script>")]
    public void FullName_RejectsMissingShortOrInvalidNames(string input)
        => Assert.Throws<ArgumentException>(() => CustomerValidation.FullName(input));

    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("+84 912-345-678", "0912345678")]
    [InlineData("090.123.4567", "0901234567")]
    [InlineData("024 3825 1234", "02438251234")]
    [InlineData("+84 24 3825 1234", "02438251234")]
    [InlineData("02838221234", "02838221234")]
    public void Phone_RecognizesVietnameseMobileAndLandline(string input, string expected)
        => Assert.Equal(expected, CustomerValidation.Phone(input));

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("091234567a")]
    [InlineData("091234567")]
    [InlineData("09123456789")]
    [InlineData("+14155552671")]
    [InlineData("0000000000")]
    [InlineData("02412")]
    public void Phone_RejectsInvalidOrNonVietnameseNumbers(string input)
        => Assert.Throws<ArgumentException>(() => CustomerValidation.Phone(input));

    [Fact]
    public void IdentityAndAddress_AreOptionalAndRespectLengthAndContent()
    {
        Assert.Equal("", CustomerValidation.Identity("   "));
        Assert.Equal("", CustomerValidation.Address("   "));
        Assert.Equal("CCCD 12345", CustomerValidation.Identity(" CCCD 12345 "));
        Assert.Equal("12 Nguyễn Trãi, Hà Nội", CustomerValidation.Address(" 12 Nguyễn Trãi, Hà Nội "));
        Assert.Throws<ArgumentException>(() => CustomerValidation.Identity(new string('A', 31)));
        Assert.Throws<ArgumentException>(() => CustomerValidation.Address(new string('A', 251)));
        Assert.Throws<ArgumentException>(() => CustomerValidation.Address("-- ..."));
    }
}

public sealed class CustomerValidationIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public void DuplicateInactiveCustomer_ExplainsHowToReuseIdentity()
    {
        var identity = Guid.NewGuid().ToString("N")[..20];
        var fields = new Dictionary<string, object?> { ["FullName"] = "Khách cũ", ["Phone"] = "0901234567", ["IdentityNumber"] = identity, ["Active"] = false };
        fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Customers, null, fields);
        fields["Active"] = true;
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Customers, null, fields));
        Assert.Contains("mở lại", error.Message);
    }

    [Fact]
    public void WalkIn_NormalizesValidFieldsAndRejectsInvalidWithoutPartialWrites()
    {
        var room = fixture.Room();
        var now = DateTime.Now;
        var input = new BookingInput(0, now, now.AddHours(2), 1, 0, "Kiểm tra thông tin", [room], "Theo giờ");
        int CustomerCount() => fixture.Db.Query("SELECT Id FROM Customers").Rows.Count;
        int BookingCount() => fixture.Db.Query("SELECT Id FROM Bookings").Rows.Count;
        var customersBefore = CustomerCount();
        var bookingsBefore = BookingCount();
        var identity = Guid.NewGuid().ToString("N")[..20];
        var fields = new Dictionary<string, object?>
        {
            ["FullName"] = "  Trần   Mai  ", ["Phone"] = "+84 912-345-678",
            ["IdentityNumber"] = identity, ["Address"] = "  Hà Nội  "
        };
        fields["Phone"] = "abc";
        Assert.Throws<ArgumentException>(() => fixture.Service.WalkIn(fixture.Reception, input, fields));
        Assert.Equal(customersBefore, CustomerCount());
        Assert.Equal(bookingsBefore, BookingCount());
        fields["Phone"] = "+84 912-345-678";
        var id = fixture.Service.WalkIn(fixture.Reception, input, fields);
        var booking = fixture.Service.Booking(fixture.Reception, id);
        var saved = fixture.Service.CatalogRecord(fixture.Reception, CatalogKind.Customers, booking.Field<int>("CustomerId"));
        Assert.Equal("Trần Mai", saved.Field<string>("FullName"));
        Assert.Equal("0912345678", saved.Field<string>("Phone"));
        Assert.Equal("Hà Nội", saved.Field<string>("Address"));
        Assert.Equal(customersBefore + 1, CustomerCount());
        Assert.Equal(bookingsBefore + 1, BookingCount());
    }

    [Fact]
    public void DuplicateIdentity_ReportsExistingCustomerInCatalogAndWalkIn()
    {
        var identity = Guid.NewGuid().ToString("N")[..20];
        var fields = new Dictionary<string, object?> { ["FullName"] = "Khách thử", ["Phone"] = "0901234567", ["IdentityNumber"] = identity };
        var customer = fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Customers, null, fields);
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Customers, null, fields));
        Assert.Contains("chọn khách hàng", error.Message);
        var count = fixture.Db.Query("SELECT Id FROM Bookings").Rows.Count;
        var now = DateTime.Now;
        var input = new BookingInput(0, now, now.AddHours(2), 1, 0, "", [fixture.Room()], "Theo giờ");
        error = Assert.Throws<InvalidOperationException>(() => fixture.Service.WalkIn(fixture.Reception, input, fields));
        Assert.Contains("chọn khách hàng", error.Message);
        Assert.Equal(count, fixture.Db.Query("SELECT Id FROM Bookings").Rows.Count);
        Assert.Equal(identity, fixture.Service.CatalogRecord(fixture.Admin, CatalogKind.Customers, customer).Field<string>("IdentityNumber"));
    }
}
