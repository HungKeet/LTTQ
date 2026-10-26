# LTTQ_QuanLyKhachSan — Quản lý Khách sạn Ngọc Khoa

Bài tập lớn môn Lập trình trực quan, sử dụng **C# Windows Forms, .NET 10 và SQL Server**. Ứng dụng có giao diện tiếng Việt, quản lý phòng, khách hàng, đặt phòng, nhận/trả phòng, dịch vụ, hóa đơn, tài khoản và doanh thu.

![Màn hình tổng quan Khách sạn Ngọc Khoa](KetQua/GiaoDien/02-ManHinhChinh-00.png)

## 1. Chuẩn bị

Chạy ứng dụng trên **Windows**. Cài các thành phần sau trước khi chạy từ mã nguồn:

| Thành phần | Mục đích |
|---|---|
| [Git for Windows](https://git-scm.com/downloads/win) | Clone repository |
| [.NET 10 SDK x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | Biên dịch và chạy ứng dụng; chọn **SDK**, không chỉ Runtime |
| [SQL Server Express](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) | Lưu dữ liệu; cấu hình mặc định dùng instance `SQLEXPRESS` |
| [Visual Studio 2026](https://visualstudio.microsoft.com/downloads/) có workload **.NET desktop development** | Tùy chọn nếu muốn xem, sửa code và chạy bằng F5; dùng bản cập nhật tương thích với SDK .NET 10 đã cài |

Có thể dùng [SQL Server LocalDB](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb) thay Express, rồi đổi chuỗi kết nối theo mục 3. SQL Server Management Studio (SSMS) là công cụ quản trị tùy chọn; ứng dụng có thể tự tạo database.

## 2. Clone mã nguồn

Mở PowerShell hoặc Terminal tại thư mục muốn lưu dự án:

```powershell
git clone https://github.com/quoctruongle766-star/LTTQ_QuanLyKhachSan.git
cd LTTQ_QuanLyKhachSan
dotnet --list-sdks
dotnet restore .\QuanLyKhachSan.sln
```

Lệnh `dotnet --list-sdks` cần hiển thị SDK **10.0.xxx** đã cài. Tệp `global.json` chọn SDK .NET 10 ổn định, từ 10.0.100 trở lên trong dòng 10.0; chạy `dotnet --version` tại thư mục dự án để kiểm tra SDK được chọn. Lần restore đầu tiên cần Internet để tải các gói NuGet.

Giao diện nhắm tới `net10.0-windows`; các thư viện và bộ kiểm thử nhắm tới `net10.0`. Dự án hiện chạy trực tiếp bằng .NET 10. Nếu dùng Visual Studio, cần Visual Studio 2026; Visual Studio 2022 không hỗ trợ nhắm tới .NET 10. Xem [yêu cầu của Microsoft](https://learn.microsoft.com/en-us/dotnet/core/install/windows#install-with-visual-studio). Có thể dùng các lệnh bên dưới mà không cài Visual Studio.

Repository chứa mã nguồn, script SQL, báo cáo và ảnh minh họa. Thư mục `BanChay` sẽ được tạo khi thực hiện bước đóng gói bên dưới.

## 3. Cấu hình SQL Server

Mở [MaNguon/GiaoDien/CauHinh.json](MaNguon/GiaoDien/CauHinh.json). Cấu hình mẫu:

```json
{
  "ConnectionString": "Server=lpc:.\\SQLEXPRESS;Database=HotelManagementBTL;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15",
  "HotelName": "KHÁCH SẠN NGỌC KHOA",
  "HotelAddress": "Địa chỉ khách sạn mẫu • Hà Nội",
  "HotelPhone": "024 0000 0000"
}
```

- **SQL Server Express trên cùng máy:** giữ `Server=lpc:.\\SQLEXPRESS` nếu máy có instance tên `SQLEXPRESS` và dịch vụ đang chạy. Nếu đặt tên khác khi cài SQL Server, thay đúng tên instance đó.
- **LocalDB:** trong JSON, đổi thành `Server=(localdb)\\MSSQLLocalDB`. Có thể kiểm tra và khởi động bằng `SqlLocalDB info` và `SqlLocalDB start MSSQLLocalDB`.
- Giữ hai dấu `\\` trong tệp JSON để biểu diễn một dấu `\` trong tên server.
- Cấu hình dùng tài khoản Windows hiện tại. Tài khoản này cần có quyền tạo database ở lần đăng nhập đầu tiên.

Tiền tố `lpc:` chỉ định kết nối Shared Memory tới SQL Server trên **cùng máy Windows**, giúp tránh lỗi dò tìm instance khi SQL Server Browser không chạy. Nếu kết nối SQL Server trên máy khác, thay toàn bộ giá trị Server bằng địa chỉ/instance phù hợp, không dùng `lpc:`. Xem [quy tắc kết nối của Microsoft](https://learn.microsoft.com/en-us/sql/tools/configuration-manager/creating-a-valid-connection-string-using-tcp-ip).

Lần đăng nhập đầu tiên, ứng dụng tự tạo database `HotelManagementBTL`, các bảng và dữ liệu mẫu nếu chưa có. Database đã có sẽ được sử dụng lại. **Clone mã nguồn không sao chép dữ liệu nghiệp vụ của máy khác.**

Nếu muốn tạo bằng SSMS, kết nối đúng instance và chạy [CoSoDuLieu/CaiDat.sql](CoSoDuLieu/CaiDat.sql). Sau đó dùng cùng instance/database trong `CauHinh.json`.

## 4. Chạy ứng dụng

### Cách A — chạy bằng lệnh

Từ thư mục `LTTQ_QuanLyKhachSan`:

```powershell
dotnet build .\QuanLyKhachSan.sln -c Release
dotnet run --project .\MaNguon\GiaoDien\GiaoDien.csproj -c Release
```

### Cách B — chạy bằng Visual Studio

1. Mở `QuanLyKhachSan.sln` bằng Visual Studio 2026 đã cập nhật.
2. Chờ Visual Studio tải xong các gói NuGet.
3. Trong Solution Explorer, nhấp phải project **GiaoDien** → **Set as Startup Project**.
4. Nhấn **F5** hoặc **Ctrl+F5**.

### Cách C — tạo bản EXE kèm .NET runtime

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CongCu\TaoBanChay.ps1
.\BanChay\QuanLyKhachSan.exe
```

Bản tạo ra dành cho Windows x64, kèm **.NET 10 và Windows Desktop Runtime 10**. Khi mang sang máy khác, chép **cả thư mục `BanChay`** và sửa `BanChay/CauHinh.json` theo SQL Server trên máy đó. Máy chạy vẫn cần SQL Server; không cần cài riêng .NET runtime cho bản này. Nếu tự tạo bản không kèm runtime, máy chạy cần **.NET Desktop Runtime 10 x64**.

Script publish giữ cấu hình `BanChay/CauHinh.json` đang có. Nếu đã publish rồi và muốn đổi kết nối cho bản EXE, sửa tệp này hoặc chọn **Xem / sửa cấu hình SQL Server** tại màn hình đăng nhập, lưu và mở lại ứng dụng.

### Cập nhật một bản clone cũ

Lưu các thay đổi mã nguồn của bạn trước, cài SDK .NET 10, đóng ứng dụng đang chạy rồi thực hiện tại thư mục dự án:

```powershell
git pull --ff-only origin main
dotnet restore .\QuanLyKhachSan.sln
dotnet build .\QuanLyKhachSan.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\CongCu\TaoBanChay.ps1
```

Sau đó mở lại `BanChay/QuanLyKhachSan.exe`. Nếu chỉ chạy bằng `dotnet run`, có thể bỏ bước publish. Bản nâng cấp .NET này không thay đổi cấu trúc database; giữ chuỗi kết nối tới database đang sử dụng.

## 5. Đăng nhập và dùng thử

| Vai trò | Tên đăng nhập | Mật khẩu mẫu |
|---|---|---|
| Quản lý | `admin` | `Admin@123` |
| Lễ tân | `letan` | `Letan@123` |

Các tài khoản trên được tạo khi khởi tạo dữ liệu mẫu. Nếu đã đổi mật khẩu thì dùng mật khẩu mới. Mật khẩu mẫu dùng cho bài tập/demo; có thể đổi bằng nút **Đổi mật khẩu**.

Quy trình thử: **Khách hàng → Đặt phòng → Nhận phòng → Thêm dịch vụ → Trả / thanh toán → Xem hóa đơn → Xác nhận đã dọn phòng**. Tài khoản quản lý có thể xem thống kê doanh thu và quản lý danh mục, nhân viên, tài khoản.

Xem [hướng dẫn thao tác và kịch bản demo](HuongDan/HuongDanSuDung.md) để làm từng bước.

## 6. Cấu trúc mã nguồn

| Thư mục / tệp | Nội dung |
|---|---|
| [MaNguon/GiaoDien](MaNguon/GiaoDien) | Các Form, sự kiện, giao diện hóa đơn và cấu hình |
| [MaNguon/NghiepVu](MaNguon/NghiepVu) | Xác thực, đặt phòng, dịch vụ, thanh toán, thống kê |
| [MaNguon/DuLieu](MaNguon/DuLieu) | ADO.NET, kết nối SQL, giao dịch và khởi tạo dữ liệu |
| [MaNguon/MoHinh](MaNguon/MoHinh) | Mô hình dùng chung và quy tắc tính tiền |
| [CoSoDuLieu](CoSoDuLieu) | Script cài đặt, tạo database và cấu trúc bảng |
| [KiemThu/KiemThuKhachSan](KiemThu/KiemThuKhachSan) | Kiểm thử tự động với xUnit và SQL Server |
| [CongCu](CongCu) | Công cụ tạo bản chạy, báo cáo và gói bàn giao |
| [TaiLieu/BaoCao_QuanLyKhachSan.docx](TaiLieu/BaoCao_QuanLyKhachSan.docx) | Báo cáo Word |
| [GioiThieu.md](GioiThieu.md) | Danh sách chức năng, quy tắc và phạm vi bài tập |
| [HuongDan/ThietKeHeThong.md](HuongDan/ThietKeHeThong.md) | Kiến trúc, quan hệ dữ liệu và thiết kế nghiệp vụ |

Tên tệp của dự án dùng tiếng Việt không dấu. `README.md`, các tệp quy ước của Git/.NET và thư viện bên ngoài giữ tên chuẩn để công cụ nhận diện.

## 7. Kiểm thử

SQL Server cần đang chạy. Mặc định bộ kiểm thử dùng `lpc:.\SQLEXPRESS` trên máy hiện tại và tạo các database riêng, không dùng database nghiệp vụ chính:

```powershell
dotnet test .\KiemThu\KiemThuKhachSan\KiemThuKhachSan.csproj -c Release --logger "trx;LogFileName=KetQuaKiemThu.trx" --results-directory .\KetQua\KiemThu
```

Nếu dùng LocalDB, đặt biến môi trường trước khi chạy kiểm thử:

```powershell
$env:HOTEL_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True'
dotnet test .\KiemThu\KiemThuKhachSan\KiemThuKhachSan.csproj -c Release
```

Các database kiểm thử được giữ để kiểm tra kết quả, có thể xóa thủ công trong SSMS khi không cần. Bộ kiểm thử hiện có **56 trường hợp**.

Bản nâng cấp đã được kiểm tra ngày **25/10/2026** bằng SDK **10.0.400**: biên dịch Release **0 lỗi, 0 cảnh báo**, **32/32 kiểm thử đạt** trên .NET 10. Bản EXE Windows x64 kèm runtime **10.0.11** đã chạy kiểm tra giao diện với 22 ảnh các màn hình, xuất hóa đơn HTML và in hóa đơn PDF. Kiểm tra gói NuGet, gồm cả phụ thuộc gián tiếp, không còn cảnh báo lỗ hổng từ nguồn NuGet tại thời điểm kiểm tra.

Bản sửa nghiệp vụ ngày **26/10/2026** đã biên dịch Release **0 lỗi, 0 cảnh báo** bằng SDK **10.0.403**, **56/56 kiểm thử đạt** trên SQL Server cục bộ với database kiểm thử riêng. Các ca mới kiểm tra lịch tương lai khi khách quá giờ trả, giờ nhận thực tế, đặt cùng ngày, đổi phòng, nhận phòng nhanh, giao dịch đồng thời, rollback và gộp dịch vụ có nhiều mức giá. Xem [hướng dẫn sử dụng](HuongDan/HuongDanSuDung.md) cho các thao tác mới. Thời gian đến/đi cùng ngày được hỗ trợ, giá hiện tại vẫn tối thiểu một đêm; chưa có bảng giá riêng theo giờ.

Để kiểm tra giao diện trên máy có SQL Server sau khi publish:

```powershell
.\BanChay\QuanLyKhachSan.exe --ui-smoke --out .\KetQua\GiaoDienKiemTra
```

Chế độ này tạo database kiểm tra riêng, lưu ảnh và `KetQua.txt`; nếu lỗi, xem `Loi.txt`. PDF được tạo khi máy có máy in **Microsoft Print to PDF**.

## 8. Lỗi thường gặp

| Hiện tượng | Cách xử lý |
|---|---|
| Không nhận lệnh `git` hoặc `dotnet` | Cài công cụ tương ứng rồi mở lại Terminal |
| `A compatible .NET SDK was not found` hoặc `NETSDK1045` | Cài SDK .NET 10 x64; kiểm tra `dotnet --list-sdks` và `dotnet --version` tại thư mục dự án |
| Visual Studio báo không hỗ trợ .NET 10 | Cập nhật Visual Studio 2026 cùng workload **.NET desktop development**, hoặc dùng lệnh `dotnet` |
| Bản không kèm runtime yêu cầu cài .NET | Cài .NET **Desktop Runtime 10 x64**, hoặc tạo lại bản kèm runtime bằng `TaoBanChay.ps1` |
| Không kết nối được SQL Server | Kiểm tra dịch vụ, tên instance và `ConnectionString`; với Express trên cùng máy, dùng `lpc:.\\SQLEXPRESS` trong JSON; cài SSMS riêng không có nghĩa là đã cài SQL Server |
| Không có quyền tạo database | Dùng tài khoản Windows có quyền phù hợp hoặc nhờ quản trị tạo database bằng script SQL |
| Sửa cấu hình nhưng bản EXE vẫn dùng kết nối cũ | Sửa đúng `BanChay/CauHinh.json` rồi đóng và mở lại bản EXE |
| Thiếu DLL khi mở EXE | Tạo lại bản chạy và giữ nguyên cả thư mục `BanChay` |
| Visual Studio không chạy đúng project | Chọn **GiaoDien** làm Startup Project |
| Báo tệp MDF đã tồn tại | Kiểm tra đúng instance đang chứa dữ liệu; không xóa tệp MDF/LDF để xử lý lỗi |

Các thư mục biên dịch, bản chạy, bản sao lưu SQL và dữ liệu trên máy cá nhân được bỏ qua bởi `.gitignore`.
