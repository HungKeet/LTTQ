using System.Data;
using System.Net;
using System.Text;
using Hotel.Business;
using Hotel.Core;

namespace Hotel.WinForms;

internal sealed class CheckoutForm : Form
{
    private CheckoutQuote? quote;
    private readonly DataGridView grid = Ui.Grid();
    private readonly NumericUpDown surcharge = Ui.Number(), discount = Ui.Number();
    private readonly Label summary = new()
    {
        Dock = DockStyle.Bottom,
        Height = 140,
        Padding = new Padding(20, 16, 20, 16),
        Font = new Font("Segoe UI", 10.5F),
        BackColor = Color.FromArgb(236, 244, 241)
    };

    public CheckoutForm(HotelService service, Session user, AppSettings settings, int bookingId)
    {
        Ui.Setup(this, $"Trả phòng và thanh toán dịch vụ • Phiếu #{bookingId}", 1040, 740);
        MinimumSize = new Size(900, 640);

        var bar = Ui.Toolbar();
        var method = Ui.Choices("Tiền mặt", "Chuyển khoản", "Thẻ");
        var notes = new TextBox { Width = 350, MaxLength = 1000, PlaceholderText = "Tình trạng phòng hoặc lưu ý khi trả phòng" };
        Button? payButton = null;

        bool Calculate()
        {
            quote = null;
            if (payButton is not null)
                payButton.Enabled = false;
            try
            {
                quote = service.PreviewCheckout(user, bookingId, surcharge.Value, discount.Value);
                var table = new DataTable();
                table.Columns.Add("STT");
                table.Columns.Add("Khoản mục");
                table.Columns.Add("Nội dung dịch vụ");
                table.Columns.Add("Số lượng", typeof(decimal));
                table.Columns.Add("Đơn vị");
                table.Columns.Add("Đơn giá", typeof(decimal));
                table.Columns.Add("Thành tiền", typeof(decimal));

                int stt = 1;
                foreach (var line in quote.Lines)
                {
                    table.Rows.Add((stt++).ToString(), line.Kind, line.Description, line.Quantity, line.Unit, line.UnitPrice, line.Amount);
                }
                Ui.Bind(grid, table);

                var balanceText = quote.Balance >= 0
                    ? $"▶ CẦN THU THÊM TẠI QUẦY: {Ui.Currency(quote.Balance)}"
                    : $"▶ CẦN HOÀN LẠI CHO KHÁCH: {Ui.Currency(-quote.Balance)}";

                summary.Text = $"Cộng tiền phòng & dịch vụ: {Ui.Currency(quote.Subtotal)}   |   Phụ thu: +{Ui.Currency(quote.Surcharge)}   |   Giảm trừ: -{Ui.Currency(quote.Discount)}\n" +
                               $"TỔNG THANH TOÁN: {Ui.Currency(quote.Total)}   •   Tiền đặt cọc đã đối trừ: -{Ui.Currency(quote.Deposit)}\n" +
                               $"{balanceText}  (Phương thức: {method.Text})";
                summary.ForeColor = Ui.DarkText;
                if (payButton is not null)
                    payButton.Enabled = true;
                return true;
            }
            catch (Exception e)
            {
                quote = null;
                grid.DataSource = null;
                summary.ForeColor = Color.Firebrick;
                summary.Text = "Chưa thể thanh toán: " + e.Message;
                return false;
            }
        }

        surcharge.Width = discount.Width = 140;
        bar.Controls.Add(OperationalLayout.Field("Phụ thu (đ)", surcharge));
        bar.Controls.Add(OperationalLayout.Field("Giảm trừ (đ)", discount));
        bar.Controls.Add(OperationalLayout.Field("Thanh toán", method));
        bar.Controls.Add(Ui.Button("Tính lại", () => Calculate()));

        var footer = Ui.Toolbar();
        footer.Dock = DockStyle.Bottom;
        footer.Controls.Add(OperationalLayout.Field("Ghi chú", notes));
        payButton = Ui.Button("Thanh toán và trả phòng", () =>
        {
            if (!Calculate())
                return;
            var q = quote!;
            var amount = q.Balance >= 0 ? $"thu thêm {Ui.Currency(q.Balance)}" : $"hoàn lại {Ui.Currency(-q.Balance)}";
            if (!Ui.Confirm($"Xác nhận {amount} qua hình thức {method.Text.ToLower()} và hoàn tất thủ tục trả phòng?\nHóa đơn thanh toán sẽ được khóa và lưu vào hệ thống."))
                return;

            var invoiceId = service.Checkout(user, q, method.Text, notes.Text);
            DialogResult = DialogResult.OK;
            using var invoice = new InvoiceForm(service.Invoice(user, invoiceId), settings);
            invoice.ShowDialog(this);
            Close();
        }, true);
        footer.Controls.Add(payButton);
        var close = Ui.Button("Đóng", Close);
        footer.Controls.Add(close);
        CancelButton = close;

        surcharge.ValueChanged += (_, _) => Calculate();
        discount.ValueChanged += (_, _) => Calculate();
        method.SelectedIndexChanged += (_, _) => Calculate();

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Ui.Background };
        body.Controls.Add(grid);
        Controls.Add(body);
        Controls.Add(bar);
        Controls.Add(summary);
        Controls.Add(footer);
        Calculate();
    }
}

internal sealed class InvoiceForm : Form
{
    public InvoiceForm(InvoiceDocument invoice, AppSettings settings)
    {
        Ui.Setup(this, $"Hóa đơn thanh toán dịch vụ • {invoice.Number} • {settings.HotelName}", 1060, 800);
        MinimumSize = new Size(920, 680);
        BackColor = Color.FromArgb(241, 245, 249); // Nền xám nhạt làm nổi bật trang giấy trắng

        // Khung cuộn chứa toàn bộ trang hóa đơn dạng A4
        var pnlScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(241, 245, 249),
            Padding = new Padding(20, 15, 20, 15)
        };

        var gridHeight = Math.Max(160, Math.Min(400, (invoice.Lines.Count + 1) * 35 + 40));

        // Trang giấy hóa đơn (Paper card)
        var pnlPaper = new Panel
        {
            Dock = DockStyle.Top,
            Height = 640 + gridHeight,
            BackColor = Color.White,
            Padding = new Padding(28, 24, 28, 24)
        };

        // 1. Phần tiêu đề khách sạn (Hotel Header)
        var pnlHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 110,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White
        };
        pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        var lblHotelBrand = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F),
            Text = $"{settings.HotelName.ToUpper()}\n" +
                   $"⭐⭐⭐⭐⭐ KHÁCH SẠN NGHỈ DƯỠNG & CĂN HỘ CAO CẤP\n" +
                   $"Địa chỉ: {settings.HotelAddress}\n" +
                   $"Hotline: {settings.HotelPhone}   •   Mã số thuế: 0108899889"
        };

        var lblInvoiceTitle = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopRight,
            Font = new Font("Segoe UI", 10F),
            Text = $"HÓA ĐƠN THANH TOÁN (HOTEL FOLIO)\n" +
                   $"Số hóa đơn: {invoice.Number}  •  Phiếu đặt: #{invoice.Id}\n" +
                   $"Ngày lập: {invoice.IssuedAt:dd/MM/yyyy HH:mm}  •  Thu ngân: {invoice.Employee}\n" +
                   $"[ TRẠNG THÁI: ĐÃ THANH TOÁN ]"
        };
        lblInvoiceTitle.ForeColor = Color.FromArgb(15, 23, 42);

        pnlHeader.Controls.Add(lblHotelBrand, 0, 0);
        pnlHeader.Controls.Add(lblInvoiceTitle, 1, 0);

        // Đường kẻ phân cách
        var lineDivider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 2,
            BackColor = Color.FromArgb(15, 23, 42),
            Margin = new Padding(0, 8, 0, 12)
        };

        // 2. Thông tin khách hàng & lưu trú (Guest & Stay Details)
        var nights = Money.Nights(invoice.CheckIn, invoice.CheckOut);
        var pnlGuestStay = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 115,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(12),
            Margin = new Padding(0, 10, 0, 14)
        };
        pnlGuestStay.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlGuestStay.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var lblGuest = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F),
            Text = $"THÔNG TIN KHÁCH HÀNG (GUEST INFO)\n" +
                   $"• Họ và tên: {invoice.Customer}\n" +
                   $"• Điện thoại: {invoice.Phone}\n" +
                   $"• CCCD / Giấy tờ: {(string.IsNullOrWhiteSpace(invoice.IdentityNumber) ? "---" : invoice.IdentityNumber)}\n" +
                   $"• Địa chỉ: {(string.IsNullOrWhiteSpace(invoice.Address) ? "Chưa cập nhật" : invoice.Address)}"
        };

        var lblStay = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F),
            Text = $"THÔNG TIN LƯU TRÚ (STAY DETAILS)\n" +
                   $"• Nhận phòng (Check-in):  {invoice.CheckIn:dd/MM/yyyy HH:mm}\n" +
                   $"• Trả phòng (Check-out): {invoice.CheckOut:dd/MM/yyyy HH:mm}\n" +
                   $"• Thời gian lưu trú: {nights} đêm\n" +
                   $"• Hình thức thanh toán: {invoice.PaymentMethod}"
        };

        pnlGuestStay.Controls.Add(lblGuest, 0, 0);
        pnlGuestStay.Controls.Add(lblStay, 1, 0);

        // 3. Bảng danh sách chi tiết phòng và dịch vụ
        var grid = Ui.Grid();
        grid.Dock = DockStyle.Top;
        grid.Height = gridHeight;

        var data = new DataTable();
        data.Columns.Add("STT");
        data.Columns.Add("Khoản mục");
        data.Columns.Add("Nội dung dịch vụ");
        data.Columns.Add("Số lượng", typeof(decimal));
        data.Columns.Add("Đơn vị");
        data.Columns.Add("Đơn giá (đ)", typeof(decimal));
        data.Columns.Add("Thành tiền (đ)", typeof(decimal));

        int idx = 1;
        foreach (var line in invoice.Lines)
        {
            data.Rows.Add((idx++).ToString(), line.Kind, line.Description, line.Quantity, line.Unit, line.UnitPrice, line.Amount);
        }
        Ui.Bind(grid, data);

        // 4. Khung tổng kết tài chính & chữ ký (Financial Summary & Signatures)
        var pnlSummary = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 220,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White,
            Padding = new Padding(0, 15, 0, 0)
        };
        pnlSummary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        pnlSummary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        var subtotal = invoice.Lines.Sum(l => l.Amount);
        var inWords = Money.ToWords(invoice.Total);

        var lblLeftSummary = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F),
            Text = $"▶ SỐ TIỀN BẰNG CHỮ:\n{inWords}\n\n" +
                   $"▶ Ghi chú: {(string.IsNullOrWhiteSpace(invoice.Notes) ? "Không có ghi chú thêm." : invoice.Notes)}\n\n" +
                   $"Khách hàng                                         Thu ngân / Lễ tân\n" +
                   $"(Ký và ghi rõ họ tên)                             (Ký và ghi rõ họ tên)\n\n\n" +
                   $"{invoice.Customer}                                {invoice.Employee}"
        };

        var balanceLabel = invoice.Balance >= 0 ? "SỐ TIỀN ĐÃ THU THÊM" : "SỐ TIỀN ĐÃ HOÀN KHÁCH";
        var lblRightSummary = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopRight,
            Font = new Font("Segoe UI", 10F),
            Text = $"Cộng tiền phòng & dịch vụ:  {Ui.Currency(subtotal)}\n" +
                   $"Phụ thu (Check-in sớm / Trả muộn):  +{Ui.Currency(invoice.Surcharge)}\n" +
                   $"Giảm giá / Ưu đãi lưu trú:  -{Ui.Currency(invoice.Discount)}\n" +
                   $"------------------------------------------------------\n" +
                   $"TỔNG CỘNG THANH TOÁN:  {Ui.Currency(invoice.Total)}\n" +
                   $"Tiền đặt cọc đã đối trừ:  -{Ui.Currency(invoice.Deposit)}\n" +
                   $"{balanceLabel}:  {Ui.Currency(Math.Abs(invoice.Balance))}\n" +
                   $"Hình thức: {invoice.PaymentMethod} • ĐÃ THANH TOÁN"
        };
        lblRightSummary.ForeColor = Color.FromArgb(15, 23, 42);

        pnlSummary.Controls.Add(lblLeftSummary, 0, 0);
        pnlSummary.Controls.Add(lblRightSummary, 1, 0);

        // 5. Chân trang lời cảm ơn của khách sạn
        var lblFooterNote = new Label
        {
            Dock = DockStyle.Top,
            Height = 45,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9F, FontStyle.Italic),
            ForeColor = Color.FromArgb(100, 116, 139),
            Text = $"Cảm ơn Quý khách đã lựa chọn {settings.HotelName}! Kính chúc Quý khách một chuyến đi vui vẻ & hẹn gặp lại!\nThank you for choosing our hotel! We look forward to welcoming you back!"
        };

        // Gắn các thành phần vào trang giấy hóa đơn
        pnlPaper.Controls.Add(lblFooterNote);
        pnlPaper.Controls.Add(pnlSummary);
        pnlPaper.Controls.Add(grid);
        pnlPaper.Controls.Add(pnlGuestStay);
        pnlPaper.Controls.Add(lineDivider);
        pnlPaper.Controls.Add(pnlHeader);

        pnlScroll.Controls.Add(pnlPaper);

        // 6. Thanh công cụ phía dưới (Bottom Action Toolbar)
        var bar = Ui.Toolbar();
        bar.Dock = DockStyle.Bottom;
        bar.BackColor = Color.White;
        bar.Padding = new Padding(16, 10, 16, 10);

        bar.Controls.Add(Ui.Button("Xuất Excel", () =>
        {
            XuatExcel.PromptExportInvoice(this, invoice, settings);
        }));

        bar.Controls.Add(Ui.Button("Xuất HTML", () =>
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "Hóa đơn HTML|*.html",
                FileName = $"HoaDon_{invoice.Number}.html"
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllText(dialog.FileName, InvoiceExport.Html(invoice, settings), new UTF8Encoding(true));
                Ui.Info("Đã xuất hóa đơn HTML thành công. Bạn có thể mở tệp bằng trình duyệt web để xem.");
            }
        }));

        var close = Ui.Button("Đóng", Close);
        bar.Controls.Add(close);
        CancelButton = close;

        Controls.Add(pnlScroll);
        Controls.Add(bar);
    }
}

internal static class InvoiceExport
{
    public static string Html(InvoiceDocument invoice, AppSettings settings)
    {
        string E(string text) => WebUtility.HtmlEncode(text);
        var nights = Money.Nights(invoice.CheckIn, invoice.CheckOut);
        var inWords = Money.ToWords(invoice.Total);
        var subtotal = invoice.Lines.Sum(l => l.Amount);

        var rows = new StringBuilder();
        int stt = 1;
        foreach (var l in invoice.Lines)
        {
            var tagClass = l.Kind is "Phòng" or "Nâng phòng" ? "kind-room" : "kind-svc";
            rows.Append($"""
                <tr>
                    <td style="text-align:center;">{stt++}</td>
                    <td><span class="kind-tag {tagClass}">{E(l.Kind)}</span></td>
                    <td style="text-align:left; font-weight:500;">{E(l.Description)}</td>
                    <td style="text-align:center;">{l.Quantity:N0}</td>
                    <td style="text-align:center;">{E(l.Unit)}</td>
                    <td style="text-align:right;">{l.UnitPrice:N0} đ</td>
                    <td style="text-align:right; font-weight:600;">{l.Amount:N0} đ</td>
                </tr>
                """);
        }

        var balanceLabel = invoice.Balance >= 0 ? "Số tiền đã thu thêm tại quầy" : "Số tiền đã hoàn lại cho khách";

        return $$"""
            <!doctype html>
            <html lang="vi">
            <head>
            <meta charset="utf-8">
            <title>Hóa đơn {{E(invoice.Number)}} - {{E(settings.HotelName)}}</title>
            <style>
              * { box-sizing: border-box; margin: 0; padding: 0; }
              body {
                font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Arial, sans-serif;
                color: #0f172a;
                background: #f1f5f9;
                padding: 30px 15px;
                line-height: 1.5;
              }
              .folio-card {
                max-width: 900px;
                margin: 0 auto;
                background: #ffffff;
                padding: 40px 48px;
                border-radius: 8px;
                box-shadow: 0 4px 20px rgba(0,0,0,0.06);
                border: 1px solid #e2e8f0;
              }
              .hotel-header {
                display: flex;
                justify-content: space-between;
                align-items: flex-start;
                padding-bottom: 20px;
                border-bottom: 2px solid #0f172a;
              }
              .hotel-brand h1 {
                font-size: 22px;
                letter-spacing: 0.5px;
                color: #0f172a;
                font-weight: 700;
              }
              .hotel-brand .stars {
                color: #d97706;
                font-size: 13px;
                margin: 3px 0 5px 0;
              }
              .hotel-brand p {
                font-size: 13px;
                color: #475569;
                line-height: 1.45;
              }
              .invoice-title-block {
                text-align: right;
              }
              .invoice-title-block h2 {
                font-size: 18px;
                color: #0f172a;
                font-weight: 700;
              }
              .invoice-title-block .meta {
                font-size: 13px;
                color: #475569;
                margin-top: 4px;
              }
              .badge-paid {
                display: inline-block;
                background: #dcfce7;
                color: #15803d;
                font-size: 11px;
                font-weight: 700;
                padding: 3px 10px;
                border-radius: 4px;
                margin-top: 6px;
                text-transform: uppercase;
              }
              .info-grid {
                display: grid;
                grid-template-columns: 1fr 1fr;
                gap: 20px;
                background: #f8fafc;
                border: 1px solid #e2e8f0;
                border-radius: 6px;
                padding: 16px 20px;
                margin: 22px 0;
              }
              .info-col h3 {
                font-size: 12px;
                text-transform: uppercase;
                letter-spacing: 0.5px;
                color: #64748b;
                border-bottom: 1px solid #cbd5e1;
                padding-bottom: 4px;
                margin-bottom: 8px;
              }
              .info-row {
                font-size: 13.5px;
                margin-bottom: 5px;
                display: flex;
              }
              .info-row .lbl {
                width: 120px;
                color: #64748b;
                flex-shrink: 0;
              }
              .info-row .val {
                color: #0f172a;
                font-weight: 500;
              }
              table.folio-table {
                width: 100%;
                border-collapse: collapse;
                margin: 15px 0 25px 0;
              }
              table.folio-table th {
                background: #0f172a;
                color: #ffffff;
                font-size: 12.5px;
                font-weight: 600;
                padding: 10px 12px;
                text-align: right;
              }
              table.folio-table th:first-child, table.folio-table th:nth-child(2), table.folio-table th:nth-child(3) {
                text-align: left;
              }
              table.folio-table td {
                padding: 9px 12px;
                border-bottom: 1px solid #e2e8f0;
                font-size: 13px;
                color: #1e293b;
              }
              table.folio-table tbody tr:nth-child(even) {
                background: #f8fafc;
              }
              .kind-tag {
                display: inline-block;
                font-size: 11px;
                padding: 2px 7px;
                border-radius: 3px;
                font-weight: 600;
              }
              .kind-room { background: #e0f2fe; color: #0369a1; }
              .kind-svc { background: #fef3c7; color: #b45309; }
              .summary-section {
                display: grid;
                grid-template-columns: 1.2fr 1fr;
                gap: 25px;
                margin-top: 10px;
                padding-top: 15px;
                border-top: 1px dashed #cbd5e1;
              }
              .words-box {
                background: #fafaf9;
                border: 1px solid #e7e5e4;
                border-radius: 6px;
                padding: 12px 16px;
                margin-bottom: 12px;
              }
              .words-box .lbl {
                font-size: 11px;
                font-weight: 700;
                text-transform: uppercase;
                color: #78716c;
              }
              .words-box .txt {
                font-size: 13.5px;
                font-style: italic;
                color: #1e3a8a;
                margin-top: 4px;
                font-weight: 600;
              }
              .table-summary {
                width: 100%;
                font-size: 13.5px;
              }
              .table-summary td {
                padding: 5px 0;
              }
              .table-summary .lbl {
                color: #64748b;
                text-align: left;
              }
              .table-summary .val {
                text-align: right;
                font-weight: 600;
                color: #0f172a;
              }
              .table-summary .total-row td {
                border-top: 2px solid #0f172a;
                border-bottom: 2px solid #0f172a;
                padding: 8px 0;
                font-size: 15px;
                font-weight: 700;
              }
              .signatures {
                display: grid;
                grid-template-columns: 1fr 1fr;
                margin-top: 35px;
                text-align: center;
              }
              .sig-title {
                font-size: 13px;
                font-weight: 700;
                color: #0f172a;
                text-transform: uppercase;
              }
              .sig-sub {
                font-size: 11px;
                color: #64748b;
                font-style: italic;
              }
              .sig-space {
                height: 55px;
              }
              .sig-name {
                font-size: 13px;
                font-weight: 600;
                color: #0f172a;
              }
              .hotel-footer {
                margin-top: 30px;
                padding-top: 15px;
                border-top: 1px solid #e2e8f0;
                text-align: center;
                font-size: 12px;
                color: #64748b;
                line-height: 1.6;
              }
              @media print {
                body { background: #fff; padding: 0; }
                .folio-card { box-shadow: none; border: none; padding: 15px 0; }
                @page { margin: 15mm; size: A4; }
              }
            </style>
            </head>
            <body>
            <div class="folio-card">
              <div class="hotel-header">
                <div class="hotel-brand">
                  <h1>{{E(settings.HotelName.ToUpper())}}</h1>
                  <div class="stars">★★★★★ KHÁCH SẠN NGHỈ DƯỠNG & DỊCH VỤ CAO CẤP</div>
                  <p>Địa chỉ: {{E(settings.HotelAddress)}}<br>Hotline: {{E(settings.HotelPhone)}} &bull; MST: 0108899889 &bull; Email: info@ngockhoahotel.vn</p>
                </div>
                <div class="invoice-title-block">
                  <h2>HÓA ĐƠN THANH TOÁN</h2>
                  <div class="meta">Số HĐ: <strong>{{E(invoice.Number)}}</strong> &bull; Phiếu: #{{invoice.Id}}</div>
                  <div class="meta">Ngày lập: {{invoice.IssuedAt:dd/MM/yyyy HH:mm}}<br>Thu ngân: {{E(invoice.Employee)}}</div>
                  <div class="badge-paid">ĐÃ THANH TOÁN / PAID</div>
                </div>
              </div>

              <div class="info-grid">
                <div class="info-col">
                  <h3>Thông tin khách hàng (Guest Details)</h3>
                  <div class="info-row"><span class="lbl">Khách hàng:</span><span class="val"><strong>{{E(invoice.Customer)}}</strong></span></div>
                  <div class="info-row"><span class="lbl">Điện thoại:</span><span class="val">{{E(invoice.Phone)}}</span></div>
                  <div class="info-row"><span class="lbl">CCCD / Hộ chiếu:</span><span class="val">{{(string.IsNullOrWhiteSpace(invoice.IdentityNumber) ? "---" : E(invoice.IdentityNumber))}}</span></div>
                  <div class="info-row"><span class="lbl">Địa chỉ:</span><span class="val">{{(string.IsNullOrWhiteSpace(invoice.Address) ? "Chưa cập nhật" : E(invoice.Address))}}</span></div>
                </div>
                <div class="info-col">
                  <h3>Thông tin lưu trú (Stay Details)</h3>
                  <div class="info-row"><span class="lbl">Nhận phòng:</span><span class="val">{{invoice.CheckIn:dd/MM/yyyy HH:mm}}</span></div>
                  <div class="info-row"><span class="lbl">Trả phòng:</span><span class="val">{{invoice.CheckOut:dd/MM/yyyy HH:mm}}</span></div>
                  <div class="info-row"><span class="lbl">Thời gian:</span><span class="val"><strong>{{nights}} đêm</strong></span></div>
                  <div class="info-row"><span class="lbl">Hình thức:</span><span class="val">{{E(invoice.PaymentMethod)}}</span></div>
                </div>
              </div>

              <table class="folio-table">
                <thead>
                  <tr>
                    <th style="width: 45px; text-align: center;">STT</th>
                    <th style="width: 95px; text-align: left;">Khoản mục</th>
                    <th style="text-align: left;">Nội dung diễn giải</th>
                    <th style="width: 65px; text-align: center;">Số lượng</th>
                    <th style="width: 70px; text-align: center;">Đơn vị</th>
                    <th style="width: 120px; text-align: right;">Đơn giá</th>
                    <th style="width: 135px; text-align: right;">Thành tiền</th>
                  </tr>
                </thead>
                <tbody>
                  {{rows}}
                </tbody>
              </table>

              <div class="summary-section">
                <div>
                  <div class="words-box">
                    <div class="lbl">Số tiền viết bằng chữ:</div>
                    <div class="txt">{{inWords}}</div>
                  </div>
                  <p style="font-size: 12.5px; color: #64748b;">
                    <strong>Ghi chú:</strong> {{(string.IsNullOrWhiteSpace(invoice.Notes) ? "Không có ghi chú thêm." : E(invoice.Notes))}}
                  </p>
                </div>
                <div>
                  <table class="table-summary">
                    <tr><td class="lbl">Cộng tiền phòng & dịch vụ:</td><td class="val">{{subtotal:N0}} đ</td></tr>
                    {{(invoice.Surcharge > 0 ? $"<tr><td class=\"lbl\">Phụ thu (Check-in/out):</td><td class=\"val\">+{invoice.Surcharge:N0} đ</td></tr>" : "")}}
                    {{(invoice.Discount > 0 ? $"<tr><td class=\"lbl\">Giảm giá / Ưu đãi:</td><td class=\"val\">-{invoice.Discount:N0} đ</td></tr>" : "")}}
                    <tr class="total-row"><td class="lbl">TỔNG THANH TOÁN:</td><td class="val">{{invoice.Total:N0}} đ</td></tr>
                    <tr><td class="lbl">Tiền đặt cọc đã đối trừ:</td><td class="val">-{{invoice.Deposit:N0}} đ</td></tr>
                    <tr style="font-size: 14px; font-weight: 700; color: #166534;"><td class="lbl" style="color:#166534;">{{balanceLabel}}:</td><td class="val" style="color:#166534;">{{Math.Abs(invoice.Balance):N0}} đ</td></tr>
                  </table>
                </div>
              </div>

              <div class="signatures">
                <div>
                  <div class="sig-title">Người thanh toán / Khách hàng</div>
                  <div class="sig-sub">(Ký và ghi rõ họ tên)</div>
                  <div class="sig-space"></div>
                  <div class="sig-name">{{E(invoice.Customer)}}</div>
                </div>
                <div>
                  <div class="sig-title">Thu ngân / Người lập phiếu</div>
                  <div class="sig-sub">(Ký và ghi rõ họ tên)</div>
                  <div class="sig-space"></div>
                  <div class="sig-name">{{E(invoice.Employee)}}</div>
                </div>
              </div>

              <div class="hotel-footer">
                <p>Cảm ơn Quý khách đã lựa chọn <strong>{{E(settings.HotelName)}}</strong>! Kính chúc Quý khách một chuyến đi vui vẻ & hẹn gặp lại!</p>
                <p>Thank you for choosing our hotel! We hope you had a pleasant stay and look forward to welcoming you back!</p>
              </div>
            </div>
            </body>
            </html>
            """;
    }
}

internal sealed class InvoicesPage : Panel
{
    private readonly DataGridView grid = Ui.Grid();
    private readonly DateTimePicker from = Ui.Date(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)), to = Ui.Date(DateTime.Today);
    private readonly TextBox search = Ui.Search("Mã hóa đơn, tên, điện thoại");
    private readonly HotelService service;
    private readonly Session user;

    public InvoicesPage(HotelService service, Session user, AppSettings settings)
    {
        this.service = service;
        this.user = user;
        Dock = DockStyle.Fill;

        var filters = Ui.Toolbar();
        filters.Controls.Add(OperationalLayout.Field("Từ ngày", from));
        filters.Controls.Add(OperationalLayout.Field("Đến ngày", to));
        search.Width = 260;
        search.AccessibleName = "Tìm hóa đơn theo mã, tên hoặc điện thoại";
        filters.Controls.Add(search);
        var bar = Ui.Toolbar();
        bar.Controls.Add(Ui.Button("Tìm / tải lại", RefreshData));
        bar.Controls.Add(Ui.Button("Xuất Excel danh sách", () =>
        {
            if (grid.DataSource is not DataTable data || data.Rows.Count == 0)
                throw new InvalidOperationException("Chưa có danh sách hóa đơn để xuất.");
            XuatExcel.PromptExport(this, data, "DanhSachHoaDon_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx", "HoaDon");
        }));

        void Open()
        {
            using var form = new InvoiceForm(service.Invoice(user, Ui.SelectedId(grid)), settings);
            form.ShowDialog(this);
        }

        bar.Controls.Add(Ui.Button("Xem hóa đơn", Open, true));
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Ui.Run(Open); };
        var page = Ui.Page(grid, bar);
        page.Controls.Add(filters);
        Controls.Add(page);
    }

    public void RefreshData()
    {
        if (to.Value.Date < from.Value.Date)
            throw new ArgumentException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
        Ui.Bind(grid, service.Invoices(user, from.Value, to.Value, search.Text));
    }
}
