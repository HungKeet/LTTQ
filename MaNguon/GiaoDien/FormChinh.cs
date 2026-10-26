using System.Data;

using System.Text;

using Hotel.Business;

using Hotel.Core;



namespace Hotel.WinForms;



internal sealed class MainForm : Form

{

    public bool LoggedOut

    {

        get; private set;

    }

    internal readonly TabControl Tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 9), Multiline = true };

    private readonly Dictionary<TabPage, Action> refreshers = new();

    public MainForm(HotelService service, Session user, AppSettings settings)

    {

        Ui.Setup(this, "Hệ thống Quản trị khách sạn • " + settings.HotelName, 1180, 720);

        MinimumSize = new Size(1000, 640);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 76, ColumnCount = 2, BackColor = Color.White, Padding = new Padding(16, 10, 16, 10) };

        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));

        top.Controls.Add(new Label { Text = $"{settings.HotelName} — {(user.Role == "Quản lý" ? "BÀN LÀM VIỆC QUẢN LÝ" : "BÀN LÀM VIỆC")}\n{user.FullName} • Vai trò: {user.Role}", Dock = DockStyle.Fill, ForeColor = Ui.DarkText, Font = new Font("Segoe UI", 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };

        actions.Controls.Add(Ui.Button("Đổi mật khẩu", () => { using var f = new PasswordForm(service, user); f.ShowDialog(this); }));

        actions.Controls.Add(Ui.Button("Đăng xuất", () => { LoggedOut = true; Close(); }));

        top.Controls.Add(actions);

        var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(226, 232, 240) };

        var status = new Label { Dock = DockStyle.Bottom, Height = 30, Text = "  Hệ thống quản lý khách sạn • Quản lý phòng • Doanh thu (Biểu đồ) • Hóa đơn • Quản lý tài khoản", ForeColor = Color.FromArgb(100, 116, 139), BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft };

        var statusDivider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(226, 232, 240) };

        void Page(string title, Control panel, Action refresh)

        {

            var tab = new TabPage(title);

            tab.Controls.Add(panel);

            Tabs.TabPages.Add(tab);

            refreshers[tab] = refresh;

        }



        // 1. Quản lý phòng & Đặt phòng
        var dashboard = new DashboardPage(service, user, settings);
        Page("Sơ đồ phòng", dashboard, dashboard.RefreshData);

        var bookings = new BookingsPage(service, user, settings);
        Page("Quản lý đặt phòng", bookings, bookings.RefreshData);

        var roomsPage = new CatalogPage(service, user, CatalogKind.Rooms);
        Page("Quản lý phòng", roomsPage, roomsPage.RefreshData);

        var roomTypesPage = new CatalogPage(service, user, CatalogKind.RoomTypes);
        Page("Loại phòng & Giá", roomTypesPage, roomTypesPage.RefreshData);

        var customersPage = new CatalogPage(service, user, CatalogKind.Customers);
        Page("Khách hàng", customersPage, customersPage.RefreshData);

        var servicesPage = new CatalogPage(service, user, CatalogKind.Services);
        Page("Dịch vụ", servicesPage, servicesPage.RefreshData);

        // 2. Quản lý hóa đơn
        var invoices = new InvoicesPage(service, user, settings);
        Page("Quản lý hóa đơn", invoices, invoices.RefreshData);

        // 3. Quản lý doanh thu & Hệ thống (Dành cho Quản lý)
        if (user.Role == "Quản lý")
        {
            var reports = new ReportsPage(service, user);
            Page("Báo cáo doanh thu", reports, reports.RefreshData);

            var usersPage = new CatalogPage(service, user, CatalogKind.Users);
            Page("Quản lý tài khoản", usersPage, usersPage.RefreshData);

            var employeesPage = new CatalogPage(service, user, CatalogKind.Employees);
            Page("Nhân viên", employeesPage, employeesPage.RefreshData);

            var auditGrid = Ui.Grid();
            var auditBar = Ui.Toolbar();
            void RefreshAudit() => Ui.Bind(auditGrid, service.AuditHistory(user));
            auditBar.Controls.Add(Ui.Label("500 thao tác gần nhất"));
            auditBar.Controls.Add(Ui.Button("Tải lại", RefreshAudit));
            Page("Nhật ký", Ui.Page(auditGrid, auditBar), RefreshAudit);
        }



        Tabs.SelectedIndexChanged += (_, _) => Ui.Run(RefreshSelected);

        Controls.Add(Tabs);

        Controls.Add(divider);

        Controls.Add(top);

        Controls.Add(statusDivider);

        Controls.Add(status);

        Shown += (_, _) => Ui.Run(RefreshSelected);

    }

    internal void RefreshSelected()

    {

        if (Tabs.SelectedTab is { } tab)

            refreshers[tab]();

    }

}



internal sealed class PasswordForm : EditorForm

{

    public PasswordForm(HotelService service, Session user) : base("Đổi mật khẩu", 340)

    {

        var old = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };

        var next = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };

        var repeat = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };

        AddField("Mật khẩu hiện tại", old);

        AddField("Mật khẩu mới", next);

        AddField("Nhập lại", repeat);

        SaveButton(() => { if (next.Text != repeat.Text) throw new ArgumentException("Hai mật khẩu mới chưa khớp."); service.ChangePassword(user, old.Text, next.Text); Ui.Info("Đã đổi mật khẩu."); Close(); });

    }

}



internal sealed class DashboardPage : Panel
{
    private readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14), WrapContents = true };
    private readonly Button summary;
    private readonly Dictionary<string, Button> statusButtons = new();
    private readonly ComboBox floor = Ui.Choices("Tất cả"), roomType = Ui.Choices("Tất cả");
    private readonly HotelService service;
    private readonly Session user;
    private readonly AppSettings settings;
    private string selectedStatus = "Tất cả";
    private DataTable? data;
    private bool loadingFilters;

    public DashboardPage(HotelService service, Session user) : this(service, user, new AppSettings()) { }

    public DashboardPage(HotelService service, Session user, AppSettings settings)
    {
        this.service = service;
        this.user = user;
        this.settings = settings;
        Dock = DockStyle.Fill;
        var bar = Ui.Toolbar();
        bar.Controls.Add(Ui.Label("Tầng"));
        floor.Width = 100;
        bar.Controls.Add(floor);
        bar.Controls.Add(Ui.Label("Loại phòng"));
        roomType.Width = 210;
        bar.Controls.Add(roomType);
        bar.Controls.Add(Ui.Button("Tải lại phòng", RefreshData, true));
        var statistics = Ui.Toolbar();
        statistics.Dock = DockStyle.Fill;
        statistics.Padding = new Padding(8, 0, 8, 8);
        foreach (var status in new[] { "Tất cả", "Sẵn sàng", "Đang ở", "Chờ dọn", "Bảo trì" })
        {
            var button = Ui.Button(status + ": 0", () => { selectedStatus = status; RenderRooms(); });
            button.Name = "FilterStatus" + status;
            button.Tag = status;
            button.Cursor = Cursors.Hand;
            statusButtons.Add(status, button);
            statistics.Controls.Add(button);
        }
        summary = statusButtons["Tất cả"];
        var controls = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.Controls.Add(bar, 0, 0);
        controls.Controls.Add(statistics, 0, 1);
        floor.SelectedIndexChanged += (_, _) => { if (!loadingFilters) RenderRooms(); };
        roomType.SelectedIndexChanged += (_, _) => { if (!loadingFilters) RenderRooms(); };
        var help = new Label { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(14), ForeColor = Color.FromArgb(100, 116, 139), Text = "Bấm trạng thái để lọc phòng; bấm thẻ phòng để mở thao tác. Bấm Tất cả để bỏ lọc trạng thái.\nSố lượng trên các nút thống kê theo tầng và loại phòng đang chọn." };
        Controls.Add(cards);
        Controls.Add(controls);
        Controls.Add(help);
    }

    public void RefreshData()
    {
        data = service.RoomsOverview(user);
        loadingFilters = true;
        try
        {
            UpdateChoices(floor, data.AsEnumerable().Select(r => r.Field<int>("Tầng")).Distinct().Order().Select(n => n.ToString()));
            UpdateChoices(roomType, data.AsEnumerable().Select(r => r.Field<string>("Loại phòng")!).Distinct().Order());
        }
        finally
        {
            loadingFilters = false;
        }
        RenderRooms();
    }

    private static void UpdateChoices(ComboBox combo, IEnumerable<string> values)
    {
        var selected = combo.SelectedItem as string;
        combo.Items.Clear();
        combo.Items.Add("Tất cả");
        combo.Items.AddRange(values.Cast<object>().ToArray());
        combo.SelectedItem = selected is not null && combo.Items.Contains(selected) ? selected : "Tất cả";
    }

    private void RenderRooms()
    {
        if (data is null)
            return;
        var scope = data.AsEnumerable().Where(r =>
            (floor.SelectedIndex <= 0 || r.Field<int>("Tầng").ToString() == floor.Text) &&
            (roomType.SelectedIndex <= 0 || r.Field<string>("Loại phòng") == roomType.Text)).ToArray();
        var rows = scope.Where(r => selectedStatus == "Tất cả" || r.Field<string>("Trạng thái") == selectedStatus).ToArray();
        var counts = scope.GroupBy(r => r.Field<string>("Trạng thái")).ToDictionary(g => g.Key!, g => g.Count());
        foreach (var (status, button) in statusButtons)
        {
            button.Text = status == "Tất cả" ? $"Tất cả: {scope.Length} phòng" : $"{status}: {counts.GetValueOrDefault(status)}";
            var selected = selectedStatus == status;
            button.BackColor = selected ? Ui.Slate : Color.White;
            button.ForeColor = selected ? Color.White : Ui.DarkText;
            button.FlatAppearance.BorderColor = selected ? Ui.Slate : Color.FromArgb(203, 213, 225);
            button.AccessibleName = button.Text + (selected ? ", đang lọc" : ", bấm để lọc");
        }
        summary.AccessibleDescription = $"Đang hiển thị {rows.Length}/{scope.Length} phòng trong {data.Rows.Count} phòng. Bấm để xem tất cả trạng thái.";
        cards.AutoScrollPosition = Point.Empty;
        cards.SuspendLayout();
        try
        {
            while (cards.Controls.Count > 0)
            {
                var child = cards.Controls[0];
                cards.Controls.Remove(child);
                child.Dispose();
            }
            if (rows.Length == 0)
                cards.Controls.Add(new Label { AutoSize = true, Padding = new Padding(14), Text = "Không có phòng phù hợp với bộ lọc đã chọn.", ForeColor = Ui.DarkText });
            foreach (var r in rows)
            {
                var roomId = r.Field<int>("Id");
                var status = r.Field<string>("Trạng thái")!;
                var overdue = !r.IsNull("Dự kiến trả") && r.Field<DateTime>("Dự kiến trả") < DateTime.Now;
                var color = status switch
                {
                    "Đang ở" => Color.FromArgb(241, 245, 249),
                    "Chờ dọn" => Color.FromArgb(254, 249, 235),
                    "Bảo trì" => Color.FromArgb(254, 242, 242),
                    _ => Color.FromArgb(240, 253, 244)
                };
                var card = new Panel { Size = new Size(278, 248), BackColor = color, Margin = new Padding(8), Padding = new Padding(12), Cursor = Cursors.Hand, Tag = roomId, AccessibleName = $"Phòng {r["Số phòng"]}, {status}. Mở thao tác phòng." };
                var title = new Label { Text = $"Phòng {r["Số phòng"]} • {status}", Dock = DockStyle.Top, Height = 44, AutoEllipsis = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Ui.DarkText };
                var detail = new Label { Dock = DockStyle.Fill, Text = $"{r["Loại phòng"]} • Tầng {r["Tầng"]} • {r["Sức chứa"]} khách\n{Ui.Currency(r.Field<decimal>("Giá / đêm"))} / đêm\n" + (r.IsNull("Khách đang ở") ? "Phòng trống" : $"Khách: {r["Khách đang ở"]}\nTrả dự kiến: {r.Field<DateTime>("Dự kiến trả"):dd/MM HH:mm}\n" + (overdue ? "QUÁ HẠN TRẢ PHÒNG" : "")), Padding = new Padding(0, 5, 0, 0) };
                card.Controls.Add(detail);
                card.Controls.Add(title);
                void OpenRoom()
                {
                    using var form = new RoomActionsForm(service, user, settings, roomId);
                    form.ShowDialog(this);
                    RefreshData();
                }
                card.Click += (_, _) => Ui.Run(OpenRoom);
                title.Cursor = detail.Cursor = Cursors.Hand;
                title.Click += (_, _) => Ui.Run(OpenRoom);
                detail.Click += (_, _) => Ui.Run(OpenRoom);
                var button = status == "Chờ dọn"
                    ? Ui.Button("Đã dọn xong", () =>
                    {
                        if (!Ui.Confirm($"Xác nhận phòng {r["Số phòng"]} đã dọn xong và sẵn sàng đón khách?"))
                            return;
                        service.MarkRoomReady(user, roomId);
                        RefreshData();
                    })
                    : Ui.Button("Thao tác", OpenRoom);
                button.Cursor = Cursors.Hand;
                button.Dock = DockStyle.Bottom;
                card.Controls.Add(button);
                cards.Controls.Add(card);
            }
        }
        finally
        {
            cards.ResumeLayout();
        }
    }
}

internal sealed class BarChartControl : Panel

{

    private List<(string Label, decimal Value)> items = new();

    private string title = "Biểu đồ cột";

    public BarChartControl()

    {

        DoubleBuffered = true;

        Dock = DockStyle.Fill;

        BackColor = Color.White;

        Padding = new Padding(10);

    }

    public void SetData(List<(string Label, decimal Value)> data, string chartTitle)

    {

        items = data;

        title = chartTitle;

        Invalidate();

    }

    protected override void OnPaint(PaintEventArgs e)

    {

        base.OnPaint(e);

        var g = e.Graphics;

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;



        int w = ClientSize.Width, h = ClientSize.Height;

        if (w <= 0 || h <= 0) return;



        using var titleFont = new Font("Segoe UI", 11F, FontStyle.Bold);

        using var font = new Font("Segoe UI", 8.5F);

        using var valFont = new Font("Segoe UI", 8F, FontStyle.Bold);

        using var darkBrush = new SolidBrush(Ui.DarkText);

        using var grayBrush = new SolidBrush(Color.FromArgb(100, 116, 139));

        using var gridPen = new Pen(Color.FromArgb(241, 245, 249), 1);

        using var axisPen = new Pen(Color.FromArgb(203, 213, 225), 1.5f);

        using var barBrush = new SolidBrush(Color.FromArgb(51, 65, 85));

        using var barAccentBrush = new SolidBrush(Color.FromArgb(37, 99, 235));



        g.DrawString(title, titleFont, darkBrush, 16, 10);



        if (items.Count == 0)

        {

            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            g.DrawString("Không có dữ liệu trong khoảng thời gian đã chọn", font, grayBrush, new RectangleF(0, 40, w, h - 40), sfCenter);

            return;

        }



        int padLeft = 85, padRight = 25, padTop = 45, padBottom = 48;

        int plotW = w - padLeft - padRight;

        int plotH = h - padTop - padBottom;

        if (plotW <= 50 || plotH <= 50) return;



        decimal maxVal = items.Max(x => x.Value);

        if (maxVal <= 0) maxVal = 100000;



        int gridSteps = 4;

        decimal stepVal = maxVal / gridSteps;

        for (int i = 0; i <= gridSteps; i++)

        {

            float y = padTop + plotH - (float)((stepVal * i) / maxVal) * plotH;

            g.DrawLine(gridPen, padLeft, y, padLeft + plotW, y);

            decimal val = stepVal * i;

            string valText = val >= 1000000 ? $"{val / 1000000m:0.#} tr" : (val >= 1000 ? $"{val / 1000m:0} k" : val.ToString("N0"));

            var sfVal = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

            g.DrawString(valText, font, grayBrush, new RectangleF(0, y - 10, padLeft - 8, 20), sfVal);

        }



        g.DrawLine(axisPen, padLeft, padTop + plotH, padLeft + plotW, padTop + plotH);



        int count = items.Count;

        float slotW = (float)plotW / count;

        float barW = Math.Clamp(slotW * 0.58f, 16f, 48f);



        for (int i = 0; i < count; i++)

        {

            var item = items[i];

            float cx = padLeft + (i + 0.5f) * slotW;

            float barH = (float)(item.Value / maxVal) * plotH;

            if (barH < 3 && item.Value > 0) barH = 3;

            float barX = cx - barW / 2;

            float barY = padTop + plotH - barH;



            var brush = (item.Value == maxVal && count > 1) ? barAccentBrush : barBrush;

            g.FillRectangle(brush, barX, barY, barW, barH);



            if (barW >= 20)

            {

                string topText = item.Value >= 1000000 ? $"{item.Value / 1000000m:0.#}tr" : (item.Value >= 1000 ? $"{item.Value / 1000m:0}k" : item.Value.ToString("N0"));

                var sfTop = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far };

                g.DrawString(topText, valFont, darkBrush, new RectangleF(barX - 15, barY - 18, barW + 30, 16), sfTop);

            }



            var sfX = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

            g.DrawString(item.Label, font, grayBrush, new RectangleF(cx - slotW / 2, padTop + plotH + 6, slotW, 36), sfX);

        }

    }

}



internal sealed class ReportsPage : Panel

{

    private readonly DataGridView grid = Ui.Grid();

    private readonly BarChartControl chart = new();

    private readonly Label summary = new() { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12), Font = new Font("Segoe UI", 11, FontStyle.Bold) };

    private readonly DateTimePicker from = Ui.Date(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)), to = Ui.Date(DateTime.Today);

    private readonly ComboBox mode = Ui.Choices("Doanh thu theo ngày", "Doanh thu theo tháng", "Doanh thu dịch vụ");

    private readonly HotelService service; private readonly Session user;

    public ReportsPage(HotelService service, Session user)

    {

        this.service = service;

        this.user = user;

        Dock = DockStyle.Fill;

        var bar = Ui.Toolbar();

        bar.Controls.Add(Ui.Label("Từ"));

        bar.Controls.Add(from);

        bar.Controls.Add(Ui.Label("Đến"));

        bar.Controls.Add(to);

        mode.Width = 220;

        bar.Controls.Add(mode);

        bar.Controls.Add(Ui.Button("Xem thống kê", RefreshData, true));

        bar.Controls.Add(Ui.Button("Xuất Excel", () => { if (grid.DataSource is not DataTable data || data.Rows.Count == 0) throw new InvalidOperationException("Chưa có dữ liệu để xuất."); XuatExcel.PromptExport(this, data, "ThongKeDoanhThu_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx", "DoanhThu"); }));

        bar.Controls.Add(Ui.Button("Xuất CSV", () => { if (grid.DataSource is not DataTable data || data.Rows.Count == 0) throw new InvalidOperationException("Chưa có dữ liệu để xuất."); using var dialog = new SaveFileDialog { Filter = "CSV UTF-8|*.csv", FileName = "ThongKe_" + DateTime.Now.ToString("yyyyMMdd") + ".csv" }; if (dialog.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dialog.FileName, Csv(data), new UTF8Encoding(true)); Ui.Info("Đã xuất thống kê."); } }));



        var split = new SplitContainer

        {

            Dock = DockStyle.Fill,

            Orientation = Orientation.Horizontal,

            SplitterDistance = 270,

            BackColor = Color.FromArgb(226, 232, 240)

        };

        split.Panel1.Controls.Add(chart);

        split.Panel2.Controls.Add(grid);



        Controls.Add(split);

        Controls.Add(bar);

        Controls.Add(summary);

    }

    public void RefreshData()

    {

        if (to.Value.Date < from.Value.Date)

            throw new ArgumentException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");

        DataTable data;
        string column;
        if (mode.SelectedIndex == 0)
        {
            data = service.Revenue(user, from.Value, to.Value);
            column = "Doanh thu";
        }
        else if (mode.SelectedIndex == 1)
        {
            data = service.MonthlyRevenue(user, from.Value, to.Value);
            column = "Doanh thu";
        }
        else
        {
            data = service.ServiceRevenue(user, from.Value, to.Value);
            column = "Doanh thu dịch vụ";
        }

        Ui.Bind(grid, data);
        var total = data.AsEnumerable().Sum(r => r.Field<decimal>(column));
        summary.Text = $"{column}: {Ui.Currency(total)}\n" + (mode.SelectedIndex < 2 ? "Theo ngày/tháng lập hóa đơn đã thanh toán; tiền cọc đã đối trừ, không cộng thêm vào doanh thu." : "Tổng tiền dòng dịch vụ trước giảm giá/phụ thu chung của hóa đơn.");

        var chartItems = new List<(string Label, decimal Value)>();
        foreach (DataRow row in data.Rows)
        {
            string label = mode.SelectedIndex switch
            {
                0 => row.Field<DateTime>("Ngày").ToString("dd/MM"),
                1 => "Tháng " + (row.Field<string>("Tháng") ?? ""),
                _ => row.Field<string>("Dịch vụ") ?? ""
            };
            decimal val = row.Field<decimal>(column);
            chartItems.Add((label, val));
        }

        string chartTitle = mode.SelectedIndex switch
        {
            0 => "Biểu đồ cột: Doanh thu theo ngày (đ)",
            1 => "Biểu đồ cột: Doanh thu theo tháng (đ)",
            _ => "Biểu đồ cột: Doanh thu theo từng dịch vụ (đ)"
        };
        chart.SetData(chartItems, chartTitle);

    }

    internal static string Csv(DataTable data)

    {

        string Escape(object value)

        {

            var s = value is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";

            if (value is string && s.Length > 0 && "=+-@\t\r".Contains(s[0]))

                s = "'" + s;

            return "\"" + s.Replace("\"", "\"\"") + "\"";

        }

        var output = new StringBuilder();

        output.AppendLine(string.Join(',', data.Columns.Cast<DataColumn>().Select(c => Escape(c.ColumnName))));

        foreach (DataRow row in data.Rows)

            output.AppendLine(string.Join(',', row.ItemArray.Select(v => Escape(v ?? ""))));

        return output.ToString();

    }

}

