using System.Data;
using Hotel.Core;
using Hotel.Data;
using Microsoft.Data.SqlClient;

namespace Hotel.Business;

public sealed partial class HotelService
{
    private static CheckoutQuote Quote(SqlConnection c, SqlTransaction? t, int bookingId, decimal surcharge, decimal discount, DateTime checkoutAt)
    {
        var b = RequireBooking(c, t, bookingId, "Đang ở");
        var nights = Money.Nights(b.Field<DateTime>("ActualCheckIn"), checkoutAt);
        var lines = new List<InvoiceLine>();
        foreach (DataRow room in Database.Query(c, t, "SELECT r.Number,br.NightlyRate FROM BookingRooms br JOIN Rooms r ON r.Id=br.RoomId WHERE br.BookingId=@id ORDER BY r.Number", ("@id", bookingId)).Rows)
            lines.Add(new InvoiceLine("Phòng", "Phòng " + room["Number"], nights, "Đêm", room.Field<decimal>("NightlyRate")));
        foreach (DataRow usage in Database.Query(c, t, "SELECT * FROM ServiceUsages WHERE BookingId=@id ORDER BY Id", ("@id", bookingId)).Rows)
            lines.Add(new InvoiceLine("Dịch vụ", usage.Field<string>("ServiceName")!, usage.Field<int>("Quantity"), usage.Field<string>("Unit")!, usage.Field<decimal>("UnitPrice")));
        Money.ValidateAdjustments(lines.Sum(x => x.Amount), surcharge, discount);
        return new CheckoutQuote(bookingId, checkoutAt, lines, Money.Round(surcharge), Money.Round(discount), b.Field<decimal>("Deposit"));
    }
    public CheckoutQuote PreviewCheckout(Session user, int bookingId, decimal surcharge, decimal discount)
    {
        return db.Transaction((c, t) => { Authorize(c, t, user); return Quote(c, t, bookingId, surcharge, discount, DateTime.Now); });
    }
    public int Checkout(Session user, CheckoutQuote preview, string method, string notes)
    {
        if (!new[] { "Tiền mặt", "Chuyển khoản", "Thẻ" }.Contains(method))
            throw new ArgumentException("Phương thức thanh toán không hợp lệ.");
        if (notes.Length > 1000)
            throw new ArgumentException("Ghi chú không quá 1000 ký tự.");
        return db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            var quote = Quote(c, t, preview.BookingId, preview.Surcharge, preview.Discount, DateTime.Now);
            if (quote.Total != preview.Total || quote.Deposit != preview.Deposit || !quote.Lines.SequenceEqual(preview.Lines))
                throw new InvalidOperationException("Chi tiết thanh toán đã thay đổi. Hãy tính lại trước khi xác nhận.");
            var b = RequireBooking(c, t, preview.BookingId, "Đang ở");
            var customer = Single(Database.Query(c, t, "SELECT * FROM Customers WHERE Id=@id", ("@id", b["CustomerId"])));
            var employee = Database.Scalar<string>(c, t, "SELECT FullName FROM Employees WHERE Id=@id", ("@id", user.EmployeeId));
            var invoiceId = Database.Scalar<int>(c, t, """
                INSERT Invoices(BookingId,EmployeeId,CustomerName,CustomerPhone,CustomerIdentity,CustomerAddress,EmployeeName,CheckIn,CheckOut,Surcharge,Discount,Deposit,Total,PaymentMethod,Notes)
                OUTPUT INSERTED.Id VALUES(@b,@employee,@name,@phone,@identity,@address,@ename,@in,@out,@surcharge,@discount,@deposit,@total,@method,@notes)
                """, ("@b", preview.BookingId), ("@employee", user.EmployeeId), ("@name", customer["FullName"]), ("@phone", customer["Phone"]), ("@identity", customer.IsNull("IdentityNumber") ? "" : customer["IdentityNumber"]), ("@address", customer["Address"]), ("@ename", employee), ("@in", b["ActualCheckIn"]), ("@out", quote.CheckoutAt), ("@surcharge", quote.Surcharge), ("@discount", quote.Discount), ("@deposit", quote.Deposit), ("@total", quote.Total), ("@method", method), ("@notes", notes.Trim()));
            foreach (var line in quote.Lines)
                Database.Execute(c, t, "INSERT InvoiceLines(InvoiceId,Kind,Description,Quantity,Unit,UnitPrice) VALUES(@id,@kind,@desc,@q,@unit,@price)", ("@id", invoiceId), ("@kind", line.Kind), ("@desc", line.Description), ("@q", line.Quantity), ("@unit", line.Unit), ("@price", line.UnitPrice));
            Database.Execute(c, t, "UPDATE Bookings SET Status=N'Đã trả',ActualCheckOut=@out WHERE Id=@id; UPDATE Rooms SET OperationalStatus=N'Chờ dọn' WHERE Id IN(SELECT RoomId FROM BookingRooms WHERE BookingId=@id)", ("@out", quote.CheckoutAt), ("@id", preview.BookingId));
            Audit(c, t, user, "Thanh toán và trả phòng", $"HĐ #{invoiceId}; tổng {quote.Total:N0}; thu/hoàn {quote.Balance:N0}");
            return invoiceId;
        });
    }
    public DataTable Invoices(Session user, DateTime from, DateTime to, string search = "")
    {
        using var c = db.Open();
        Authorize(c, null, user);
        return Database.Query(c, null, """
            SELECT Id,N'HD'+RIGHT(N'000000'+CONVERT(nvarchar(20),Id),6) AS [Số hóa đơn],BookingId AS [Mã phiếu],CustomerName AS [Khách hàng],CustomerPhone AS [Điện thoại],IssuedAt AS [Ngày lập],Total AS [Tổng tiền],Deposit AS [Đã cọc],Total-Deposit AS [Thu thêm / hoàn âm],PaymentMethod AS [Thanh toán],EmployeeName AS [Nhân viên]
            FROM Invoices WHERE IssuedAt>=@from AND IssuedAt<@to AND (CustomerName LIKE @s OR CustomerPhone LIKE @s OR CONVERT(nvarchar(20),Id)=@exact OR N'HD'+RIGHT(N'000000'+CONVERT(nvarchar(20),Id),6) LIKE @s) ORDER BY Id DESC
            """, ("@from", from.Date), ("@to", to.Date.AddDays(1)), ("@s", "%" + search.Trim() + "%"), ("@exact", search.Trim()));
    }
    public InvoiceDocument Invoice(Session user, int id)
    {
        using var c = db.Open();
        Authorize(c, null, user);
        var r = Single(Database.Query(c, null, "SELECT * FROM Invoices WHERE Id=@id", ("@id", id)));
        var lines = Database.Query(c, null, "SELECT * FROM InvoiceLines WHERE InvoiceId=@id ORDER BY Id", ("@id", id)).AsEnumerable().Select(l => new InvoiceLine(l.Field<string>("Kind")!, l.Field<string>("Description")!, l.Field<decimal>("Quantity"), l.Field<string>("Unit")!, l.Field<decimal>("UnitPrice"))).ToList();
        return new InvoiceDocument(id, $"HD{id:D6}", r.Field<string>("CustomerName")!, r.Field<string>("CustomerPhone")!, r.Field<string>("CustomerIdentity")!, r.Field<string>("CustomerAddress")!, r.Field<string>("EmployeeName")!, r.Field<DateTime>("IssuedAt"), r.Field<DateTime>("CheckIn"), r.Field<DateTime>("CheckOut"), r.Field<decimal>("Surcharge"), r.Field<decimal>("Discount"), r.Field<decimal>("Deposit"), r.Field<decimal>("Total"), r.Field<string>("PaymentMethod")!, r.Field<string>("Notes")!, lines);
    }
    public DataTable Revenue(Session user, DateTime from, DateTime to)
    {
        if (to.Date < from.Date)
            throw new ArgumentException("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");
        using var c = db.Open();
        Authorize(c, null, user, true);
        return Database.Query(c, null, """
            SELECT CAST(i.IssuedAt AS date) AS [Ngày],COUNT(*) AS [Số hóa đơn],SUM(x.RoomAmount) AS [Tiền phòng],SUM(x.ServiceAmount) AS [Tiền dịch vụ],SUM(i.Surcharge) AS [Phụ thu],SUM(i.Discount) AS [Giảm giá],SUM(i.Total) AS [Doanh thu],SUM(i.Deposit) AS [Cọc đã đối trừ],SUM(i.Total-i.Deposit) AS [Thu thêm / hoàn âm]
            FROM Invoices i CROSS APPLY(SELECT COALESCE(SUM(CASE WHEN Kind=N'Phòng' THEN Amount ELSE 0 END),0) RoomAmount,COALESCE(SUM(CASE WHEN Kind=N'Dịch vụ' THEN Amount ELSE 0 END),0) ServiceAmount FROM InvoiceLines WHERE InvoiceId=i.Id) x
            WHERE i.IssuedAt>=@from AND i.IssuedAt<@to GROUP BY CAST(i.IssuedAt AS date) ORDER BY [Ngày]
            """, ("@from", from.Date), ("@to", to.Date.AddDays(1)));
    }
    public DataTable MonthlyRevenue(Session user, DateTime from, DateTime to)
    {
        if (to.Date < from.Date)
            throw new ArgumentException("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");
        using var c = db.Open();
        Authorize(c, null, user, true);
        return Database.Query(c, null, """
            SELECT FORMAT(i.IssuedAt, 'MM/yyyy') AS [Tháng],COUNT(*) AS [Số hóa đơn],SUM(x.RoomAmount) AS [Tiền phòng],SUM(x.ServiceAmount) AS [Tiền dịch vụ],SUM(i.Surcharge) AS [Phụ thu],SUM(i.Discount) AS [Giảm giá],SUM(i.Total) AS [Doanh thu],SUM(i.Deposit) AS [Cọc đã đối trừ],SUM(i.Total-i.Deposit) AS [Thu thêm / hoàn âm]
            FROM Invoices i CROSS APPLY(SELECT COALESCE(SUM(CASE WHEN Kind=N'Phòng' THEN Amount ELSE 0 END),0) RoomAmount,COALESCE(SUM(CASE WHEN Kind=N'Dịch vụ' THEN Amount ELSE 0 END),0) ServiceAmount FROM InvoiceLines WHERE InvoiceId=i.Id) x
            WHERE i.IssuedAt>=@from AND i.IssuedAt<@to GROUP BY FORMAT(i.IssuedAt, 'yyyy-MM'), FORMAT(i.IssuedAt, 'MM/yyyy') ORDER BY FORMAT(i.IssuedAt, 'yyyy-MM')
            """, ("@from", from.Date), ("@to", to.Date.AddDays(1)));
    }
    public DataTable ServiceRevenue(Session user, DateTime from, DateTime to)
    {
        using var c = db.Open();
        Authorize(c, null, user, true);
        return Database.Query(c, null, "SELECT l.Description AS [Dịch vụ],l.Unit AS [Đơn vị],SUM(l.Quantity) AS [Số lượng],SUM(l.Amount) AS [Doanh thu dịch vụ] FROM InvoiceLines l JOIN Invoices i ON i.Id=l.InvoiceId WHERE l.Kind=N'Dịch vụ' AND i.IssuedAt>=@from AND i.IssuedAt<@to GROUP BY l.Description,l.Unit ORDER BY [Doanh thu dịch vụ] DESC", ("@from", from.Date), ("@to", to.Date.AddDays(1)));
    }
    public DataTable AuditHistory(Session user)
    {
        using var c = db.Open();
        Authorize(c, null, user, true);
        return Database.Query(c, null, "SELECT TOP(500) a.CreatedAt AS [Thời điểm],u.Username AS [Tài khoản],a.Action AS [Thao tác],a.Detail AS [Chi tiết] FROM AuditLog a JOIN Users u ON u.Id=a.UserId ORDER BY a.Id DESC");
    }
}
