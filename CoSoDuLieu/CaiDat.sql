-- Chay toan bo trong SQL Server Management Studio.
-- Tao database rieng, khong xoa du lieu da co.
USE master;
GO
DECLARE @DatabaseName sysname = N'HotelManagementBTL';
-- @DatabaseName is supplied by the application or the installation script.
-- Physical names must be unique: different LocalDB instances can share a folder.
DECLARE @Resource nvarchar(255) = N'HotelBTL.Initialize.' + @DatabaseName;
DECLARE @LockResult int;
EXEC @LockResult = sys.sp_getapplock @Resource=@Resource, @LockMode='Exclusive',
    @LockOwner='Session', @LockTimeout=60000;
IF @LockResult < 0 THROW 51000, N'Không thể khóa khởi tạo dữ liệu. Vui lòng thử lại.', 1;

BEGIN TRY
    IF DB_ID(@DatabaseName) IS NULL
    BEGIN
        DECLARE @DataPath nvarchar(4000) = CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath'));
        DECLARE @LogPath nvarchar(4000) = CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultLogPath'));
        IF NULLIF(@DataPath, N'') IS NULL OR NULLIF(@LogPath, N'') IS NULL
            THROW 51000, N'Không xác định được thư mục dữ liệu của SQL Server.', 1;
        IF RIGHT(@DataPath, 1) NOT IN (N'\', N'/') SET @DataPath += N'\';
        IF RIGHT(@LogPath, 1) NOT IN (N'\', N'/') SET @LogPath += N'\';

        DECLARE @Stem nvarchar(200) = @DatabaseName + N'_' + REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'');
        DECLARE @DataFile nvarchar(4000) = @DataPath + @Stem + N'.mdf';
        DECLARE @LogFile nvarchar(4000) = @LogPath + @Stem + N'_log.ldf';
        DECLARE @Sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName)
            + N' ON PRIMARY (NAME = ' + QUOTENAME(@DatabaseName, '''')
            + N', FILENAME = N''' + REPLACE(@DataFile, N'''', N'''''') + N''')'
            + N' LOG ON (NAME = ' + QUOTENAME(@DatabaseName + N'_log', '''')
            + N', FILENAME = N''' + REPLACE(@LogFile, N'''', N'''''') + N''')';
        EXEC sys.sp_executesql @Sql;
    END;
    EXEC sys.sp_releaseapplock @Resource=@Resource, @LockOwner='Session';
END TRY
BEGIN CATCH
    EXEC sys.sp_releaseapplock @Resource=@Resource, @LockOwner='Session';
    THROW;
END CATCH;

GO
USE HotelManagementBTL;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource=N'HotelBTL.Schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000;
IF @r<0 THROW 51000, N'Cannot acquire schema initialization lock.', 1;
GO
-- HotelManagementBTL - additive initialization; never drops existing data.
IF OBJECT_ID(N'dbo.SchemaInfo') IS NULL
BEGIN
CREATE TABLE dbo.SchemaInfo(Version int NOT NULL PRIMARY KEY);
INSERT dbo.SchemaInfo VALUES(1);
CREATE TABLE dbo.RoomTypes(
 Id int IDENTITY PRIMARY KEY, Name nvarchar(80) NOT NULL UNIQUE,
 Capacity int NOT NULL CHECK(Capacity BETWEEN 1 AND 20), Rate decimal(18,0) NOT NULL CHECK(Rate>0), Active bit NOT NULL DEFAULT 1);
CREATE TABLE dbo.Rooms(
 Id int IDENTITY PRIMARY KEY, Number nvarchar(20) NOT NULL UNIQUE, Floor int NOT NULL CHECK(Floor BETWEEN 0 AND 100),
 RoomTypeId int NOT NULL REFERENCES dbo.RoomTypes(Id), OperationalStatus nvarchar(20) NOT NULL DEFAULT N'Sẵn sàng' CHECK(OperationalStatus IN(N'Sẵn sàng',N'Chờ dọn',N'Bảo trì')), Active bit NOT NULL DEFAULT 1);
CREATE TABLE dbo.Customers(
 Id int IDENTITY PRIMARY KEY, FullName nvarchar(100) NOT NULL, Phone nvarchar(25) NOT NULL,
 IdentityNumber nvarchar(30) NULL, Address nvarchar(250) NOT NULL DEFAULT N'', Active bit NOT NULL DEFAULT 1);
CREATE UNIQUE INDEX UX_Customers_Identity ON dbo.Customers(IdentityNumber) WHERE IdentityNumber IS NOT NULL;
CREATE TABLE dbo.Employees(
 Id int IDENTITY PRIMARY KEY, FullName nvarchar(100) NOT NULL, Phone nvarchar(25) NOT NULL, Position nvarchar(80) NOT NULL, Active bit NOT NULL DEFAULT 1);
CREATE TABLE dbo.Users(
 Id int IDENTITY PRIMARY KEY, EmployeeId int NOT NULL REFERENCES dbo.Employees(Id), Username nvarchar(40) NOT NULL UNIQUE,
 PasswordHash nvarchar(300) NOT NULL, Role nvarchar(50) NOT NULL, Active bit NOT NULL DEFAULT 1);
CREATE TABLE dbo.Services(
 Id int IDENTITY PRIMARY KEY, Name nvarchar(100) NOT NULL UNIQUE, Unit nvarchar(30) NOT NULL,
 Price decimal(18,0) NOT NULL CHECK(Price>0), Active bit NOT NULL DEFAULT 1);
CREATE TABLE dbo.Bookings(
 Id int IDENTITY PRIMARY KEY, CustomerId int NOT NULL REFERENCES dbo.Customers(Id), EmployeeId int NOT NULL REFERENCES dbo.Employees(Id),
 Arrival datetime2 NOT NULL, Departure datetime2 NOT NULL, Guests int NOT NULL CHECK(Guests>0),
 Deposit decimal(18,0) NOT NULL DEFAULT 0 CHECK(Deposit>=0), RefundedDeposit decimal(18,0) NOT NULL DEFAULT 0 CHECK(RefundedDeposit>=0),
 Status nvarchar(20) NOT NULL DEFAULT N'Đã đặt' CHECK(Status IN(N'Đã đặt',N'Đang ở',N'Đã trả',N'Đã hủy')),
 ActualCheckIn datetime2 NULL, ActualCheckOut datetime2 NULL, Notes nvarchar(1000) NOT NULL DEFAULT N'', CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME(),
 CONSTRAINT CK_BookingDates CHECK(Departure>Arrival));
CREATE TABLE dbo.BookingRooms(
 BookingId int NOT NULL REFERENCES dbo.Bookings(Id), RoomId int NOT NULL REFERENCES dbo.Rooms(Id),
 NightlyRate decimal(18,0) NOT NULL CHECK(NightlyRate>0), Capacity int NOT NULL CHECK(Capacity>0), PRIMARY KEY(BookingId, RoomId));
CREATE INDEX IX_BookingRooms_Room ON dbo.BookingRooms(RoomId,BookingId);
CREATE INDEX IX_Bookings_Dates ON dbo.Bookings(Status, Arrival, Departure);
CREATE TABLE dbo.ServiceUsages(
 Id int IDENTITY PRIMARY KEY, BookingId int NOT NULL REFERENCES dbo.Bookings(Id), ServiceId int NOT NULL REFERENCES dbo.Services(Id),
 ServiceName nvarchar(100) NOT NULL, Unit nvarchar(30) NOT NULL, Quantity int NOT NULL CHECK(Quantity>0), UnitPrice decimal(18,0) NOT NULL CHECK(UnitPrice>0),
 UsedAt datetime2 NOT NULL DEFAULT SYSDATETIME());
CREATE TABLE dbo.Invoices(
 Id int IDENTITY PRIMARY KEY, BookingId int NOT NULL UNIQUE REFERENCES dbo.Bookings(Id), EmployeeId int NOT NULL REFERENCES dbo.Employees(Id),
 CustomerName nvarchar(100) NOT NULL, CustomerPhone nvarchar(25) NOT NULL, CustomerIdentity nvarchar(30) NOT NULL, CustomerAddress nvarchar(250) NOT NULL,
 EmployeeName nvarchar(100) NOT NULL, IssuedAt datetime2 NOT NULL DEFAULT SYSDATETIME(), CheckIn datetime2 NOT NULL, CheckOut datetime2 NOT NULL,
 Surcharge decimal(18,0) NOT NULL CHECK(Surcharge>=0), Discount decimal(18,0) NOT NULL CHECK(Discount>=0), Deposit decimal(18,0) NOT NULL CHECK(Deposit>=0),
 Total decimal(18,0) NOT NULL CHECK(Total>=0), PaymentMethod nvarchar(30) NOT NULL CHECK(PaymentMethod IN(N'Tiền mặt',N'Chuyển khoản',N'Thẻ')),
 Notes nvarchar(1000) NOT NULL DEFAULT N'');
CREATE INDEX IX_Invoices_Date ON dbo.Invoices(IssuedAt);
CREATE TABLE dbo.InvoiceLines(
 Id int IDENTITY PRIMARY KEY, InvoiceId int NOT NULL REFERENCES dbo.Invoices(Id), Kind nvarchar(20) NOT NULL,
 Description nvarchar(200) NOT NULL, Quantity decimal(18,0) NOT NULL CHECK(Quantity>0), Unit nvarchar(30) NOT NULL,
 UnitPrice decimal(18,0) NOT NULL CHECK(UnitPrice>0), Amount AS (Quantity*UnitPrice) PERSISTED);
CREATE TABLE dbo.AuditLog(
 Id int IDENTITY PRIMARY KEY, UserId int NOT NULL REFERENCES dbo.Users(Id), Action nvarchar(100) NOT NULL,
 Detail nvarchar(1000) NOT NULL, CreatedAt datetime2 NOT NULL DEFAULT SYSDATETIME());
END
GO
CREATE OR ALTER TRIGGER dbo.TR_Invoices_Immutable ON dbo.Invoices INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51001, N'Hóa đơn đã thanh toán không được sửa hoặc xóa.', 1;
END
GO
CREATE OR ALTER TRIGGER dbo.TR_InvoiceLines_Immutable ON dbo.InvoiceLines INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51002, N'Chi tiết hóa đơn đã thanh toán không được sửa hoặc xóa.', 1;
END
GO

DECLARE @seedLock int;
EXEC @seedLock=sys.sp_getapplock @Resource=N'HotelBTL.Write', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=60000;
IF @seedLock<0 THROW 51000, N'Cannot acquire sample data initialization lock.', 1;
IF NOT EXISTS(SELECT 1 FROM Users)
BEGIN
INSERT RoomTypes(Name,Capacity,Rate) VALUES(N'Phòng đơn',2,450000),(N'Phòng đôi',4,700000),(N'Phòng gia đình',6,1000000);
INSERT Rooms(Number,Floor,RoomTypeId) SELECT v.Number,v.Floor,t.Id FROM (VALUES(N'101',1,N'Phòng đơn'),(N'102',1,N'Phòng đơn'),(N'103',1,N'Phòng đôi'),(N'201',2,N'Phòng đôi'),(N'202',2,N'Phòng đôi'),(N'203',2,N'Phòng gia đình'),(N'301',3,N'Phòng đơn'),(N'302',3,N'Phòng gia đình')) v(Number,Floor,TypeName) JOIN RoomTypes t ON t.Name=v.TypeName;
INSERT Customers(FullName,Phone,IdentityNumber,Address) VALUES(N'Nguyễn Văn An (mẫu)',N'0900000001',N'DEMO001',N'Hà Nội'),(N'Trần Thị Bình (mẫu)',N'0900000002',N'DEMO002',N'Đà Nẵng'),(N'Lê Minh Châu (mẫu)',N'0900000003',N'DEMO003',N'TP. Hồ Chí Minh');
INSERT Services(Name,Unit,Price) VALUES(N'Nước suối',N'Chai',10000),(N'Giặt là',N'Kg',40000),(N'Ăn sáng',N'Suất',70000),(N'Thuê xe máy',N'Ngày',150000);
INSERT Employees(FullName,Phone,Position) VALUES(N'Quản lý mẫu',N'0900000010',N'Quản lý'),(N'Lễ tân mẫu',N'0900000011',N'Lễ tân');
INSERT Users(EmployeeId,Username,PasswordHash,Role) SELECT Id,N'admin',N'210000.nZfiNbhICASQghOkUXQjAg==.i0SNI0hatrFoY+Y9JmkG9vVK1V7xKppfOdwpYvYjdGw=',N'Quản lý' FROM Employees WHERE FullName=N'Quản lý mẫu';
INSERT Users(EmployeeId,Username,PasswordHash,Role) SELECT Id,N'letan',N'210000.9s8ilYMbfgzl/fdTkiiTBg==.MqWDWA2lzZ1/Ju4nDGslLd5cd5JkH1CFk0EJCSfQ+EU=',N'Lễ tân' FROM Employees WHERE FullName=N'Lễ tân mẫu';
END
GO
COMMIT TRANSACTION;
GO
SELECT N'Hoàn tất. admin / Admin@123; letan / Letan@123' AS KetQua;