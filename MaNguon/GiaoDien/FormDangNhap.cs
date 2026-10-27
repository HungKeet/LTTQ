using Hotel.Business;
using Hotel.Core;

namespace Hotel.WinForms;

internal sealed class LoginForm : EditorForm
{
    public Session? Session
    {
        get; private set;
    }
    public LoginForm(HotelService service, AppSettings settings) : base("Đăng nhập • Quản lý khách sạn", 270)
    {
        SetHeading(settings.HotelName, "Đăng nhập để bắt đầu ca làm việc.");
        var username = new TextBox { Text = "admin", MaxLength = 40 };
        var password = new TextBox { UseSystemPasswordChar = true, MaxLength = 100 };
        AddField("Tên đăng nhập", username);
        AddField("Mật khẩu", password);
        SaveButton(() =>
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                service.Initialize();
                service.EnsureDemoData();
                Session = service.Login(username.Text, password.Text);
                if (string.IsNullOrWhiteSpace(Session.Role))
                    throw new UnauthorizedAccessException("Tài khoản chưa được phân quyền truy cập hệ thống.");
                DialogResult = DialogResult.OK;
                Close();
            }
            finally { Cursor = Cursors.Default; }
        }, "Đăng nhập");
        Shown += (_, _) => password.Focus();
    }
}
internal sealed class EditorConnection : EditorForm
{
    public bool Saved
    {
        get; private set;
    }
    public EditorConnection(AppSettings settings) : base("Cấu hình ứng dụng", 490)
    {
        var connection = new TextBox { Text = settings.ConnectionString, Multiline = true, ScrollBars = ScrollBars.Vertical };
        var name = new TextBox { Text = settings.HotelName, MaxLength = 150 };
        var address = new TextBox { Text = settings.HotelAddress, MaxLength = 250 };
        var phone = new TextBox { Text = settings.HotelPhone, MaxLength = 30 };
        AddField("Chuỗi kết nối", connection, 110);
        AddField("Tên khách sạn", name);
        AddField("Địa chỉ", address);
        AddField("Điện thoại", phone);
        AddField("Gợi ý", new Label { Text = "Mặc định: lpc:.\\SQLEXPRESS trên máy này. Có thể đổi Server theo instance SQL Server đã cài. Dùng một database riêng cho bài tập.", AutoSize = false }, 75);
        SaveButton(() =>
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection.Text);
            if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
                throw new ArgumentException("Cần chỉ định Database trong chuỗi kết nối.");
            settings.ConnectionString = builder.ConnectionString;
            settings.HotelName = name.Text.Trim();
            settings.HotelAddress = address.Text.Trim();
            settings.HotelPhone = phone.Text.Trim();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "CauHinh.json"), System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Saved = true;
            Close();
        });
    }
}
