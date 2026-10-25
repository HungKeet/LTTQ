using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Hotel.Core;

namespace Hotel.WinForms;

internal static class XuatExcel
{
    public static void ExportDataTable(DataTable data, string filePath, string sheetName = "DuLieu")
    {
        if (data is null || data.Columns.Count == 0)
            throw new InvalidOperationException("Không có dữ liệu bảng để xuất.");

        // Chuẩn hóa tên sheet (tối đa 31 ký tự, không chứa ký tự cấm: \ / ? * [ ])
        sheetName = string.Join("", sheetName.Where(ch => !":\\/?*[]".Contains(ch))).Trim();
        if (string.IsNullOrWhiteSpace(sheetName)) sheetName = "Sheet1";
        if (sheetName.Length > 30) sheetName = sheetName[..30];

        // Tính toán độ rộng cột tự động
        var colWidths = new double[data.Columns.Count];
        for (int c = 0; c < data.Columns.Count; c++)
        {
            var headerLen = data.Columns[c].ColumnName.Length;
            colWidths[c] = Math.Max(headerLen + 4, 12);
        }

        // Tạo nội dung sheetData
        var sbSheet = new StringBuilder(16384);
        sbSheet.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
            """);

        // Ghi cấu hình độ rộng cột
        sbSheet.Append("<cols>");
        for (int c = 0; c < data.Columns.Count; c++)
        {
            // Đo sơ bộ độ dài dữ liệu để mở rộng cột nếu cần
            for (int r = 0; r < Math.Min(data.Rows.Count, 30); r++)
            {
                var valStr = Convert.ToString(data.Rows[r][c]) ?? "";
                if (valStr.Length + 4 > colWidths[c])
                    colWidths[c] = Math.Min(valStr.Length + 4, 45);
            }
            var w = colWidths[c].ToString("0.0", CultureInfo.InvariantCulture);
            sbSheet.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w}\" customWidth=\"1\"/>");
        }
        sbSheet.Append("</cols>");

        sbSheet.Append("<sheetData>");

        // Dòng 1: Tiêu đề cột (Header)
        sbSheet.Append("<row r=\"1\">");
        for (int c = 0; c < data.Columns.Count; c++)
        {
            var colRef = ColumnLetter(c) + "1";
            var text = EscapeXml(data.Columns[c].ColumnName);
            sbSheet.Append($"<c r=\"{colRef}\" s=\"1\" t=\"inlineStr\"><is><t>{text}</t></is></c>");
        }
        sbSheet.Append("</row>");

        // Dòng 2..N: Dữ liệu
        for (int r = 0; r < data.Rows.Count; r++)
        {
            int rowIdx = r + 2;
            sbSheet.Append($"<row r=\"{rowIdx}\">");
            var row = data.Rows[r];

            for (int c = 0; c < data.Columns.Count; c++)
            {
                var colRef = ColumnLetter(c) + rowIdx;
                var raw = row[c];

                if (raw is null || raw is DBNull)
                {
                    sbSheet.Append($"<c r=\"{colRef}\" s=\"0\"/>");
                }
                else if (raw is decimal dec)
                {
                    var isCurrency = data.Columns[c].ColumnName.Contains("Giá", StringComparison.OrdinalIgnoreCase)
                                  || data.Columns[c].ColumnName.Contains("Tiền", StringComparison.OrdinalIgnoreCase)
                                  || data.Columns[c].ColumnName.Contains("Doanh thu", StringComparison.OrdinalIgnoreCase)
                                  || data.Columns[c].ColumnName.Contains("Cọc", StringComparison.OrdinalIgnoreCase);
                    var styleId = isCurrency ? "2" : "0";
                    sbSheet.Append($"<c r=\"{colRef}\" s=\"{styleId}\"><v>{dec.ToString(CultureInfo.InvariantCulture)}</v></c>");
                }
                else if (raw is double or float)
                {
                    var d = Convert.ToDouble(raw);
                    sbSheet.Append($"<c r=\"{colRef}\" s=\"0\"><v>{d.ToString(CultureInfo.InvariantCulture)}</v></c>");
                }
                else if (raw is int or long or short or byte)
                {
                    sbSheet.Append($"<c r=\"{colRef}\" s=\"3\"><v>{raw}</v></c>");
                }
                else if (raw is DateTime dt)
                {
                    var oaDate = dt.ToOADate().ToString("0.000000", CultureInfo.InvariantCulture);
                    sbSheet.Append($"<c r=\"{colRef}\" s=\"4\"><v>{oaDate}</v></c>");
                }
                else if (raw is bool b)
                {
                    var text = b ? "Hoạt động" : "Khóa";
                    sbSheet.Append($"<c r=\"{colRef}\" s=\"5\" t=\"inlineStr\"><is><t>{text}</t></is></c>");
                }
                else
                {
                    var text = EscapeXml(raw.ToString());
                    sbSheet.Append($"<c r=\"{colRef}\" s=\"0\" t=\"inlineStr\"><is><t>{text}</t></is></c>");
                }
            }
            sbSheet.Append("</row>");
        }

        sbSheet.Append("</sheetData></worksheet>");

        // Đóng gói OpenXML zip
        if (File.Exists(filePath)) File.Delete(filePath);

        using var zip = new ZipArchive(File.Open(filePath, FileMode.Create), ZipArchiveMode.Create);

        void AddXml(string entryName, string xml)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }

        // 1. [Content_Types].xml
        AddXml("[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);

        // 2. _rels/.rels
        AddXml("_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

        // 3. xl/_rels/workbook.xml.rels
        AddXml("xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);

        // 4. xl/workbook.xml
        AddXml("xl/workbook.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="{EscapeXml(sheetName)}" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """);

        // 5. xl/styles.xml
        AddXml("xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="2">
                <numFmt numFmtId="164" formatCode="#,##0 &quot;đ&quot;"/>
                <numFmt numFmtId="165" formatCode="dd/mm/yyyy hh:mm"/>
              </numFmts>
              <fonts count="2">
                <font><sz val="11"/><name val="Segoe UI"/></font>
                <font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Segoe UI"/></font>
              </fonts>
              <fills count="3">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FF334155"/></patternFill></fill>
              </fills>
              <borders count="2">
                <border><left/><right/><top/><bottom/><diagonal/></border>
                <border>
                  <left style="thin"><color rgb="FFE2E8F0"/></left>
                  <right style="thin"><color rgb="FFE2E8F0"/></right>
                  <top style="thin"><color rgb="FFE2E8F0"/></top>
                  <bottom style="thin"><color rgb="FFE2E8F0"/></bottom>
                </border>
              </borders>
              <cellStyleXfs count="1">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
              </cellStyleXfs>
              <cellXfs count="6">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
                <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
                <xf numFmtId="3" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
                <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
              </cellXfs>
            </styleSheet>
            """);

        // 6. xl/worksheets/sheet1.xml
        AddXml("xl/worksheets/sheet1.xml", sbSheet.ToString());
    }

    public static void ExportHotelInvoice(InvoiceDocument invoice, AppSettings settings, string filePath)
    {
        var nights = Money.Nights(invoice.CheckIn, invoice.CheckOut);
        var table = new DataTable("HoaDon");
        table.Columns.Add("STT");
        table.Columns.Add("Khoản mục");
        table.Columns.Add("Nội dung diễn giải");
        table.Columns.Add("Số lượng", typeof(decimal));
        table.Columns.Add("Đơn vị");
        table.Columns.Add("Đơn giá", typeof(decimal));
        table.Columns.Add("Thành tiền", typeof(decimal));

        // Header metadata information inside the table rows
        table.Rows.Add("", settings.HotelName.ToUpper(), "HÓA ĐƠN THANH TOÁN DỊCH VỤ KHÁCH SẠN (HOTEL GUEST FOLIO)", DBNull.Value, "", DBNull.Value, DBNull.Value);
        table.Rows.Add("", "Khách sạn:", $"{settings.HotelAddress} • Hotline: {settings.HotelPhone} • MST: 0108899889", DBNull.Value, "", DBNull.Value, DBNull.Value);
        table.Rows.Add("", "Số hóa đơn:", $"{invoice.Number} (Mã đặt phòng #{invoice.Id})", DBNull.Value, "Ngày lập:", DBNull.Value, DBNull.Value);
        table.Rows.Add("", "Khách hàng:", $"{invoice.Customer} • Điện thoại: {invoice.Phone} • CCCD: {invoice.IdentityNumber}", DBNull.Value, "Thu ngân:", DBNull.Value, DBNull.Value);
        table.Rows.Add("", "Lưu trú:", $"Từ {invoice.CheckIn:dd/MM/yyyy HH:mm} đến {invoice.CheckOut:dd/MM/yyyy HH:mm} ({nights} đêm)", DBNull.Value, "Thanh toán:", DBNull.Value, DBNull.Value);
        table.Rows.Add("", "-----------------", "--------------------------------------------------------", DBNull.Value, "------", DBNull.Value, DBNull.Value);

        // Line items
        int stt = 1;
        foreach (var l in invoice.Lines)
        {
            table.Rows.Add((stt++).ToString(), l.Kind, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.Amount);
        }

        // Summary items
        table.Rows.Add("", "-----------------", "--------------------------------------------------------", DBNull.Value, "------", DBNull.Value, DBNull.Value);
        table.Rows.Add("", "TỔNG KẾT", "Cộng tiền phòng và dịch vụ", DBNull.Value, "", DBNull.Value, invoice.Lines.Sum(x => x.Amount));
        if (invoice.Surcharge > 0)
            table.Rows.Add("", "PHỤ THU", "Phụ thu (Check-in sớm / Check-out muộn / Thêm người)", DBNull.Value, "", DBNull.Value, invoice.Surcharge);
        if (invoice.Discount > 0)
            table.Rows.Add("", "GIẢM GIÁ", "Giảm trừ / Khuyến mãi", DBNull.Value, "", DBNull.Value, -invoice.Discount);
        table.Rows.Add("", "TỔNG CỘNG", "TỔNG THANH TOÁN (GRAND TOTAL)", DBNull.Value, "", DBNull.Value, invoice.Total);
        table.Rows.Add("", "ĐÃ ĐẶT CỌC", "Tiền đặt cọc đối trừ", DBNull.Value, "", DBNull.Value, -invoice.Deposit);
        table.Rows.Add("", "THỰC THU", (invoice.Balance >= 0 ? "SỐ TIỀN THU THÊM TẠI QUẦY" : "SỐ TIỀN HOÀN LẠI KHÁCH"), DBNull.Value, "", DBNull.Value, Math.Abs(invoice.Balance));
        table.Rows.Add("", "BẰNG CHỮ", Money.ToWords(invoice.Total), DBNull.Value, "", DBNull.Value, DBNull.Value);
        table.Rows.Add("", "HÌNH THỨC", $"{invoice.PaymentMethod} • TRẠNG THÁI: ĐÃ THANH TOÁN", DBNull.Value, "", DBNull.Value, DBNull.Value);
        if (!string.IsNullOrWhiteSpace(invoice.Notes))
            table.Rows.Add("", "GHI CHÚ", invoice.Notes, DBNull.Value, "", DBNull.Value, DBNull.Value);
        table.Rows.Add("", "XÁC NHẬN", "Khách hàng ký: (Ký, họ tên)               Thu ngân ký: " + invoice.Employee, DBNull.Value, "", DBNull.Value, DBNull.Value);

        ExportDataTable(table, filePath, invoice.Number);
    }

    public static void PromptExportInvoice(IWin32Window owner, InvoiceDocument invoice, AppSettings settings)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Microsoft Excel (*.xlsx)|*.xlsx|Hóa đơn HTML (*.html)|*.html",
            FileName = $"HoaDon_{invoice.Number}_{DateTime.Now:yyyyMMdd}.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true
        };

        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return;

        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (ext == ".html")
        {
            File.WriteAllText(dialog.FileName, InvoiceExport.Html(invoice, settings), new UTF8Encoding(true));
        }
        else
        {
            ExportHotelInvoice(invoice, settings, dialog.FileName);
        }

        Ui.Info($"Đã xuất hóa đơn khách sạn thành công:\n{dialog.FileName}");
    }

    public static void PromptExport(IWin32Window owner, DataTable data, string defaultFileName, string sheetName = "DuLieu")
    {
        if (data is null || data.Rows.Count == 0)
            throw new InvalidOperationException("Chưa có dữ liệu để xuất.");

        using var dialog = new SaveFileDialog
        {
            Filter = "Microsoft Excel (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
            FileName = defaultFileName,
            DefaultExt = "xlsx",
            AddExtension = true
        };

        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return;

        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (ext == ".csv")
        {
            File.WriteAllText(dialog.FileName, ReportsPage.Csv(data), new UTF8Encoding(true));
        }
        else
        {
            ExportDataTable(data, dialog.FileName, sheetName);
        }

        Ui.Info($"Đã xuất tệp thành công:\n{dialog.FileName}");
    }

    private static string ColumnLetter(int colIndex)
    {
        string letter = "";
        while (colIndex >= 0)
        {
            letter = (char)('A' + (colIndex % 26)) + letter;
            colIndex = (colIndex / 26) - 1;
        }
        return letter;
    }

    private static string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return SecurityElement.Escape(value) ?? "";
    }
}
