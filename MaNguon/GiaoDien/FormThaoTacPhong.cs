using System.Data;
using Hotel.Business;
using Hotel.Core;

namespace Hotel.WinForms;

internal sealed class RoomActionsForm : Form
{
    private readonly HotelService service;
    private readonly Session user;
    private readonly AppSettings settings;
    private readonly int roomId;
    private readonly Label heading = new() { Dock = DockStyle.Top, Height = 68, Font = new Font("Segoe UI", 16, FontStyle.Bold), Padding = new Padding(20, 16, 20, 12), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label details = new() { Dock = DockStyle.Top, Height = 180, Padding = new Padding(20, 14, 20, 12), BackColor = Color.White, ForeColor = Ui.DarkText };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 4, 14, 12), WrapContents = true };
    private string displayedStatus = "";
    private int? displayedBooking;

    public RoomActionsForm(HotelService service, Session user, AppSettings settings, int roomId)
    {
        this.service = service;
        this.user = user;
        this.settings = settings;
        this.roomId = roomId;
        Ui.Setup(this, "Thao tác phòng", 680, 540);
        MinimumSize = new Size(660, 520);
        var footer = Ui.Toolbar();
        footer.Dock = DockStyle.Bottom;
        footer.FlowDirection = FlowDirection.RightToLeft;
        var close = Ui.Button("Đóng", Close);
        footer.Controls.Add(close);
        footer.Controls.Add(Ui.Button("Tải lại", RefreshRoom));
        CancelButton = close;
        Controls.Add(actions);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 36, Text = "THAO TÁC VỚI PHÒNG", Padding = new Padding(20, 10, 20, 0), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(100, 116, 139) });
        Controls.Add(details);
        Controls.Add(heading);
        Controls.Add(footer);
        RefreshRoom();
    }

    private DataRow CurrentRoom() => service.RoomsOverview(user).AsEnumerable().SingleOrDefault(r => r.Field<int>("Id") == roomId)
        ?? throw new InvalidOperationException("Phòng này đã ngừng hoạt động. Hãy tải lại sơ đồ phòng.");

    private DataRow RequireCurrent(string expectedStatus)
    {
        var row = CurrentRoom();
        var booking = row.Field<int?>("Mã phiếu");
        if (row.Field<string>("Trạng thái") != expectedStatus || booking != displayedBooking)
        {
            RefreshRoom();
            throw new InvalidOperationException("Trạng thái hoặc khách đang ở đã thay đổi. Thông tin phòng đã được cập nhật; hãy chọn lại thao tác.");
        }
        return row;
    }

    private void AddAction(string name, string text, Action action, bool primary = false)
    {
        var button = Ui.Button(text, action, primary);
        button.Name = name;
        button.MinimumSize = new Size(190, 42);
        button.Margin = new Padding(6);
        actions.Controls.Add(button);
    }

    private void RefreshRoom()
    {
        var row = CurrentRoom();
        displayedStatus = row.Field<string>("Trạng thái")!;
        displayedBooking = row.Field<int?>("Mã phiếu");
        Text = "Thao tác phòng " + row["Số phòng"];
        heading.Text = $"Phòng {row["Số phòng"]} • {displayedStatus}";
        heading.BackColor = displayedStatus switch
        {
            "Sẵn sàng" => Color.FromArgb(220, 252, 231),
            "Đang ở" => Color.FromArgb(224, 242, 254),
            "Chờ dọn" => Color.FromArgb(254, 243, 199),
            _ => Color.FromArgb(254, 226, 226)
        };
        heading.ForeColor = displayedStatus switch
        {
            "Sẵn sàng" => Color.FromArgb(22, 101, 52),
            "Đang ở" => Color.FromArgb(7, 89, 133),
            "Chờ dọn" => Color.FromArgb(146, 64, 14),
            _ => Color.FromArgb(153, 27, 27)
        };
        details.Text = $"{row["Loại phòng"]} • Tầng {row["Tầng"]} • Sức chứa {row["Sức chứa"]} khách\n" +
            $"Giá danh mục: {Ui.Currency(row.Field<decimal>("Giá / đêm"))} / đêm\n\n" +
            (displayedBooking.HasValue
                ? $"Khách đang ở: {row["Khách đang ở"]}\nPhiếu lưu trú: #{displayedBooking}\nDự kiến trả: {row.Field<DateTime>("Dự kiến trả"):dd/MM/yyyy HH:mm}"
                : displayedStatus switch
                {
                    "Sẵn sàng" => "Chọn Đặt phòng hoặc Nhận phòng nhanh. Phòng này được chọn sẵn trong biểu mẫu; lịch trống sẽ được kiểm tra theo thời gian bạn nhập.",
                    "Chờ dọn" => "Phòng cần được dọn xong trước khi nhận khách mới. Xác nhận sau khi nhân viên đã hoàn tất dọn phòng.",
                    _ => "Phòng đang bảo trì và chưa thể nhận khách. Quản lý có thể cập nhật tình trạng phòng sau khi hoàn tất bảo trì."
                });
        actions.SuspendLayout();
        try
        {
            while (actions.Controls.Count > 0)
            {
                var control = actions.Controls[0];
                actions.Controls.Remove(control);
                control.Dispose();
            }
            if (displayedStatus == "Sẵn sàng")
            {
                AddAction("BookRoom", "Đặt phòng", () => OpenBooking(false), true);
                AddAction("WalkIn", "Nhận phòng nhanh", () => OpenBooking(true), true);
            }
            else if (displayedStatus == "Đang ở")
            {
                AddAction("Services", "Dịch vụ", () => OpenStay((id) => new ServicesForm(service, user, id)), true);
                AddAction("ChangeRoom", "Đổi phòng", () => OpenStay((id) => new ChangeRoomForm(service, user, id)));
                AddAction("ChangeDeparture", "Đổi ngày đi", () => OpenStay((id) => new DepartureForm(service, user, id)));
                AddAction("Checkout", "Trả / thanh toán", () => OpenStay((id) => new CheckoutForm(service, user, settings, id)), true);
            }
            else if (displayedStatus == "Chờ dọn")
            {
                AddAction("MarkReady", "Xác nhận đã dọn xong", () =>
                {
                    var current = RequireCurrent("Chờ dọn");
                    if (!Ui.Confirm($"Xác nhận phòng {current["Số phòng"]} đã dọn xong và sẵn sàng nhận khách?"))
                        return;
                    service.MarkRoomReady(user, roomId);
                    DialogResult = DialogResult.OK;
                    Close();
                }, true);
            }
            if (user.Role == "Quản lý")
                AddAction("EditRoom", "Quản lý phòng", () =>
                {
                    RequireCurrent(displayedStatus);
                    using var editor = new CatalogEditor(service, user, CatalogKind.Rooms, roomId);
                    if (editor.ShowDialog(this) == DialogResult.OK)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    else
                        RefreshRoom();
                });
        }
        finally { actions.ResumeLayout(); }
    }

    private void OpenBooking(bool walkIn)
    {
        RequireCurrent("Sẵn sàng");
        using var editor = new BookingEditor(service, user, null, walkIn, roomId);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
        else
            RefreshRoom();
    }

    private void OpenStay(Func<int, Form> createForm)
    {
        var current = RequireCurrent("Đang ở");
        var bookingId = current.Field<int?>("Mã phiếu")
            ?? throw new InvalidOperationException("Không tìm thấy phiếu đang ở cho phòng này.");
        using var form = createForm(bookingId);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
        else
            RefreshRoom();
    }
}
