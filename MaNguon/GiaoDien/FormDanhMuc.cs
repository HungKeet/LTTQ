using System.Data;

using Hotel.Business;

using Hotel.Core;



namespace Hotel.WinForms;



internal sealed class CatalogEditor : EditorForm

{

    public int SavedId

    {

        get; private set;

    }

    private readonly Dictionary<string, Control> controls = new();

    public CatalogEditor(HotelService service, Session user, CatalogKind kind, int? id = null) : base((id.HasValue ? "Sửa " : "Thêm ") + CatalogPage.Title(kind), 540)

    {

        DataRow? row = id.HasValue ? service.CatalogRecord(user, kind, id.Value) : null;

        object? Old(string key) => row is not null && row.Table.Columns.Contains(key) && !row.IsNull(key) ? row[key] : null;

        void TextField(string key, string label, int max, bool multiline = false)

        {

            var t = new TextBox { Text = Convert.ToString(Old(key)) ?? "", MaxLength = max, Multiline = multiline };

            controls[key] = t;

            AddField(label, t, multiline ? 75 : 44);

        }

        void NumberField(string key, string label, int max, int min = 0)

        {

            var n = Ui.Number(max, Convert.ToDecimal(Old(key) ?? min), min);

            controls[key] = n;

            AddField(label, n);

        }

        void LookupField(string key, string label, CatalogKind lookup)

        {

            var combo = Ui.Lookup(service.Lookup(user, lookup), Old(key) is int value ? value : null);

            controls[key] = combo;

            AddField(label, combo);

        }

        void Choice(string key, string label, params string[] choices)

        {

            var combo = Ui.Choices(choices);

            if (Old(key) is string value)

                combo.SelectedItem = value;

            controls[key] = combo;

            AddField(label, combo);

        }

        switch (kind)

        {

            case CatalogKind.RoomTypes:

                TextField("Name", "Tên loại phòng *", 80);

                NumberField("Capacity", "Sức chứa *", 20, 1);

                NumberField("Rate", "Giá mỗi đêm (đ) *", 100000000, 1);

                break;

            case CatalogKind.Rooms:

                TextField("Number", "Số phòng *", 20);

                NumberField("Floor", "Tầng *", 100);

                LookupField("RoomTypeId", "Loại phòng *", CatalogKind.RoomTypes);

                Choice("OperationalStatus", "Tình trạng *", "Sẵn sàng", "Chờ dọn", "Bảo trì");

                break;

            case CatalogKind.Customers:

                TextField("FullName", "Họ tên *", 100);

                TextField("Phone", "Điện thoại *", 25);

                TextField("IdentityNumber", "Giấy tờ", 30);

                TextField("Address", "Địa chỉ", 250, true);

                break;

            case CatalogKind.Services:

                TextField("Name", "Tên dịch vụ *", 100);

                TextField("Unit", "Đơn vị tính *", 30);

                NumberField("Price", "Đơn giá (đ) *", 100000000, 1);

                break;

            case CatalogKind.Employees:

                TextField("FullName", "Họ tên *", 100);

                TextField("Phone", "Điện thoại *", 25);

                Choice("Position", "Chức vụ *",
                    "Quản lý",
                    "Lễ tân",
                    "Kế toán",
                    "Quản lý buồng phòng",
                    "Nhân viên dịch vụ",
                    "Nhân viên kỹ thuật",
                    "Bảo vệ",
                    "Thu ngân");

                var empPass = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };
                controls["Password"] = empPass;
                AddField(id.HasValue ? "Mật khẩu mới" : "Mật khẩu *", empPass);
                AddField("Lưu ý", new Label { Text = id.HasValue ? "Để trống nếu không đổi mật khẩu đăng nhập.\nMật khẩu tài khoản từ 8 ký tự trở lên." : "Mật khẩu tài khoản từ 8 ký tự trở lên để đăng nhập bằng Họ tên.", AutoSize = false }, 65);

                break;

            case CatalogKind.Users:



                TextField("Username", "Tên tài khoản *", 40);

                Choice("Role", "Chức vụ *",
                    "Quản lý",
                    "Lễ tân",
                    "Kế toán",
                    "Quản lý buồng phòng",
                    "Nhân viên dịch vụ",
                    "Nhân viên kỹ thuật",
                    "Bảo vệ",
                    "Thu ngân");

                var password = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };

                controls["Password"] = password;

                AddField(id.HasValue ? "Mật khẩu mới" : "Mật khẩu *", password);

                AddField("Lưu ý", new Label { Text = id.HasValue ? "Để trống mật khẩu để giữ nguyên.\nTên tài khoản và mật khẩu từ 8 ký tự trở lên." : "Tên tài khoản và mật khẩu từ 8 ký tự trở lên.", AutoSize = false }, 65);

                break;

        }

        var active = new CheckBox { Text = "Cho phép sử dụng", Checked = Old("Active") is not bool old || old };

        controls["Active"] = active;

        AddField("Hoạt động", active);

        AddField("", new Label { Text = "* Thông tin bắt buộc. Bỏ Hoạt động để giữ lại lịch sử thay vì xóa.", AutoSize = false }, 60);

        SaveButton(() =>

        {

            var values = controls.ToDictionary(k => k.Key, k => k.Value switch { TextBox t => (object?)t.Text, NumericUpDown n => n.Value, CheckBox check => check.Checked, ComboBox combo => combo.DataSource is null ? combo.SelectedItem : combo.SelectedValue, _ => null });

            SavedId = service.SaveCatalog(user, kind, id, values);

            DialogResult = DialogResult.OK;

            Close();

        });

    }

}

internal sealed class CatalogPage : Panel

{

    private readonly DataGridView grid = Ui.Grid(); private readonly TextBox search = Ui.Search();

    private readonly HotelService service; private readonly Session user; private readonly CatalogKind kind;

    public static string Title(CatalogKind kind) => kind switch { CatalogKind.RoomTypes => "loại phòng", CatalogKind.Rooms => "phòng", CatalogKind.Customers => "khách hàng", CatalogKind.Services => "dịch vụ", CatalogKind.Employees => "nhân viên", _ => "tài khoản" };

    public CatalogPage(HotelService service, Session user, CatalogKind kind)

    {

        this.service = service;

        this.user = user;

        this.kind = kind;

        Dock = DockStyle.Fill;

        var bar = Ui.Toolbar();

        bar.Controls.Add(search);

        bar.Controls.Add(Ui.Button("Tìm / tải lại", RefreshData));

        bar.Controls.Add(Ui.Button("Xuất Excel", () => { if (grid.DataSource is not DataTable data || data.Rows.Count == 0) throw new InvalidOperationException("Chưa có dữ liệu để xuất."); XuatExcel.PromptExport(this, data, "DanhSach_" + Title(kind).Replace(" ", "") + "_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx", Title(kind)); }));

        if (user.Role == "Quản lý" || kind == CatalogKind.Customers)

        {

            bar.Controls.Add(Ui.Button("Thêm mới", () => Edit(null), true));

            bar.Controls.Add(Ui.Button("Sửa", () => Edit(Ui.SelectedId(grid))));

            bar.Controls.Add(Ui.Button("Xóa", () => { var id = Ui.SelectedId(grid); if (Ui.Confirm("Xóa bản ghi #" + id + "? Dữ liệu có lịch sử sử dụng sẽ được bảo vệ.")) { service.DeleteCatalog(user, kind, id); RefreshData(); } }));

            grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Ui.Run(() => Edit(Ui.SelectedId(grid))); };

        }

        if (kind == CatalogKind.Customers)

            bar.Controls.Add(Ui.Button("Lịch sử lưu trú", () =>

            {

                using var history = new Form();

                Ui.Setup(history, "Lịch sử khách hàng", 1100, 600);

                var list = Ui.Grid();

                history.Controls.Add(list);

                Ui.Bind(list, service.Bookings(user, customerId: Ui.SelectedId(grid)));

                history.ShowDialog(this);

            }));

        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ui.Run(RefreshData); } };

        Controls.Add(Ui.Page(grid, bar));

    }

    private void Edit(int? id)

    {

        using var editor = new CatalogEditor(service, user, kind, id);

        if (editor.ShowDialog(this) == DialogResult.OK)

            RefreshData();

    }

    public void RefreshData() => Ui.Bind(grid, service.Catalog(user, kind, search.Text));

}

