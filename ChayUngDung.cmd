@echo off
cd /d "%~dp0"
if exist "BanChay\QuanLyKhachSan.exe" (
  start "" "BanChay\QuanLyKhachSan.exe"
) else (
  dotnet run --project "MaNguon\GiaoDien\GiaoDien.csproj"
)
