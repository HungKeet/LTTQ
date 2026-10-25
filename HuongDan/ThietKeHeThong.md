# Thiết kế hệ thống quản lý khách sạn

## Các tầng

```mermaid
flowchart LR
    UI[Windows Forms] --> BLL[NghiepVu]
    BLL --> DAL[DuLieu]
    DAL --> DB[(SQL Server)]
    UI --> Core[MoHinh]
    BLL --> Core
```

`GiaoDien` nhận dữ liệu và hiển thị thông báo; `NghiepVu` là nơi kiểm tra quyền, trạng thái và quy tắc; `DuLieu` thực thi SQL tham số. `MoHinh` giữ các record dùng chung và phép tính tiền. Giao diện được xây dựng bằng C# để dễ theo dõi; chỉnh bố cục trong các constructor, không có file Designer kéo thả.

## Quan hệ dữ liệu

```mermaid
erDiagram
    RoomTypes ||--o{ Rooms : classifies
    Customers ||--o{ Bookings : reserves
    Employees ||--o{ Bookings : creates
    Employees ||--o| Users : account
    Bookings ||--|{ BookingRooms : includes
    Rooms ||--o{ BookingRooms : reserved_in
    Bookings ||--o{ ServiceUsages : uses
    Services ||--o{ ServiceUsages : referenced_by
    Bookings ||--o| Invoices : settles
    Employees ||--o{ Invoices : issues
    Invoices ||--|{ InvoiceLines : contains
    Users ||--o{ AuditLog : records
```

Phiếu đặt và lượt lưu trú dùng chung bảng `Bookings`, phân biệt bằng trạng thái. Cách này tránh sao chép thông tin từ phiếu đặt sang bảng thuê; có cả thời gian dự kiến và thực tế. `BookingRooms` giải quyết quan hệ nhiều-nhiều giữa phòng và phiếu, lưu giá/sức chứa chốt tại thời điểm đặt.

## Trạng thái

```mermaid
stateDiagram-v2
    [*] --> DaDat
    DaDat --> DangO: Nhận phòng
    DaDat --> DaHuy: Hủy và hoàn cọc
    DangO --> DaTra: Lập hóa đơn và thanh toán
    DaHuy --> [*]
    DaTra --> [*]
```

Phòng lưu tình trạng vận hành: Sẵn sàng, Chờ dọn, Bảo trì. Trạng thái Đang ở được suy ra từ `Bookings`, không lưu lặp trong `Rooms`. Sau trả phòng, phòng chuyển Chờ dọn; lễ tân xác nhận đã dọn để nhận khách mới.

## Kiểm tra trùng lịch

Hai khoảng thời gian giao nhau nếu `existing.Arrival < requested.Departure` và `existing.Departure > requested.Arrival`. Bỏ qua phiếu đã hủy/đã trả, và bỏ qua chính phiếu đang sửa. Một lượt đang ở nhưng đã quá ngày đi dự kiến vẫn được xem là chiếm phòng cho đến khi xử lý. Khi nhận phòng, kiểm tra thêm khách thực tế còn ở và phòng đã được dọn.

Mọi thao tác ghi sử dụng giao dịch `Serializable` và `sp_getapplock` tài nguyên `HotelBTL.Write`. Kiểm tra phòng và lưu phiếu nằm trong cùng giao dịch. Khóa chung phù hợp quy mô bài tập một khách sạn; có thể tối ưu khóa theo từng phòng khi mở rộng.

## Lưu giá lịch sử và hóa đơn

- `BookingRooms.NightlyRate`: giá phòng lúc đặt, giữ nguyên khi sửa cùng phòng.
- `ServiceUsages`: tên, đơn vị, đơn giá lúc ghi nhận dịch vụ.
- `Invoices`: ảnh chụp thông tin khách, nhân viên, thời gian và tổng tiền lúc thanh toán.
- `InvoiceLines`: từng dòng phòng/dịch vụ và đơn giá chốt. Trigger từ chối UPDATE/DELETE hóa đơn và chi tiết đã phát hành.

`Checkout` đọc lại dữ liệu dưới khóa và so với bản xem trước, tránh xác nhận số tiền cũ sau khi dịch vụ thay đổi. Trong cùng giao dịch: chèn hóa đơn, chèn chi tiết, chuyển phiếu Đã trả, chuyển phòng Chờ dọn, ghi nhật ký. Nếu bất kỳ câu lệnh nào lỗi thì tất cả cùng rollback.

## Bảo vệ dữ liệu và quyền

Mật khẩu dùng PBKDF2 SHA-256, salt ngẫu nhiên 16 byte, 210.000 vòng, khóa 32 byte; so sánh thời gian cố định. Các giá trị nhập được truyền qua tham số SQL. Business kiểm tra lại tài khoản và trạng thái nhân viên ở mỗi thao tác, không chỉ ẩn nút trên giao diện. Mô hình phân quyền là phân quyền ứng dụng; người có quyền quản trị trực tiếp SQL Server vẫn có thể quản trị database.

## Kiểm thử

32 kiểm thử ở bản bàn giao bao gồm xác thực, phân quyền, dữ liệu trùng, SQL tham số, lịch phòng, sức chứa, đặt đồng thời, nhiều phòng, nhận phòng, hủy/hoàn cọc, giữ giá cũ, bảo trì, gia hạn, dịch vụ, tính tiền, cọc dư, giảm giá, bản xem trước hết hiệu lực, khóa hóa đơn, rollback và doanh thu. Các ca bổ sung tái hiện lỗi SQL 5170 do trùng đường dẫn MDF, xác nhận ứng dụng và script cài đặt giữ nguyên dữ liệu cũ, và kiểm tra ba lần khởi tạo đồng thời. Mỗi lần chạy tạo database kiểm thử riêng và giữ lại để có thể kiểm tra dữ liệu; không dùng database bài tập chính.

`TaoCoSoDuLieu.sql` được dùng chung bởi DAL và script cài đặt: khóa theo tên database trong master, kiểm tra tồn tại, rồi tạo tệp MDF/LDF với hậu tố GUID trong thư mục mặc định của SQL Server. Schema và dữ liệu mẫu cũng có khóa riêng. Không tự xóa hay gắn lại tệp cũ không thuộc database hiện tại.

## Giới hạn và mở rộng

Hiện mỗi phiếu có một khách đại diện; chưa quản lý danh sách từng người ở ghép. Phiếu nhiều phòng nhận/trả đồng thời, chưa hỗ trợ chuyển phòng trong lượt ở, trả riêng từng phòng, chia hóa đơn, công nợ, giá theo mùa/giờ, tự động tính thuế, thanh toán trực tuyến hoặc tích hợp hóa đơn điện tử. Các chức năng này là hướng phát triển, không được coi là đã triển khai.
