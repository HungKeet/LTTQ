# Hướng dẫn chạy và bảo vệ bài tập lớn

Để clone repository và cài đặt trên máy mới, làm theo [hướng dẫn trong README](../README.md) trước. Tài liệu này hướng dẫn thao tác sau khi ứng dụng đã chạy.

## 1. Chuẩn bị

Bản chạy: Windows x64 và SQL Server LocalDB/Express; thư mục `BanChay` đã kèm .NET 10. Bản mã nguồn: thêm .NET 10 SDK x64; nếu dùng Visual Studio, cài Visual Studio 2026 đã cập nhật và workload .NET desktop development. Có thể chạy bằng lệnh `dotnet` theo README. Không cần thiết lập SQL username/password khi dùng xác thực Windows mặc định.

Chạy `ChayUngDung.cmd` hoặc `BanChay/QuanLyKhachSan.exe`. Đăng nhập bằng admin / Admin@123 hoặc letan / Letan@123. Lần đầu sẽ tạo database và dữ liệu danh mục mẫu. Các giao dịch demo không nạp vào database chính để bạn tự thực hành từ đầu.

Cấu hình mặc định hiện là `Server=lpc:.\SQLEXPRESS;Database=HotelManagementBTL` với xác thực Windows và kết nối nội bộ Shared Memory. Trên máy khác, đổi Server thành instance đã cài; tiền tố `lpc:` chỉ dùng khi SQL Server nằm trên cùng máy với ứng dụng. LocalDB vẫn được hỗ trợ, nhưng tên instance có thể trỏ đến vùng cấu hình khác nhau khi chạy qua ứng dụng đóng gói của Windows; không đổi instance nếu chưa xác định nơi chứa dữ liệu cần dùng.

Muốn tạo dữ liệu bằng SSMS: mở `CoSoDuLieu/CaiDat.sql`, kết nối đúng instance, Execute. Script chỉ tạo khi chưa tồn tại và không xóa dữ liệu. Sau đó sửa chuỗi kết nối trong `BanChay/CauHinh.json` để trỏ cùng instance/database.

## 2. Quy trình sử dụng

1. **Khách hàng → Thêm mới**: nhập họ tên, điện thoại; có thể thêm giấy tờ và địa chỉ. Nhấn Lưu. Khi nhập sai, thông báo xuất hiện và biểu mẫu giữ nguyên dữ liệu để sửa.
2. **Đặt phòng & lưu trú → Đặt phòng**: chọn khách, ngày giờ đến, ngày giờ đi, số khách. Danh sách phòng trống và tiền dự kiến tự cập nhật; tích các phòng muốn đặt, nhập cọc đã nhận, ghi chú rồi Lưu phiếu đặt. Phòng đã tích được giữ nếu vẫn còn trống. Có thể chọn đến/đi trong cùng ngày, giờ đi phải sau giờ đến. Giá vẫn tính theo số đêm lịch, tối thiểu một đêm; chưa có đơn giá riêng theo giờ.
3. **Nhận phòng**: chọn phiếu Đã đặt, bấm Nhận phòng và xác nhận. Với khách đến trực tiếp, bấm **Nhận phòng nhanh tại quầy**, chọn khách có sẵn hoặc nhập khách mới ngay trên form, chọn phòng sẵn sàng, giờ đi và cọc rồi xác nhận nhận phòng. Thông tin khách mới, phiếu và việc nhận phòng được lưu cùng một giao dịch; nếu lỗi thì không để lại dữ liệu dở dang. Không nhận phiếu tương lai hoặc đã quá giờ đi.
4. **Dịch vụ**: chọn phiếu đang ở, mở Dịch vụ, chọn dịch vụ và số lượng, bấm Thêm. Chọn dòng để Sửa số lượng hoặc Xóa dòng. Đơn giá được lưu tại thời điểm thêm.
5. **Đổi ngày đi / Đổi phòng**: dùng cho phiếu đang ở. Đổi ngày giờ đi kiểm tra lịch các phòng trong khoảng lưu trú còn lại. Đổi phòng cho chọn phòng hiện tại và phòng mới đang sẵn sàng, đủ tổng sức chứa, không trùng lịch; giữ nguyên giá đã chốt của phòng cũ, cọc và dịch vụ. Phòng cũ chuyển Chờ dọn, nhật ký ghi lại phòng cũ/phòng mới. Với phiếu quá giờ trả, cần gia hạn ngày giờ đi trước khi đổi phòng.
6. **Trả / thanh toán**: kiểm tra chi tiết, nhập phụ thu hoặc giảm giá; số tiền tự cập nhật. Giảm giá vượt tổng tiền sẽ báo ngay trên form và khóa xác nhận đến khi sửa hợp lệ. Dịch vụ lặp lại được gộp theo dịch vụ và giá/tên/đơn vị đã chốt; các mức giá khác nhau vẫn tách riêng. Xác nhận sau khi thực tế đã thu thêm hoặc hoàn khách đúng số tiền. Chương trình ghi nhận, không tự chuyển tiền.
7. **Hóa đơn**: sau thanh toán, xem/in hóa đơn. Chọn In / lưu PDF và máy in Microsoft Print to PDF để lưu PDF nếu máy hỗ trợ. Xuất HTML để có tệp mở bằng trình duyệt. Tra cứu lại hóa đơn theo ngày, mã, tên hoặc điện thoại.
8. **Tổng quan phòng**: chọn Tầng và Loại phòng, rồi bấm trực tiếp các nút **Sẵn sàng / Đang ở / Chờ dọn / Bảo trì** để lọc; bấm **Tất cả** để bỏ lọc trạng thái. Các con số thống kê theo tầng và loại phòng đang chọn. Bộ lọc được giữ khi tải lại. Bấm vào nền, tên hoặc thông tin trên thẻ phòng (hoặc nút **Thao tác**) để mở các chức năng tương ứng: phòng sẵn sàng có Đặt phòng / Nhận phòng nhanh và được chọn sẵn; phòng đang ở có Dịch vụ / Đổi phòng / Đổi ngày đi / Trả và thanh toán; phòng chờ dọn có xác nhận đã dọn; quản lý có thể mở thông tin phòng để cập nhật bảo trì. Bấm **Đã dọn xong** sau khi nhân viên hoàn tất dọn phòng để chuyển phòng trở lại Sẵn sàng.
9. **Hủy đặt**: chỉ cho phiếu Đã đặt; xác nhận đã hoàn toàn bộ tiền cọc. Không hủy trực tiếp phiếu đang ở/đã trả.
10. **Thống kê**: chọn thời gian, loại báo cáo, Xem thống kê; có thể xuất CSV UTF-8. Tiền cọc là khoản đã thu trước, không cộng thêm lần nữa vào doanh thu.

## 3. Demo khoảng 10 phút

- Đăng nhập quản lý, giới thiệu các tab và sơ đồ phòng.
- Mở Loại phòng, Phòng, Khách hàng để trình bày thêm/sửa/tìm kiếm.
- Chọn khách mẫu, đặt phòng 101 từ hôm nay đến ngày mai, 2 khách, cọc 300.000 đ. Giá mẫu phòng 101 là 450.000 đ/đêm.
- Thử đặt lại cùng phòng cùng thời gian: phòng không còn trong danh sách trống. Giải thích kiểm tra lại ở tầng nghiệp vụ.
- Nhận phòng 101, thêm 2 chai nước suối × 10.000 đ.
- Trả phòng trong cùng ngày: tối thiểu một đêm, tổng 470.000 đ, còn thu 170.000 đ. Không phụ thu/giảm giá trong ví dụ này.
- Xem hóa đơn, xuất HTML hoặc xem bản in. Mở lại Hóa đơn để tra cứu.
- Phòng 101 chuyển Chờ dọn, bấm Đã dọn xong.
- Xem doanh thu 470.000 đ; giải thích vì sao không cộng thêm cọc 300.000 đ.
- Đăng xuất, đăng nhập letan / Letan@123; chỉ ra các tab quản trị và thống kê bị giới hạn.

Để demo hoàn cọc, dùng một phiếu khác có tiền cọc lớn hơn tổng tiền. Để demo hủy, tạo phiếu ngày mai rồi hủy và kiểm tra cọc đã hoàn. Nên tạo mỗi kịch bản trên một phiếu riêng.

## 4. Các tệp nên hiểu khi bảo vệ

| Nội dung | Tệp |
|---|---|
| Kết nối, tham số, giao dịch, khóa ứng dụng | `MaNguon/DuLieu/KetNoiDuLieu.cs` |
| Đăng nhập, mật khẩu, phân quyền, danh mục | `MaNguon/NghiepVu/XuLyKhachSan.cs` |
| Kiểm tra trùng lịch, lưu phiếu, nhận phòng, dịch vụ | `MaNguon/NghiepVu/XuLyDatPhong.cs` |
| Tính tiền, lưu hóa đơn, rollback, báo cáo | `MaNguon/NghiepVu/XuLyHoaDon.cs` |
| Quy tắc số đêm và mô hình dữ liệu | `MaNguon/MoHinh/MoHinhDuLieu.cs` |
| Sự kiện và điều hướng màn hình chính | `MaNguon/GiaoDien/FormChinh.cs` |
| Biểu mẫu dùng chung cho các danh mục | `MaNguon/GiaoDien/FormDanhMuc.cs` |
| Đặt phòng và dịch vụ | `MaNguon/GiaoDien/FormDatPhong.cs` |
| Hóa đơn và in/xuất | `MaNguon/GiaoDien/FormHoaDon.cs` |

## 5. Câu hỏi thường gặp khi bảo vệ

**Tại sao có BookingRooms?** Một phiếu có nhiều phòng và mỗi phòng có nhiều phiếu qua các thời điểm. Bảng chi tiết tách quan hệ nhiều-nhiều, đồng thời lưu giá/sức chứa đã chốt.

**Tại sao không lưu Đang ở trong Rooms?** Trạng thái có khách được suy ra từ phiếu đang ở, tránh cập nhật hai nơi không đồng bộ. Rooms chỉ lưu tình trạng vận hành.

**Vì sao đổi giá dịch vụ không đổi hóa đơn cũ?** ServiceUsages lưu đơn giá sử dụng; InvoiceLines tiếp tục lưu ảnh chụp khi thanh toán. Không tính lại hóa đơn cũ từ bảng giá hiện tại.

**Hai lễ tân đặt cùng lúc xử lý thế nào?** Kiểm tra và ghi trong một transaction Serializable, có sp_getapplock dùng chung. Sau khi người thứ nhất commit, người thứ hai kiểm tra lại và bị từ chối nếu trùng.

**Nếu mất kết nối khi thanh toán?** Các cập nhật cùng transaction, chưa commit thì rollback. Nếu kết nối mất sau commit và không biết kết quả, tra cứu hóa đơn theo phiếu; ràng buộc UNIQUE BookingId ngăn lập trùng.

**Tại sao dùng tiền cọc riêng?** Đó là tiền đã thu trước. Khoản còn phải thu bằng tổng hóa đơn trừ cọc. Doanh thu ghi nhận tổng hóa đơn lúc hoàn tất.

**Phân quyền có chỉ ẩn nút không?** Không. NghiepVu đọc lại tài khoản/nhân viên và quyền ở mỗi lệnh. Quản trị SQL trực tiếp nằm ngoài cơ chế quyền ứng dụng.

## 6. Khắc phục lỗi

- Không kết nối SQL Server: kiểm tra `SqlLocalDB info`, instance đã cài và chuỗi kết nối. Có thể dùng `.\SQLEXPRESS` / `.\KHOA` đã có trên máy. Cấu hình của mã nguồn và BanChay là hai tệp khác nhau.
- Không tạo được database: dùng tài khoản Windows có quyền tạo, hoặc nhờ quản trị tạo database bằng script rồi cấp quyền phù hợp.
- Báo tệp `.mdf` đã tồn tại: dùng bản chạy mới. Ứng dụng và `CaiDat.sql` đã dùng tên tệp vật lý riêng khi tạo database, đồng thời khóa khởi tạo. Nếu có dữ liệu cũ nhưng database không xuất hiện trong instance đang chọn, cần kết nối lại đúng instance hoặc sao lưu/khôi phục dữ liệu. Không xóa tệp MDF/LDF để xử lý lỗi; ứng dụng không tự gắn tệp cũ không rõ nguồn gốc.
- Dữ liệu nghiệp vụ nằm trong SQL Server, độc lập với mã nguồn Git. Clone về máy mới sẽ tạo dữ liệu mẫu khi đăng nhập lần đầu; muốn chuyển dữ liệu đang dùng cần sao lưu/khôi phục database riêng.
- Phòng không nhận được: xem có Chờ dọn/Bảo trì, khách cũ chưa trả hoặc phiếu đã quá hạn hay không.
- Không xóa được danh mục: dữ liệu đã được sử dụng; bỏ chọn Hoạt động nếu quy tắc cho phép.
- Mở bằng Visual Studio nhưng chạy sai project: đặt GiaoDien làm Startup Project.
- In PDF: cần máy in Microsoft Print to PDF; nếu không có, xuất HTML và dùng chức năng in của trình duyệt.

## 7. Chạy lại kiểm thử và đóng gói

`dotnet test KiemThu/KiemThuKhachSan -c Release --logger "trx;LogFileName=KetQuaKiemThu.trx" --results-directory KetQua/KiemThu`

Kiểm thử không dùng database chính; tên test có tiền tố `HotelManagementBTL_Test_`, `InstallTest_`, `Collision_` hoặc `Concurrent_` sau `HotelManagementBTL_`. Các database này được giữ để kiểm tra, có thể xóa thủ công trong SSMS sau khi không cần. Mặc định kiểm thử dùng `lpc:.\SQLEXPRESS`; đặt biến môi trường `HOTEL_TEST_CONNECTION` để dùng instance khác. Mã chụp giao diện `--ui-smoke --out <thư_mục>` cũng dùng database riêng `HotelManagementBTL_UiTest_`.

`powershell -File CongCu/TaoBanChay.ps1`

Lệnh publish giữ cấu hình đang có của BanChay và tạo bản Windows x64 kèm runtime. Để nộp, gửi mã nguồn, database, báo cáo và hướng dẫn; loại thư mục bin/obj để giảm dung lượng. Điền thông tin sinh viên, lớp, giảng viên trên trang bìa báo cáo trước khi nộp.
