using System.Data;
using Hotel.Core;
using Hotel.Data;
using Microsoft.Data.SqlClient;

namespace Hotel.Business;

public sealed partial class HotelService
{
    private static void EnsureCustomerIdentityAvailable(SqlConnection c, SqlTransaction t, string identity, int? exceptId)
    {
        if (identity.Length == 0) return;
        var matches = Database.Query(c, t,
            "SELECT Active FROM Customers WHERE IdentityNumber=@identity AND Id<>@id",
            ("@identity", identity), ("@id", exceptId ?? 0));
        if (matches.Rows.Count > 0)
            throw new InvalidOperationException(matches.Rows[0].Field<bool>("Active")
                ? "Giấy tờ đã tồn tại. Hãy chọn khách hàng đã có."
                : "Giấy tờ thuộc khách đã ngừng hoạt động. Hãy mở lại khách hàng trước khi sử dụng.");
    }

    private const string ConflictCondition = """
        (((CASE WHEN b.Status=N'Đang ở' THEN COALESCE(b.ActualCheckIn,b.Arrival) ELSE b.Arrival END)<@to AND b.Departure>@from)
        OR (b.Status=N'Đang ở' AND b.Departure<=@now AND @from<=@now))
        """;
    private const string ConflictSql = """
        SELECT COUNT(*) FROM BookingRooms br JOIN Bookings b ON b.Id=br.BookingId
        WHERE br.RoomId=@room AND b.Id<>@exclude AND b.Status IN(N'Đã đặt',N'Đang ở')
          AND
        """ + ConflictCondition;
    public DataTable RoomsOverview(Session user)
    {
        using var c = db.Open();
        Authorize(c, null, user);
        return Database.Query(c, null, """
            SELECT r.Id, r.Number AS [Số phòng], t.Name AS [Loại phòng],r.Floor AS [Tầng],t.Capacity AS [Sức chứa],t.Rate AS [Giá / đêm],
            CASE WHEN b.Id IS NOT NULL THEN N'Đang ở' ELSE r.OperationalStatus END AS [Trạng thái],
            c.FullName AS [Khách đang ở], b.Departure AS [Dự kiến trả], b.Id AS [Mã phiếu], b.RentalType AS [Hình thức]
            FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId
            OUTER APPLY(SELECT TOP(1) b.* FROM Bookings b JOIN BookingRooms br ON br.BookingId=b.Id WHERE br.RoomId=r.Id AND b.Status=N'Đang ở') b
            LEFT JOIN Customers c ON c.Id=b.CustomerId WHERE r.Active=1 ORDER BY r.Number
            """);
    }
    public DataTable AvailableRooms(Session user, DateTime from, DateTime to, int excludeBooking = 0, bool readyOnly = false)
    {
        ValidateDates(from, to);
        using var c = db.Open();
        Authorize(c, null, user);
        var now = DateTime.Now;
        return Database.Query(c, null, $"""
            SELECT r.Id,r.Number AS [Số phòng],t.Name AS [Loại phòng],r.Floor AS [Tầng],t.Capacity AS [Sức chứa],t.Rate AS [Giá / đêm]
            FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId
            WHERE r.Active=1 AND t.Active=1 AND r.OperationalStatus<>N'Bảo trì'
            AND (@ready=0 OR (r.OperationalStatus=N'Sẵn sàng' AND NOT EXISTS(
                SELECT 1 FROM BookingRooms occupiedRoom JOIN Bookings occupiedBooking ON occupiedBooking.Id=occupiedRoom.BookingId
                WHERE occupiedRoom.RoomId=r.Id AND occupiedBooking.Status=N'Đang ở')))
            AND NOT EXISTS(SELECT 1 FROM BookingRooms br JOIN Bookings b ON b.Id=br.BookingId WHERE br.RoomId=r.Id AND b.Id<>@exclude AND b.Status IN(N'Đã đặt',N'Đang ở')
            AND {ConflictCondition})
            ORDER BY r.Number
            """, ("@from", from), ("@to", to), ("@exclude", excludeBooking), ("@ready", readyOnly), ("@now", now));
    }
    private static void ValidateDates(DateTime from, DateTime to)
    {
        if (to <= from)
            throw new ArgumentException("Thời điểm đi phải sau thời điểm đến.");
        if ((to - from).TotalDays > 365)
            throw new ArgumentException("Một lượt đặt không vượt quá 365 ngày.");
    }
    private static void ValidateHourlyDuration(string? rentalType, DateTime from, DateTime to)
    {
        if (rentalType == "Theo giờ" && to - from > TimeSpan.FromHours(24))
            throw new ArgumentException("Thuê theo giờ tối đa 24 tiếng dự kiến. Thời gian dài hơn vui lòng chọn hình thức Theo ngày.");
    }
    public int SaveBooking(Session user, BookingInput input, int? bookingId = null)
        => db.Transaction((c, t) => SaveBooking(c, t, user, input, bookingId));

    private static int SaveBooking(SqlConnection c, SqlTransaction t, Session user, BookingInput input, int? bookingId = null)
    {
        var now = DateTime.Now;
        ValidateDates(input.Arrival, input.Departure);
        if (input.Arrival.Date < now.Date)
            throw new ArgumentException("Ngày đến không được trước hôm nay.");
        ValidateHourlyDuration(input.RentalType, input.Arrival, input.Departure);
        if (input.RoomIds.Count == 0 || input.RoomIds.Distinct().Count() != input.RoomIds.Count)
            throw new ArgumentException("Cần chọn ít nhất một phòng; không chọn phòng trùng.");
        if (input.Guests < 1 || input.Deposit < 0 || input.Deposit > 1000000000 || input.Notes.Length > 1000)
            throw new ArgumentException("Kiểm tra số khách, tiền cọc (0–1 tỷ đồng) và ghi chú (tối đa 1000 ký tự).");
        Authorize(c, t, user);
        if (Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Customers WHERE Id=@id AND Active=1", ("@id", input.CustomerId)) == 0)
            throw new ArgumentException("Khách hàng không còn hoạt động.");
        var prior = new Dictionary<int, (decimal Rate, int Capacity)>();
        if (bookingId.HasValue)
        {
            RequireBooking(c, t, bookingId.Value, "Đã đặt");
            foreach (DataRow row in Database.Query(c, t, "SELECT * FROM BookingRooms WHERE BookingId=@id", ("@id", bookingId)).Rows)
                prior.Add(row.Field<int>("RoomId"), (row.Field<decimal>("NightlyRate"), row.Field<int>("Capacity")));
        }
        var rooms = new List<(int Id, decimal Rate, int Capacity)>();
        foreach (var roomId in input.RoomIds.Order())
        {
            var room = Single(Database.Query(c, t, "SELECT r.Number,t.Rate,t.Capacity FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@id AND r.Active=1 AND t.Active=1 AND r.OperationalStatus<>N'Bảo trì'", ("@id", roomId)));
            if (Database.Scalar<int>(c, t, ConflictSql, ("@room", roomId), ("@exclude", bookingId ?? 0), ("@from", input.Arrival), ("@to", input.Departure), ("@now", now)) > 0)
                throw new InvalidOperationException($"Phòng {room["Number"]} đã có khách đặt/đang ở trong thời gian này. Hãy tìm phòng lại.");
            var snapshot = prior.GetValueOrDefault(roomId, (Rate: room.Field<decimal>("Rate"), Capacity: room.Field<int>("Capacity")));
            rooms.Add((roomId, snapshot.Rate, snapshot.Capacity));
        }
        if (input.Guests > rooms.Sum(r => r.Capacity))
            throw new ArgumentException("Số khách vượt tổng sức chứa của các phòng đã chọn.");
        var args = new (string, object?)[] { ("@customer", input.CustomerId), ("@employee", user.EmployeeId), ("@from", input.Arrival), ("@to", input.Departure), ("@guests", input.Guests), ("@deposit", Money.Round(input.Deposit)), ("@notes", input.Notes.Trim()), ("@rental", input.RentalType), ("@id", bookingId ?? 0) };
        int id;
        if (bookingId.HasValue)
        {
            id = bookingId.Value;
            Database.Execute(c, t, "UPDATE Bookings SET CustomerId=@customer,Arrival=@from,Departure=@to,Guests=@guests,Deposit=@deposit,Notes=@notes,RentalType=@rental WHERE Id=@id", args);
            Database.Execute(c, t, "DELETE BookingRooms WHERE BookingId=@id", ("@id", id));
        }
        else
            id = Database.Scalar<int>(c, t, "INSERT Bookings(CustomerId,EmployeeId,Arrival,Departure,Guests,Deposit,Notes,RentalType) OUTPUT INSERTED.Id VALUES(@customer,@employee,@from,@to,@guests,@deposit,@notes,@rental)", args);
        foreach (var room in rooms)
            Database.Execute(c, t, "INSERT BookingRooms(BookingId,RoomId,NightlyRate,Capacity) VALUES(@b,@r,@rate,@capacity)", ("@b", id), ("@r", room.Id), ("@rate", room.Rate), ("@capacity", room.Capacity));
        Audit(c, t, user, bookingId.HasValue ? "Sửa đặt phòng" : "Đặt phòng", $"Phiếu #{id} ({input.RentalType}); cọc {input.Deposit:N0}");
        return id;
    }
    private static DataRow RequireBooking(SqlConnection c, SqlTransaction? t, int id, params string[] statuses)
    {
        var row = Single(Database.Query(c, t, "SELECT * FROM Bookings WHERE Id=@id", ("@id", id)));
        if (statuses.Length > 0 && !statuses.Contains(row.Field<string>("Status")))
            throw new InvalidOperationException($"Phiếu #{id} đang ở trạng thái '{row["Status"]}', không thể thực hiện thao tác này.");
        return row;
    }
    public DataRow Booking(Session user, int id)
    {
        using var c = db.Open();
        Authorize(c, null, user);
        return RequireBooking(c, null, id);
    }
    public DataTable BookingRooms(Session user, int id)
    {
        using var c = db.Open();
        Authorize(c, null, user);
        return Database.Query(c, null, """
            SELECT br.RoomId AS Id,r.Number AS [Số phòng],br.NightlyRate+
                COALESCE((SELECT SUM(u.NewNightlyRate-u.OldNightlyRate) FROM RoomUpgrades u WHERE u.StayId=br.StayId),0) AS [Giá / đêm],
                br.Capacity AS [Sức chứa]
            FROM BookingRooms br JOIN Rooms r ON r.Id=br.RoomId WHERE br.BookingId=@id ORDER BY r.Number
            """, ("@id", id));
    }
    public DataTable Bookings(Session user, string search = "", string status = "Tất cả", int? customerId = null, string rentalType = "Tất cả")
    {
        using var c = db.Open();
        Authorize(c, null, user);
        return Database.Query(c, null, """
            SELECT b.Id,c.FullName AS [Khách hàng],c.Phone AS [Điện thoại],
            b.RentalType AS [Hình thức],
            (SELECT STRING_AGG(r.Number,N', ') FROM BookingRooms br JOIN Rooms r ON r.Id=br.RoomId WHERE br.BookingId=b.Id) AS [Phòng],
            b.Arrival AS [Ngày đến],b.Departure AS [Ngày đi],b.Guests AS [Số khách],b.Deposit AS [Tiền cọc],b.RefundedDeposit AS [Cọc đã hoàn],b.Status AS [Trạng thái],b.ActualCheckIn AS [Nhận thực tế],b.ActualCheckOut AS [Trả thực tế],b.Notes AS [Ghi chú]
            FROM Bookings b JOIN Customers c ON c.Id=b.CustomerId
            WHERE (@status=N'Tất cả' OR b.Status=@status) AND (@customer=0 OR c.Id=@customer)
              AND (@rental=N'Tất cả' OR b.RentalType=@rental)
            AND (c.FullName LIKE @s OR c.Phone LIKE @s OR CONVERT(nvarchar(20),b.Id)=@exact OR EXISTS(SELECT 1 FROM BookingRooms br JOIN Rooms r ON r.Id=br.RoomId WHERE br.BookingId=b.Id AND r.Number LIKE @s))
            ORDER BY b.Id DESC
            """, ("@s", "%" + search.Trim() + "%"), ("@exact", search.Trim()), ("@status", status), ("@customer", customerId ?? 0), ("@rental", rentalType));
    }
    public void CancelBooking(Session user, int id)
    {
        db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            var b = RequireBooking(c, t, id, "Đã đặt");
            Database.Execute(c, t, "UPDATE Bookings SET Status=N'Đã hủy',RefundedDeposit=Deposit WHERE Id=@id", ("@id", id));
            Audit(c, t, user, "Hủy đặt, hoàn cọc", $"Phiếu #{id}; hoàn {b.Field<decimal>("Deposit"):N0}");
            return true;
        });
    }
    public void CheckIn(Session user, int id)
    {
        db.Transaction((c, t) =>
        {
            CheckIn(c, t, user, id, DateTime.Now);
            return true;
        });
    }
    private static void CheckIn(SqlConnection c, SqlTransaction t, Session user, int id, DateTime now)
    {
        Authorize(c, t, user);
        var b = RequireBooking(c, t, id, "Đã đặt");
        if (now.Date < b.Field<DateTime>("Arrival").Date || now >= b.Field<DateTime>("Departure"))
            throw new InvalidOperationException("Chưa đến ngày nhận phòng hoặc đã quá ngày đi. Hãy sửa thời gian đặt trước.");
        ValidateHourlyDuration(b.Field<string>("RentalType"), now, b.Field<DateTime>("Departure"));
        foreach (DataRow r in Database.Query(c, t, "SELECT r.*,rt.Active AS TypeActive FROM Rooms r JOIN RoomTypes rt ON rt.Id=r.RoomTypeId JOIN BookingRooms br ON br.RoomId=r.Id WHERE br.BookingId=@id", ("@id", id)).Rows)
        {
            if (!r.Field<bool>("Active") || !r.Field<bool>("TypeActive") || r.Field<string>("OperationalStatus") != "Sẵn sàng")
                throw new InvalidOperationException($"Phòng {r["Number"]} chưa sẵn sàng. Hãy hoàn tất dọn phòng/bảo trì.");
            var occupied = Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM BookingRooms br JOIN Bookings b ON b.Id=br.BookingId WHERE br.RoomId=@room AND b.Status=N'Đang ở'", ("@room", r["Id"]));
            if (occupied > 0)
                throw new InvalidOperationException($"Phòng {r["Number"]} vẫn đang có khách.");
            if (Database.Scalar<int>(c, t, ConflictSql, ("@room", r["Id"]), ("@exclude", id), ("@from", now), ("@to", b["Departure"]), ("@now", now)) > 0)
                throw new InvalidOperationException("Thời gian nhận thực tế trùng phiếu đặt khác.");
        }
        Database.Execute(c, t, "UPDATE Bookings SET Status=N'Đang ở',ActualCheckIn=@now WHERE Id=@id", ("@now", now), ("@id", id));
        Audit(c, t, user, "Nhận phòng", $"Phiếu #{id}");
    }

    public int WalkIn(Session user, BookingInput input, Dictionary<string, object?>? newCustomer = null)
    {
        return db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            var now = DateTime.Now;
            if (input.Departure <= now)
                throw new ArgumentException("Thời điểm đi phải sau thời điểm nhận phòng hiện tại.");
            var customerId = input.CustomerId;
            if (newCustomer is not null)
            {
                var name = CustomerValidation.FullName(Convert.ToString(newCustomer.GetValueOrDefault("FullName")));
                var phone = CustomerValidation.Phone(Convert.ToString(newCustomer.GetValueOrDefault("Phone")));
                var identity = CustomerValidation.Identity(Convert.ToString(newCustomer.GetValueOrDefault("IdentityNumber")));
                var address = CustomerValidation.Address(Convert.ToString(newCustomer.GetValueOrDefault("Address")));
                EnsureCustomerIdentityAvailable(c, t, identity, null);
                customerId = Database.Scalar<int>(c, t, "INSERT Customers(FullName,Phone,IdentityNumber,Address) OUTPUT INSERTED.Id VALUES(@name,@phone,@identity,@address)",
                    ("@name", name), ("@phone", phone), ("@identity", identity.Length == 0 ? null : identity), ("@address", address));
                Audit(c, t, user, "Lưu danh mục", $"Customers #{customerId}");
            }
            var id = SaveBooking(c, t, user, input with { CustomerId = customerId, Arrival = now });
            CheckIn(c, t, user, id, now);
            Audit(c, t, user, "Nhận phòng nhanh", $"Phiếu #{id}; khách #{customerId}");
            return id;
        });
    }

    public void ChangeRoom(Session user, int bookingId, int oldRoomId, int newRoomId)
    {
        if (oldRoomId == newRoomId)
            throw new ArgumentException("Hãy chọn phòng mới khác phòng đang ở.");
        db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            var b = RequireBooking(c, t, bookingId, "Đang ở");
            var now = DateTime.Now;
            if (b.Field<DateTime>("Departure") <= now)
                throw new InvalidOperationException("Phiếu đã quá giờ trả dự kiến. Hãy đổi ngày đi trước khi đổi phòng.");
            var oldRoom = Single(Database.Query(c, t, """
                SELECT br.NightlyRate+
                    COALESCE((SELECT SUM(u.NewNightlyRate-u.OldNightlyRate) FROM RoomUpgrades u WHERE u.StayId=br.StayId),0) AS NightlyRate,r.Number
                FROM BookingRooms br JOIN Rooms r ON r.Id=br.RoomId WHERE br.BookingId=@b AND br.RoomId=@old
                """, ("@b", bookingId), ("@old", oldRoomId)));
            var target = Single(Database.Query(c, t, "SELECT r.Number,r.OperationalStatus,r.Active,t.Active AS TypeActive,t.Capacity FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@new", ("@new", newRoomId)));
            if (!target.Field<bool>("Active") || !target.Field<bool>("TypeActive") || target.Field<string>("OperationalStatus") != "Sẵn sàng")
                throw new InvalidOperationException("Phòng mới phải đang hoạt động và sẵn sàng nhận khách.");
            if (Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM BookingRooms br JOIN Bookings b ON b.Id=br.BookingId WHERE br.RoomId=@new AND b.Status=N'Đang ở'", ("@new", newRoomId)) > 0)
                throw new InvalidOperationException("Phòng mới vẫn đang có khách.");
            if (Database.Scalar<int>(c, t, ConflictSql, ("@room", newRoomId), ("@exclude", bookingId), ("@from", now), ("@to", b["Departure"]), ("@now", now)) > 0)
                throw new InvalidOperationException("Phòng mới trùng lịch đặt trong thời gian lưu trú còn lại.");
            var capacity = target.Field<int>("Capacity");
            var otherCapacity = Database.Scalar<int>(c, t, "SELECT COALESCE(SUM(Capacity),0) FROM BookingRooms WHERE BookingId=@b AND RoomId<>@old", ("@b", bookingId), ("@old", oldRoomId));
            if (b.Field<int>("Guests") > otherCapacity + capacity)
                throw new ArgumentException("Số khách vượt tổng sức chứa sau khi đổi phòng.");
            // Keep the rate agreed for this stay; changing rooms does not reprice past nights.
            Database.Execute(c, t, "UPDATE BookingRooms SET RoomId=@new,Capacity=@capacity WHERE BookingId=@b AND RoomId=@old", ("@new", newRoomId), ("@capacity", capacity), ("@b", bookingId), ("@old", oldRoomId));
            Database.Execute(c, t, "UPDATE Rooms SET OperationalStatus=N'Chờ dọn' WHERE Id=@old", ("@old", oldRoomId));
            Audit(c, t, user, "Đổi phòng", $"Phiếu #{bookingId}: phòng {oldRoom["Number"]} → {target["Number"]}; giữ giá {oldRoom.Field<decimal>("NightlyRate"):N0} đ/đêm");
            return true;
        });
    }
    public void ChangeDeparture(Session user, int id, DateTime departure)
    {
        db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            var b = RequireBooking(c, t, id, "Đang ở");
            ValidateDates(b.Field<DateTime>("Arrival"), departure);
            ValidateHourlyDuration(b.Field<string>("RentalType"), b.Field<DateTime>("ActualCheckIn"), departure);
            var now = DateTime.Now;
            if (departure <= now)
                throw new ArgumentException("Ngày đi mới phải ở tương lai.");
            foreach (DataRow r in Database.Query(c, t, "SELECT RoomId FROM BookingRooms WHERE BookingId=@id", ("@id", id)).Rows)
                if (Database.Scalar<int>(c, t, ConflictSql, ("@room", r["RoomId"]), ("@exclude", id), ("@from", now), ("@to", departure), ("@now", now)) > 0)
                    throw new InvalidOperationException("Không thể thay đổi ngày đi vì trùng lịch đặt phòng khác.");
            Database.Execute(c, t, "UPDATE Bookings SET Departure=@d WHERE Id=@id", ("@d", departure), ("@id", id));
            Audit(c, t, user, "Đổi ngày đi", $"Phiếu #{id}: {departure:dd/MM/yyyy HH:mm}");
            return true;
        });
    }
    public void MarkRoomReady(Session user, int roomId)
    {
        db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            if (Database.Execute(c, t, "UPDATE Rooms SET OperationalStatus=N'Sẵn sàng' WHERE Id=@id AND OperationalStatus=N'Chờ dọn' AND NOT EXISTS(SELECT 1 FROM Bookings b JOIN BookingRooms br ON br.BookingId=b.Id WHERE br.RoomId=@id AND b.Status=N'Đang ở')", ("@id", roomId)) == 0)
                throw new InvalidOperationException("Chỉ đánh dấu đã dọn cho phòng đang chờ dọn và không có khách.");
            Audit(c, t, user, "Hoàn tất dọn phòng", $"Phòng #{roomId}");
            return true;
        });
    }
    public DataTable Usages(Session user, int bookingId)
    {
        using var c = db.Open();
        Authorize(c, null, user);
        return Database.Query(c, null, "SELECT Id,ServiceName AS [Dịch vụ],Unit AS [Đơn vị],Quantity AS [Số lượng],UnitPrice AS [Đơn giá],Quantity*UnitPrice AS [Thành tiền],UsedAt AS [Thời điểm] FROM ServiceUsages WHERE BookingId=@id ORDER BY Id DESC", ("@id", bookingId));
    }
    public int AddService(Session user, int bookingId, int serviceId, int quantity)
    {
        if (quantity < 1 || quantity > 10000)
            throw new ArgumentException("Số lượng phải từ 1 đến 10000.");
        return db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            RequireBooking(c, t, bookingId, "Đang ở");
            var service = Single(Database.Query(c, t, "SELECT * FROM Services WHERE Id=@id AND Active=1", ("@id", serviceId)));
            var id = Database.Scalar<int>(c, t, "INSERT ServiceUsages(BookingId,ServiceId,ServiceName,Unit,Quantity,UnitPrice) OUTPUT INSERTED.Id VALUES(@b,@s,@name,@unit,@q,@p)", ("@b", bookingId), ("@s", serviceId), ("@name", service["Name"]), ("@unit", service["Unit"]), ("@q", quantity), ("@p", service["Price"]));
            Audit(c, t, user, "Thêm dịch vụ", $"Phiếu #{bookingId}; dịch vụ #{id}; SL {quantity}");
            return id;
        });
    }
    public void UpdateUsage(Session user, int usageId, int quantity)
    {
        if (quantity < 0 || quantity > 10000)
            throw new ArgumentException("Số lượng phải từ 0 đến 10000.");
        db.Transaction((c, t) =>
        {
            Authorize(c, t, user);
            var row = Single(Database.Query(c, t, "SELECT BookingId FROM ServiceUsages WHERE Id=@id", ("@id", usageId)));
            RequireBooking(c, t, row.Field<int>("BookingId"), "Đang ở");
            Database.Execute(c, t, quantity == 0 ? "DELETE ServiceUsages WHERE Id=@id" : "UPDATE ServiceUsages SET Quantity=@q WHERE Id=@id", ("@id", usageId), ("@q", quantity));
            Audit(c, t, user, quantity == 0 ? "Xóa dịch vụ" : "Sửa dịch vụ", $"Chi tiết #{usageId}; SL {quantity}");
            return true;
        });
    }
}
