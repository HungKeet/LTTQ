$connectionString = "Server=.\SQLEXPRESS;Database=HotelManagementBTL;Integrated Security=True;TrustServerCertificate=True"
$conn = [System.Data.SqlClient.SqlConnection]::new($connectionString)
$conn.Open()

$tables = @(
    @{ Name = "RoomTypes"; Identity = $true; Columns = @("Id","Name","Capacity","Rate","Active") },
    @{ Name = "Rooms"; Identity = $true; Columns = @("Id","Number","Floor","RoomTypeId","OperationalStatus","Active") },
    @{ Name = "Customers"; Identity = $true; Columns = @("Id","FullName","Phone","IdentityNumber","Address","Active") },
    @{ Name = "Employees"; Identity = $true; Columns = @("Id","FullName","Phone","Position","Active") },
    @{ Name = "Users"; Identity = $true; Columns = @("Id","EmployeeId","Username","PasswordHash","Role","Active") },
    @{ Name = "Services"; Identity = $true; Columns = @("Id","Name","Unit","Price","Active") },
    @{ Name = "Bookings"; Identity = $true; Columns = @("Id","CustomerId","EmployeeId","Arrival","Departure","Guests","Deposit","RefundedDeposit","Status","ActualCheckIn","ActualCheckOut","Notes","CreatedAt") },
    @{ Name = "BookingRooms"; Identity = $false; Columns = @("BookingId","RoomId","NightlyRate","Capacity") },
    @{ Name = "ServiceUsages"; Identity = $true; Columns = @("Id","BookingId","ServiceId","ServiceName","Unit","Quantity","UnitPrice","UsedAt") },
    @{ Name = "Invoices"; Identity = $true; Columns = @("Id","BookingId","EmployeeId","CustomerName","CustomerPhone","CustomerIdentity","CustomerAddress","EmployeeName","IssuedAt","CheckIn","CheckOut","Surcharge","Discount","Deposit","Total","PaymentMethod","Notes") },
    @{ Name = "InvoiceLines"; Identity = $true; Columns = @("Id","InvoiceId","Kind","Description","Quantity","Unit","UnitPrice") },
    @{ Name = "AuditLog"; Identity = $true; Columns = @("Id","UserId","Action","Detail","CreatedAt") }
)

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("-- Du lieu day du xuat tu database HotelManagementBTL tren may cuc bo")
[void]$sb.AppendLine("-- Thoi diem xuat: " + (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))
[void]$sb.AppendLine("USE HotelManagementBTL;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("SET QUOTED_IDENTIFIER ON;")
[void]$sb.AppendLine("SET ANSI_NULLS ON;")
[void]$sb.AppendLine("SET XACT_ABORT ON;")
[void]$sb.AppendLine("BEGIN TRANSACTION;")
[void]$sb.AppendLine("GO")

# Disable constraints and triggers
[void]$sb.AppendLine("-- Vo hieu hoa trigger bat bien de nap du lieu")
[void]$sb.AppendLine("IF OBJECT_ID('dbo.TR_Invoices_Immutable') IS NOT NULL DISABLE TRIGGER dbo.TR_Invoices_Immutable ON dbo.Invoices;")
[void]$sb.AppendLine("IF OBJECT_ID('dbo.TR_InvoiceLines_Immutable') IS NOT NULL DISABLE TRIGGER dbo.TR_InvoiceLines_Immutable ON dbo.InvoiceLines;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("-- Xoa du lieu cu de nap du lieu day du")
[void]$sb.AppendLine("DELETE FROM AuditLog;")
[void]$sb.AppendLine("DELETE FROM InvoiceLines;")
[void]$sb.AppendLine("DELETE FROM Invoices;")
[void]$sb.AppendLine("DELETE FROM ServiceUsages;")
[void]$sb.AppendLine("DELETE FROM BookingRooms;")
[void]$sb.AppendLine("DELETE FROM Bookings;")
[void]$sb.AppendLine("DELETE FROM Services;")
[void]$sb.AppendLine("DELETE FROM Users;")
[void]$sb.AppendLine("DELETE FROM Employees;")
[void]$sb.AppendLine("DELETE FROM Customers;")
[void]$sb.AppendLine("DELETE FROM Rooms;")
[void]$sb.AppendLine("DELETE FROM RoomTypes;")
[void]$sb.AppendLine("GO")

function Format-SqlValue($val) {
    if ($null -eq $val -or [System.DBNull]::Value.Equals($val)) { return "NULL" }
    if ($val -is [bool]) { return $(if ($val) { "1" } else { "0" }) }
    if ($val -is [DateTime]) { return "'" + $val.ToString("yyyy-MM-dd HH:mm:ss.fffffff") + "'" }
    if ($val -is [int] -or $val -is [long] -or $val -is [decimal] -or $val -is [double]) { return $val.ToString([System.Globalization.CultureInfo]::InvariantCulture) }
    $escaped = $val.ToString().Replace("'", "''")
    return "N'" + $escaped + "'"
}

foreach ($t in $tables) {
    $tName = $t.Name
    $cols = $t.Columns
    $colList = ($cols | ForEach-Object { "[$_]" }) -join ","
    
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT $colList FROM [$tName] ORDER BY " + $cols[0]
    $adapter = [System.Data.SqlClient.SqlDataAdapter]::new($cmd)
    $dt = [System.Data.DataTable]::new()
    [void]$adapter.Fill($dt)
    
    if ($dt.Rows.Count -gt 0) {
        [void]$sb.AppendLine("-- Table: $tName ($($dt.Rows.Count) dong)")
        if ($t.Identity) {
            [void]$sb.AppendLine("SET IDENTITY_INSERT [$tName] ON;")
        }
        
        $batch = 0
        $valuesList = [System.Collections.Generic.List[string]]::new()
        foreach ($row in $dt.Rows) {
            $rowValues = @()
            for ($i = 0; $i -lt $cols.Count; $i++) {
                $rowValues += Format-SqlValue $row[$i]
            }
            $valuesList.Add("(" + ($rowValues -join ",") + ")")
            
            if ($valuesList.Count -ge 100) {
                [void]$sb.AppendLine("INSERT INTO [$tName] ($colList) VALUES")
                [void]$sb.AppendLine(($valuesList -join ",`r`n") + ";")
                $valuesList.Clear()
            }
        }
        if ($valuesList.Count -gt 0) {
            [void]$sb.AppendLine("INSERT INTO [$tName] ($colList) VALUES")
            [void]$sb.AppendLine(($valuesList -join ",`r`n") + ";")
            $valuesList.Clear()
        }
        
        if ($t.Identity) {
            [void]$sb.AppendLine("SET IDENTITY_INSERT [$tName] OFF;")
        }
        [void]$sb.AppendLine("GO")
    }
}

[void]$sb.AppendLine("-- Bat lai trigger bao ve hoa don")
[void]$sb.AppendLine("IF OBJECT_ID('dbo.TR_Invoices_Immutable') IS NOT NULL ENABLE TRIGGER dbo.TR_Invoices_Immutable ON dbo.Invoices;")
[void]$sb.AppendLine("IF OBJECT_ID('dbo.TR_InvoiceLines_Immutable') IS NOT NULL ENABLE TRIGGER dbo.TR_InvoiceLines_Immutable ON dbo.InvoiceLines;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("COMMIT TRANSACTION;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("PRINT N'Da nap thanh cong toan bo du lieu tu may cuc bo!';")
[void]$sb.AppendLine("GO")

$outputPath = "C:\Users\huyhu\Downloads\QuanLyKhachSan_SourceCode\CoSoDuLieu\DuLieuDayDu.sql"
[System.IO.File]::WriteAllText($outputPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($true))
Write-Output "Da xuat file thanh cong: $outputPath (size: $((Get-Item $outputPath).Length) bytes)"

$conn.Close()
