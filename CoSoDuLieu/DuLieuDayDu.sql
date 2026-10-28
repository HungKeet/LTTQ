-- Du lieu day du xuat tu database HotelManagementBTL tren may cuc bo
-- Thoi diem xuat: 2026-10-27 18:08:42
USE HotelManagementBTL;
GO
-- Legacy data snapshot: refuse to overwrite existing paid room upgrade history.
SET XACT_ABORT ON;
DECLARE @ActiveIdentity sysname = NULL;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @LockResult int;
EXEC @LockResult=sys.sp_getapplock @Resource=N'HotelBTL.Write', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
IF @LockResult<0 THROW 51010, N'Hotel is busy; retry the import later.', 1;
DECLARE @HasUpgrades bit = 0;
IF OBJECT_ID(N'dbo.RoomUpgrades') IS NOT NULL
    EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.RoomUpgrades WITH(HOLDLOCK)) SET @found=1;', N'@found bit OUTPUT', @found=@HasUpgrades OUTPUT;
IF @HasUpgrades=1
    THROW 51012, N'Target contains paid room upgrades. Import this legacy snapshot into a fresh initialized database or restore a full database backup; existing upgrade history will not be erased.', 1;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
-- Vo hieu hoa trigger bat bien de nap du lieu
IF OBJECT_ID('dbo.TR_Invoices_Immutable') IS NOT NULL DISABLE TRIGGER dbo.TR_Invoices_Immutable ON dbo.Invoices;
IF OBJECT_ID('dbo.TR_InvoiceLines_Immutable') IS NOT NULL DISABLE TRIGGER dbo.TR_InvoiceLines_Immutable ON dbo.InvoiceLines;
-- Xoa du lieu cu de nap du lieu day du
DELETE FROM AuditLog;
DELETE FROM InvoiceLines;
DELETE FROM Invoices;
DELETE FROM ServiceUsages;
DELETE FROM BookingRooms;
DELETE FROM Bookings;
DELETE FROM Services;
DELETE FROM Users;
DELETE FROM Employees;
DELETE FROM Customers;
DELETE FROM Rooms;
DELETE FROM RoomTypes;
-- Table: RoomTypes (4 dong)
SET IDENTITY_INSERT [RoomTypes] ON;
SET @ActiveIdentity=N'RoomTypes';
INSERT INTO [RoomTypes] ([Id],[Name],[Capacity],[Rate],[Active]) VALUES
(1,N'Phòng đơn',2,450000,1),
(2,N'Phòng đôi',4,700000,1),
(3,N'Phòng gia đình',6,1000000,1),
(4,N'President Room',10,3000000,1);
SET IDENTITY_INSERT [RoomTypes] OFF;
SET @ActiveIdentity=NULL;
-- Table: Rooms (23 dong)
SET IDENTITY_INSERT [Rooms] ON;
SET @ActiveIdentity=N'Rooms';
INSERT INTO [Rooms] ([Id],[Number],[Floor],[RoomTypeId],[OperationalStatus],[Active]) VALUES
(1,N'101',1,1,N'Sẵn sàng',1),
(2,N'102',1,2,N'Chờ dọn',1),
(3,N'103',1,2,N'Sẵn sàng',1),
(4,N'201',2,2,N'Sẵn sàng',1),
(5,N'202',2,2,N'Sẵn sàng',1),
(6,N'203',2,3,N'Bảo trì',1),
(7,N'301',3,1,N'Sẵn sàng',1),
(8,N'302',3,3,N'Sẵn sàng',1),
(24,N'104',1,2,N'Sẵn sàng',1),
(25,N'105',1,1,N'Sẵn sàng',1),
(26,N'204',2,1,N'Chờ dọn',1),
(27,N'205',2,3,N'Sẵn sàng',1),
(28,N'303',3,2,N'Sẵn sàng',1),
(29,N'304',3,1,N'Sẵn sàng',1),
(30,N'305',3,3,N'Chờ dọn',1),
(31,N'401',4,1,N'Sẵn sàng',1),
(32,N'402',4,1,N'Sẵn sàng',1),
(33,N'403',4,2,N'Sẵn sàng',1),
(34,N'404',4,2,N'Sẵn sàng',1),
(35,N'405',4,3,N'Sẵn sàng',1),
(36,N'501',5,2,N'Sẵn sàng',1),
(37,N'502',5,3,N'Chờ dọn',1),
(38,N'503',5,4,N'Sẵn sàng',1);
SET IDENTITY_INSERT [Rooms] OFF;
SET @ActiveIdentity=NULL;
-- Table: Customers (53 dong)
SET IDENTITY_INSERT [Customers] ON;
SET @ActiveIdentity=N'Customers';
INSERT INTO [Customers] ([Id],[FullName],[Phone],[IdentityNumber],[Address],[Active]) VALUES
(1,N'Trần Đình Trí',N'0900000001',N'DEMO001',N'Hà Nội',1),
(2,N'Phạm Thu Hà',N'0900000002',N'DEMO002',N'Đà Nẵng',1),
(3,N'Vũ Mạnh Hùng',N'0900000003',N'DEMO003',N'TP. Hồ Chí Minh',1),
(4,N'Hoàng Minh Đức',N'0912345001',N'001200001001',N'Hà Nội',1),
(5,N'Phạm Thanh Tùng',N'0912345002',N'001200001002',N'TP. Hồ Chí Minh',1),
(6,N'Đặng Quỳnh Trang',N'0912345003',N'001200001003',N'Đà Nẵng',1),
(7,N'Vũ Quốc Bảo',N'0912345004',N'001200001004',N'Hải Phòng',1),
(8,N'Bùi Thu Thảo',N'0912345005',N'001200001005',N'Cần Thơ',1),
(9,N'Đỗ Nhật Minh',N'0912345006',N'001200001006',N'Nha Trang',1),
(10,N'Ngô Phương Linh',N'0912345007',N'001200001007',N'Huế',1),
(11,N'Dương Tuấn Kiệt',N'0912345008',N'001200001008',N'Vũng Tàu',1),
(12,N'Đinh Bảo Ngọc',N'0912345009',N'001200001009',N'Đà Lạt',1),
(13,N'Lý Gia Huy',N'0912345010',N'001200001010',N'Quảng Ninh',1),
(14,N'Mai Trúc Quỳnh',N'0912345011',N'001200001011',N'Bình Dương',1),
(15,N'Trịnh Đình Khang',N'0912345012',N'001200001012',N'Đồng Nai',1),
(16,N'Phan Ánh Dương',N'0912345013',N'001200001013',N'Bắc Ninh',1),
(17,N'Lương Thùy Chi',N'0912345014',N'001200001014',N'Thanh Hóa',1),
(18,N'Tạ Quang Thắng',N'0912345015',N'001200001015',N'Nghệ An',1),
(19,N'Đoàn Minh Quân',N'0912345016',N'001200001016',N'Hà Tĩnh',1),
(20,N'Cao Thị Khánh Huyền',N'0912345017',N'001200001017',N'Quảng Bình',1),
(21,N'Hà Công Minh',N'0912345018',N'001200001018',N'Lâm Đồng',1),
(22,N'Lưu Đức Trọng',N'0912345019',N'001200001019',N'Khánh Hòa',1),
(23,N'Trương Mỹ Duyên',N'0912345020',N'001200001020',N'Kiên Giang',1),
(24,N'Hồ Hải Đăng',N'0912345021',N'001200001021',N'An Giang',1),
(25,N'Chu Đình Dũng',N'0912345022',N'001200001022',N'Thái Nguyên',1),
(26,N'Tạ Diệu Linh',N'0912345023',N'001200001023',N'Phú Thọ',1),
(27,N'Đinh Hữu Phúc',N'0912345024',N'001200001024',N'Nam Định',1),
(28,N'Diệp Hoài Nam',N'0912345025',N'001200001025',N'Ninh Bình',1),
(29,N'Bùi Đăng Khoa',N'0912345026',N'001200001026',N'Hà Nam',1),
(30,N'Vương Bích Thủy',N'0912345027',N'001200001027',N'Hưng Yên',1),
(31,N'Lâm Quốc Hưng',N'0912345028',N'001200001028',N'Hải Dương',1),
(32,N'Khổng Minh Trí',N'0912345029',N'001200001029',N'Vĩnh Phúc',1),
(33,N'Thân Thúy Hằng',N'0912345030',N'001200001030',N'Bắc Giang',1),
(34,N'Nông Văn Chiến',N'0912345031',N'001200001031',N'Lạng Sơn',1),
(35,N'Quách Diễm My',N'0912345032',N'001200001032',N'Hòa Bình',1),
(36,N'La Quốc Đạt',N'0912345033',N'001200001033',N'Sơn La',1),
(37,N'Trần Đình Trọng',N'0912345034',N'001200001034',N'Quảng Nam',1),
(38,N'Lê Bảo Trâm',N'0912345035',N'001200001035',N'Quảng Ngãi',1),
(39,N'Nguyễn Hoàng Yến',N'0912345036',N'001200001036',N'Bình Định',1),
(40,N'Huỳnh Quốc Tuấn',N'0912345037',N'001200001037',N'Phú Yên',1),
(41,N'Tống Gia Bảo',N'0912345038',N'001200001038',N'Gia Lai',1),
(42,N'Nghiêm Xuân Thành',N'0912345039',N'001200001039',N'Đắk Lắk',1),
(43,N'Bạch Thảo Nhi',N'0912345040',N'001200001040',N'Đắk Nông',1),
(44,N'Mai Công Tuấn',N'0912345041',N'001200001041',N'Tây Ninh',1),
(45,N'Giang Nhật Cường',N'0912345042',N'001200001042',N'Bình Phước',1),
(46,N'Châu Ngọc Diệp',N'0912345043',N'001200001043',N'Tiền Giang',1),
(47,N'Đào Kim Phượng',N'0912345044',N'001200001044',N'Bến Tre',1),
(48,N'Thạch Bảo An',N'0912345045',N'001200001045',N'Trà Vinh',1),
(49,N'Uông Minh Triết',N'0912345046',N'001200001046',N'Vĩnh Long',1),
(50,N'Hứa Thanh Thảo',N'0912345047',N'001200001047',N'Hậu Giang',1),
(51,N'Doãn Ngọc Sơn',N'0912345048',N'001200001048',N'Sóc Trăng',1),
(52,N'Đỗ Phương Thảo',N'0912345049',N'001200001049',N'Bạc Liêu',1),
(53,N'Cù Huy Hoàng',N'0912345050',N'001200001050',N'Cà Mau',1);
SET IDENTITY_INSERT [Customers] OFF;
SET @ActiveIdentity=NULL;
-- Table: Employees (3 dong)
SET IDENTITY_INSERT [Employees] ON;
SET @ActiveIdentity=N'Employees';
INSERT INTO [Employees] ([Id],[FullName],[Phone],[Position],[Active]) VALUES
(1,N'Quản lý',N'0900000010',N'Quản lý',1),
(2,N'Lễ tân',N'0900000011',N'Lễ tân',1),
(4,N'hunghuy',N'0398870266',N'Quản lý buồng phòng',1);
SET IDENTITY_INSERT [Employees] OFF;
SET @ActiveIdentity=NULL;
-- Table: Users (3 dong)
SET IDENTITY_INSERT [Users] ON;
SET @ActiveIdentity=N'Users';
INSERT INTO [Users] ([Id],[EmployeeId],[Username],[PasswordHash],[Role],[Active]) VALUES
(1,1,N'admin',N'210000.BuMVJjuZro6p/nCLaM/zYw==./U9EvJmbXjmWQSFpb5KfhOreP+36AWeZPDsaTA5OgNw=',N'Quản lý',1),
(2,2,N'letan',N'210000.7/bbbH1gpS+h2ijGg4Ly9Q==.z9MCnN2fejcpMW5MeIk/dEY0VQ2BpkALqVPirzHorew=',N'Lễ tân',1),
(6,4,N'hunghuy',N'210000.Q7LEeG73hCdRzYCBTsxuCA==.xB9EDLPIhBWYhCd5490SfJNSvQXWfidzperLhn629dA=',N'Quản lý buồng phòng',1);
SET IDENTITY_INSERT [Users] OFF;
SET @ActiveIdentity=NULL;
-- Table: Services (4 dong)
SET IDENTITY_INSERT [Services] ON;
SET @ActiveIdentity=N'Services';
INSERT INTO [Services] ([Id],[Name],[Unit],[Price],[Active]) VALUES
(1,N'Nước suối',N'Chai',10000,1),
(2,N'Giặt là',N'Kg',40000,1),
(3,N'Ăn sáng',N'Suất',70000,1),
(4,N'Thuê xe máy',N'Ngày',150000,1);
SET IDENTITY_INSERT [Services] OFF;
SET @ActiveIdentity=NULL;
-- Table: Bookings (20 dong)
SET IDENTITY_INSERT [Bookings] ON;
SET @ActiveIdentity=N'Bookings';
INSERT INTO [Bookings] ([Id],[CustomerId],[EmployeeId],[Arrival],[Departure],[Guests],[Deposit],[RefundedDeposit],[Status],[ActualCheckIn],[ActualCheckOut],[Notes],[CreatedAt]) VALUES
(1,1,1,'2026-10-23 16:43:47.4579084','2026-10-25 16:43:47.4579084',2,300000,0,N'Đã trả','2026-10-23 16:43:47.4579084','2026-10-25 16:43:47.4579084',N'Khách mẫu đặt trước','2026-10-23 16:43:47.4579084'),
(2,2,1,'2026-10-24 16:43:47.4614215','2026-10-25 16:43:47.4614215',2,500000,0,N'Đã trả','2026-10-24 16:43:47.4614215','2026-10-25 16:43:47.4614215',N'Khách công tác','2026-10-24 16:43:47.4614215'),
(3,3,1,'2026-10-21 16:43:47.4634298','2026-10-24 16:43:47.4634298',4,1500000,0,N'Đã trả','2026-10-21 16:43:47.4634298','2026-10-24 16:43:47.4634298',N'Khách gia đình','2026-10-21 16:43:47.4634298'),
(12,4,1,'2026-10-25 12:51:02.9966667','2026-10-27 17:51:03.0000000',1,300000,0,N'Đang ở','2026-10-25 12:51:02.9966667',NULL,N'Khách lưu trú ','2026-10-25 17:51:03.0150797'),
(13,5,1,'2026-10-25 07:51:03.5566667','2026-10-26 17:51:03.5566667',2,500000,0,N'Đang ở','2026-10-25 07:51:03.5566667',NULL,N'Khách lưu trú ','2026-10-25 17:51:03.5680588'),
(14,6,1,'2026-10-24 17:51:03.7066667','2026-10-28 17:51:03.7066667',2,1000000,0,N'Đang ở','2026-10-24 17:51:03.7066667',NULL,N'Khách lưu trú ','2026-10-25 17:51:03.7220171'),
(15,7,1,'2026-10-23 17:51:03.7666667','2026-10-26 17:51:03.7666667',2,700000,0,N'Đang ở','2026-10-23 17:51:03.7666667',NULL,N'Khách lưu trú ','2026-10-25 17:51:03.7750038'),
(16,17,1,'2026-10-22 09:00:00.0000000','2026-10-24 11:00:00.0000000',2,500000,0,N'Đã trả','2026-10-22 09:00:00.0000000','2026-10-24 11:00:00.0000000',N'Lịch sử','2026-10-22 09:00:00.0000000'),
(17,15,1,'2026-10-22 09:00:00.0000000','2026-10-23 11:00:00.0000000',2,450000,0,N'Đã trả','2026-10-22 09:00:00.0000000','2026-10-23 11:00:00.0000000',N'Lịch sử','2026-10-22 09:00:00.0000000'),
(18,16,1,'2026-10-19 09:00:00.0000000','2026-10-22 11:00:00.0000000',2,1500000,0,N'Đã trả','2026-10-19 09:00:00.0000000','2026-10-22 11:00:00.0000000',N'Lịch sử','2026-10-19 09:00:00.0000000'),
(19,8,1,'2026-10-18 09:00:00.0000000','2026-10-20 11:00:00.0000000',2,700000,0,N'Đã trả','2026-10-18 09:00:00.0000000','2026-10-20 11:00:00.0000000',N'Lịch sử','2026-10-18 09:00:00.0000000'),
(20,9,1,'2026-10-16 09:00:00.0000000','2026-10-18 11:00:00.0000000',2,1000000,0,N'Đã trả','2026-10-16 09:00:00.0000000','2026-10-18 11:00:00.0000000',N'Lịch sử','2026-10-16 09:00:00.0000000'),
(21,10,1,'2026-10-15 09:00:00.0000000','2026-10-16 11:00:00.0000000',2,400000,0,N'Đã trả','2026-10-15 09:00:00.0000000','2026-10-16 11:00:00.0000000',N'Lịch sử','2026-10-15 09:00:00.0000000'),
(22,11,1,'2026-10-11 09:00:00.0000000','2026-10-13 11:00:00.0000000',2,800000,0,N'Đã trả','2026-10-11 09:00:00.0000000','2026-10-13 11:00:00.0000000',N'Lịch sử','2026-10-11 09:00:00.0000000'),
(23,12,1,'2026-10-07 09:00:00.0000000','2026-10-10 11:00:00.0000000',2,1500000,0,N'Đã trả','2026-10-07 09:00:00.0000000','2026-10-10 11:00:00.0000000',N'Lịch sử','2026-10-07 09:00:00.0000000'),
(24,13,1,'2026-10-06 09:00:00.0000000','2026-10-07 11:00:00.0000000',2,500000,0,N'Đã trả','2026-10-06 09:00:00.0000000','2026-10-07 11:00:00.0000000',N'Lịch sử','2026-10-06 09:00:00.0000000'),
(25,14,1,'2026-10-02 09:00:00.0000000','2026-10-04 11:00:00.0000000',2,500000,0,N'Đã trả','2026-10-02 09:00:00.0000000','2026-10-04 11:00:00.0000000',N'Lịch sử','2026-10-02 09:00:00.0000000'),
(26,43,2,'2026-10-25 14:00:00.0000000','2026-10-29 12:00:00.0000000',2,0,0,N'Đã trả','2026-10-25 23:22:25.1000000','2026-10-25 23:23:22.5766667',N'','2026-10-25 23:21:26.2177638'),
(27,43,1,'2026-10-26 14:00:00.0000000','2026-11-01 12:00:00.0000000',5,0,0,N'Đang ở','2026-10-26 10:54:59.9366667',NULL,N'','2026-10-26 10:39:49.0154692'),
(28,8,1,'2026-10-26 14:00:00.0000000','2026-10-27 12:00:00.0000000',6,0,0,N'Đã trả','2026-10-26 10:41:06.6966667','2026-10-26 10:42:06.0333333',N'','2026-10-26 10:41:02.7606790');
SET IDENTITY_INSERT [Bookings] OFF;
SET @ActiveIdentity=NULL;
-- Table: BookingRooms (21 dong)
INSERT INTO [BookingRooms] ([BookingId],[RoomId],[NightlyRate],[Capacity]) VALUES
(1,1,450000,2),
(2,4,700000,4),
(3,8,1000000,6),
(12,1,450000,2),
(13,5,700000,4),
(14,28,700000,4),
(15,34,700000,4),
(16,4,700000,4),
(17,7,450000,2),
(18,8,1000000,6),
(19,3,700000,4),
(20,27,1000000,6),
(21,1,450000,2),
(22,33,700000,4),
(23,37,1000000,6),
(24,24,700000,4),
(25,32,450000,2),
(26,2,700000,4),
(27,4,700000,4),
(27,38,3000000,10),
(28,37,1000000,6);
-- Table: ServiceUsages (22 dong)
SET IDENTITY_INSERT [ServiceUsages] ON;
SET @ActiveIdentity=N'ServiceUsages';
INSERT INTO [ServiceUsages] ([Id],[BookingId],[ServiceId],[ServiceName],[Unit],[Quantity],[UnitPrice],[UsedAt]) VALUES
(1,1,1,N'Nước suối',N'Chai',2,10000,'2026-10-24 16:43:47.4599079'),
(2,1,3,N'Ăn sáng',N'Suất',2,70000,'2026-10-24 16:43:47.4599079'),
(3,2,2,N'Giặt là',N'Kg',3,40000,'2026-10-24 16:43:47.4624332'),
(4,2,1,N'Nước suối',N'Chai',1,10000,'2026-10-24 16:43:47.4624332'),
(5,3,4,N'Thuê xe máy',N'Ngày',2,150000,'2026-10-22 16:43:47.4634298'),
(6,3,3,N'Ăn sáng',N'Suất',4,70000,'2026-10-23 16:43:47.4634298'),
(15,12,1,N'Nước suối',N'Chai',2,10000,'2026-10-25 17:51:03.2502620'),
(16,13,3,N'Ăn sáng',N'Suất',2,70000,'2026-10-25 17:51:03.5999195'),
(17,14,4,N'Thuê xe máy',N'Ngày',1,150000,'2026-10-25 17:51:03.7432451'),
(18,15,2,N'Giặt là',N'Kg',3,40000,'2026-10-25 17:51:03.8059302'),
(19,16,1,N'Nước suối',N'Chai',4,10000,'2026-10-23 09:00:00.0000000'),
(20,17,3,N'Ăn sáng',N'Suất',2,70000,'2026-10-23 09:00:00.0000000'),
(21,18,4,N'Thuê xe máy',N'Ngày',2,150000,'2026-10-20 09:00:00.0000000'),
(22,19,2,N'Giặt là',N'Kg',3,40000,'2026-10-19 09:00:00.0000000'),
(23,20,3,N'Ăn sáng',N'Suất',4,70000,'2026-10-17 09:00:00.0000000'),
(24,21,1,N'Nước suối',N'Chai',2,10000,'2026-10-16 09:00:00.0000000'),
(25,22,4,N'Thuê xe máy',N'Ngày',1,150000,'2026-10-12 09:00:00.0000000'),
(26,23,3,N'Ăn sáng',N'Suất',3,70000,'2026-10-08 09:00:00.0000000'),
(27,24,2,N'Giặt là',N'Kg',2,40000,'2026-10-07 09:00:00.0000000'),
(28,25,1,N'Nước suối',N'Chai',3,10000,'2026-10-03 09:00:00.0000000'),
(29,26,3,N'Ăn sáng',N'Suất',1,70000,'2026-10-25 23:22:41.1722176'),
(31,26,2,N'Giặt là',N'Kg',2,40000,'2026-10-25 23:22:50.5087099');
SET IDENTITY_INSERT [ServiceUsages] OFF;
SET @ActiveIdentity=NULL;
-- Table: Invoices (15 dong)
SET IDENTITY_INSERT [Invoices] ON;
SET @ActiveIdentity=N'Invoices';
INSERT INTO [Invoices] ([Id],[BookingId],[EmployeeId],[CustomerName],[CustomerPhone],[CustomerIdentity],[CustomerAddress],[EmployeeName],[IssuedAt],[CheckIn],[CheckOut],[Surcharge],[Discount],[Deposit],[Total],[PaymentMethod],[Notes]) VALUES
(1,1,1,N'Nguyễn Văn An (mẫu)',N'0900000001',N'DEMO001',N'Hà Nội',N'Quản lý mẫu','2026-10-25 16:43:47.4599079','2026-10-23 16:43:47.4599079','2026-10-25 16:43:47.4599079',50000,0,300000,1110000,N'Chuyển khoản',N'Đã thanh toán đủ'),
(2,2,1,N'Trần Thị Bình (mẫu)',N'0900000002',N'DEMO002',N'Đà Nẵng',N'Quản lý mẫu','2026-10-25 16:43:47.4624332','2026-10-24 16:43:47.4624332','2026-10-25 16:43:47.4624332',0,50000,500000,780000,N'Tiền mặt',N'Khách thanh toán tiền mặt'),
(3,3,1,N'Lê Minh Châu (mẫu)',N'0900000003',N'DEMO003',N'TP. Hồ Chí Minh',N'Quản lý mẫu','2026-10-24 16:43:47.4634298','2026-10-21 16:43:47.4634298','2026-10-24 16:43:47.4634298',100000,100000,1500000,3580000,N'Thẻ',N'Thanh toán thẻ Visa'),
(4,16,1,N'Lương Thùy Chi',N'0912345014',N'001200001014',N'Thanh Hóa',N'Quản lý mẫu','2026-10-24 11:00:00.0000000','2026-10-22 09:00:00.0000000','2026-10-24 11:00:00.0000000',50000,0,500000,1490000,N'Chuyển khoản',N'Hóa đơn lưu mẫu'),
(5,17,1,N'Trịnh Đình Khang',N'0912345012',N'001200001012',N'Đồng Nai',N'Quản lý mẫu','2026-10-23 11:00:00.0000000','2026-10-22 09:00:00.0000000','2026-10-23 11:00:00.0000000',0,50000,450000,540000,N'Tiền mặt',N'Hóa đơn lưu mẫu'),
(6,18,1,N'Phan Ánh Dương',N'0912345013',N'001200001013',N'Bắc Ninh',N'Quản lý mẫu','2026-10-22 11:00:00.0000000','2026-10-19 09:00:00.0000000','2026-10-22 11:00:00.0000000',100000,100000,1500000,3300000,N'Thẻ',N'Hóa đơn lưu mẫu'),
(7,19,1,N'Bùi Thu Thảo',N'0912345005',N'001200001005',N'Cần Thơ',N'Quản lý mẫu','2026-10-20 11:00:00.0000000','2026-10-18 09:00:00.0000000','2026-10-20 11:00:00.0000000',0,0,700000,1520000,N'Chuyển khoản',N'Hóa đơn lưu mẫu'),
(8,20,1,N'Đỗ Nhật Minh',N'0912345006',N'001200001006',N'Nha Trang',N'Quản lý mẫu','2026-10-18 11:00:00.0000000','2026-10-16 09:00:00.0000000','2026-10-18 11:00:00.0000000',80000,50000,1000000,2310000,N'Thẻ',N'Hóa đơn lưu mẫu'),
(9,21,1,N'Ngô Phương Linh',N'0912345007',N'001200001007',N'Huế',N'Quản lý mẫu','2026-10-16 11:00:00.0000000','2026-10-15 09:00:00.0000000','2026-10-16 11:00:00.0000000',0,20000,400000,450000,N'Tiền mặt',N'Hóa đơn lưu mẫu'),
(10,22,1,N'Dương Tuấn Kiệt',N'0912345008',N'001200001008',N'Vũng Tàu',N'Quản lý mẫu','2026-10-13 11:00:00.0000000','2026-10-11 09:00:00.0000000','2026-10-13 11:00:00.0000000',50000,0,800000,1600000,N'Chuyển khoản',N'Hóa đơn lưu mẫu'),
(11,23,1,N'Đinh Bảo Ngọc',N'0912345009',N'001200001009',N'Đà Lạt',N'Quản lý mẫu','2026-10-10 11:00:00.0000000','2026-10-07 09:00:00.0000000','2026-10-10 11:00:00.0000000',120000,100000,1500000,3230000,N'Thẻ',N'Hóa đơn lưu mẫu'),
(12,24,1,N'Lý Gia Huy',N'0912345010',N'001200001010',N'Quảng Ninh',N'Quản lý mẫu','2026-10-07 11:00:00.0000000','2026-10-06 09:00:00.0000000','2026-10-07 11:00:00.0000000',0,0,500000,780000,N'Tiền mặt',N'Hóa đơn lưu mẫu'),
(13,25,1,N'Mai Trúc Quỳnh',N'0912345011',N'001200001011',N'Bình Dương',N'Quản lý mẫu','2026-10-04 11:00:00.0000000','2026-10-02 09:00:00.0000000','2026-10-04 11:00:00.0000000',30000,0,500000,960000,N'Chuyển khoản',N'Hóa đơn lưu mẫu'),
(14,26,2,N'Bạch Thảo Nhi',N'0912345040',N'001200001040',N'Đắk Nông',N'Lễ tân','2026-10-25 23:23:22.5971272','2026-10-25 23:22:25.1000000','2026-10-25 23:23:22.5766667',0,0,0,850000,N'Tiền mặt',N''),
(15,28,1,N'Bùi Thu Thảo',N'0912345005',N'001200001005',N'Cần Thơ',N'Quản lý','2026-10-26 10:42:06.0447079','2026-10-26 10:41:06.6966667','2026-10-26 10:42:06.0333333',0,0,0,1000000,N'Tiền mặt',N'');
SET IDENTITY_INSERT [Invoices] OFF;
SET @ActiveIdentity=NULL;
-- Table: InvoiceLines (33 dong)
SET IDENTITY_INSERT [InvoiceLines] ON;
SET @ActiveIdentity=N'InvoiceLines';
INSERT INTO [InvoiceLines] ([Id],[InvoiceId],[Kind],[Description],[Quantity],[Unit],[UnitPrice]) VALUES
(1,1,N'Phòng',N'Phòng 101',2,N'Đêm',450000),
(2,1,N'Dịch vụ',N'Nước suối',2,N'Chai',10000),
(3,1,N'Dịch vụ',N'Ăn sáng',2,N'Suất',70000),
(4,2,N'Phòng',N'Phòng 201',1,N'Đêm',700000),
(5,2,N'Dịch vụ',N'Giặt là',3,N'Kg',40000),
(6,2,N'Dịch vụ',N'Nước suối',1,N'Chai',10000),
(7,3,N'Phòng',N'Phòng 302',3,N'Đêm',1000000),
(8,3,N'Dịch vụ',N'Thuê xe máy',2,N'Ngày',150000),
(9,3,N'Dịch vụ',N'Ăn sáng',4,N'Suất',70000),
(10,4,N'Phòng',N'Phòng 201',2,N'Đêm',700000),
(11,4,N'Dịch vụ',N'Nước suối',4,N'Chai',10000),
(12,5,N'Phòng',N'Phòng 301',1,N'Đêm',450000),
(13,5,N'Dịch vụ',N'Ăn sáng',2,N'Suất',70000),
(14,6,N'Phòng',N'Phòng 302',3,N'Đêm',1000000),
(15,6,N'Dịch vụ',N'Thuê xe máy',2,N'Ngày',150000),
(16,7,N'Phòng',N'Phòng 103',2,N'Đêm',700000),
(17,7,N'Dịch vụ',N'Giặt là',3,N'Kg',40000),
(18,8,N'Phòng',N'Phòng 205',2,N'Đêm',1000000),
(19,8,N'Dịch vụ',N'Ăn sáng',4,N'Suất',70000),
(20,9,N'Phòng',N'Phòng 101',1,N'Đêm',450000),
(21,9,N'Dịch vụ',N'Nước suối',2,N'Chai',10000),
(22,10,N'Phòng',N'Phòng 403',2,N'Đêm',700000),
(23,10,N'Dịch vụ',N'Thuê xe máy',1,N'Ngày',150000),
(24,11,N'Phòng',N'Phòng 502',3,N'Đêm',1000000),
(25,11,N'Dịch vụ',N'Ăn sáng',3,N'Suất',70000),
(26,12,N'Phòng',N'Phòng 104',1,N'Đêm',700000),
(27,12,N'Dịch vụ',N'Giặt là',2,N'Kg',40000),
(28,13,N'Phòng',N'Phòng 402',2,N'Đêm',450000),
(29,13,N'Dịch vụ',N'Nước suối',3,N'Chai',10000),
(30,14,N'Phòng',N'Phòng 102',1,N'Đêm',700000),
(31,14,N'Dịch vụ',N'Ăn sáng',1,N'Suất',70000),
(32,14,N'Dịch vụ',N'Giặt là',2,N'Kg',40000),
(33,15,N'Phòng',N'Phòng 502',1,N'Đêm',1000000);
SET IDENTITY_INSERT [InvoiceLines] OFF;
SET @ActiveIdentity=NULL;
-- Table: AuditLog (40 dong)
SET IDENTITY_INSERT [AuditLog] ON;
SET @ActiveIdentity=N'AuditLog';
INSERT INTO [AuditLog] ([Id],[UserId],[Action],[Detail],[CreatedAt]) VALUES
(1,1,N'Hoàn tất dọn phòng',N'Phòng #2','2026-10-25 19:11:45.7311684'),
(2,1,N'Lưu danh mục',N'Rooms #2','2026-10-25 19:19:40.7497741'),
(3,1,N'Lưu danh mục',N'Rooms #39','2026-10-25 20:28:10.0251513'),
(4,1,N'Lưu danh mục',N'RoomTypes #4','2026-10-25 20:28:48.8902538'),
(5,1,N'Lưu danh mục',N'Employees #3','2026-10-25 20:35:40.9756232'),
(6,1,N'Lưu danh mục',N'Rooms #3','2026-10-25 21:01:49.0329482'),
(7,1,N'Lưu danh mục',N'Rooms #3','2026-10-25 21:02:01.4768340'),
(8,1,N'Lưu danh mục',N'Rooms #38','2026-10-25 21:02:16.6116948'),
(9,1,N'Lưu danh mục',N'Employees #1','2026-10-25 21:13:50.5632477'),
(10,1,N'Lưu danh mục',N'Employees #2','2026-10-25 21:13:55.1271507'),
(11,1,N'Xóa danh mục',N'Employees #3','2026-10-25 22:00:27.6897071'),
(12,2,N'Hoàn tất dọn phòng',N'Phòng #2','2026-10-25 22:02:49.6554428'),
(13,2,N'Hoàn tất dọn phòng',N'Phòng #2','2026-10-25 22:06:46.0606070'),
(14,1,N'Lưu danh mục',N'Employees #4','2026-10-25 22:16:03.1655962'),
(15,1,N'Lưu danh mục',N'Rooms #2','2026-10-25 22:31:00.6067721'),
(16,6,N'Hoàn tất dọn phòng',N'Phòng #2','2026-10-25 22:31:43.3879015'),
(17,1,N'Lưu danh mục',N'Rooms #2','2026-10-25 22:32:06.5762456'),
(18,2,N'Đặt phòng',N'Phiếu #26; cọc 0','2026-10-25 23:21:26.2237968'),
(19,2,N'Hoàn tất dọn phòng',N'Phòng #2','2026-10-25 23:22:09.8395617'),
(20,2,N'Sửa đặt phòng',N'Phiếu #26; cọc 0','2026-10-25 23:22:21.5715889'),
(21,2,N'Nhận phòng',N'Phiếu #26','2026-10-25 23:22:25.1194338'),
(22,2,N'Thêm dịch vụ',N'Phiếu #26; dịch vụ #29; SL 1','2026-10-25 23:22:41.1742285'),
(23,2,N'Thêm dịch vụ',N'Phiếu #26; dịch vụ #30; SL 1','2026-10-25 23:22:44.2876632'),
(24,2,N'Sửa dịch vụ',N'Chi tiết #30; SL 1','2026-10-25 23:22:45.5085778'),
(25,2,N'Sửa dịch vụ',N'Chi tiết #29; SL 1','2026-10-25 23:22:47.1027929'),
(26,2,N'Thêm dịch vụ',N'Phiếu #26; dịch vụ #31; SL 2','2026-10-25 23:22:50.5087099'),
(27,2,N'Xóa dịch vụ',N'Chi tiết #30; SL 0','2026-10-25 23:22:54.2416522'),
(28,2,N'Thanh toán và trả phòng',N'HĐ #14; tổng 850.000; thu/hoàn 850.000','2026-10-25 23:23:22.6273323'),
(29,1,N'Hoàn tất dọn phòng',N'Phòng #2','2026-10-25 23:25:21.6370498'),
(30,1,N'Xóa danh mục',N'Rooms #39','2026-10-26 08:54:50.9300335'),
(31,1,N'Lưu danh mục',N'Rooms #2','2026-10-26 10:35:11.2160300'),
(32,1,N'Lưu danh mục',N'Rooms #38','2026-10-26 10:38:27.9880922'),
(33,1,N'Lưu danh mục',N'Rooms #38','2026-10-26 10:39:15.4479908'),
(34,1,N'Đặt phòng',N'Phiếu #27; cọc 0','2026-10-26 10:39:49.0208039'),
(35,1,N'Đặt phòng',N'Phiếu #28; cọc 0','2026-10-26 10:41:02.7606790'),
(36,1,N'Nhận phòng',N'Phiếu #28','2026-10-26 10:41:06.7093844'),
(37,1,N'Thanh toán và trả phòng',N'HĐ #15; tổng 1.000.000; thu/hoàn 1.000.000','2026-10-26 10:42:06.0666116'),
(38,1,N'Sửa đặt phòng',N'Phiếu #27; cọc 0','2026-10-26 10:54:31.1392471'),
(39,1,N'Sửa đặt phòng',N'Phiếu #27; cọc 0','2026-10-26 10:54:50.4131188'),
(40,1,N'Nhận phòng',N'Phiếu #27','2026-10-26 10:54:59.9422605');
SET IDENTITY_INSERT [AuditLog] OFF;
SET @ActiveIdentity=NULL;
-- Bat lai trigger bao ve hoa don
IF OBJECT_ID('dbo.TR_Invoices_Immutable') IS NOT NULL ENABLE TRIGGER dbo.TR_Invoices_Immutable ON dbo.Invoices;
IF OBJECT_ID('dbo.TR_InvoiceLines_Immutable') IS NOT NULL ENABLE TRIGGER dbo.TR_InvoiceLines_Immutable ON dbo.InvoiceLines;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    IF @ActiveIdentity IS NOT NULL
    BEGIN
        DECLARE @IdentityOff nvarchar(300)=N'SET IDENTITY_INSERT dbo.' + QUOTENAME(@ActiveIdentity) + N' OFF;';
        EXEC(@IdentityOff);
    END;
    THROW;
END CATCH;
PRINT N'Da nap thanh cong toan bo du lieu tu may cuc bo!';
GO
