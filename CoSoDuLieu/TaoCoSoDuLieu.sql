-- @DatabaseName is supplied by the application or the installation script.
-- Physical names must be unique: different LocalDB instances can share a folder.
DECLARE @Resource nvarchar(255) = N'HotelBTL.Initialize.' + @DatabaseName;
DECLARE @LockResult int;
EXEC @LockResult = sys.sp_getapplock @Resource=@Resource, @LockMode='Exclusive',
    @LockOwner='Session', @LockTimeout=60000;
IF @LockResult < 0 THROW 51000, N'Không thể khóa khởi tạo dữ liệu. Vui lòng thử lại.', 1;

BEGIN TRY
    IF DB_ID(@DatabaseName) IS NULL
    BEGIN
        DECLARE @DataPath nvarchar(4000) = CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath'));
        DECLARE @LogPath nvarchar(4000) = CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultLogPath'));
        IF NULLIF(@DataPath, N'') IS NULL OR NULLIF(@LogPath, N'') IS NULL
            THROW 51000, N'Không xác định được thư mục dữ liệu của SQL Server.', 1;
        IF RIGHT(@DataPath, 1) NOT IN (N'\', N'/') SET @DataPath += N'\';
        IF RIGHT(@LogPath, 1) NOT IN (N'\', N'/') SET @LogPath += N'\';

        DECLARE @Stem nvarchar(200) = @DatabaseName + N'_' + REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'');
        DECLARE @DataFile nvarchar(4000) = @DataPath + @Stem + N'.mdf';
        DECLARE @LogFile nvarchar(4000) = @LogPath + @Stem + N'_log.ldf';
        DECLARE @Sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName)
            + N' ON PRIMARY (NAME = ' + QUOTENAME(@DatabaseName, '''')
            + N', FILENAME = N''' + REPLACE(@DataFile, N'''', N'''''') + N''')'
            + N' LOG ON (NAME = ' + QUOTENAME(@DatabaseName + N'_log', '''')
            + N', FILENAME = N''' + REPLACE(@LogFile, N'''', N'''''') + N''')';
        EXEC sys.sp_executesql @Sql;
    END;
    EXEC sys.sp_releaseapplock @Resource=@Resource, @LockOwner='Session';
END TRY
BEGIN CATCH
    EXEC sys.sp_releaseapplock @Resource=@Resource, @LockOwner='Session';
    THROW;
END CATCH;
