# Chuẩn bị máy chủ Windows Server/IIS lần đầu (chạy TRÊN MÁY CHỦ, quyền Administrator)
# Yêu cầu đã cài: IIS + ASP.NET Core Hosting Bundle 8.0 (xem docs\TRIEN_KHAI_WEB.md, bước 2)
param(
    [string]$SiteName = "EEMC-WorkManager",
    [string]$SiteDir = "C:\inetpub\eemc-workmanager",
    [string]$DataDir = "D:\EEMC-WorkManager-Data",
    [string]$HostName = "",          # ví dụ: congviec.eemc.local (để trống = dùng mọi tên)
    [int]$Port = 80
)
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

New-Item -ItemType Directory -Force $SiteDir, $DataDir | Out-Null

# Nhóm ứng dụng riêng, không dùng mã quản lý .NET Framework (ASP.NET Core tự chạy)
if (-not (Test-Path "IIS:\AppPools\$SiteName")) { New-WebAppPool -Name $SiteName | Out-Null }
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name startMode -Value "AlwaysRunning"          # để nhắc việc chạy liên tục
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)

if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
    New-Website -Name $SiteName -PhysicalPath $SiteDir -ApplicationPool $SiteName -Port $Port -HostHeader $HostName | Out-Null
}
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true

# Quyền ghi thư mục dữ liệu cho tài khoản nhóm ứng dụng
$acct = "IIS AppPool\$SiteName"
icacls $DataDir /grant "${acct}:(OI)(CI)M" | Out-Null

# Cấu hình riêng của máy chủ (không bị ghi đè khi cập nhật)
$cfg = Join-Path $SiteDir "appsettings.Production.json"
if (-not (Test-Path $cfg)) {
    @{ EEMC = @{ DataDir = $DataDir } } | ConvertTo-Json | Set-Content $cfg -Encoding UTF8
}
Write-Host "Đã tạo website $SiteName → $SiteDir, dữ liệu tại $DataDir" -ForegroundColor Green
Write-Host "Tiếp theo: chạy deploy-iis.ps1 -Package <gói .zip> -SiteDir $SiteDir -DataDir $DataDir -AppPool $SiteName"
