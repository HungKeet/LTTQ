# LTTQ_QuanLyKhachSan — Quản lý Khách sạn Ngọc Khoa

Bài tập lớn Lập trình trực quan, dùng **C# Windows Forms, .NET 10 và SQL Server**. Quản lý phòng, khách hàng, đặt/nhận/trả phòng, dịch vụ, hóa đơn và doanh thu.

## Chạy dự án

Máy cần Windows, .NET 10 SDK và SQL Server Express hoặc LocalDB.

```powershell
git clone https://github.com/quoctruongle766-star/LTTQ_QuanLyKhachSan.git
cd LTTQ_QuanLyKhachSan
dotnet restore .\QuanLyKhachSan.sln
dotnet run --project .\MaNguon\GiaoDien\GiaoDien.csproj -c Release
```

Sửa `MaNguon/GiaoDien/CauHinh.json` theo SQL Server trên máy. Có thể chạy bằng `ChayUngDung.cmd`; trong Visual Studio, chọn **GiaoDien → Set as Startup Project** trước khi nhấn F5.

Tài khoản mẫu khi khởi tạo: quản lý `admin / Admin@123`, lễ tân `letan / Letan@123`.

## Chức năng

- Sơ đồ phòng có bộ lọc trạng thái, tầng và loại phòng; bấm thẻ để thao tác.
- Đặt trước tại **Đặt phòng & lưu trú**; nhận khách ngay bằng **Nhận phòng nhanh**.
- **Đổi phòng** giữ giá đã chốt. **Nâng cấp phòng** hiển thị chênh lệch giá × số đêm còn lại, rồi cộng phí riêng vào hóa đơn khi trả phòng; các đêm trước giữ giá cũ.
- Hóa đơn xuất Excel/HTML; báo cáo doanh thu theo ngày/tháng và dịch vụ.
- Giữ các cải tiến mới về màu trạng thái phòng và bảng dữ liệu cuộn mượt.

## Cơ sở dữ liệu và kiểm thử

Ứng dụng tự khởi tạo/nâng cấp cấu trúc dữ liệu khi đăng nhập. Script cài đặt ở `CoSoDuLieu/CaiDat.sql`; bộ dữ liệu có sẵn và hướng dẫn xuất/nạp được mô tả trong tài liệu sử dụng. Khi phục hồi dữ liệu có lịch sử nâng cấp, dùng bản xuất tương thích hoặc bản sao lưu đầy đủ.

```powershell
dotnet test .\KiemThu\KiemThuKhachSan\KiemThuKhachSan.csproj -c Release
```

Bộ kiểm thử dùng database riêng. Đặt biến `HOTEL_TEST_CONNECTION` nếu máy không dùng instance mặc định `SQLEXPRESS`.

## Tài liệu

- [Hướng dẫn sử dụng và cấu hình SQL](HuongDan/HuongDanSuDung.md)
- [Thiết kế hệ thống và quan hệ dữ liệu](HuongDan/ThietKeHeThong.md)
- [Phạm vi chức năng và quy tắc tính tiền](GioiThieu.md)

Các thư mục `bin/obj` là file biên dịch có thể tạo lại. Bản sao lưu, log và ảnh kiểm thử cục bộ nằm trong `KetQua` và được bỏ qua khi commit.
