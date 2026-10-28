using System.Data;
using Hotel.Core;
using Hotel.Data;
using Microsoft.Data.SqlClient;

namespace Hotel.Business;

public sealed partial class HotelService
{
    public int InvoiceIdForBooking(Session user, int bookingId)
    {
        using var connection = db.Open();
        Authorize(connection, null, user);
        return Database.Scalar<int>(connection, null,
            "SELECT Id FROM Invoices WHERE BookingId=@id", ("@id", bookingId));
    }

    public RoomUpgradeQuote PreviewRoomUpgrade(Session user, int bookingId, int oldRoomId, int newRoomId)
    {
        return db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            return QuoteRoomUpgrade(c, t, bookingId, oldRoomId, newRoomId, DateTime.Now);
        });
    }

    private static RoomUpgradeQuote QuoteRoomUpgrade(SqlConnection c, SqlTransaction t, int bookingId, int oldRoomId, int newRoomId, DateTime now)
    {
        if (oldRoomId == newRoomId)
            throw new ArgumentException("Hãy chọn phòng mới khác phòng đang ở.");
        var booking = RequireBooking(c, t, bookingId, "Đang ở");
        var departure = booking.Field<DateTime>("Departure");
        if (departure <= now)
            throw new InvalidOperationException("Phiếu đã quá giờ trả dự kiến. Hãy đổi ngày đi trước khi nâng cấp phòng.");
        var oldRoom = Single(Database.Query(c, t, """
            SELECT br.StayId,r.Number,br.NightlyRate+
                COALESCE((SELECT SUM(u.NewNightlyRate-u.OldNightlyRate) FROM RoomUpgrades u WHERE u.StayId=br.StayId),0) AS AgreedRate
            FROM BookingRooms br JOIN Rooms r ON r.Id=br.RoomId
            WHERE br.BookingId=@b AND br.RoomId=@old
            """, ("@b", bookingId), ("@old", oldRoomId)));
        var target = Single(Database.Query(c, t, """
            SELECT r.Number,r.OperationalStatus,r.Active,t.Active AS TypeActive,t.Capacity,t.Rate
            FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@new
            """, ("@new", newRoomId)));
        if (!target.Field<bool>("Active") || !target.Field<bool>("TypeActive") || target.Field<string>("OperationalStatus") != "Sẵn sàng")
            throw new InvalidOperationException("Phòng nâng cấp phải đang hoạt động và sẵn sàng nhận khách.");
        if (Database.Scalar<int>(c, t, """
            SELECT COUNT(*) FROM BookingRooms br JOIN Bookings b ON b.Id=br.BookingId
            WHERE br.RoomId=@new AND b.Status=N'Đang ở'
            """, ("@new", newRoomId)) > 0)
            throw new InvalidOperationException("Phòng nâng cấp vẫn đang có khách.");
        if (Database.Scalar<int>(c, t, ConflictSql, ("@room", newRoomId), ("@exclude", bookingId), ("@from", now), ("@to", departure), ("@now", now)) > 0)
            throw new InvalidOperationException("Phòng nâng cấp trùng lịch đặt trong thời gian lưu trú còn lại.");
        var otherCapacity = Database.Scalar<int>(c, t,
            "SELECT COALESCE(SUM(Capacity),0) FROM BookingRooms WHERE BookingId=@b AND RoomId<>@old",
            ("@b", bookingId), ("@old", oldRoomId));
        if (booking.Field<int>("Guests") > otherCapacity + target.Field<int>("Capacity"))
            throw new ArgumentException("Số khách vượt tổng sức chứa sau khi nâng cấp phòng.");
        var oldRate = oldRoom.Field<decimal>("AgreedRate");
        var newRate = target.Field<decimal>("Rate");
        if (newRate <= oldRate)
            throw new ArgumentException("Phòng nâng cấp phải có đơn giá cao hơn giá đang áp dụng. Chuyển phòng giữ giá dùng chức năng Đổi phòng.");
        return new RoomUpgradeQuote(bookingId, oldRoom.Field<int>("StayId"), oldRoomId, newRoomId,
            oldRoom.Field<string>("Number")!, target.Field<string>("Number")!, oldRate, newRate,
            departure, now, Money.Nights(now, departure), booking.Field<string>("RentalType")!,
            booking.Field<string>("RentalType") == "Theo giờ" ? Money.Hours(now, departure) : 0);
    }

    public void UpgradeRoom(Session user, RoomUpgradeQuote preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            var quote = QuoteRoomUpgrade(c, t, preview.BookingId, preview.OldRoomId, preview.NewRoomId, DateTime.Now);
            if (quote.StayId != preview.StayId || quote.OldRoomNumber != preview.OldRoomNumber || quote.NewRoomNumber != preview.NewRoomNumber
                || quote.OldRate != preview.OldRate || quote.NewRate != preview.NewRate || quote.Departure != preview.Departure
                || quote.Nights != preview.Nights || quote.RentalType != preview.RentalType || quote.Hours != preview.Hours || quote.Fee != preview.Fee)
                throw new InvalidOperationException("Thông tin hoặc phí nâng cấp đã thay đổi. Hãy tải lại và kiểm tra khoản chênh lệch trước khi xác nhận.");

            var capacity = Database.Scalar<int>(c, t,
                "SELECT t.Capacity FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@id", ("@id", quote.NewRoomId));
            // The original rate and stable stay identity remain intact; increments start only at their recorded upgrade time.
            Database.Execute(c, t, "UPDATE BookingRooms SET RoomId=@new,Capacity=@capacity WHERE StayId=@stay",
                ("@new", quote.NewRoomId), ("@capacity", capacity), ("@stay", quote.StayId));
            using (var command = Database.Command(c, t, """
                INSERT RoomUpgrades(StayId,FromRoomId,ToRoomId,FromRoomNumber,ToRoomNumber,OldNightlyRate,NewNightlyRate,UpgradedAt,EmployeeId)
                VALUES(@stay,@old,@new,@oldNumber,@newNumber,@oldRate,@newRate,@at,@employee)
                """, ("@stay", quote.StayId), ("@old", quote.OldRoomId), ("@new", quote.NewRoomId),
                ("@oldNumber", quote.OldRoomNumber), ("@newNumber", quote.NewRoomNumber),
                ("@oldRate", quote.OldRate), ("@newRate", quote.NewRate), ("@at", quote.QuotedAt), ("@employee", user.EmployeeId)))
            {
                command.Parameters["@at"].SqlDbType = SqlDbType.DateTime2;
                command.ExecuteNonQuery();
            }
            Database.Execute(c, t, "UPDATE Rooms SET OperationalStatus=N'Chờ dọn' WHERE Id=@old", ("@old", quote.OldRoomId));
            Audit(c, t, user, "Nâng cấp phòng",
                $"Phiếu #{quote.BookingId}: phòng {quote.OldRoomNumber} → {quote.NewRoomNumber}; {quote.OldRate:N0} → {quote.NewRate:N0} đ/đêm; " +
                $"chênh {quote.Difference:N0}; {(quote.RentalType == "Theo giờ" ? $"{quote.Hours} giờ" : $"{quote.Nights} đêm")} dự kiến = {quote.Fee:N0} đ; cộng hóa đơn khi trả phòng");
            return true;
        });
    }
}
