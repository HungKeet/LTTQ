using System.Data;
using Microsoft.Data.SqlClient;

namespace Hotel.WinForms;

internal static class Ui
{
    public static readonly Color DarkText = Color.FromArgb(30, 41, 59), Slate = Color.FromArgb(51, 65, 85), Background = Color.FromArgb(248, 250, 252);
    public static readonly Color Navy = DarkText, Blue = Slate;
    public static void Setup(Form form, string title, int width = 1100, int height = 720)
    {
        form.Text = title;
        form.Font = new Font("Segoe UI", 10F);
        form.AutoScaleMode = AutoScaleMode.None;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.ClientSize = new Size(width, height);
        form.BackColor = Background;
    }
    public static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(95, 34), Height = 36, Padding = new Padding(12, 4, 12, 4), Margin = new Padding(4), FlatStyle = FlatStyle.Flat, BackColor = primary ? Slate : Color.White, ForeColor = primary ? Color.White : DarkText, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        b.Click += (_, _) => Run(action);
        return b;
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
            RowTemplate = { Height = 32 },
            ColumnHeadersHeight = 36,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(241, 245, 249), ForeColor = DarkText, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(250, 251, 252) },
            DefaultCellStyle = new DataGridViewCellStyle { SelectionBackColor = Color.FromArgb(226, 232, 240), SelectionForeColor = Color.Black, Padding = new Padding(4) }
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
    public static FlowLayoutPanel Toolbar() => new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true, BackColor = Color.FromArgb(248, 250, 252) };
    public static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(6, 10, 6, 4), ForeColor = DarkText };
    public static TextBox Search(string placeholder = "Tìm theo tên, mã...") => new() { Width = 220, PlaceholderText = placeholder, Margin = new Padding(4, 7, 4, 4) };
    public static DateTimePicker Date(DateTime value) => new() { Value = value, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 140, Margin = new Padding(4, 7, 4, 4) };
    public static ComboBox Choices(params string[] values)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155, Margin = new Padding(4, 7, 4, 4) };
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
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        p.Controls.Add(grid);
        p.Controls.Add(bar);
        return p;
    }
}

internal class EditorForm : Form
{
    protected readonly TableLayoutPanel Fields = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(16) };
    protected readonly FlowLayoutPanel Actions = new() { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
    public EditorForm(string title, int height = 500)
    {
        Ui.Setup(this, title, 620, height);
        MinimumSize = new Size(580, 400);
        MaximizeBox = false;
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(Fields);
        Controls.Add(Actions);
        var cancel = Ui.Button("Đóng", () => Close());
        Actions.Controls.Add(cancel);
        CancelButton = cancel;
    }
    protected void AddField(string label, Control control, int height = 44)
    {
        var row = Fields.RowCount++;
        Fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var text = Ui.Label(label);
        text.Dock = DockStyle.Fill;
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(4, 6, 4, 6);
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
