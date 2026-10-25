using System.Data;
using Hotel.Business;
using Hotel.Core;

namespace Hotel.WinForms;

internal sealed class BookingEditor : Form
{
    private readonly HotelService service; private readonly Session user; private readonly int? id;
    private readonly DataGridView rooms = Ui.Grid(); private readonly ComboBox customer;
    private readonly DateTimePicker arrival = Ui.Date(DateTime.Today), departure = Ui.Date(DateTime.Today.AddDays(1));
    private readonly NumericUpDown guests = Ui.Number(200, 1, 1), deposit = Ui.Number();
    private readonly TextBox notes = new() { MaxLength = 1000, Width = 550 };
    private readonly Label totals = Ui.Label(""); private DateTime searchedArrival, searchedDeparture;
    private readonly Dictionary<int, (decimal Rate, int Capacity)> snapshots = new();
    public int SavedId
    {
        get; private set;
    }
    public BookingEditor(HotelService service, Session user, int? id = null)
    {
        this.service = service;
        this.user = user;
        this.id = id;
        Ui.Setup(this, id.HasValue ? $"Sửa phiếu đặt #{id}" : "Lập phiếu đặt phòng", 1040, 720);
        MinimumSize = new Size(900, 650);
        var b = id.HasValue ? service.Booking(user, id.Value) : null;
        customer = Ui.Lookup(service.Lookup(user, CatalogKind.Customers), b?.Field<int>("CustomerId"));
        if (b is not null)
        {
            arrival.Value = b.Field<DateTime>("Arrival");
            departure.Value = b.Field<DateTime>("Departure");
            guests.Value = b.Field<int>("Guests");
            deposit.Value = b.Field<decimal>("Deposit");
            notes.Text = b.Field<string>("Notes");
            foreach (DataRow r in service.BookingRooms(user, id!.Value).Rows)
                snapshots[r.Field<int>("Id")] = (r.Field<decimal>("Giá / đêm"), r.Field<int>("Sức chứa"));
        }
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        var line1 = Ui.Toolbar();
        line1.Controls.Add(Ui.Label("Khách hàng *"));
        line1.Controls.Add(customer);
        line1.Controls.Add(Ui.Button("Thêm khách", () => { using var editor = new CatalogEditor(service, user, CatalogKind.Customers); if (editor.ShowDialog(this) == DialogResult.OK) { customer.DataSource = service.Lookup(user, CatalogKind.Customers); customer.SelectedValue = editor.SavedId; } }));
        var line2 = Ui.Toolbar();
        line2.Controls.Add(Ui.Label("Ngày đến *"));
        line2.Controls.Add(arrival);
        line2.Controls.Add(Ui.Label("Ngày đi *"));
        line2.Controls.Add(departure);
        line2.Controls.Add(Ui.Label("Số khách *"));
        guests.Width = 90;
        line2.Controls.Add(guests);
        line2.Controls.Add(Ui.Button("Tìm phòng trống", FindRooms, true));
        var line3 = Ui.Toolbar();
        line3.Controls.Add(Ui.Label("Cọc đã nhận (đ)"));
        line3.Controls.Add(deposit);
        line3.Controls.Add(Ui.Label("Ghi chú"));
        line3.Controls.Add(notes);
        form.Controls.Add(line1);
        form.Controls.Add(line2);
        form.Controls.Add(line3);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.Add(Ui.Button("Đóng", Close));
        footer.Controls.Add(Ui.Button("Lưu phiếu đặt", Save, true));
        var info = new Label { Dock = DockStyle.Bottom, Height = 65, Padding = new Padding(12), Text = "Chọn phòng bằng ô tích. Giờ dự kiến: đến 14:00, đi 12:00.\nTính tiền theo số đêm lịch thực tế, tối thiểu 1 đêm; phụ thu nhập riêng khi trả phòng." };
        totals.Dock = DockStyle.Bottom;
        totals.Height = 36;
        totals.AutoSize = false;
        totals.Padding = new Padding(12, 0, 0, 0);
        rooms.ReadOnly = false;
        rooms.CurrentCellDirtyStateChanged += (_, _) => { if (rooms.IsCurrentCellDirty) rooms.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        rooms.CellValueChanged += (_, _) => UpdateTotal();
        rooms.DataError += (_, e) => { e.ThrowException = false; };
        Controls.Add(rooms);
        Controls.Add(form);
        Controls.Add(totals);
        Controls.Add(info);
        Controls.Add(footer);
        arrival.ValueChanged += (_, _) => totals.Text = "Ngày đã thay đổi: bấm Tìm phòng trống trước khi lưu.";
        departure.ValueChanged += (_, _) => totals.Text = "Ngày đã thay đổi: bấm Tìm phòng trống trước khi lưu.";
        Shown += (_, _) => Ui.Run(FindRooms);
    }
    private DateTime From => arrival.Value.Date.AddHours(14); private DateTime To => departure.Value.Date.AddHours(12);
    private void FindRooms()
    {
        var data = service.AvailableRooms(user, From, To, id ?? 0);
        data.Columns.Add("Chọn", typeof(bool));
        data.Columns["Chọn"]!.SetOrdinal(0);
        foreach (DataRow r in data.Rows)
        {
            var rid = r.Field<int>("Id");
            r["Chọn"] = snapshots.ContainsKey(rid);
            if (snapshots.TryGetValue(rid, out var s))
            {
                r["Giá / đêm"] = s.Rate;
                r["Sức chứa"] = s.Capacity;
            }
        }
        Ui.Bind(rooms, data);
        foreach (DataGridViewColumn col in rooms.Columns)
            col.ReadOnly = col.Name != "Chọn";
        searchedArrival = From;
        searchedDeparture = To;
        UpdateTotal();
    }
    private void UpdateTotal()
    {
        if (rooms.DataSource is not DataTable table)
            return;
        var selected = table.AsEnumerable().Where(r => r.Field<bool?>("Chọn") == true).ToArray();
        var nights = Math.Max(1, (To.Date - From.Date).Days);
        totals.Text = $"Đã chọn {selected.Length} phòng • Sức chứa {selected.Sum(r => r.Field<int>("Sức chứa"))} khách • Tiền phòng dự kiến ({nights} đêm): {Ui.Currency(selected.Sum(r => r.Field<decimal>("Giá / đêm")) * nights)}";
    }
    private void Save()
    {
        rooms.EndEdit();
        if (From != searchedArrival || To != searchedDeparture)
            throw new InvalidOperationException("Hãy bấm Tìm phòng trống sau khi thay đổi ngày.");
        var chosen = ((DataTable)rooms.DataSource!).AsEnumerable().Where(r => r.Field<bool?>("Chọn") == true).Select(r => r.Field<int>("Id")).ToList();
        SavedId = service.SaveBooking(user, new BookingInput(Ui.Value(customer), From, To, (int)guests.Value, deposit.Value, notes.Text, chosen), id);
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class ServicesForm : Form
{
    public ServicesForm(HotelService service, Session user, int bookingId)
    {
        Ui.Setup(this, $"Dịch vụ • Phiếu #{bookingId}", 1050, 580);
        MinimumSize = new Size(800, 500);
        var grid = Ui.Grid();
        var bar = Ui.Toolbar();
        var choice = Ui.Lookup(service.Lookup(user, CatalogKind.Services));
        var quantity = Ui.Number(10000, 1, 1);
        quantity.Width = 90;
        void Refresh() => Ui.Bind(grid, service.Usages(user, bookingId));
        bar.Controls.Add(Ui.Label("Dịch vụ"));
        bar.Controls.Add(choice);
        bar.Controls.Add(Ui.Label("Số lượng"));
        bar.Controls.Add(quantity);
        var editable = service.Booking(user, bookingId).Field<string>("Status") == "Đang ở";
        if (editable)
        {
            bar.Controls.Add(Ui.Button("Thêm", () => { service.AddService(user, bookingId, Ui.Value(choice), (int)quantity.Value); Refresh(); }, true));
            bar.Controls.Add(Ui.Button("Sửa số lượng", () => { service.UpdateUsage(user, Ui.SelectedId(grid), (int)quantity.Value); Refresh(); }));
            bar.Controls.Add(Ui.Button("Xóa dòng", () => { var id = Ui.SelectedId(grid); if (Ui.Confirm("Xóa dịch vụ đã chọn khỏi lượt lưu trú?")) { service.UpdateUsage(user, id, 0); Refresh(); } }));
        }
        bar.Controls.Add(Ui.Button("Tải lại", Refresh));
        grid.SelectionChanged += (_, _) => { if (grid.CurrentRow is not null && grid.Columns.Contains("Số lượng")) quantity.Value = Convert.ToDecimal(grid.CurrentRow.Cells["Số lượng"].Value); };
        Controls.Add(Ui.Page(grid, bar));
        Refresh();
    }
}

internal sealed class DepartureForm : EditorForm
{
    public DepartureForm(HotelService service, Session user, int bookingId) : base("Đổi ngày đi dự kiến", 320)
    {
        var date = Ui.Date(service.Booking(user, bookingId).Field<DateTime>("Departure"));
        AddField("Ngày đi mới", date);
        AddField("Quy tắc", new Label { Text = "Trả dự kiến lúc 12:00. Hệ thống kiểm tra lịch đặt khác của tất cả phòng trong phiếu.", AutoSize = false }, 80);
        SaveButton(() => { service.ChangeDeparture(user, bookingId, date.Value.Date.AddHours(12)); DialogResult = DialogResult.OK; Close(); });
    }
}

internal sealed class BookingsPage : Panel
{
    private readonly DataGridView grid = Ui.Grid(); private readonly TextBox search = Ui.Search("Mã phiếu, khách, điện thoại, phòng");
    private readonly ComboBox status = Ui.Choices("Tất cả", "Đã đặt", "Đang ở", "Đã trả", "Đã hủy");
    private readonly HotelService service; private readonly Session user; private readonly AppSettings settings;
    public BookingsPage(HotelService service, Session user, AppSettings settings)
    {
        this.service = service;
        this.user = user;
        this.settings = settings;
        Dock = DockStyle.Fill;
        var bar = Ui.Toolbar();
        bar.Controls.Add(search);
        bar.Controls.Add(status);
        bar.Controls.Add(Ui.Button("Tìm / tải lại", RefreshData));
        bar.Controls.Add(Ui.Button("Đặt phòng", () => { using var form = new BookingEditor(service, user); if (form.ShowDialog(this) == DialogResult.OK) { RefreshData(); Select(form.SavedId); } }, true));
        bar.Controls.Add(Ui.Button("Sửa phiếu", () => { var id = Selected(); if (service.Booking(user, id).Field<string>("Status") != "Đã đặt") throw new InvalidOperationException("Chỉ sửa phiếu chưa nhận phòng. Với khách đang ở, dùng Đổi ngày đi."); using var form = new BookingEditor(service, user, id); if (form.ShowDialog(this) == DialogResult.OK) RefreshData(); }));
        bar.Controls.Add(Ui.Button("Nhận phòng", () => { var id = Selected(); if (Ui.Confirm($"Xác nhận nhận phòng cho phiếu #{id}?")) { service.CheckIn(user, id); RefreshData(); } }));
        bar.Controls.Add(Ui.Button("Dịch vụ", () => { using var form = new ServicesForm(service, user, Selected()); form.ShowDialog(this); }));
        bar.Controls.Add(Ui.Button("Đổi ngày đi", () => { var id = Selected(); if (service.Booking(user, id).Field<string>("Status") != "Đang ở") throw new InvalidOperationException("Chức năng này dành cho khách đang ở."); using var form = new DepartureForm(service, user, id); if (form.ShowDialog(this) == DialogResult.OK) RefreshData(); }));
        bar.Controls.Add(Ui.Button("Trả / thanh toán", () => { using var form = new CheckoutForm(service, user, settings, Selected()); form.ShowDialog(this); RefreshData(); }, true));
        bar.Controls.Add(Ui.Button("Hủy đặt", () => { var id = Selected(); var b = service.Booking(user, id); if (b.Field<string>("Status") != "Đã đặt") throw new InvalidOperationException("Chỉ hủy được phiếu đang ở trạng thái Đã đặt."); if (Ui.Confirm($"Hủy phiếu #{id} và xác nhận đã hoàn toàn bộ tiền cọc {Ui.Currency(b.Field<decimal>("Deposit"))} cho khách?")) { service.CancelBooking(user, id); RefreshData(); } }));
        status.SelectedIndexChanged += (_, _) => Ui.Run(RefreshData);
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ui.Run(RefreshData); } };
        Controls.Add(Ui.Page(grid, bar));
    }
    private int Selected() => Ui.SelectedId(grid);
    public void RefreshData() => Ui.Bind(grid, service.Bookings(user, search.Text, status.Text));
    public void Select(int id)
    {
        foreach (DataGridViewRow row in grid.Rows)
        if (Convert.ToInt32(row.Cells["Id"].Value) == id)
        {
            grid.CurrentCell = row.Cells[0];
            break;
        }
    }
}
