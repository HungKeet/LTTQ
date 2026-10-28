using System.Data;
using Hotel.Business;
using Hotel.Core;

namespace Hotel.WinForms;

internal sealed class UpgradeRoomForm : EditorForm
{
    private readonly HotelService service;
    private readonly Session user;
    private readonly int bookingId, roomId;
    private readonly ComboBox newRoom;
    private readonly Label currentRoom = new() { AutoSize = false, UseMnemonic = false };
    private readonly Label newRate = new() { AutoSize = false, UseMnemonic = false };
    private readonly Label calculation = new() { AutoSize = false, UseMnemonic = false };
    private readonly Label fee = new() { AutoSize = false, UseMnemonic = false };
    private readonly Label status = new() { AutoSize = false, UseMnemonic = false };
    private readonly Button confirm;
    private RoomUpgradeQuote? quote;
    private bool loaded, binding;

    public UpgradeRoomForm(HotelService service, Session user, int bookingId, int roomId)
        : base($"Nâng cấp phòng • Phiếu #{bookingId}", 590)
    {
        this.service = service;
        this.user = user;
        this.bookingId = bookingId;
        this.roomId = roomId;
        Ui.Setup(this, $"Nâng cấp phòng • Phiếu #{bookingId}", 820, 650);
        MinimumSize = new Size(780, 680);
        SetHeading("Nâng cấp phòng", "Chọn phòng giá cao hơn cho thời gian lưu trú còn lại.");

        var choices = new DataTable();
        choices.Columns.Add("Id", typeof(int));
        choices.Columns.Add("Name", typeof(string));
        newRoom = Ui.Lookup(choices);
        newRoom.Name = "UpgradeTargetRoom";

        AddField("Phòng đang ở", currentRoom, 64);
        AddField("Nâng cấp đến", newRoom, 56);
        AddField("Giá phòng mới", newRate, 42);
        AddField("Cách tính phí", calculation, 82);
        AddField("Phí nâng cấp dự kiến", fee, 56);
        AddField("Thanh toán", new Label
        {
            Text = "Phí = chênh lệch giá × số đêm còn lại, tối thiểu 1 đêm kể cả nâng cấp trong ngày.\nPhí được cộng tự động vào hóa đơn khi trả phòng, tính theo thời gian thực tế; chưa thu tiền ở bước này.",
            AutoSize = false,
            UseMnemonic = false
        }, 82);
        AddField("Trạng thái", status, 72);
        fee.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        fee.ForeColor = Ui.Slate;

        SaveButton(Upgrade, "Xác nhận nâng cấp");
        confirm = (Button)AcceptButton!;
        confirm.Name = "ConfirmUpgrade";
        confirm.Enabled = false;
        Actions.Controls.Add(Ui.Button("Tải lại phòng", RefreshRooms));
        newRoom.SelectedValueChanged += (_, _) => { if (loaded && !binding) Preview(); };
        Shown += (_, _) => { loaded = true; RefreshRooms(); };
        ClearPreview();
    }

    private void ClearPreview()
    {
        quote = null;
        confirm.Enabled = false;
        newRate.Text = "—";
        calculation.Text = "Chọn phòng mới để xem chênh lệch và số đêm còn lại.";
        fee.Text = "—";
    }

    private void RefreshRooms()
    {
        ClearPreview();
        if (!loaded)
            return;
        binding = true;
        try
        {
            var booking = service.Booking(user, bookingId);
            if (booking.Field<string>("Status") != "Đang ở")
                throw new InvalidOperationException("Chỉ nâng cấp phòng cho khách đang ở.");
            var departure = booking.Field<DateTime>("Departure");
            var now = DateTime.Now;
            if (departure <= now)
                throw new InvalidOperationException("Phiếu đã quá giờ trả dự kiến. Hãy đổi ngày đi trước khi nâng cấp phòng.");
            var booked = service.BookingRooms(user, bookingId);
            var source = booked.AsEnumerable().SingleOrDefault(row => row.Field<int>("Id") == roomId)
                ?? throw new InvalidOperationException("Khách đã chuyển khỏi phòng này. Hãy đóng và tải lại sơ đồ phòng.");
            var currentPrice = source.Field<decimal>("Giá / đêm");
            currentRoom.Text = $"Phòng {source["Số phòng"]}\nGiá đang áp dụng: {Ui.Currency(currentPrice)} / đêm";
            var otherCapacity = booked.AsEnumerable().Where(row => row.Field<int>("Id") != roomId)
                .Sum(row => row.Field<int>("Sức chứa"));
            var bookedIds = booked.AsEnumerable().Select(row => row.Field<int>("Id")).ToHashSet();
            var selected = newRoom.SelectedValue as int?;
            var available = service.AvailableRooms(user, now, departure, bookingId, readyOnly: true);
            var choices = new DataTable();
            choices.Columns.Add("Id", typeof(int));
            choices.Columns.Add("Name", typeof(string));
            foreach (DataRow row in available.Rows)
            {
                if (bookedIds.Contains(row.Field<int>("Id")) || row.Field<decimal>("Giá / đêm") <= currentPrice ||
                    otherCapacity + row.Field<int>("Sức chứa") < booking.Field<int>("Guests"))
                    continue;
                choices.Rows.Add(row["Id"], $"Phòng {row["Số phòng"]} • {row["Loại phòng"]} • {row["Sức chứa"]} khách");
            }
            newRoom.DataSource = choices;
            if (selected.HasValue && choices.AsEnumerable().Any(row => row.Field<int>("Id") == selected.Value))
                newRoom.SelectedValue = selected.Value;
            status.ForeColor = Ui.Muted;
            status.Text = choices.Rows.Count > 0
                ? $"Có {choices.Rows.Count} phòng giá cao hơn, đủ sức chứa và trống đến giờ đi."
                : "Không có phòng phù hợp để nâng cấp trong thời gian còn lại. Có thể tải lại sau khi có phòng sẵn sàng.";
            if (choices.Rows.Count == 0)
                return;
        }
        catch (Exception exception)
        {
            newRoom.DataSource = null;
            status.ForeColor = Color.Firebrick;
            status.Text = exception.Message;
            return;
        }
        finally { binding = false; }
        Preview();
    }

    private void Preview()
    {
        ClearPreview();
        if (newRoom.SelectedValue is not int target)
            return;
        try
        {
            quote = service.PreviewRoomUpgrade(user, bookingId, roomId, target);
            currentRoom.Text = $"Phòng {quote.OldRoomNumber}\nGiá đang áp dụng: {Ui.Currency(quote.OldRate)} / đêm";
            newRate.Text = Ui.Currency(quote.NewRate) + " / đêm";
            calculation.Text = $"{Ui.Currency(quote.Difference)} / đêm × {quote.Nights} đêm còn lại\n" +
                $"Theo ngày đi dự kiến: {quote.Departure:dd/MM/yyyy HH:mm}\nCác đêm trước khi nâng cấp vẫn giữ giá cũ.";
            fee.Text = Ui.Currency(quote.Fee);
            status.ForeColor = Ui.Muted;
            status.Text = "Đã kiểm tra lịch phòng và phí dự kiến. Phòng cũ sẽ chuyển sang Chờ dọn sau khi nâng cấp.";
            confirm.Enabled = true;
        }
        catch (Exception exception)
        {
            status.ForeColor = Color.Firebrick;
            status.Text = exception.Message + "\nHãy tải lại phòng nếu lịch hoặc giá vừa thay đổi.";
        }
    }

    private void Upgrade()
    {
        if (quote is null)
            throw new InvalidOperationException("Hãy chọn phòng phù hợp và kiểm tra phí nâng cấp trước.");
        confirm.Enabled = false;
        try
        {
            service.UpgradeRoom(user, quote);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            RefreshRooms();
            status.ForeColor = Color.Firebrick;
            status.Text = exception.Message + "\nThông tin đã được tải lại. Hãy kiểm tra phí trước khi xác nhận lại.";
        }
        catch
        {
            confirm.Enabled = quote is not null;
            throw;
        }
    }
}
