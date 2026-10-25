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
