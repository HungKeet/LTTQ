using System.Data;
using Hotel.Business;
using Xunit;

namespace Hotel.Tests;

public sealed class EmployeeAccountConsistencyTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public void CurrentManagerCannotDemoteOwnEmployeeEvenWithOtherManager()
    {
        var other = fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Employees, null, new()
        {
            ["FullName"] = "Quản lý khác", ["Phone"] = "0912345678", ["Position"] = "Quản lý", ["Active"] = true, ["Password"] = "Manager@123"
        });
        var original = fixture.Service.CatalogRecord(fixture.Admin, CatalogKind.Employees, fixture.Admin.EmployeeId);
        var changes = new Dictionary<string, object?>
        {
            ["FullName"] = original["FullName"], ["Phone"] = original["Phone"], ["Position"] = "Lễ tân", ["Active"] = true
        };
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Employees, fixture.Admin.EmployeeId, changes));
        Assert.Equal("Quản lý", fixture.Service.CatalogRecord(fixture.Admin, CatalogKind.Users, fixture.Admin.UserId)["Role"]);
        Assert.Equal("Quản lý", fixture.Service.CatalogRecord(fixture.Admin, CatalogKind.Employees, fixture.Admin.EmployeeId)["Position"]);
        Assert.True(other > 0);
    }

    [Fact]
    public void EmployeeRoleChangeSynchronizesAllAccountsWithoutUnlockingDisabledAccounts()
    {
        var employee = fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Employees, null, new()
        {
            ["FullName"] = "Nhân viên nhiều tài khoản", ["Phone"] = "0912345678", ["Position"] = "Quản lý", ["Active"] = true, ["Password"] = "Employee@123"
        });
        var first = fixture.Db.Query("SELECT Id FROM Users WHERE EmployeeId=@eid", ("@eid", employee)).Rows[0].Field<int>("Id");
        var second = fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Users, null, new()
        {
            ["Username"] = "nv_" + Guid.NewGuid().ToString("N")[..8], ["EmployeeId"] = employee, ["Role"] = "Kế toán", ["Active"] = false, ["Password"] = "Employee@123"
        });
        fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Employees, employee, new()
        {
            ["FullName"] = "Nhân viên nhiều tài khoản", ["Phone"] = "0912345678", ["Position"] = "Quản lý", ["Active"] = true
        });
        Assert.Equal("Kế toán", fixture.Db.Query("SELECT Role FROM Users WHERE Id=@id", ("@id", second)).Rows[0].Field<string>("Role"));
        Dictionary<string, object?> Changes(bool active, string password = "") => new()
        {
            ["FullName"] = "Nhân viên nhiều tài khoản", ["Phone"] = "0912345678", ["Position"] = "Lễ tân", ["Active"] = active, ["Password"] = password
        };
        fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Employees, employee, Changes(true));
        var accounts = fixture.Db.Query("SELECT Id, Role, Active FROM Users WHERE EmployeeId=@eid ORDER BY Id", ("@eid", employee)).AsEnumerable().ToArray();
        Assert.Equal(2, accounts.Length);
        Assert.All(accounts, row => Assert.Equal("Lễ tân", row.Field<string>("Role")));
        Assert.True(accounts.Single(row => row.Field<int>("Id") == first).Field<bool>("Active"));
        Assert.False(accounts.Single(row => row.Field<int>("Id") == second).Field<bool>("Active"));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Employees, employee, Changes(true, "Changed@123")));
        fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Employees, employee, Changes(false));
        fixture.Service.SaveCatalog(fixture.Admin, CatalogKind.Employees, employee, Changes(true));
        Assert.False(fixture.Db.Query("SELECT Active FROM Users WHERE Id=@id", ("@id", second)).Rows[0].Field<bool>("Active"));
    }
}
