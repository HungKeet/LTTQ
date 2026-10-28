# Bài tập lớn quản lý khách sạn

Ứng dụng **C# Windows Forms trên .NET 10 + SQL Server**, giao diện tiếng Việt. Bao gồm đặt nhiều phòng, nhận phòng, dịch vụ, trả phòng, hóa đơn, quản lý danh mục, phân quyền và thống kê.

Nếu tải từ GitHub lần đầu, xem [hướng dẫn clone, cài SQL Server và chạy ứng dụng](README.md). Repository cung cấp mã nguồn; dùng lệnh `dotnet publish` trong README khi cần tạo bản EXE trong thư mục `BanChay`.

## Chạy ngay

1. Mở `BanChay/QuanLyKhachSan.exe` hoặc chạy `ChayUngDung.cmd` tại thư mục dự án.
2. Cấu hình mặc định dùng database `HotelManagementBTL` trên `lpc:.\SQLEXPRESS` (SQL Server Express cùng máy). Nếu database chưa có, chương trình tự tạo và nạp danh mục mẫu. Nếu đã có, chương trình sử dụng lại dữ liệu.
3. Tài khoản quản lý: **admin / Admin@123**. Tài khoản lễ tân: **letan / Letan@123**.
4. Thay mật khẩu bằng nút **Đổi mật khẩu** khi cần. Mật khẩu mẫu chỉ dùng cho bài tập/demo.

Nếu đóng gói theo README, bản `BanChay` dành cho Windows x64 và kèm .NET runtime. Trên máy khác, sửa `BanChay/CauHinh.json` với đúng tên instance đã cài (ví dụ `.\SQLEXPRESS` hoặc `(localdb)\MSSQLLocalDB`) rồi mở lại chương trình. Tài khoản Windows cần quyền tạo database ở lần đầu. Cấu hình mã nguồn và BanChay nằm trong hai tệp `CauHinh.json` riêng.

Đã sửa lỗi `Cannot create file ... HotelManagementBTL.mdf because it already exists`: tệp dữ liệu mới có tên riêng, database có sẵn được giữ lại; quá trình khởi tạo có khóa để nhiều lần mở cùng lúc không tạo trùng. Bản sửa đã đạt **32/32 kiểm thử**, gồm tái hiện lỗi trùng tệp, giữ dữ liệu cũ và khởi tạo đồng thời.

## Mở mã nguồn

Mã nguồn nằm trong `MaNguon`, gồm `GiaoDien`, `NghiepVu`, `DuLieu` và `MoHinh`. Tên các tệp của dự án dùng tiếng Việt không dấu. Các tên theo quy ước công cụ như `Directory.Build.props`, `.editorconfig`, `.gitignore` và thư viện .NET/SQL Server giữ nguyên để công cụ nhận diện đúng.

Mở `QuanLyKhachSan.sln` bằng Visual Studio 2026 đã cập nhật, có workload **.NET desktop development** và SDK .NET 10. Chọn **GiaoDien** làm Startup Project, nhấn F5. Tệp `global.json` chọn SDK 10.0.xxx ổn định đã cài.

```powershell
dotnet build QuanLyKhachSan.sln -c Release
dotnet run --project MaNguon/GiaoDien
dotnet test KiemThu/KiemThuKhachSan -c Release
```

## Tổ chức mã nguồn

- `MaNguon/GiaoDien`: các Form, danh mục dùng chung, xem/in hóa đơn, xuất HTML/CSV.
- `MaNguon/NghiepVu`: xác thực, kiểm tra quyền, danh mục, nghiệp vụ đặt/lưu trú/thanh toán/thống kê.
- `MaNguon/DuLieu`: DAL ADO.NET, truy vấn tham số, giao dịch và tạo schema.
- `MaNguon/MoHinh`: các mô hình và quy tắc tính tiền.
- `CoSoDuLieu/CauTrucDuLieu.sql`: các bảng, khóa, ràng buộc và trigger bảo vệ hóa đơn.
- `CoSoDuLieu/CaiDat.sql`: script độc lập tạo database và dữ liệu mẫu, chạy trong SSMS.
- `KiemThu/KiemThuKhachSan`: kiểm thử đơn vị và tích hợp SQL Server; mỗi lượt tạo database kiểm thử riêng.
- `HuongDan/HuongDanSuDung.md`: cài đặt, cách dùng, kịch bản demo và giải thích code.
- `HuongDan/ThietKeHeThong.md`: quan hệ dữ liệu, thuật toán, quy tắc và phạm vi.
- `KetQua/KiemThu/KetQuaKiemThu.trx`: kết quả kiểm thử máy đọc được, được tạo khi chạy kiểm thử trên máy của bạn.

## Chức năng

| Nhóm | Đã có |
|---|---|
| Tài khoản | Đăng nhập/đăng xuất, đổi mật khẩu, quản lý tài khoản, khóa/mở, vai trò quản lý/lễ tân |
| Danh mục | Thêm, sửa, xóa bản ghi chưa dùng, ngừng hoạt động, tìm kiếm phòng/loại phòng/khách/dịch vụ/nhân viên |
| Đặt phòng | Tra cứu theo khoảng ngày, nhiều phòng trong một phiếu, kiểm tra sức chứa, chống trùng lịch, sửa phiếu, cọc, hủy và ghi nhận hoàn cọc |
| Lưu trú | Nhận phòng, nhận khách nhanh tại quầy với khách mới/có sẵn, đổi phòng giữ giá, nâng cấp phòng có phí chênh lệch, đổi ngày giờ đi, cảnh báo quá hạn |
| Dịch vụ | Thêm, sửa số lượng, xóa dòng trước thanh toán; lưu tên, đơn vị và giá lịch sử |
| Thanh toán | Tiền đêm thực tế, phí nâng phòng từng chặng, dịch vụ, phụ thu, giảm giá, đối trừ tiền cọc, thu thêm hoặc hoàn khách, phương thức thanh toán |
| Hóa đơn | Tra cứu, xem chi tiết, xuất Excel/HTML; khóa sửa/xóa sau thanh toán |
| Vận hành | Bấm thống kê để lọc trạng thái kết hợp tầng/loại phòng, bấm thẻ phòng để nhận khách ngay hoặc quản lý lưu trú; đặt trước tại Đặt phòng & lưu trú trong menu bên trái; phòng chờ dọn sau trả/đổi phòng, xác nhận đã dọn, bảo trì, lịch sử khách hàng |
| Báo cáo | Doanh thu theo ngày trong khoảng lọc, doanh thu dịch vụ, xuất CSV, nhật ký 500 thao tác gần nhất |

## Quy tắc đã chọn

- Giờ dự kiến mặc định đến 14:00, đi 12:00; có thể sửa giờ và chọn đến/đi cùng ngày khi giờ đi sau giờ đến. Nhận sớm trong cùng ngày được phép nếu phòng sẵn sàng và không xung đột.
- Đổi phòng trong lượt ở giữ giá đã chốt, tiền cọc và dịch vụ; kiểm tra sức chứa/lịch phòng mới, đánh dấu phòng cũ chờ dọn và ghi nhật ký. Phiếu quá giờ trả cần gia hạn trước khi đổi phòng.
- Nâng cấp phòng dành cho khách đang ở: chọn phòng trống có giá cao hơn giá đang áp dụng. Phí dự kiến = chênh lệch giá × số đêm còn lại; khi trả phòng, hóa đơn ghi dòng Nâng phòng theo thời gian thực tế từ lúc nâng cấp, tối thiểu một đêm. Không đổi giá các đêm trước đó, không thu tiền ở bước xác nhận nâng cấp. Nếu nâng nhiều lần, mỗi lần chỉ cộng phần tăng thêm; đổi phòng thường sau đó vẫn giữ giá đã thỏa thuận.
- Tính theo **chênh lệch ngày lịch thực tế, tối thiểu 1 đêm**; không phải đơn giá theo từng 24 giờ. Phụ thu sớm/muộn nhập thủ công sau khi thống nhất với khách.
- Một phiếu nhận/trả cùng lúc tất cả phòng đã đặt. Dịch vụ gắn với phiếu chung. Chưa có tách hóa đơn hoặc trả riêng từng phòng.
- Hủy trước nhận phòng hoàn toàn bộ tiền cọc, cần người dùng xác nhận đã hoàn. Chương trình ghi nhận thanh toán, không thực hiện chuyển tiền ngân hàng.
- Tổng tiền = tiền phòng + dịch vụ + phụ thu − giảm giá. Thu thêm = tổng tiền − cọc; số âm là tiền hoàn.
- Doanh thu lấy từ hóa đơn đã thanh toán theo ngày lập, không cộng tiền cọc lần thứ hai.
- Giao dịch và khóa ứng dụng SQL bảo vệ các thao tác ghi đồng thời. Khi lỗi, dữ liệu trở lại trước thao tác.

## Phạm vi đối chiếu yêu cầu giảng viên

Đã triển khai theo danh sách chức năng đã thống nhất trong cuộc trò chuyện. Hai tài liệu SharePoint của giảng viên hiện yêu cầu đăng nhập nên chưa được đối chiếu nội dung. Cần kiểm tra lại mẫu báo cáo, yêu cầu chi tiết form hóa đơn, công nghệ/.NET cụ thể và tiêu chí chấm khi có hai file gốc. Báo cáo để trống các thông tin cá nhân chưa được cung cấp.

## Tài liệu kỹ thuật

- [Windows Forms — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/overview/)
- [ADO.NET transactions — Microsoft Learn](https://learn.microsoft.com/en-us/sql/connect/ado-net/local-transactions?view=sql-server-ver17)
