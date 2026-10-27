using System.Globalization;
using System.Text.Json;
using Hotel.Business;
using Hotel.Data;

namespace Hotel.WinForms;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var uiSmoke = args.Contains("--ui-smoke");
        if (uiSmoke)
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("vi-VN");
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Ui.Error(e.Exception);
        try
        {
            var settings = AppSettings.Load();
            if (uiSmoke)
            {
                UiSmoke.Run(settings, args);
                return;
            }
            var service = new HotelService(new Database(settings.ConnectionString));
            if (args.Contains("--initialize"))
            {
                service.Initialize();

            service.EnsureDemoData();
                return;
            }

            while (true)
            {
                using var login = new LoginForm(service, settings);
                if (login.ShowDialog() != DialogResult.OK || login.Session is null)
                    break;
                using var main = new MainForm(service, login.Session, settings);
                Application.Run(main);
                if (!main.LoggedOut)
                    break;
            }
        }
        catch (Exception e) { Ui.Error(e); }
    }
}
public sealed class AppSettings
{
    public string ConnectionString { get; set; } = "Server=lpc:.\\SQLEXPRESS;Database=HotelManagementBTL;Integrated Security=True;TrustServerCertificate=True";
    public string HotelName { get; set; } = "KHÁCH SẠN NGỌC KHOA";
    public string HotelAddress { get; set; } = "Hà Nội";
    public string HotelPhone { get; set; } = "024 0000 0000";
    public static AppSettings Load() => JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CauHinh.json"))) ?? new();
}

