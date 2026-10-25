using System.Data;

using System.Security.Cryptography;

using System.Text.RegularExpressions;

using Hotel.Core;

using Hotel.Data;

using Microsoft.Data.SqlClient;



namespace Hotel.Business;



public enum CatalogKind

{

    RoomTypes, Rooms, Customers, Services, Employees, Users

}



public sealed partial class HotelService(Database database)

{

    private readonly Database db = database;

    public void Initialize()

    {

        db.Initialize();

        Seed();

    }

    public static string HashPassword(string password)

    {

        var salt = RandomNumberGenerator.GetBytes(16);

        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);

        return $"210000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";

    }

    public static bool VerifyPassword(string password, string encoded)

    {

        try

        {

            var parts = encoded.Split('.');

            if (parts.Length != 3)

                return false;

            var expected = Convert.FromBase64String(parts[2]);

            var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), int.Parse(parts[0]), HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(hash, expected);

        }

        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) { return false; }

    }

    public Session Login(string username, string password)

    {

        var rows = db.Query("SELECT u.*, e.FullName FROM Users u JOIN Employees e ON e.Id=u.EmployeeId WHERE u.Username=@name AND u.Active=1 AND e.Active=1", ("@name", username.Trim()));

        if (rows.Rows.Count != 1 || !VerifyPassword(password, rows.Rows[0].Field<string>("PasswordHash")!))

            throw new InvalidOperationException("Tên đăng nhập hoặc mật khẩu không đúng, hoặc tài khoản đã ngừng hoạt động.");

        var row = rows.Rows[0];

        return new Session(row.Field<int>("Id"), row.Field<int>("EmployeeId"), row.Field<string>("Username")!, row.Field<string>("FullName")!, row.Field<string>("Role")!);

    }

    private static void Authorize(SqlConnection c, SqlTransaction? t, Session user, bool admin = false)

    {

        var rows = Database.Query(c, t, "SELECT u.Role FROM Users u JOIN Employees e ON e.Id=u.EmployeeId WHERE u.Id=@id AND u.EmployeeId=@employee AND u.Active=1 AND e.Active=1", ("@id", user.UserId), ("@employee", user.EmployeeId));

        if (rows.Rows.Count == 0 || (admin && rows.Rows[0].Field<string>("Role") != "Quản lý"))

            throw new UnauthorizedAccessException("Bạn không có quyền thực hiện chức năng này hoặc tài khoản đã bị khóa. Vui lòng đăng nhập lại.");

    }

    private static void Audit(SqlConnection c, SqlTransaction t, Session user, string action, string detail) =>

        Database.Execute(c, t, "INSERT AuditLog(UserId,Action,Detail) VALUES(@u,@a,@d)", ("@u", user.UserId), ("@a", action), ("@d", detail));

    public void ChangePassword(Session user, string oldPassword, string newPassword)

    {

        ValidatePassword(newPassword);

        db.Transaction((c, t) =>

        {

            Authorize(c, t, user);

            var hash = Database.Scalar<string>(c, t, "SELECT PasswordHash FROM Users WHERE Id=@id", ("@id", user.UserId));

            if (!VerifyPassword(oldPassword, hash))

                throw new ArgumentException("Mật khẩu hiện tại không đúng.");

            Database.Execute(c, t, "UPDATE Users SET PasswordHash=@hash WHERE Id=@id", ("@hash", HashPassword(newPassword)), ("@id", user.UserId));

            Audit(c, t, user, "Đổi mật khẩu", user.Username);

            return true;

        });

    }

    public void EnsureDemoData()

    {

        db.Transaction((c, t) =>

        {

            if (Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Rooms") < 20)

            {

                Database.Execute(c, t, """

                    INSERT Rooms(Number,Floor,RoomTypeId,OperationalStatus)

                    SELECT v.Number,v.Floor,t.Id,N'Sẵn sàng'

                    FROM (VALUES

                        (N'104',1,N'Phòng đôi'),(N'105',1,N'Phòng đơn'),

                        (N'204',2,N'Phòng đơn'),(N'205',2,N'Phòng gia đình'),

                        (N'303',3,N'Phòng đôi'),(N'304',3,N'Phòng đơn'),(N'305',3,N'Phòng gia đình'),

                        (N'401',4,N'Phòng đơn'),(N'402',4,N'Phòng đơn'),(N'403',4,N'Phòng đôi'),(N'404',4,N'Phòng đôi'),(N'405',4,N'Phòng gia đình'),

                        (N'501',5,N'Phòng đôi'),(N'502',5,N'Phòng gia đình'),(N'503',5,N'Phòng gia đình')

                    ) v(Number,Floor,TypeName) JOIN RoomTypes t ON t.Name=v.TypeName

                    WHERE NOT EXISTS (SELECT 1 FROM Rooms r WHERE r.Number=v.Number);

                    UPDATE Rooms SET OperationalStatus=N'Chờ dọn' WHERE Number IN(N'102',N'204',N'305') AND NOT EXISTS(SELECT 1 FROM Bookings b JOIN BookingRooms br ON br.BookingId=b.Id WHERE br.RoomId=Rooms.Id AND b.Status=N'Đang ở');

                    UPDATE Rooms SET OperationalStatus=N'Bảo trì' WHERE Number IN(N'203',N'503') AND NOT EXISTS(SELECT 1 FROM Bookings b JOIN BookingRooms br ON br.BookingId=b.Id WHERE br.RoomId=Rooms.Id AND b.Status=N'Đang ở');

                    """);

            }

            Database.Execute(c, t, """

                UPDATE Customers SET FullName=N'Trần Đình Trí' WHERE IdentityNumber=N'DEMO001' AND (FullName LIKE N'%Nguyễn Văn An%' OR FullName LIKE N'%An%');

                UPDATE Customers SET FullName=N'Phạm Thu Hà' WHERE IdentityNumber=N'DEMO002' AND (FullName LIKE N'%Trần Thị Bình%' OR FullName LIKE N'%Bình%');

                UPDATE Customers SET FullName=N'Vũ Mạnh Hùng' WHERE IdentityNumber=N'DEMO003' AND (FullName LIKE N'%Lê Minh Châu%' OR FullName LIKE N'%Châu%');

                """);



            var customers = new (string Name, string Phone, string Ident, string Address)[]

            {

                ("Hoàng Minh Đức", "0912345001", "001200001001", "Hà Nội"),

                ("Phạm Thanh Tùng", "0912345002", "001200001002", "TP. Hồ Chí Minh"),

                ("Đặng Quỳnh Trang", "0912345003", "001200001003", "Đà Nẵng"),

                ("Vũ Quốc Bảo", "0912345004", "001200001004", "Hải Phòng"),

                ("Bùi Thu Thảo", "0912345005", "001200001005", "Cần Thơ"),

                ("Đỗ Nhật Minh", "0912345006", "001200001006", "Nha Trang"),

                ("Ngô Phương Linh", "0912345007", "001200001007", "Huế"),

                ("Dương Tuấn Kiệt", "0912345008", "001200001008", "Vũng Tàu"),

                ("Đinh Bảo Ngọc", "0912345009", "001200001009", "Đà Lạt"),

                ("Lý Gia Huy", "0912345010", "001200001010", "Quảng Ninh"),

                ("Mai Trúc Quỳnh", "0912345011", "001200001011", "Bình Dương"),

                ("Trịnh Đình Khang", "0912345012", "001200001012", "Đồng Nai"),

                ("Phan Ánh Dương", "0912345013", "001200001013", "Bắc Ninh"),

                ("Lương Thùy Chi", "0912345014", "001200001014", "Thanh Hóa"),

                ("Tạ Quang Thắng", "0912345015", "001200001015", "Nghệ An"),

                ("Đoàn Minh Quân", "0912345016", "001200001016", "Hà Tĩnh"),

                ("Cao Thị Khánh Huyền", "0912345017", "001200001017", "Quảng Bình"),

                ("Hà Công Minh", "0912345018", "001200001018", "Lâm Đồng"),

                ("Lưu Đức Trọng", "0912345019", "001200001019", "Khánh Hòa"),

                ("Trương Mỹ Duyên", "0912345020", "001200001020", "Kiên Giang"),

                ("Hồ Hải Đăng", "0912345021", "001200001021", "An Giang"),

                ("Chu Đình Dũng", "0912345022", "001200001022", "Thái Nguyên"),

                ("Tạ Diệu Linh", "0912345023", "001200001023", "Phú Thọ"),

                ("Đinh Hữu Phúc", "0912345024", "001200001024", "Nam Định"),

                ("Diệp Hoài Nam", "0912345025", "001200001025", "Ninh Bình"),

                ("Bùi Đăng Khoa", "0912345026", "001200001026", "Hà Nam"),

                ("Vương Bích Thủy", "0912345027", "001200001027", "Hưng Yên"),

                ("Lâm Quốc Hưng", "0912345028", "001200001028", "Hải Dương"),

                ("Khổng Minh Trí", "0912345029", "001200001029", "Vĩnh Phúc"),

                ("Thân Thúy Hằng", "0912345030", "001200001030", "Bắc Giang"),

                ("Nông Văn Chiến", "0912345031", "001200001031", "Lạng Sơn"),

                ("Quách Diễm My", "0912345032", "001200001032", "Hòa Bình"),

                ("La Quốc Đạt", "0912345033", "001200001033", "Sơn La"),

                ("Trần Đình Trọng", "0912345034", "001200001034", "Quảng Nam"),

                ("Lê Bảo Trâm", "0912345035", "001200001035", "Quảng Ngãi"),

                ("Nguyễn Hoàng Yến", "0912345036", "001200001036", "Bình Định"),

                ("Huỳnh Quốc Tuấn", "0912345037", "001200001037", "Phú Yên"),

                ("Tống Gia Bảo", "0912345038", "001200001038", "Gia Lai"),

                ("Nghiêm Xuân Thành", "0912345039", "001200001039", "Đắk Lắk"),

                ("Bạch Thảo Nhi", "0912345040", "001200001040", "Đắk Nông"),

                ("Mai Công Tuấn", "0912345041", "001200001041", "Tây Ninh"),

                ("Giang Nhật Cường", "0912345042", "001200001042", "Bình Phước"),

                ("Châu Ngọc Diệp", "0912345043", "001200001043", "Tiền Giang"),

                ("Đào Kim Phượng", "0912345044", "001200001044", "Bến Tre"),

                ("Thạch Bảo An", "0912345045", "001200001045", "Trà Vinh"),

                ("Uông Minh Triết", "0912345046", "001200001046", "Vĩnh Long"),

                ("Hứa Thanh Thảo", "0912345047", "001200001047", "Hậu Giang"),

                ("Doãn Ngọc Sơn", "0912345048", "001200001048", "Sóc Trăng"),

                ("Đỗ Phương Thảo", "0912345049", "001200001049", "Bạc Liêu"),

                ("Cù Huy Hoàng", "0912345050", "001200001050", "Cà Mau")

            };

            foreach (var cust in customers)

            {

                var exists = Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Customers WHERE IdentityNumber=@id", ("@id", cust.Ident)) > 0;

                if (exists)

                {

                    Database.Execute(c, t, "UPDATE Customers SET FullName=@name, Phone=@phone, Address=@addr, Active=1 WHERE IdentityNumber=@id",

                        ("@name", cust.Name), ("@phone", cust.Phone), ("@addr", cust.Address), ("@id", cust.Ident));

                }

                else

                {

                    Database.Execute(c, t, "INSERT Customers(FullName,Phone,IdentityNumber,Address,Active) VALUES(@name,@phone,@id,@addr,1)",

                        ("@name", cust.Name), ("@phone", cust.Phone), ("@id", cust.Ident), ("@addr", cust.Address));

                }

            }



            var empId = Database.Scalar<int>(c, t, "SELECT TOP 1 Id FROM Employees WHERE Position=N'Quản lý'");

            var activeSetups = new[]

            {

                (RoomNumber: "101", Ident: "001200001001", HoursAgo: 5, DaysStay: 2, Guests: 1, Deposit: 300000m, ServiceId: 1, Qty: 2),

                (RoomNumber: "202", Ident: "001200001002", HoursAgo: 10, DaysStay: 1, Guests: 2, Deposit: 500000m, ServiceId: 3, Qty: 2),

                (RoomNumber: "303", Ident: "001200001003", HoursAgo: 24, DaysStay: 3, Guests: 2, Deposit: 1000000m, ServiceId: 4, Qty: 1),

                (RoomNumber: "404", Ident: "001200001004", HoursAgo: 48, DaysStay: 1, Guests: 2, Deposit: 700000m, ServiceId: 2, Qty: 3)

            };

            foreach (var a in activeSetups)

            {

                var rid = Database.Scalar<int>(c, t, "SELECT Id FROM Rooms WHERE Number=@num", ("@num", a.RoomNumber));

                if (Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Bookings b JOIN BookingRooms br ON br.BookingId=b.Id WHERE br.RoomId=@r AND b.Status=N'Đang ở'", ("@r", rid)) == 0)

                {

                    var cid = Database.Scalar<int>(c, t, "SELECT Id FROM Customers WHERE IdentityNumber=@id", ("@id", a.Ident));

                    var rate = Database.Scalar<decimal>(c, t, "SELECT t.Rate FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@id", ("@id", rid));

                    var cap = Database.Scalar<int>(c, t, "SELECT t.Capacity FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@id", ("@id", rid));

                    var checkin = DateTime.Now.AddHours(-a.HoursAgo);

                    var checkout = DateTime.Now.AddDays(a.DaysStay);

                    var bid = Database.Scalar<int>(c, t, """

                        INSERT Bookings(CustomerId,EmployeeId,Arrival,Departure,Guests,Deposit,Status,ActualCheckIn,Notes)

                        OUTPUT INSERTED.Id VALUES(@c,@e,@arr,@dep,@g,@depo,N'Đang ở',@in,N'Khách lưu trú mẫu')

                        """, ("@c", cid), ("@e", empId), ("@arr", checkin), ("@dep", checkout), ("@g", a.Guests), ("@depo", a.Deposit), ("@in", checkin));

                    Database.Execute(c, t, "INSERT BookingRooms(BookingId,RoomId,NightlyRate,Capacity) VALUES(@b,@r,@rate,@cap)", ("@b", bid), ("@r", rid), ("@rate", rate), ("@cap", cap));

                    var srv = Single(Database.Query(c, t, "SELECT Name,Unit,Price FROM Services WHERE Id=@id", ("@id", a.ServiceId)));

                    Database.Execute(c, t, "INSERT ServiceUsages(BookingId,ServiceId,ServiceName,Unit,Quantity,UnitPrice) VALUES(@b,@s,@name,@unit,@q,@p)",

                        ("@b", bid), ("@s", a.ServiceId), ("@name", srv["Name"]), ("@unit", srv["Unit"]), ("@q", a.Qty), ("@p", srv["Price"]));

                }

            }



            if (Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Invoices") < 10)

            {

                var pastInvoices = new[]

                {

                    (DaysAgo: 1, Ident: "001200001014", RoomNum: "201", Nights: 2, SrvId: 1, SrvQty: 4, Surcharge: 50000m, Disc: 0m, Dep: 500000m, Method: "Chuyển khoản"),

                    (DaysAgo: 2, Ident: "001200001012", RoomNum: "301", Nights: 1, SrvId: 3, SrvQty: 2, Surcharge: 0m, Disc: 50000m, Dep: 450000m, Method: "Tiền mặt"),

                    (DaysAgo: 3, Ident: "001200001013", RoomNum: "302", Nights: 3, SrvId: 4, SrvQty: 2, Surcharge: 100000m, Disc: 100000m, Dep: 1500000m, Method: "Thẻ"),

                    (DaysAgo: 5, Ident: "001200001005", RoomNum: "103", Nights: 2, SrvId: 2, SrvQty: 3, Surcharge: 0m, Disc: 0m, Dep: 700000m, Method: "Chuyển khoản"),

                    (DaysAgo: 7, Ident: "001200001006", RoomNum: "205", Nights: 2, SrvId: 3, SrvQty: 4, Surcharge: 80000m, Disc: 50000m, Dep: 1000000m, Method: "Thẻ"),

                    (DaysAgo: 9, Ident: "001200001007", RoomNum: "101", Nights: 1, SrvId: 1, SrvQty: 2, Surcharge: 0m, Disc: 20000m, Dep: 400000m, Method: "Tiền mặt"),

                    (DaysAgo: 12, Ident: "001200001008", RoomNum: "403", Nights: 2, SrvId: 4, SrvQty: 1, Surcharge: 50000m, Disc: 0m, Dep: 800000m, Method: "Chuyển khoản"),

                    (DaysAgo: 15, Ident: "001200001009", RoomNum: "502", Nights: 3, SrvId: 3, SrvQty: 3, Surcharge: 120000m, Disc: 100000m, Dep: 1500000m, Method: "Thẻ"),

                    (DaysAgo: 18, Ident: "001200001010", RoomNum: "104", Nights: 1, SrvId: 2, SrvQty: 2, Surcharge: 0m, Disc: 0m, Dep: 500000m, Method: "Tiền mặt"),

                    (DaysAgo: 21, Ident: "001200001011", RoomNum: "402", Nights: 2, SrvId: 1, SrvQty: 3, Surcharge: 30000m, Disc: 0m, Dep: 500000m, Method: "Chuyển khoản")

                };



                foreach (var inv in pastInvoices)

                {

                    var issuedAt = DateTime.Today.AddDays(-inv.DaysAgo).AddHours(11);

                    var checkin = issuedAt.AddDays(-inv.Nights).AddHours(-2);

                    var cust = Single(Database.Query(c, t, "SELECT Id, FullName, Phone, IdentityNumber, Address FROM Customers WHERE IdentityNumber=@id", ("@id", inv.Ident)));

                    var cid = Convert.ToInt32(cust["Id"]);

                    var cname = Convert.ToString(cust["FullName"]);

                    var cphone = Convert.ToString(cust["Phone"]);

                    var cident = Convert.ToString(cust["IdentityNumber"]);

                    var caddr = Convert.ToString(cust["Address"]);



                    var rid = Database.Scalar<int>(c, t, "SELECT Id FROM Rooms WHERE Number=@num", ("@num", inv.RoomNum));

                    var rate = Database.Scalar<decimal>(c, t, "SELECT t.Rate FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@id", ("@id", rid));

                    var cap = Database.Scalar<int>(c, t, "SELECT t.Capacity FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@id", ("@id", rid));

                    var srv = Single(Database.Query(c, t, "SELECT Name,Unit,Price FROM Services WHERE Id=@id", ("@id", inv.SrvId)));



                    var bid = Database.Scalar<int>(c, t, """

                        INSERT Bookings(CustomerId,EmployeeId,Arrival,Departure,Guests,Deposit,Status,ActualCheckIn,ActualCheckOut,Notes,CreatedAt)

                        OUTPUT INSERTED.Id VALUES(@c,@e,@arr,@dep,2,@depo,N'Đã trả',@in,@out,N'Lịch sử',@in)

                        """, ("@c", cid), ("@e", empId), ("@arr", checkin), ("@dep", issuedAt), ("@depo", inv.Dep), ("@in", checkin), ("@out", issuedAt));

                    Database.Execute(c, t, "INSERT BookingRooms(BookingId,RoomId,NightlyRate,Capacity) VALUES(@b,@r,@rate,@cap)", ("@b", bid), ("@r", rid), ("@rate", rate), ("@cap", cap));

                    Database.Execute(c, t, "INSERT ServiceUsages(BookingId,ServiceId,ServiceName,Unit,Quantity,UnitPrice,UsedAt) VALUES(@b,@s,@name,@unit,@q,@p,@used)",

                        ("@b", bid), ("@s", inv.SrvId), ("@name", srv["Name"]), ("@unit", srv["Unit"]), ("@q", inv.SrvQty), ("@p", srv["Price"]), ("@used", checkin.AddDays(1)));



                    decimal roomAmount = rate * inv.Nights;

                    decimal srvAmount = Convert.ToDecimal(srv["Price"]) * inv.SrvQty;

                    decimal total = roomAmount + srvAmount + inv.Surcharge - inv.Disc;



                    var invoiceId = Database.Scalar<int>(c, t, """

                        INSERT Invoices(BookingId,EmployeeId,CustomerName,CustomerPhone,CustomerIdentity,CustomerAddress,EmployeeName,IssuedAt,CheckIn,CheckOut,Surcharge,Discount,Deposit,Total,PaymentMethod,Notes)

                        OUTPUT INSERTED.Id VALUES(@b,@e,@cname,@cphone,@cident,@caddr,N'Quản lý mẫu',@issued,@in,@out,@sur,@disc,@dep,@tot,@method,N'Hóa đơn lưu mẫu')

                        """, ("@b", bid), ("@e", empId), ("@cname", cname), ("@cphone", cphone), ("@cident", cident), ("@caddr", caddr),

                        ("@issued", issuedAt), ("@in", checkin), ("@out", issuedAt), ("@sur", inv.Surcharge), ("@disc", inv.Disc), ("@dep", inv.Dep), ("@tot", total), ("@method", inv.Method));



                    Database.Execute(c, t, "SET QUOTED_IDENTIFIER ON; INSERT InvoiceLines(InvoiceId,Kind,Description,Quantity,Unit,UnitPrice) VALUES(@id,N'Phòng',@desc,@q,N'Đêm',@p)",

                        ("@id", invoiceId), ("@desc", "Phòng " + inv.RoomNum), ("@q", (decimal)inv.Nights), ("@p", rate));

                    Database.Execute(c, t, "SET QUOTED_IDENTIFIER ON; INSERT InvoiceLines(InvoiceId,Kind,Description,Quantity,Unit,UnitPrice) VALUES(@id,N'Dịch vụ',@desc,@q,@unit,@p)",

                        ("@id", invoiceId), ("@desc", srv["Name"]), ("@q", (decimal)inv.SrvQty), ("@unit", srv["Unit"]), ("@p", srv["Price"]));

                }

            }



            return true;

        });

    }



    private static void ValidatePassword(string password)
    {
        if (password.Length < 8)
            throw new ArgumentException("Mật khẩu phải có từ 8 ký tự trở lên.");
        if (password.Length > 100)
            throw new ArgumentException("Mật khẩu không được vượt quá 100 ký tự.");
    }

    private void Seed()

    {

        db.Transaction((c, t) =>

        {

            if (Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Users") > 0)

                return false;

            Database.Execute(c, t, """

                INSERT RoomTypes(Name,Capacity,Rate) VALUES(N'Phòng đơn',2,450000),(N'Phòng đôi',4,700000),(N'Phòng gia đình',6,1000000);

                INSERT Rooms(Number,Floor,RoomTypeId) SELECT v.Number,v.Floor,r.Id FROM (VALUES(N'101',1,N'Phòng đơn'),(N'102',1,N'Phòng đơn'),(N'103',1,N'Phòng đôi'),(N'201',2,N'Phòng đôi'),(N'202',2,N'Phòng đôi'),(N'203',2,N'Phòng gia đình'),(N'301',3,N'Phòng đơn'),(N'302',3,N'Phòng gia đình')) v(Number,Floor,TypeName) JOIN RoomTypes r ON r.Name=v.TypeName;

                INSERT Customers(FullName,Phone,IdentityNumber,Address) VALUES(N'Nguyễn Văn An (mẫu)',N'0900000001',N'DEMO001',N'Hà Nội'),(N'Trần Thị Bình (mẫu)',N'0900000002',N'DEMO002',N'Đà Nẵng'),(N'Lê Minh Châu (mẫu)',N'0900000003',N'DEMO003',N'TP. Hồ Chí Minh');

                INSERT Services(Name,Unit,Price) VALUES(N'Nước suối',N'Chai',10000),(N'Giặt là',N'Kg',40000),(N'Ăn sáng',N'Suất',70000),(N'Thuê xe máy',N'Ngày',150000);

                INSERT Employees(FullName,Phone,Position) VALUES(N'Quản lý mẫu',N'0900000010',N'Quản lý'),(N'Lễ tân mẫu',N'0900000011',N'Lễ tân');

                """);

            Database.Execute(c, t, "INSERT Users(EmployeeId,Username,PasswordHash,Role) SELECT Id,N'admin',@hash,N'Quản lý' FROM Employees WHERE FullName=N'Quản lý mẫu'", ("@hash", HashPassword("Admin@123")));

            Database.Execute(c, t, "INSERT Users(EmployeeId,Username,PasswordHash,Role) SELECT Id,N'letan',@hash,N'Lễ tân' FROM Employees WHERE FullName=N'Lễ tân mẫu'", ("@hash", HashPassword("Letan@123")));

            return true;

        });

    }

    public DataTable Catalog(Session user, CatalogKind kind, string search = "")

    {

        using var c = db.Open();

        Authorize(c, null, user, kind is CatalogKind.Users or CatalogKind.Employees);

        var sql = kind switch

        {

            CatalogKind.RoomTypes => "SELECT Id, Name AS [Tên loại phòng], Capacity AS [Sức chứa], Rate AS [Giá / đêm], Active AS [Hoạt động] FROM RoomTypes WHERE Name LIKE @s ORDER BY Id",

            CatalogKind.Rooms => "SELECT r.Id, r.Number AS [Số phòng], r.Floor AS [Tầng], t.Name AS [Loại phòng], r.OperationalStatus AS [Tình trạng], r.Active AS [Hoạt động] FROM Rooms r JOIN RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Number LIKE @s OR t.Name LIKE @s ORDER BY r.Number",

            CatalogKind.Customers => "SELECT Id,FullName AS [Họ tên],Phone AS [Điện thoại],IdentityNumber AS [Giấy tờ],Address AS [Địa chỉ],Active AS [Hoạt động] FROM Customers WHERE FullName LIKE @s OR Phone LIKE @s OR IdentityNumber LIKE @s ORDER BY Id DESC",

            CatalogKind.Services => "SELECT Id,Name AS [Tên dịch vụ],Unit AS [Đơn vị],Price AS [Đơn giá],Active AS [Hoạt động] FROM Services WHERE Name LIKE @s ORDER BY Id",

            CatalogKind.Employees => "SELECT Id,FullName AS [Họ tên],Phone AS [Điện thoại],Position AS [Chức vụ],Active AS [Hoạt động] FROM Employees WHERE FullName LIKE @s OR Phone LIKE @s ORDER BY Id",

            CatalogKind.Users => "SELECT u.Id,u.Username AS [Tên đăng nhập],e.FullName AS [Nhân viên],u.Role AS [Chức vụ / Vai trò],u.Active AS [Hoạt động] FROM Users u JOIN Employees e ON e.Id=u.EmployeeId WHERE u.Username LIKE @s OR e.FullName LIKE @s ORDER BY u.Id",

            _ => throw new ArgumentOutOfRangeException(nameof(kind))

        };

        return Database.Query(c, null, sql, ("@s", "%" + search.Trim() + "%"));

    }

    public DataRow CatalogRecord(Session user, CatalogKind kind, int id)

    {

        using var c = db.Open();

        Authorize(c, null, user, kind is CatalogKind.Users or CatalogKind.Employees);

        var columns = kind == CatalogKind.Users ? "Id, EmployeeId, Username, Role, Active" : "*";

        return Single(Database.Query(c, null, $"SELECT {columns} FROM {kind} WHERE Id=@id", ("@id", id)));

    }

    internal static DataRow Single(DataTable table) => table.Rows.Count == 1 ? table.Rows[0] : throw new InvalidOperationException("Không tìm thấy dữ liệu. Hãy tải lại danh sách.");

    public DataTable Lookup(Session user, CatalogKind kind)

    {

        using var c = db.Open();

        Authorize(c, null, user, kind is CatalogKind.Users or CatalogKind.Employees);

        var expression = kind switch

        {

            CatalogKind.RoomTypes => "Name",

            CatalogKind.Rooms => "Number",

            CatalogKind.Customers => "FullName + N' • ' + Phone",

            CatalogKind.Services => "Name + N' (' + Unit + N')'",

            CatalogKind.Employees => "FullName",

            _ => throw new ArgumentException("Danh mục không hỗ trợ.")

        };

        return Database.Query(c, null, $"SELECT Id, {expression} AS Name FROM {kind} WHERE Active=1 ORDER BY Name");

    }

    public int SaveCatalog(Session user, CatalogKind kind, int? id, Dictionary<string, object?> fields)

    {

        string Text(string key, int max, bool required = true)

        {

            var value = Convert.ToString(fields.GetValueOrDefault(key))?.Trim() ?? "";

            if ((required && value.Length == 0) || value.Length > max)

                throw new ArgumentException($"Thông tin {Labels.GetValueOrDefault(key, key)} phải có {(required ? "1" : "0")}–{max} ký tự.");

            return value;

        }

        int Int(string key, int min, int max)

        {

            var v = Convert.ToInt32(fields.GetValueOrDefault(key));

            if (v < min || v > max)

                throw new ArgumentException($"{Labels.GetValueOrDefault(key, key)} phải từ {min} đến {max}.");

            return v;

        }

        decimal Price(string key)

        {

            var v = Convert.ToDecimal(fields.GetValueOrDefault(key));

            if (v <= 0 || v > 100000000)

                throw new ArgumentException("Đơn giá phải lớn hơn 0 và không vượt 100 triệu đồng.");

            return Money.Round(v);

        }

        string Phone()

        {

            var p = Text("Phone", 25);

            if (!Regex.IsMatch(p, @"^\+?[0-9]{9,15}$"))

                throw new ArgumentException("Điện thoại gồm 9–15 chữ số, có thể bắt đầu bằng +.");

            return p;

        }

        var active = Convert.ToBoolean(fields.GetValueOrDefault("Active", true));

        var values = new Dictionary<string, object?> { ["Active"] = active };

        switch (kind)

        {

            case CatalogKind.RoomTypes:

                values["Name"] = Text("Name", 80);

                values["Capacity"] = Int("Capacity", 1, 20);

                values["Rate"] = Price("Rate");

                break;

            case CatalogKind.Rooms:

                values["Number"] = Text("Number", 20);

                values["Floor"] = Int("Floor", 0, 100);

                values["RoomTypeId"] = Int("RoomTypeId", 1, int.MaxValue);

                values["OperationalStatus"] = Text("OperationalStatus", 20);

                break;

            case CatalogKind.Customers:

                values["FullName"] = Text("FullName", 100);

                values["Phone"] = Phone();

                var identity = Text("IdentityNumber", 30, false);

                values["IdentityNumber"] = identity.Length == 0 ? null : identity;

                values["Address"] = Text("Address", 250, false);

                break;

            case CatalogKind.Services:

                values["Name"] = Text("Name", 100);

                values["Unit"] = Text("Unit", 30);

                values["Price"] = Price("Price");

                break;

            case CatalogKind.Employees:

                values["FullName"] = Text("FullName", 100);

                values["Phone"] = Phone();

                values["Position"] = Text("Position", 80);

                var empPassword = Convert.ToString(fields.GetValueOrDefault("Password")) ?? "";
                if (!id.HasValue || empPassword.Length > 0)
                {
                    if (!id.HasValue && empPassword.Length == 0)
                        throw new ArgumentException("Vui lòng nhập mật khẩu cho nhân viên (từ 8 ký tự trở lên).");
                    if (empPassword.Length > 0)
                        ValidatePassword(empPassword);
                }

                break;

            case CatalogKind.Users:

                values["Username"] = Text("Username", 40);

                if (fields.TryGetValue("EmployeeId", out var empIdVal) && empIdVal is int eid && eid > 0)
                    values["EmployeeId"] = eid;

                values["Role"] = Text("Role", 50);

                var uname = (string)values["Username"]!;
                if (uname.Length < 3)
                    throw new ArgumentException("Tên đăng nhập phải có ít nhất 3 ký tự.");

                var password = Convert.ToString(fields.GetValueOrDefault("Password")) ?? "";

                if (!id.HasValue || password.Length > 0)

                {

                    ValidatePassword(password);

                    values["PasswordHash"] = HashPassword(password);

                }

                break;

        }

        return db.Transaction((c, t) =>

        {

            Authorize(c, t, user, kind != CatalogKind.Customers);

            if (id.HasValue)

            {

                var old = Single(Database.Query(c, t, $"SELECT * FROM {kind} WHERE Id=@id", ("@id", id)));
                if (kind == CatalogKind.Users && !values.ContainsKey("EmployeeId"))
                    values["EmployeeId"] = old["EmployeeId"];

                if (kind == CatalogKind.Users && id == user.UserId && (!active || !Equals(values["Role"], "Quản lý") || !Equals(values["EmployeeId"], user.EmployeeId)))

                    throw new InvalidOperationException("Không thể khóa, chuyển nhân viên hoặc hạ quyền tài khoản đang sử dụng.");

                if (kind == CatalogKind.Employees && id == user.EmployeeId && !active)

                    throw new InvalidOperationException("Không thể ngừng hoạt động nhân viên đang đăng nhập.");

                if (kind is CatalogKind.Rooms && (!active || !Equals(old["RoomTypeId"], values["RoomTypeId"]) || !Equals(old["Number"], values["Number"]) || !Equals(old["OperationalStatus"], values["OperationalStatus"])))

                {

                    var busy = Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM BookingRooms br JOIN Bookings b ON b.Id=br.BookingId WHERE br.RoomId=@id AND b.Status IN(N'Đã đặt',N'Đang ở')", ("@id", id));

                    if (busy > 0)

                        throw new InvalidOperationException("Phòng có phiếu đặt hoặc đang có khách. Hãy xử lý các phiếu trước khi đổi thông tin/trạng thái phòng.");

                }

                if (kind is CatalogKind.RoomTypes && !active && Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Rooms WHERE RoomTypeId=@id AND Active=1", ("@id", id)) > 0)

                    throw new InvalidOperationException("Loại phòng còn phòng đang hoạt động.");

                if (kind == CatalogKind.Customers && !active && Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Bookings WHERE CustomerId=@id AND Status IN(N'Đã đặt',N'Đang ở')", ("@id", id)) > 0)

                    throw new InvalidOperationException("Khách hàng đang có phiếu đặt hoặc đang lưu trú.");

            }

            else if (kind == CatalogKind.Users && !values.ContainsKey("EmployeeId"))
            {
                var uName = (string)values["Username"]!;
                var uRole = (string)values["Role"]!;
                var empRows = Database.Query(c, t, "SELECT TOP 1 Id FROM Employees WHERE FullName=@n", ("@n", uName));
                if (empRows.Rows.Count > 0)
                    values["EmployeeId"] = empRows.Rows[0].Field<int>("Id");
                else
                    values["EmployeeId"] = Database.Scalar<int>(c, t, "INSERT Employees(FullName, Phone, Position, Active) OUTPUT INSERTED.Id VALUES(@n, N'0900000000', @p, 1)", ("@n", uName), ("@p", uRole));
            }

            if (kind == CatalogKind.Rooms && Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM RoomTypes WHERE Id=@id AND Active=1", ("@id", values["RoomTypeId"])) == 0)

                throw new ArgumentException("Loại phòng không còn hoạt động.");

            if (kind == CatalogKind.Users && Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Employees WHERE Id=@id AND Active=1", ("@id", values["EmployeeId"])) == 0)

                throw new ArgumentException("Nhân viên không còn hoạt động.");

            var parameters = values.Select(v => ("@" + v.Key, v.Value)).ToList();

            int savedId;

            if (id.HasValue)

            {

                parameters.Add(("@id", id.Value));

                Database.Execute(c, t, $"UPDATE {kind} SET {string.Join(',', values.Keys.Select(k => $"{k}=@{k}"))} WHERE Id=@id", parameters.ToArray());

                savedId = id.Value;

            }

            else

                savedId = Database.Scalar<int>(c, t, $"INSERT {kind}({string.Join(',', values.Keys)}) OUTPUT INSERTED.Id VALUES({string.Join(',', values.Keys.Select(k => "@" + k))})", parameters.ToArray());

            if (kind == CatalogKind.Employees)
            {
                var empName = (string)values["FullName"]!;
                var empRole = (string)values["Position"]!;
                var empPassVal = Convert.ToString(fields.GetValueOrDefault("Password")) ?? "";
                var userRows = Database.Query(c, t, "SELECT Id, Username FROM Users WHERE EmployeeId=@eid", ("@eid", savedId));
                if (userRows.Rows.Count > 0)
                {
                    var uid = userRows.Rows[0].Field<int>("Id");
                    if (empPassVal.Length > 0)
                    {
                        Database.Execute(c, t, "UPDATE Users SET PasswordHash=@p, Role=@r, Active=@a WHERE Id=@id",
                            ("@p", HashPassword(empPassVal)), ("@r", empRole), ("@a", active), ("@id", uid));
                    }
                    else
                    {
                        Database.Execute(c, t, "UPDATE Users SET Role=@r, Active=@a WHERE Id=@id",
                            ("@r", empRole), ("@a", active), ("@id", uid));
                    }
                }
                else if (empPassVal.Length > 0)
                {
                    var existingCount = Database.Scalar<int>(c, t, "SELECT COUNT(*) FROM Users WHERE Username=@u", ("@u", empName));
                    var finalUsername = existingCount == 0 ? empName : (empName + "_" + savedId);
                    Database.Execute(c, t, "INSERT Users(EmployeeId, Username, PasswordHash, Role, Active) VALUES(@eid, @u, @p, @r, @a)",
                        ("@eid", savedId), ("@u", finalUsername), ("@p", HashPassword(empPassVal)), ("@r", empRole), ("@a", active));
                }
            }

            Audit(c, t, user, "Lưu danh mục", $"{kind} #{savedId}");

            return savedId;

        });

    }

    public void DeleteCatalog(Session user, CatalogKind kind, int id)

    {

        db.Transaction((c, t) =>

        {

            Authorize(c, t, user, kind != CatalogKind.Customers);

            if (kind == CatalogKind.Users && id == user.UserId || kind == CatalogKind.Employees && id == user.EmployeeId)

                throw new InvalidOperationException("Không thể xóa tài khoản/nhân viên đang sử dụng.");

            if (Database.Execute(c, t, $"DELETE FROM {kind} WHERE Id=@id", ("@id", id)) == 0)

                throw new InvalidOperationException("Bản ghi đã được xóa.");

            Audit(c, t, user, "Xóa danh mục", $"{kind} #{id}");

            return true;

        });

    }

    private static readonly Dictionary<string, string> Labels = new() { ["FullName"] = "họ tên", ["Name"] = "tên", ["Phone"] = "điện thoại", ["Capacity"] = "sức chứa", ["Floor"] = "tầng", ["Number"] = "số phòng", ["Unit"] = "đơn vị", ["Address"] = "địa chỉ", ["IdentityNumber"] = "giấy tờ", ["Position"] = "chức vụ", ["Username"] = "tên tài khoản" };

}

