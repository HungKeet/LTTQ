using System.Data;
using Hotel.Business;
using Hotel.Core;

namespace Hotel.WinForms;

// Each field reports the same rules used by the business layer, without blocking editing.
internal sealed class CustomerFieldValidator
{
    private readonly List<(TextBox Input, Label Error, Func<string, string> Check)> fields = new();
    private readonly HashSet<TextBox> touched = new();

    public void Attach(TextBox input, Label error, Func<string, string> check)
    {
        fields.Add((input, error, check));
        input.Leave += (_, _) => { touched.Add(input); Validate(input); };
        input.TextChanged += (_, _) => { if (touched.Contains(input)) Validate(input); };
    }

    private bool Validate(TextBox input)
    {
        var field = fields.Single(f => f.Input == input);
        try { field.Check(input.Text); field.Error.Text = ""; field.Error.Visible = false; return true; }
        catch (ArgumentException ex) { field.Error.Text = ex.Message; field.Error.Visible = true; return false; }
    }

    public bool ValidateAll()
    {
        TextBox? first = null;
        foreach (var (input, _, _) in fields)
        {
            touched.Add(input);
            if (!Validate(input) && first is null) first = input;
        }
        first?.Focus();
        return first is null;
    }

    public void Clear()
    {
        touched.Clear();
        foreach (var (_, error, _) in fields) { error.Text = ""; error.Visible = false; }
    }
}

internal static class OperationalLayout
{
    // Keep a label with its input when a toolbar wraps on a smaller window.
    internal static FlowLayoutPanel Field(string text, Control input)
    {
        var group = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 12, 0),
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        var label = Ui.Label(text);
        label.Margin = new Padding(0, 9, 8, 4);
        input.Margin = new Padding(0, 5, 0, 4);
        group.Controls.Add(label);
        group.Controls.Add(input);
        return group;
    }
}

internal sealed class BookingEditor : Form
{
    private readonly HotelService service; private readonly Session user; private readonly int? id;
    private readonly bool walkIn;
    private readonly DataGridView rooms = Ui.Grid(); private readonly CustomerPicker customer;
    private readonly ComboBox rentalType = Ui.Choices("Theo ngày", "Theo giờ");
    private readonly DateTimePicker arrival = Ui.Date(DateTime.Today.AddHours(14)), departure = Ui.Date(DateTime.Today.AddDays(1).AddHours(12));
    private readonly NumericUpDown guests = Ui.Number(200, 1, 1);
    private readonly MoneyBox deposit = Ui.Money();
    private readonly TextBox notes = new() { MaxLength = 1000, Width = 380, PlaceholderText = "Yêu cầu hoặc lưu ý của khách" };
    private readonly Label totals = Ui.Label(""); private DateTime searchedArrival, searchedDeparture;
    private readonly Dictionary<int, (decimal Rate, int Capacity)> snapshots = new();
    private readonly HashSet<int> selectedRooms = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 250 };
    private readonly Button saveButton;
    private readonly CheckBox newCustomer = new() { Text = "Khách mới", AutoSize = true, Margin = new Padding(8) };
    private readonly TextBox fullName = new() { MaxLength = 100, Width = 240 }, phone = new() { MaxLength = 25, Width = 180 };
    private readonly TextBox identity = new() { MaxLength = 30, Width = 240 }, address = new() { MaxLength = 250, Width = 350 };
    private readonly CustomerFieldValidator customerValidation = new();
    private bool availabilityCurrent, refreshing, loaded, synchronizingArrival;
    public int SavedId
    {
        get; private set;
    }
    public BookingEditor(HotelService service, Session user, int? id = null, bool walkIn = false, string initialRentalType = "Theo ngày")
        : this(service, user, id, walkIn, null, initialRentalType)
    {
    }
    public BookingEditor(HotelService service, Session user, int? id, bool walkIn, int? preferredRoomId, string initialRentalType = "Theo ngày")
    {
        this.service = service;
        this.user = user;
        this.id = id;
        this.walkIn = walkIn;
        Ui.Setup(this, walkIn ? "Nhận phòng nhanh tại quầy" : id.HasValue ? $"Sửa phiếu đặt #{id}" : "Lập phiếu đặt phòng", 1100, 760);
        MinimumSize = new Size(960, 700);
        arrival.CustomFormat = departure.CustomFormat = "dd/MM/yyyy HH:mm";
        arrival.Width = departure.Width = 185;
        rentalType.SelectedItem = initialRentalType;
        if (!walkIn && !id.HasValue) rentalType.Enabled = false;
        if (walkIn)
        {
            arrival.Value = DateTime.Now;
            arrival.Enabled = false;
            if (initialRentalType == "Theo giờ")
                departure.Value = arrival.Value.AddHours(2);
        }
        else if (!id.HasValue && initialRentalType == "Theo giờ")
        {
            var now = DateTime.Now.AddMinutes(1);
            arrival.Value = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            departure.Value = arrival.Value.AddHours(2);
        }
        var b = id.HasValue ? service.Booking(user, id.Value) : null;
        customer = new CustomerPicker(service.Lookup(user, CatalogKind.Customers), b?.Field<int>("CustomerId"));
        if (b is not null)
        {
            arrival.Value = b.Field<DateTime>("Arrival");
            departure.Value = b.Field<DateTime>("Departure");
            guests.Value = b.Field<int>("Guests");
            deposit.Value = b.Field<decimal>("Deposit");
            notes.Text = b.Field<string>("Notes");
            if (b.Table.Columns.Contains("RentalType") && !b.IsNull("RentalType"))
                rentalType.SelectedItem = b.Field<string>("RentalType");
            foreach (DataRow r in service.BookingRooms(user, id!.Value).Rows)
            {
                snapshots[r.Field<int>("Id")] = (r.Field<decimal>("Giá / đêm"), r.Field<int>("Sức chứa"));
                selectedRooms.Add(r.Field<int>("Id"));
            }
        }
        else if (preferredRoomId.HasValue)
            selectedRooms.Add(preferredRoomId.Value);
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 5, Padding = new Padding(12, 12, 12, 8), BackColor = Ui.Background };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < form.RowCount; row++)
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var line1 = Ui.Toolbar();
        customer.Width = 340;
        line1.Controls.Add(OperationalLayout.Field("Tìm khách hàng *", customer));
        if (walkIn)
            line1.Controls.Add(newCustomer);
        else
            line1.Controls.Add(Ui.Button("Thêm khách", () => { using var editor = new CatalogEditor(service, user, CatalogKind.Customers); if (editor.ShowDialog(this) == DialogResult.OK) customer.Reload(service.Lookup(user, CatalogKind.Customers), editor.SavedId); }));
        var customerLine1 = Ui.Toolbar();
        customerLine1.Visible = false;
        Control CustomerInput(TextBox input, Func<string, string> check)
        {
            var wrapper = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            var error = new Label { AutoSize = true, MaximumSize = new Size(input.Width, 0), ForeColor = Color.Firebrick, Visible = false, Margin = new Padding(0, 0, 0, 4), Font = new Font("Segoe UI", 8F) };
            input.Margin = new Padding(0, 5, 0, 3);
            wrapper.Controls.Add(input);
            wrapper.Controls.Add(error);
            customerValidation.Attach(input, error, check);
            return wrapper;
        }
        customerLine1.Controls.Add(OperationalLayout.Field("Họ tên *", CustomerInput(fullName, CustomerValidation.FullName)));
        customerLine1.Controls.Add(OperationalLayout.Field("Điện thoại *", CustomerInput(phone, CustomerValidation.Phone)));
        var customerLine2 = Ui.Toolbar();
        customerLine2.Visible = false;
        customerLine2.Controls.Add(OperationalLayout.Field("Giấy tờ", CustomerInput(identity, CustomerValidation.Identity)));
        customerLine2.Controls.Add(OperationalLayout.Field("Địa chỉ", CustomerInput(address, CustomerValidation.Address)));
        newCustomer.CheckedChanged += (_, _) =>
        {
            customer.Enabled = !newCustomer.Checked;
            customerLine1.Visible = customerLine2.Visible = walkIn && newCustomer.Checked;
            if (!newCustomer.Checked) customerValidation.Clear();
        };
        if (walkIn && customer.Items.Count == 0)
            newCustomer.Checked = true;
        var line2 = Ui.Toolbar();
        rentalType.Width = 120;
        line2.Controls.Add(OperationalLayout.Field("Hình thức *", rentalType));
        line2.Controls.Add(OperationalLayout.Field(walkIn ? "Nhận ngay" : "Đến lúc *", arrival));
        line2.Controls.Add(OperationalLayout.Field("Đi lúc *", departure));
        guests.Width = 90;
        line2.Controls.Add(OperationalLayout.Field("Số khách *", guests));
        line2.Controls.Add(Ui.Button("Tải lại phòng", FindRooms));
        var line3 = Ui.Toolbar();
        line3.Controls.Add(OperationalLayout.Field("Cọc đã nhận", deposit));
        line3.Controls.Add(OperationalLayout.Field("Ghi chú", notes));
        form.Controls.Add(line1, 0, 0);
        form.Controls.Add(customerLine1, 0, 1);
        form.Controls.Add(customerLine2, 0, 2);
        form.Controls.Add(line2, 0, 3);
        form.Controls.Add(line3, 0, 4);
        var footer = Ui.Toolbar();
        footer.Dock = DockStyle.Bottom;
        footer.FlowDirection = FlowDirection.RightToLeft;
        var close = Ui.Button("Đóng", Close);
        footer.Controls.Add(close);
        CancelButton = close;
        saveButton = Ui.Button(walkIn ? "Lưu và nhận phòng ngay" : "Lưu phiếu đặt", Save, true);
        saveButton.Enabled = false;
        footer.Controls.Add(saveButton);
        var info = new Label { Dock = DockStyle.Bottom, Height = walkIn ? 80 : 60, Padding = new Padding(16, 10, 16, 8), Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(100, 116, 139), Text = "Thuê giờ: 1–2h 35%, 3–5h cộng 10%/giờ, 6–24h 100%; quá 24h: cộng từng ngày và giờ lẻ. Thuê ngày: theo đêm lịch."
            + (walkIn ? "\nThông tin khách, phiếu đặt và nhận phòng được lưu cùng một lần." : "") };
        totals.Dock = DockStyle.Bottom;
        totals.Height = 48;
        totals.AutoSize = false;
        totals.Padding = new Padding(16, 4, 16, 4);
        totals.TextAlign = ContentAlignment.MiddleLeft;
        totals.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        totals.ForeColor = Ui.Blue;
        totals.BackColor = Color.FromArgb(236, 244, 241);
        rooms.ReadOnly = false;
        rooms.CurrentCellDirtyStateChanged += (_, _) => { if (rooms.IsCurrentCellDirty) rooms.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        rooms.CellValueChanged += (_, _) => { if (!refreshing) { RememberSelection(); UpdateTotal(); } };
        rooms.DataError += (_, e) => { e.ThrowException = false; };
        Controls.Add(rooms);
        Controls.Add(form);
        Controls.Add(totals);
        Controls.Add(info);
        Controls.Add(footer);
        rentalType.SelectedIndexChanged += (_, _) =>
        {
            if (rentalType.Text == "Theo giờ")
            {
                if (walkIn || departure.Value <= arrival.Value || (departure.Value - arrival.Value).TotalHours > 24)
                    departure.Value = arrival.Value.AddHours(2);
            }
            ScheduleRefresh();
        };
        arrival.ValueChanged += (_, _) => ScheduleRefresh();
        departure.ValueChanged += (_, _) => ScheduleRefresh();
        guests.ValueChanged += (_, _) => UpdateTotal();
        deposit.ValueChanged += (_, _) => UpdateTotal();
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); FindRooms(); };
        Disposed += (_, _) => refreshTimer.Dispose();
        Shown += (_, _) => { loaded = true; FindRooms(); };
    }
    private DateTime From => arrival.Value; private DateTime To => departure.Value;
    private void RememberSelection()
    {
        if (rooms.DataSource is not DataTable table)
            return;
        selectedRooms.Clear();
        foreach (var row in table.AsEnumerable().Where(r => r.Field<bool?>("Chọn") == true))
            selectedRooms.Add(row.Field<int>("Id"));
    }
    private void ScheduleRefresh()
    {
        if (synchronizingArrival)
            return;
        availabilityCurrent = false;
        saveButton.Enabled = false;
        rooms.Enabled = false;
        totals.Text = "Đang cập nhật phòng trống theo thời gian mới…";
        refreshTimer.Stop();
        if (loaded)
            refreshTimer.Start();
    }
    private void FindRooms()
    {
        refreshTimer.Stop();
        if (walkIn)
        {
            // A walk-in starts when it is saved, even if the form has been open for a while.
            synchronizingArrival = true;
            try { arrival.Value = DateTime.Now; }
            finally { synchronizingArrival = false; }
        }
        availabilityCurrent = false;
        saveButton.Enabled = false;
        rooms.Enabled = false;
        var previous = selectedRooms.ToHashSet();
        try
        {
            if (To <= From || (To - From).TotalDays > 365 || From.Date < DateTime.Today || To <= DateTime.Now)
            {
                totals.Text = "Chọn giờ đi sau giờ đến và sau thời điểm hiện tại; thời gian ở tối đa 365 ngày.";
                return;
            }
            if (rentalType.Text == "Theo giờ" && (To - From).TotalHours > 24)
            {
                totals.Text = "Thuê theo giờ chỉ áp dụng tối đa 24 giờ. Vui lòng chọn Theo ngày nếu lưu trú dài hơn.";
                return;
            }
            var data = service.AvailableRooms(user, From, To, id ?? 0, readyOnly: walkIn);
            data.Columns.Add("Chọn", typeof(bool));
            data.Columns["Chọn"]!.SetOrdinal(0);
            foreach (DataRow r in data.Rows)
            {
                var rid = r.Field<int>("Id");
                r["Chọn"] = previous.Contains(rid);
                if (snapshots.TryGetValue(rid, out var s))
                {
                    r["Giá / đêm"] = s.Rate;
                    r["Sức chứa"] = s.Capacity;
                }
            }
            refreshing = true;
            Ui.Bind(rooms, data);
            if (rooms.Columns.Contains("Id") && rooms.Columns["Id"] is { } colId)
                colId.Visible = false;
            foreach (DataGridViewColumn col in rooms.Columns)
                col.ReadOnly = col.Name != "Chọn";
            searchedArrival = From;
            searchedDeparture = To;
            availabilityCurrent = true;
            rooms.Enabled = true;
            RememberSelection();
            UpdateTotal();
            var removed = previous.Except(selectedRooms).Count();
            if (removed > 0)
                totals.Text += $" • Đã bỏ {removed} phòng không còn trống.";
        }
        catch (Exception e) { totals.Text = "Không tải được phòng trống: " + e.Message; }
        finally { refreshing = false; }
    }
    private void UpdateTotal()
    {
        if (!availabilityCurrent || rooms.DataSource is not DataTable table)
            return;
        if (To <= DateTime.Now)
        {
            ScheduleRefresh();
            return;
        }
        var selected = table.AsEnumerable().Where(r => r.Field<bool?>("Chọn") == true).ToArray();
        var capacity = selected.Sum(r => r.Field<int>("Sức chứa"));
        var isHourly = rentalType.Text == "Theo giờ";
        decimal amount;
        string durationText;
        if (isHourly)
        {
            var hours = Money.Hours(From, To);
            amount = selected.Sum(r => Money.HourlyCharge(r.Field<decimal>("Giá / đêm"), hours));
            durationText = $"{hours} giờ";
        }
        else
        {
            var nights = Money.Nights(From, To);
            amount = selected.Sum(r => r.Field<decimal>("Giá / đêm")) * nights;
            durationText = $"{nights} đêm";
        }
        totals.Text = $"{selected.Length} phòng • Sức chứa {capacity} / {(int)guests.Value} khách • {durationText}: {Ui.Currency(amount)} • Sau cọc: {Ui.Currency(amount - deposit.Value)}";
        if (guests.Value > capacity && selected.Length > 0)
            totals.Text += " • Vượt sức chứa.";
        saveButton.Enabled = selected.Length > 0 && guests.Value <= capacity;
    }
    private void Save()
    {
        if (walkIn && newCustomer.Checked && !customerValidation.ValidateAll())
            return;
        rooms.EndEdit();
        if (To <= DateTime.Now)
        {
            FindRooms();
            throw new InvalidOperationException("Giờ đi phải sau thời điểm hiện tại. Hãy chọn lại thời gian.");
        }
        if (walkIn || !availabilityCurrent || From != searchedArrival || To != searchedDeparture)
            FindRooms();
        if (!availabilityCurrent)
            throw new InvalidOperationException("Thời gian chưa hợp lệ hoặc chưa tải được danh sách phòng trống.");
        var chosen = ((DataTable)rooms.DataSource!).AsEnumerable().Where(r => r.Field<bool?>("Chọn") == true).Select(r => r.Field<int>("Id")).ToList();
        var customerId = walkIn && newCustomer.Checked ? 0 : customer.SelectedCustomerId();
        var input = new BookingInput(customerId, From, To, (int)guests.Value, deposit.Value, notes.Text, chosen, rentalType.Text);
        Dictionary<string, object?>? details = walkIn && newCustomer.Checked
            ? new() { ["FullName"] = fullName.Text, ["Phone"] = phone.Text, ["IdentityNumber"] = identity.Text, ["Address"] = address.Text }
            : null;
        SavedId = walkIn ? service.WalkIn(user, input, details) : service.SaveBooking(user, input, id);
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
        var actions = Ui.Toolbar();
        var choice = Ui.Lookup(service.Lookup(user, CatalogKind.Services));
        var quantity = Ui.Number(10000, 1, 1);
        quantity.Width = 90;
        void Refresh() => Ui.Bind(grid, service.Usages(user, bookingId));
        bar.Controls.Add(OperationalLayout.Field("Dịch vụ", choice));
        bar.Controls.Add(OperationalLayout.Field("Số lượng", quantity));
        var editable = service.Booking(user, bookingId).Field<string>("Status") == "Đang ở";
        if (editable)
        {
            bar.Controls.Add(Ui.Button("Thêm", () => { service.AddService(user, bookingId, Ui.Value(choice), (int)quantity.Value); Refresh(); }, true));
            actions.Controls.Add(Ui.Button("Sửa số lượng", () => { service.UpdateUsage(user, Ui.SelectedId(grid), (int)quantity.Value); Refresh(); }));
            actions.Controls.Add(Ui.Button("Xóa dòng", () => { var id = Ui.SelectedId(grid); if (Ui.Confirm("Xóa dịch vụ đã chọn khỏi lượt lưu trú?")) { service.UpdateUsage(user, id, 0); Refresh(); } }));
        }
        actions.Controls.Add(Ui.Button("Tải lại", Refresh));
        grid.SelectionChanged += (_, _) => { if (grid.CurrentRow is not null && grid.Columns.Contains("Số lượng")) quantity.Value = Convert.ToDecimal(grid.CurrentRow.Cells["Số lượng"].Value); };
        var page = Ui.Page(grid, actions);
        page.Controls.Add(bar);
        Controls.Add(page);
        Refresh();
    }
}

internal sealed class DepartureForm : EditorForm
{
    public DepartureForm(HotelService service, Session user, int bookingId) : base("Đổi ngày đi dự kiến", 320)
    {
        var date = Ui.Date(service.Booking(user, bookingId).Field<DateTime>("Departure"));
        date.CustomFormat = "dd/MM/yyyy HH:mm";
        AddField("Đi lúc", date);
        AddField("Quy tắc", new Label { Text = "Có thể chọn giờ đi trong cùng ngày. Hệ thống kiểm tra lịch đặt của tất cả phòng trong phiếu.", AutoSize = false }, 80);
        SaveButton(() => { service.ChangeDeparture(user, bookingId, date.Value); DialogResult = DialogResult.OK; Close(); });
    }
}

internal sealed class ChangeRoomForm : EditorForm
{
    public ChangeRoomForm(HotelService service, Session user, int bookingId) : base($"Đổi phòng • Phiếu #{bookingId}", 450)
    {
        MinimumSize = new Size(620, 540);
        var booking = service.Booking(user, bookingId);
        if (booking.Field<string>("Status") != "Đang ở")
            throw new InvalidOperationException("Chức năng đổi phòng dành cho khách đang ở.");
        var departure = booking.Field<DateTime>("Departure");
        if (departure <= DateTime.Now)
        {
            if (booking.Field<string>("RentalType") == "Theo giờ" && booking.Field<DateTime>("ActualCheckIn").AddHours(24) <= DateTime.Now)
                throw new InvalidOperationException("Lượt thuê giờ đã quá 24 giờ. Hãy thanh toán để kết thúc lượt ở hiện tại trước khi nhận phòng mới.");
            throw new InvalidOperationException("Phiếu đã quá giờ đi dự kiến. Hãy đổi ngày đi trước khi đổi phòng.");
        }
        var booked = service.BookingRooms(user, bookingId);
        var current = new DataTable();
        current.Columns.Add("Id", typeof(int));
        current.Columns.Add("Name", typeof(string));
        foreach (DataRow row in booked.Rows)
            current.Rows.Add(row["Id"], "Phòng " + row["Số phòng"]);
        var oldRoom = Ui.Lookup(current);
        var candidates = new DataTable();
        candidates.Columns.Add("Id", typeof(int));
        candidates.Columns.Add("Name", typeof(string));
        var newRoom = Ui.Lookup(candidates);
        var status = new Label { AutoSize = false };
        var rate = new Label { AutoSize = false };
        AddField("Phòng hiện tại", oldRoom);
        AddField("Chuyển đến", newRoom, 55);
        AddField("Đơn giá", rate, 55);
        AddField("Lưu ý", new Label { Text = "Giữ đơn giá đã chốt của phòng cũ cho lượt lưu trú này. Phòng mới phải đủ sức chứa, sẵn sàng và trống đến giờ đi. Phòng cũ chuyển sang Chờ dọn.", AutoSize = false }, 90);
        AddField("Phòng trống", status, 65);
        SaveButton(() => { service.ChangeRoom(user, bookingId, Ui.Value(oldRoom), Ui.Value(newRoom)); DialogResult = DialogResult.OK; Close(); }, "Xác nhận đổi phòng");
        var save = (Button)AcceptButton!;
        save.Enabled = false;
        var bindingReady = false;
        void UpdateRate()
        {
            if (oldRoom.SelectedValue is int roomId)
            {
                var row = booked.AsEnumerable().First(r => r.Field<int>("Id") == roomId);
                rate.Text = Ui.Currency(row.Field<decimal>("Giá / đêm")) + " / đêm (giữ nguyên)";
            }
        }
        void RefreshRooms()
        {
            save.Enabled = false;
            if (oldRoom.SelectedValue is not int oldId)
            {
                status.Text = "Hãy chọn phòng hiện tại để tải phòng có thể chuyển đến.";
                return;
            }
            try
            {
                var available = service.AvailableRooms(user, DateTime.Now, departure, bookingId, readyOnly: true);
                var selected = newRoom.SelectedValue as int?;
                candidates.Clear();
                var currentIds = booked.AsEnumerable().Select(r => r.Field<int>("Id")).ToHashSet();
                var otherCapacity = booked.AsEnumerable().Where(r => r.Field<int>("Id") != oldId).Sum(r => r.Field<int>("Sức chứa"));
                var requiredCapacity = Math.Max(1, booking.Field<int>("Guests") - otherCapacity);
                foreach (DataRow row in available.Rows)
                    if (!currentIds.Contains(row.Field<int>("Id")) && row.Field<int>("Sức chứa") >= requiredCapacity)
                        candidates.Rows.Add(row["Id"], $"{row["Số phòng"]} • {row["Loại phòng"]} • {row["Sức chứa"]} khách");
                if (selected.HasValue && candidates.AsEnumerable().Any(r => r.Field<int>("Id") == selected.Value))
                    newRoom.SelectedValue = selected.Value;
                status.Text = candidates.Rows.Count > 0 ? $"Có {candidates.Rows.Count} phòng sẵn sàng. Lịch trống được kiểm tra lại khi xác nhận." : "Không có phòng phù hợp để chuyển. Có thể tải lại sau khi dọn phòng.";
                save.Enabled = candidates.Rows.Count > 0;
            }
            catch (Exception e) { status.Text = "Không tải được phòng trống: " + e.Message; }
        }
        oldRoom.SelectedValueChanged += (_, _) =>
        {
            if (bindingReady && oldRoom.SelectedValue is int)
            {
                UpdateRate();
                RefreshRooms();
            }
        };
        Actions.Controls.Add(Ui.Button("Tải lại phòng", RefreshRooms));
        Shown += (_, _) =>
        {
            bindingReady = true;
            UpdateRate();
            RefreshRooms();
        };
    }
}

internal sealed class BookingsPage : Panel
{
    private readonly DataGridView grid = Ui.Grid(); private readonly TextBox search = Ui.Search("Mã phiếu, khách, điện thoại, phòng");
    private readonly ComboBox status = Ui.Choices("Tất cả", "Đã đặt", "Đang ở", "Đã trả", "Đã hủy");
    private readonly ComboBox rental = Ui.Choices("Tất cả hình thức", "Theo ngày", "Theo giờ");
    private readonly HotelService service; private readonly Session user; private readonly AppSettings settings;
    private bool changingFilters;
    public BookingsPage(HotelService service, Session user, AppSettings settings)
    {
        this.service = service;
        this.user = user;
        this.settings = settings;
        Dock = DockStyle.Fill;
        search.Width = 280;
        var filters = Ui.Toolbar();
        filters.Controls.Add(OperationalLayout.Field("Tìm phiếu", search));
        filters.Controls.Add(OperationalLayout.Field("Trạng thái", status));
        filters.Controls.Add(OperationalLayout.Field("Hình thức", rental));
        filters.Controls.Add(Ui.Button("Tìm / tải lại", RefreshData));
        var bar = Ui.Toolbar();
        bar.Controls.Add(Ui.Button("Đặt phòng", () => { using var form = new BookingEditor(service, user); if (form.ShowDialog(this) == DialogResult.OK) ShowSaved(form.SavedId); }, true));
        bar.Controls.Add(Ui.Button("Nhận khách tại quầy", () => { using var form = new BookingEditor(service, user, walkIn: true); if (form.ShowDialog(this) == DialogResult.OK) ShowSaved(form.SavedId); }, true));
        var hint = new Label { Text = "Chọn một phiếu để xem thao tác", AutoSize = true, Margin = new Padding(12, 13, 4, 4), ForeColor = Ui.Muted };
        var edit = Ui.Button("Sửa phiếu", () => { var id = Selected(); if (service.Booking(user, id).Field<string>("Status") != "Đã đặt") throw new InvalidOperationException("Chỉ sửa phiếu chưa nhận phòng."); using var form = new BookingEditor(service, user, id); if (form.ShowDialog(this) == DialogResult.OK) ShowSaved(id); });
        var checkIn = Ui.Button("Nhận phòng", () => { var id = Selected(); if (Ui.Confirm($"Xác nhận nhận phòng cho phiếu #{id}?")) { service.CheckIn(user, id); ShowSaved(id); } }, true);
        var cancel = Ui.Button("Hủy đặt", () => { var id = Selected(); var b = service.Booking(user, id); if (b.Field<string>("Status") != "Đã đặt") throw new InvalidOperationException("Chỉ hủy được phiếu đang ở trạng thái Đã đặt."); if (Ui.Confirm($"Hủy phiếu #{id} và xác nhận đã hoàn toàn bộ tiền cọc {Ui.Currency(b.Field<decimal>("Deposit"))} cho khách?")) { service.CancelBooking(user, id); ShowSaved(id); } });
        var services = Ui.Button("Dịch vụ", () => { using var form = new ServicesForm(service, user, Selected()); form.ShowDialog(this); });
        var changeRoom = Ui.Button("Đổi phòng", () => { var id = Selected(); using var form = new ChangeRoomForm(service, user, id); if (form.ShowDialog(this) == DialogResult.OK) ShowSaved(id); });
        var changeDeparture = Ui.Button("Đổi ngày đi", () => { var id = Selected(); using var form = new DepartureForm(service, user, id); if (form.ShowDialog(this) == DialogResult.OK) ShowSaved(id); });
        var checkout = Ui.Button("Trả / thanh toán", () => { using var form = new CheckoutForm(service, user, settings, Selected()); form.ShowDialog(this); RefreshData(); }, true);
        var invoice = Ui.Button("Xem hóa đơn", () => { var id = service.InvoiceIdForBooking(user, Selected()); using var form = new InvoiceForm(service.Invoice(user, id), settings); form.ShowDialog(this); });
        var info = Ui.Button("Xem thông tin phiếu", () => { var id = Selected(); var b = service.Booking(user, id); Ui.Info($"Phiếu #{id} • {b["Status"]}\nĐến: {b.Field<DateTime>("Arrival"):dd/MM/yyyy HH:mm}\nĐi: {b.Field<DateTime>("Departure"):dd/MM/yyyy HH:mm}\nSố khách: {b["Guests"]}\nGhi chú: {b["Notes"]}"); });
        var actions = new[] { edit, checkIn, cancel, services, changeRoom, changeDeparture, checkout, invoice, info };
        bar.Controls.Add(hint);
        foreach (var button in actions) bar.Controls.Add(button);
        void UpdateActions()
        {
            var row = grid.CurrentRow;
            var state = row?.DataBoundItem is DataRowView view ? view.Row.Field<string>("Trạng thái") : null;
            hint.Visible = state is null;
            edit.Visible = checkIn.Visible = cancel.Visible = state == "Đã đặt";
            services.Visible = changeRoom.Visible = changeDeparture.Visible = checkout.Visible = state == "Đang ở";
            invoice.Visible = state == "Đã trả";
            info.Visible = state == "Đã hủy";
        }
        grid.SelectionChanged += (_, _) => UpdateActions();
        grid.CurrentCellChanged += (_, _) => UpdateActions();
        grid.DataBindingComplete += (_, _) => UpdateActions();
        UpdateActions();
        status.SelectedIndexChanged += (_, _) => { if (!changingFilters) Ui.Run(RefreshData); };
        rental.SelectedIndexChanged += (_, _) => { if (!changingFilters) Ui.Run(RefreshData); };
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ui.Run(RefreshData); } };
        var page = Ui.Page(grid, bar);
        page.Controls.Add(filters);
        Controls.Add(page);
    }
    private int Selected() => Ui.SelectedId(grid);
    private void ShowSaved(int id)
    {
        changingFilters = true;
        try
        {
            search.Clear();
            status.SelectedIndex = 0;
            rental.SelectedIndex = 0;
        }
        finally { changingFilters = false; }
        RefreshData();
        Select(id);
    }
    public void RefreshData()
    {
        Ui.Bind(grid, service.Bookings(user, search.Text, status.Text, null, rental.SelectedIndex == 0 ? "Tất cả" : rental.Text));
        grid.ClearSelection();
        grid.CurrentCell = null;
    }
    public void Select(int id)
    {
        foreach (DataGridViewRow row in grid.Rows)
        if (Convert.ToInt32(row.Cells["Id"].Value) == id)
        {
            grid.CurrentCell = row.Cells[0];
            row.Selected = true;
            return;
        }
        grid.ClearSelection();
        grid.CurrentCell = null;
        throw new InvalidOperationException($"Không tìm thấy phiếu #{id} trong danh sách. Hãy tải lại và chọn đúng phiếu trước khi thao tác.");
    }
}
