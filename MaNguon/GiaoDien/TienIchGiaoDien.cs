using System.Data;
using Microsoft.Data.SqlClient;

namespace Hotel.WinForms;

internal static class Ui
{
    public static readonly Color DarkText = Color.FromArgb(35, 49, 59), Slate = Color.FromArgb(45, 95, 91), Background = Color.FromArgb(244, 247, 248);
    public static readonly Color Navy = DarkText, Blue = Slate, Border = Color.FromArgb(221, 229, 232), Muted = Color.FromArgb(105, 120, 128), SoftAccent = Color.FromArgb(230, 241, 238);
    public static void Setup(Form form, string title, int width = 1100, int height = 720)
    {
        form.Text = title;
        form.Font = new Font("Segoe UI", 10F);
        form.AutoScaleMode = AutoScaleMode.None;
        form.StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        form.ClientSize = new Size(Math.Min(width, area.Width - 48), Math.Min(height, area.Height - 72));
        form.BackColor = Background;
        form.ForeColor = DarkText;
    }
    public static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Text = text, UseMnemonic = false, AutoSize = true, MinimumSize = new Size(86, 38), Height = 38, Padding = new Padding(12, 4, 12, 4), Margin = new Padding(4, 3, 4, 3), FlatStyle = FlatStyle.Flat, BackColor = primary ? Slate : Color.White, ForeColor = primary ? Color.White : DarkText, UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9.5F, primary ? FontStyle.Bold : FontStyle.Regular), TextImageRelation = TextImageRelation.ImageBeforeText };
        b.FlatAppearance.BorderColor = primary ? Slate : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(36, 80, 77) : SoftAccent;
        b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(29, 67, 64) : Color.FromArgb(211, 229, 224);
        var icon = ButtonIcon(text, primary ? Color.White : Slate);
        var disabledIcon = icon is null ? null : ButtonIcon(text, Muted);
        if (icon is not null)
        {
            b.Image = icon;
            b.Disposed += (_, _) => { icon.Dispose(); disabledIcon?.Dispose(); };
        }
        b.EnabledChanged += (_, _) =>
        {
            b.BackColor = b.Enabled ? (primary ? Slate : Color.White) : Color.FromArgb(235, 239, 241);
            b.ForeColor = b.Enabled ? (primary ? Color.White : DarkText) : Muted;
            b.FlatAppearance.BorderColor = b.Enabled && primary ? Slate : Border;
            if (icon is not null) b.Image = b.Enabled ? icon : disabledIcon;
        };
        b.Click += (_, _) => Run(action);
        return b;
    }
    private static Bitmap? ButtonIcon(string text, Color color)
    {
        var kind = text.StartsWith("Tìm") ? "search" : text.StartsWith("Thêm") ? "add" : text.StartsWith("Sửa") ? "edit" : text.StartsWith("Xóa") ? "delete" : text.StartsWith("Xuất") ? "export" : text.StartsWith("Tải") ? "reload" : text.StartsWith("Lưu") ? "save" : "";
        if (kind.Length == 0) return null;
        var icon = new Bitmap(20, 18);
        using var g = Graphics.FromImage(icon);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.6F) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        switch (kind)
        {
            case "search": g.DrawEllipse(pen, 2, 2, 9, 9); g.DrawLine(pen, 10, 10, 15, 15); break;
            case "add": g.DrawLine(pen, 8, 3, 8, 15); g.DrawLine(pen, 2, 9, 14, 9); break;
            case "edit": g.DrawLines(pen, new Point[] { new Point(3, 12), new Point(12, 3), new Point(15, 6), new Point(6, 15), new Point(3, 15), new Point(3, 12) }); break;
            case "delete": g.DrawLine(pen, 3, 5, 15, 5); g.DrawRectangle(pen, 6, 2, 6, 3); g.DrawLines(pen, new Point[] { new Point(4, 6), new Point(5, 15), new Point(13, 15), new Point(14, 6) }); break;
            case "export": g.DrawLine(pen, 9, 2, 9, 11); g.DrawLines(pen, new Point[] { new Point(5, 7), new Point(9, 11), new Point(13, 7) }); g.DrawLines(pen, new Point[] { new Point(3, 11), new Point(3, 15), new Point(15, 15), new Point(15, 11) }); break;
            case "reload": g.DrawArc(pen, 3, 3, 12, 12, 35, 295); g.DrawLines(pen, new Point[] { new Point(11, 2), new Point(15, 4), new Point(15, 0) }); break;
            case "save": g.DrawRectangle(pen, 3, 2, 12, 14); g.DrawRectangle(pen, 6, 2, 6, 5); g.DrawRectangle(pen, 6, 10, 6, 6); break;
        }
        return icon;
    }
    public static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) { Error(e); }
    }
    public static void Error(Exception e)
    {
        string message = e switch
        {
            SqlException sql when sql.Number is 2601 or 2627 => ParseDuplicateSqlError(sql),
            SqlException sql when sql.Number == 547 => "Dữ liệu đang được sử dụng hoặc không thỏa điều kiện. Với bản ghi có lịch sử, hãy bỏ chọn Hoạt động thay vì xóa.",
            SqlException sql when sql.Number is -2 or 2 or 53 or 40 or 4060 => "Chưa kết nối được SQL Server. Kiểm tra dịch vụ SQL Server và cấu hình kết nối ở màn hình đăng nhập.\n\n" + sql.Message,
            _ => e.Message
        };
        MessageBox.Show(message, "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    private static string ParseDuplicateSqlError(SqlException sql)
    {
        var msg = sql.Message;
        if (msg.Contains("Users", StringComparison.OrdinalIgnoreCase) && msg.Contains("Username", StringComparison.OrdinalIgnoreCase))
            return "Tên đăng nhập này đã tồn tại trong hệ thống. Vui lòng chọn tên đăng nhập khác.";
        if (msg.Contains("Users", StringComparison.OrdinalIgnoreCase) && msg.Contains("EmployeeId", StringComparison.OrdinalIgnoreCase))
            return "Nhân viên này đã được cấp tài khoản người dùng trong hệ thống.";
        if (msg.Contains("Rooms", StringComparison.OrdinalIgnoreCase) && (msg.Contains("Number", StringComparison.OrdinalIgnoreCase) || msg.Contains("UX_") || msg.Contains("UQ_")))
            return "Số phòng này đã tồn tại trong danh mục phòng. Vui lòng kiểm tra lại.";
        if (msg.Contains("RoomTypes", StringComparison.OrdinalIgnoreCase))
            return "Tên loại phòng này đã tồn tại. Vui lòng chọn tên khác.";
        if (msg.Contains("Customers", StringComparison.OrdinalIgnoreCase) || msg.Contains("UX_Customers_Identity", StringComparison.OrdinalIgnoreCase))
            return "Số giấy tờ (CCCD/Hộ chiếu) này đã tồn tại trong danh bạ khách hàng.";
        if (msg.Contains("Services", StringComparison.OrdinalIgnoreCase))
            return "Tên dịch vụ này đã tồn tại. Vui lòng chọn tên khác.";
        if (msg.Contains("Invoices", StringComparison.OrdinalIgnoreCase))
            return "Phiếu đặt phòng này đã được lập hóa đơn trước đó.";
        return "Dữ liệu bị trùng lặp trong hệ thống (số phòng, tên đăng nhập, giấy tờ hoặc dịch vụ). Vui lòng kiểm tra lại.\n\nChi tiết: " + sql.Message;
    }
    public static bool Confirm(string message) => MessageBox.Show(message, "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    public static void Info(string message) => MessageBox.Show(message, "Thông tin", MessageBoxButtons.OK, MessageBoxIcon.Information);
    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = true,
            RowTemplate = { Height = 38 },
            ColumnHeadersHeight = 42,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            GridColor = Border,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(236, 242, 243), ForeColor = DarkText, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Padding = new Padding(9, 5, 9, 5), SelectionBackColor = Color.FromArgb(236, 242, 243) },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(250, 251, 252) },
            DefaultCellStyle = new DataGridViewCellStyle { SelectionBackColor = SoftAccent, SelectionForeColor = DarkText, Padding = new Padding(9, 5, 9, 5), ForeColor = DarkText }
        };
        grid.DataBindingComplete += (_, _) => FormatColumns(grid);
        return grid;
    }
    public static void Bind(DataGridView grid, DataTable data)
    {
        grid.DataSource = data;
        FormatColumns(grid);
    }
    private static void FormatColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn col in grid.Columns)
        {
            if (col.Name == "Id")
            {
                col.HeaderText = "Mã";
                col.Width = 60;
            }
            if (col.ValueType == typeof(decimal))
            {
                col.DefaultCellStyle.Format = "N0";
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            if (col.ValueType == typeof(DateTime))
                col.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
            col.MinimumWidth = 60;
        }
    }
    public static int SelectedId(DataGridView grid) => grid.CurrentRow is { IsNewRow: false } row ? Convert.ToInt32(row.Cells["Id"].Value) : throw new InvalidOperationException("Hãy chọn một dòng trong danh sách.");
    public static FlowLayoutPanel Toolbar() => new ThanhCongCu();
    public static Label Label(string text) => new() { Text = text, UseMnemonic = false, AutoSize = true, Margin = new Padding(6, 11, 6, 4), ForeColor = Muted };
    public static TextBox Search(string placeholder = "Tìm theo tên, mã...") => new() { Width = 230, PlaceholderText = placeholder, Margin = new Padding(4, 8, 4, 5), BorderStyle = BorderStyle.FixedSingle };
    public static DateTimePicker Date(DateTime value) => new() { Value = value, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 140, Margin = new Padding(4, 8, 4, 5) };
    public static ComboBox Choices(params string[] values)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155, Margin = new Padding(4, 8, 4, 5) };
        combo.Items.AddRange(values);
        if (values.Length > 0)
            combo.SelectedIndex = 0;
        return combo;
    }
    public static ComboBox Lookup(DataTable table, int? selected = null)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", ValueMember = "Id", DataSource = table, Width = 290 };
        if (selected.HasValue)
            combo.SelectedValue = selected.Value;
        return combo;
    }
    public static int Value(ComboBox combo) => combo.SelectedValue is int id ? id : throw new ArgumentException("Vui lòng chọn đầy đủ thông tin trong danh sách.");
    public static NumericUpDown Number(decimal max = 1000000000, decimal value = 0, decimal min = 0) => new() { Maximum = max, Minimum = min, Value = Math.Clamp(value, min, max), ThousandsSeparator = true, Width = 170 };
    public static string Currency(decimal value) => value.ToString("N0") + " đ";
    public static Panel Page(DataGridView grid, FlowLayoutPanel bar)
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Background };
        p.Controls.Add(grid);
        p.Controls.Add(bar);
        return p;
    }
}

internal sealed class ThanhCongCu : FlowLayoutPanel
{
    public ThanhCongCu()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10, 9, 10, 10);
        Margin = Padding.Empty;
        WrapContents = true;
        BackColor = Color.White;
        DoubleBuffered = true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Ui.Border);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

internal sealed class KhungNoiDung : TabControl
{
    public KhungNoiDung()
    {
        Appearance = TabAppearance.FlatButtons;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(1, 1);
        Multiline = true;
    }
    protected override void WndProc(ref Message m)
    {
        // Noi dung chuyen bang thanh dieu huong ben trai, an hang tab goc.
        if (m.Msg == 0x1328 && !DesignMode) { m.Result = (IntPtr)1; return; }
        base.WndProc(ref m);
    }
}

internal class EditorForm : Form
{
    protected readonly TableLayoutPanel Fields = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(20), BackColor = Color.White };
    protected readonly FlowLayoutPanel Actions = new() { Dock = DockStyle.Bottom, Height = 68, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 10), BackColor = Color.White };
    private readonly Label heading = new() { Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Ui.DarkText };
    private readonly Label description = new() { Dock = DockStyle.Fill, ForeColor = Ui.Muted, Font = new Font("Segoe UI", 9F) };
    public EditorForm(string title, int height = 500)
    {
        Ui.Setup(this, title, 660, height + 64);
        MinimumSize = new Size(580, 400);
        MaximizeBox = false;
        var header = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(24, 12, 24, 8), BackColor = Ui.Background };
        header.Controls.Add(description);
        header.Controls.Add(heading);
        SetHeading(title, "Các mục có dấu * là thông tin bắt buộc.");
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(Fields);
        Controls.Add(Actions);
        Controls.Add(header);
        Actions.Paint += (_, e) => { using var pen = new Pen(Ui.Border); e.Graphics.DrawLine(pen, 0, 0, Actions.Width, 0); };
        var cancel = Ui.Button("Đóng", () => Close());
        Actions.Controls.Add(cancel);
        CancelButton = cancel;
    }
    protected void SetHeading(string title, string subtitle)
    {
        heading.Text = title;
        description.Text = subtitle;
    }
    protected void AddField(string label, Control control, int height = 44)
    {
        var row = Fields.RowCount++;
        Fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var text = Ui.Label(label);
        text.Dock = DockStyle.Fill;
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(4, 6, 4, 6);
        if (control is TextBox input) input.BorderStyle = BorderStyle.FixedSingle;
        if (control is Label hint) { hint.ForeColor = Ui.Muted; hint.Font = new Font("Segoe UI", 9F); }
        Fields.Controls.Add(text, 0, row);
        Fields.Controls.Add(control, 1, row);
    }
    protected void SaveButton(Action save, string text = "Lưu")
    {
        var b = Ui.Button(text, save, true);
        Actions.Controls.Add(b);
        AcceptButton = b;
    }
}
