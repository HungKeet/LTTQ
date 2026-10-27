using System.Data;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Hotel.Business;
using Hotel.Core;
using Hotel.Data;
using Microsoft.Data.SqlClient;

namespace Hotel.WinForms;

// Chi chay khi truyen --ui-smoke; toan bo du lieu duoc tao trong database kiem tra rieng.
internal static class UiSmoke
{
    internal static void Run(AppSettings settings, string[] args)
    {
        var outputIndex = Array.IndexOf(args, "--out");
        var output = Path.GetFullPath(outputIndex >= 0 && outputIndex + 1 < args.Length
            ? args[outputIndex + 1] : "KetQua/GiaoDienKiemTra");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "KetQua.txt"), "RUNNING: đang kiểm tra giao diện.");
        var layoutNotes = new List<string>();
        var captures = new List<string>();
        try
        {
            var builder = new SqlConnectionStringBuilder(settings.ConnectionString)
            {
                InitialCatalog = "HotelManagementBTL_UiTest_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N")[..6]
            };
            var smokeSettings = new AppSettings
            {
                ConnectionString = builder.ConnectionString,
                HotelName = settings.HotelName,
                HotelAddress = settings.HotelAddress,
                HotelPhone = settings.HotelPhone
            };
            var database = new Database(builder.ConnectionString);
            var service = new HotelService(database);
            service.Initialize();
            service.EnsureDemoData();
            var manager = service.Login("admin", "Admin@123");
            var reception = service.Login("letan", "Letan@123");
            var active = database.Query("SELECT TOP (1) Id FROM Bookings WHERE Status=N'Đang ở' AND Departure>GETDATE() ORDER BY Id").Rows[0].Field<int>("Id");
            var invoiceId = database.Query("SELECT TOP (1) Id FROM Invoices ORDER BY Id DESC").Rows[0].Field<int>("Id");
            var invoice = service.Invoice(manager, invoiceId);
            var arrival = DateTime.Today.AddDays(3).AddHours(14);
            var departure = DateTime.Today.AddDays(5).AddHours(12);
            var available = service.AvailableRooms(manager, arrival, departure);
            var future = service.SaveBooking(manager, new BookingInput(1, arrival, departure, 1, 200000,
                "Dữ liệu kiểm tra giao diện", new[] { available.Rows[0].Field<int>("Id") }));

            void Save(Form form, string name)
            {
                PumpLayout(form);
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                captures.Add(name);
                InspectFlowPanels(form, name, layoutNotes);
            }

            void Capture(Form form, string name, Action<Form>? prepare = null)
            {
                using (form)
                {
                    form.Show();
                    prepare?.Invoke(form);
                    Save(form, name);
                    if (form.MinimumSize.Width > 0 && form.MinimumSize.Height > 0 && form.Size != form.MinimumSize)
                    {
                        form.Size = form.MinimumSize;
                        Save(form, name + "-ToiThieu");
                    }
                    form.Close();
                }
            }

            void CaptureMain(Session user, string name)
            {
                using var main = new MainForm(service, user, smokeSettings);
                main.Show();
                var normal = main.Size;
                foreach (var small in new[] { false, true })
                {
                    main.Size = small ? main.MinimumSize : normal;
                    for (var i = 0; i < main.Tabs.TabPages.Count; i++)
                    {
                        main.Tabs.SelectedIndex = i;
                        main.RefreshSelected();
                        Save(main, $"{name}-{i:D2}" + (small ? "-ToiThieu" : ""));
                    }
                }
                main.Close();
            }

            Capture(new LoginForm(service, smokeSettings), "01-DangNhap");
            Capture(new EditorConnection(smokeSettings), "02-CauHinh");
            Capture(new PasswordForm(service, manager), "03-DoiMatKhau");
            CaptureMain(manager, "04-QuanLy");
            CaptureMain(reception, "05-LeTan");
            Capture(new BookingEditor(service, manager), "06-DatPhong");
            Capture(new BookingEditor(service, manager, future), "07-SuaDatPhong");
            Capture(new BookingEditor(service, reception, null, true), "08-NhanPhongNhanh");
            Capture(new BookingEditor(service, reception, null, true), "08b-NhanPhongKhachMoi",
                form => Descendants(form).OfType<CheckBox>().Single(control => control.Text == "Khách mới").Checked = true);
            Capture(new ServicesForm(service, reception, active), "09-DichVu");
            Capture(new DepartureForm(service, reception, active), "10-DoiNgayDi");
            Capture(new ChangeRoomForm(service, reception, active), "11-DoiPhong");
            Capture(new CheckoutForm(service, reception, smokeSettings, active), "12-ThanhToan");
            Capture(new InvoiceForm(invoice, smokeSettings), "13-HoaDon");

            foreach (var kind in Enum.GetValues<CatalogKind>())
            {
                var name = kind switch
                {
                    CatalogKind.Rooms => "Phong", CatalogKind.RoomTypes => "LoaiPhong",
                    CatalogKind.Customers => "KhachHang", CatalogKind.Services => "DichVu",
                    CatalogKind.Employees => "NhanVien", CatalogKind.Users => "TaiKhoan",
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                Capture(new CatalogEditor(service, manager, kind), "14-DanhMuc-" + name);
            }
            var overview = service.RoomsOverview(manager);
            foreach (var (status, name) in new[] { ("Sẵn sàng", "SanSang"), ("Đang ở", "DangO"), ("Chờ dọn", "ChoDon"), ("Bảo trì", "BaoTri") })
            {
                var room = overview.AsEnumerable().First(row => row.Field<string>("Trạng thái") == status);
                Capture(new RoomActionsForm(service, manager, smokeSettings, room.Field<int>("Id")), "15-ThaoTacPhong-" + name);
                Capture(new RoomActionsForm(service, reception, smokeSettings, room.Field<int>("Id")), "16-ThaoTacLeTan-" + name);
            }

            File.WriteAllText(Path.Combine(output, "HoaDon.html"), InvoiceExport.Html(invoice, smokeSettings));
            File.WriteAllLines(Path.Combine(output, "BoCuc.txt"), layoutNotes.Count > 0
                ? layoutNotes : new[] { "Không phát hiện control vượt khung FlowLayoutPanel không cuộn." });
            File.WriteAllLines(Path.Combine(output, "KetQua.txt"), new[]
            {
                "PASS: đã mở và render các form/tab quản lý và lễ tân ở kích thước thường và tối thiểu.",
                "Runtime: " + RuntimeInformation.FrameworkDescription,
                "Database kiểm tra: " + builder.InitialCatalog,
                "Số ảnh: " + captures.Count,
                "Cảnh báo cần xem bố cục trong BoCuc.txt: " + layoutNotes.Count,
                "Đã xuất HoaDon.html.",
                "Các ảnh cần được xem trực quan; chế độ này không thay thế kiểm thử thao tác người dùng.",
                "Không sử dụng database nghiệp vụ chính. Database kiểm tra được giữ lại để đối chiếu."
            });
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(output, "Loi.txt"), exception.ToString());
            File.WriteAllText(Path.Combine(output, "KetQua.txt"), "FAIL: xem Loi.txt để biết chi tiết.");
            File.WriteAllLines(Path.Combine(output, "BoCuc.txt"), layoutNotes);
            Environment.ExitCode = 1;
        }
    }

    private static void PumpLayout(Form form)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            form.PerformLayout();
            Application.DoEvents();
            Thread.Sleep(15);
        } while (timer.ElapsedMilliseconds < 300);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void InspectFlowPanels(Control root, string screenshot, List<string> notes)
    {
        if (!root.Visible)
            return;
        if (root is FlowLayoutPanel panel && !panel.AutoScroll)
        {
            foreach (Control control in panel.Controls)
            {
                if (!control.Visible || control.Width <= 0 || control.Height <= 0)
                    continue;
                var bounds = control.Bounds;
                if (bounds.Left < -1 || bounds.Top < -1 || bounds.Right > panel.ClientSize.Width + 1 || bounds.Bottom > panel.ClientSize.Height + 1)
                {
                    var caption = control.Text.Replace('\r', ' ').Replace('\n', ' ');
                    notes.Add($"{screenshot}: {control.GetType().Name} '{caption}' {bounds} vượt khung {panel.ClientSize}.");
                }
            }
        }
        foreach (Control control in root.Controls)
            InspectFlowPanels(control, screenshot, notes);
    }
}
