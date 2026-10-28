[CmdletBinding()]
param(
    [string]$ConnectionString,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $configPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'MaNguon\GiaoDien\CauHinh.json'
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $ConnectionString = $config.ConnectionString
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw 'ConnectionString is required.' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $PSScriptRoot 'DuLieuDayDu.sql' }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
# Microsoft.Data.SqlClient emits aliases that Windows PowerShell's older provider does not accept.
$options = [System.Data.Common.DbConnectionStringBuilder]::new()
$options.set_ConnectionString($ConnectionString)
if ($options.ContainsKey('Trust Server Certificate')) {
    $options['TrustServerCertificate'] = $options['Trust Server Certificate']
    [void]$options.Remove('Trust Server Certificate')
}
if ($options.ContainsKey('Encrypt')) {
    switch ([string]$options['Encrypt']) {
        'Mandatory' { $options['Encrypt'] = $true }
        'Optional' { $options['Encrypt'] = $false }
        'Strict' { throw 'Encrypt=Strict requires a newer SQL client; this exporter will not downgrade the requested encryption.' }
    }
} else {
    $options['Encrypt'] = $true
}
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($options.get_ConnectionString())
if ([string]::IsNullOrWhiteSpace($builder.InitialCatalog)) { throw 'ConnectionString must select a database.' }
$quotedDatabase = '[' + $builder.InitialCatalog.Replace(']', ']]') + ']'

$tables = @(
    @{ Name = 'RoomTypes'; Identity = $true; Columns = @('Id','Name','Capacity','Rate','Active') },
    @{ Name = 'Rooms'; Identity = $true; Columns = @('Id','Number','Floor','RoomTypeId','OperationalStatus','Active') },
    @{ Name = 'Customers'; Identity = $true; Columns = @('Id','FullName','Phone','IdentityNumber','Address','Active') },
    @{ Name = 'Employees'; Identity = $true; Columns = @('Id','FullName','Phone','Position','Active') },
    @{ Name = 'Users'; Identity = $true; Columns = @('Id','EmployeeId','Username','PasswordHash','Role','Active') },
    @{ Name = 'Services'; Identity = $true; Columns = @('Id','Name','Unit','Price','Active') },
    @{ Name = 'Bookings'; Identity = $true; Columns = @('Id','CustomerId','EmployeeId','Arrival','Departure','Guests','Deposit','RefundedDeposit','Status','ActualCheckIn','ActualCheckOut','Notes','CreatedAt') },
    @{ Name = 'BookingRooms'; Identity = $true; Columns = @('StayId','BookingId','RoomId','NightlyRate','Capacity') },
    @{ Name = 'RoomUpgrades'; Identity = $true; Columns = @('Id','StayId','FromRoomId','ToRoomId','FromRoomNumber','ToRoomNumber','OldNightlyRate','NewNightlyRate','UpgradedAt','EmployeeId') },
    @{ Name = 'ServiceUsages'; Identity = $true; Columns = @('Id','BookingId','ServiceId','ServiceName','Unit','Quantity','UnitPrice','UsedAt') },
    @{ Name = 'Invoices'; Identity = $true; Columns = @('Id','BookingId','EmployeeId','CustomerName','CustomerPhone','CustomerIdentity','CustomerAddress','EmployeeName','IssuedAt','CheckIn','CheckOut','Surcharge','Discount','Deposit','Total','PaymentMethod','Notes') },
    @{ Name = 'InvoiceLines'; Identity = $true; Columns = @('Id','InvoiceId','Kind','Description','Quantity','Unit','UnitPrice') },
    @{ Name = 'AuditLog'; Identity = $true; Columns = @('Id','UserId','Action','Detail','CreatedAt') }
)

function Format-SqlValue($value) {
    if ($null -eq $value -or [System.DBNull]::Value.Equals($value)) { return 'NULL' }
    if ($value -is [bool]) { if ($value) { return '1' } else { return '0' } }
    if ($value -is [DateTime]) { return "'" + $value.ToString('yyyy-MM-ddTHH:mm:ss.fffffff', [System.Globalization.CultureInfo]::InvariantCulture) + "'" }
    if ($value -is [int] -or $value -is [long] -or $value -is [decimal] -or $value -is [double]) {
        return $value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }
    return "N'" + $value.ToString().Replace("'", "''") + "'"
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('-- Snapshot schema v2: includes stable room stays and paid upgrade history.')
[void]$sb.AppendLine('-- Initialize an empty target with CaiDat.sql; change USE only when restoring to another database.')
[void]$sb.AppendLine('-- Existing paid upgrade history is protected: use a fresh target or a full database backup restore.')
[void]$sb.AppendLine('-- Exported: ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))
[void]$sb.AppendLine("USE $quotedDatabase;")
[void]$sb.AppendLine('GO')
[void]$sb.AppendLine(@"
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
DECLARE @ActiveIdentity sysname = NULL;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult=sys.sp_getapplock @Resource=N'HotelBTL.Write', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
    IF @LockResult<0 THROW 51010, N'Hotel is busy; retry the import later.', 1;
    IF COL_LENGTH(N'dbo.BookingRooms',N'StayId') IS NULL OR OBJECT_ID(N'dbo.RoomUpgrades') IS NULL
        THROW 51011, N'Initialize the target with the current CaiDat.sql before importing schema v2 data.', 1;
    DECLARE @HasUpgrades bit = 0;
    IF OBJECT_ID(N'dbo.RoomUpgrades') IS NOT NULL
        EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.RoomUpgrades WITH(HOLDLOCK)) SET @found=1;', N'@found bit OUTPUT', @found=@HasUpgrades OUTPUT;
    IF @HasUpgrades=1
        THROW 51012, N'Target contains paid room upgrades. Import into a fresh initialized database or restore a full database backup; existing upgrade history will not be erased.', 1;

    IF OBJECT_ID(N'dbo.TR_Invoices_Immutable') IS NOT NULL DISABLE TRIGGER dbo.TR_Invoices_Immutable ON dbo.Invoices;
    IF OBJECT_ID(N'dbo.TR_InvoiceLines_Immutable') IS NOT NULL DISABLE TRIGGER dbo.TR_InvoiceLines_Immutable ON dbo.InvoiceLines;
    DELETE FROM dbo.AuditLog;
    DELETE FROM dbo.InvoiceLines;
    DELETE FROM dbo.Invoices;
    DELETE FROM dbo.ServiceUsages;
    DELETE FROM dbo.BookingRooms;
    DELETE FROM dbo.Bookings;
    DELETE FROM dbo.Services;
    DELETE FROM dbo.Users;
    DELETE FROM dbo.Employees;
    DELETE FROM dbo.Customers;
    DELETE FROM dbo.Rooms;
    DELETE FROM dbo.RoomTypes;
"@)

$conn = [System.Data.SqlClient.SqlConnection]::new($builder.get_ConnectionString())
$transaction = $null
try {
    $conn.Open()
    $transaction = $conn.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    $lockCommand = $conn.CreateCommand()
    try {
        $lockCommand.Transaction = $transaction
        $lockCommand.CommandTimeout = 30
        $lockCommand.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'HotelBTL.Write', @LockMode='Shared', @LockOwner='Transaction', @LockTimeout=10000; IF @r<0 THROW 51010, N'Hotel is busy; retry the export later.', 1; IF COL_LENGTH(N'dbo.BookingRooms',N'StayId') IS NULL OR OBJECT_ID(N'dbo.RoomUpgrades') IS NULL THROW 51011, N'Run the current application schema initialization before exporting.', 1;"
        [void]$lockCommand.ExecuteNonQuery()
    } finally { $lockCommand.Dispose() }

    foreach ($table in $tables) {
        $tableName = $table.Name
        $columns = $table.Columns
        $columnList = ($columns | ForEach-Object { '[' + $_ + ']' }) -join ','
        $command = $conn.CreateCommand()
        $adapter = $null
        try {
            $command.Transaction = $transaction
            $command.CommandTimeout = 60
            $command.CommandText = "SELECT $columnList FROM dbo.[$tableName] ORDER BY [" + $columns[0] + ']'
            $adapter = [System.Data.SqlClient.SqlDataAdapter]::new($command)
            $data = [System.Data.DataTable]::new()
            [void]$adapter.Fill($data)
            if ($data.Rows.Count -eq 0) { continue }
            [void]$sb.AppendLine("    -- Table: $tableName ($($data.Rows.Count) rows)")
            if ($table.Identity) {
                [void]$sb.AppendLine("    SET IDENTITY_INSERT dbo.[$tableName] ON;")
                [void]$sb.AppendLine("    SET @ActiveIdentity=N'$tableName';")
            }
            $values = [System.Collections.Generic.List[string]]::new()
            foreach ($row in $data.Rows) {
                $rowValues = for ($i = 0; $i -lt $columns.Count; $i++) { Format-SqlValue $row[$i] }
                $values.Add('(' + ($rowValues -join ',') + ')')
                if ($values.Count -ge 100) {
                    [void]$sb.AppendLine("    INSERT INTO dbo.[$tableName] ($columnList) VALUES")
                    [void]$sb.AppendLine(($values -join (',' + [Environment]::NewLine)) + ';')
                    $values.Clear()
                }
            }
            if ($values.Count -gt 0) {
                [void]$sb.AppendLine("    INSERT INTO dbo.[$tableName] ($columnList) VALUES")
                [void]$sb.AppendLine(($values -join (',' + [Environment]::NewLine)) + ';')
            }
            if ($table.Identity) {
                [void]$sb.AppendLine("    SET IDENTITY_INSERT dbo.[$tableName] OFF;")
                [void]$sb.AppendLine('    SET @ActiveIdentity=NULL;')
            }
        } finally {
            if ($null -ne $adapter) { $adapter.Dispose() }
            $command.Dispose()
        }
    }
    $transaction.Commit()
    $transaction.Dispose()
    $transaction = $null
} catch {
    if ($null -ne $transaction) { $transaction.Rollback() }
    throw
} finally {
    if ($null -ne $transaction) { $transaction.Dispose() }
    $conn.Dispose()
}

[void]$sb.AppendLine(@"
    IF OBJECT_ID(N'dbo.TR_Invoices_Immutable') IS NOT NULL ENABLE TRIGGER dbo.TR_Invoices_Immutable ON dbo.Invoices;
    IF OBJECT_ID(N'dbo.TR_InvoiceLines_Immutable') IS NOT NULL ENABLE TRIGGER dbo.TR_InvoiceLines_Immutable ON dbo.InvoiceLines;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    IF @ActiveIdentity IS NOT NULL
    BEGIN
        DECLARE @IdentityOff nvarchar(300)=N'SET IDENTITY_INSERT dbo.' + QUOTENAME(@ActiveIdentity) + N' OFF;';
        EXEC(@IdentityOff);
    END;
    THROW;
END CATCH;
PRINT N'Data imported successfully, including paid room upgrades.';
GO
"@)
$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
[void][System.IO.Directory]::CreateDirectory($outputDirectory)
[System.IO.File]::WriteAllText($OutputPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($true))
Write-Output "Exported: $OutputPath ($((Get-Item -LiteralPath $OutputPath).Length) bytes)"
