# Cập nhật bản web trên máy chủ IIS (chạy TRÊN MÁY CHỦ, quyền Administrator)
#
#   .\deploy-iis.ps1 -Package D:\Releases\EEMC-WorkManager-Web-1.2.0.zip
#
# Các bước: sao lưu CSDL → đưa trang vào chế độ bảo trì (app_offline.htm) → giữ bản cũ để quay lại
# → chép bản mới (KHÔNG đụng dữ liệu, appsettings.Production.json) → chạy lại → kiểm tra /health
# → nếu lỗi thì tự quay lại bản cũ.
param(
    [Parameter(Mandatory = $true)][string]$Package,
    [string]$SiteDir = "C:\inetpub\eemc-workmanager",
    [string]$DataDir = "D:\EEMC-WorkManager-Data",
    [string]$AppPool = "EEMC-WorkManager",
    [string]$HealthUrl = "http://localhost/health",
    [int]$KeepReleases = 5
)
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

function Log($m, $c = 'Gray') { Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) -ForegroundColor $c }

if (-not (Test-Path $Package)) { throw "Không thấy gói $Package" }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$releases = Join-Path (Split-Path $SiteDir) "eemc-workmanager-releases"
New-Item -ItemType Directory -Force $releases, $SiteDir | Out-Null

# 1. Sao lưu CSDL (sao chép nóng an toàn bằng bản sao lưu của chính phần mềm sẽ có sẵn hằng ngày;
#    ở đây chép thêm một bản ngay trước khi cập nhật)
$db = Join-Path $DataDir "workmanager.db"
if (Test-Path $db) {
    $bk = Join-Path $DataDir "SaoLuu"
    New-Item -ItemType Directory -Force $bk | Out-Null
    Log "Dừng ứng dụng để sao lưu CSDL..."
    Stop-WebAppPool -Name $AppPool -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    foreach ($ext in "", "-wal", "-shm") {
        if (Test-Path "$db$ext") { Copy-Item "$db$ext" (Join-Path $bk "EEMC-WM-$stamp-truoc-cap-nhat.db$ext") }
    }
    Log "Đã sao lưu CSDL vào $bk" 'Green'
}

# 2. Chế độ bảo trì
Set-Content (Join-Path $SiteDir "app_offline.htm") '<html><body style="font-family:Segoe UI;padding:40px"><h2>EEMC Work Manager đang cập nhật</h2><p>Vui lòng quay lại sau khoảng 1 phút.</p></body></html>' -Encoding UTF8
Stop-WebAppPool -Name $AppPool -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

# 3. Giữ bản cũ để quay lại
$prev = Join-Path $releases "ban-cu-$stamp"
if ((Get-ChildItem $SiteDir -Exclude app_offline.htm | Measure-Object).Count -gt 0) {
    robocopy $SiteDir $prev /MIR /XF app_offline.htm /NFL /NDL /NJH /NJS | Out-Null
    Log "Đã giữ bản cũ tại $prev"
}

# 4. Chép bản mới (giữ cấu hình máy chủ)
$tmp = Join-Path $env:TEMP "eemc-deploy-$stamp"
Expand-Archive $Package $tmp -Force
robocopy $tmp $SiteDir /MIR /XF appsettings.Production.json app_offline.htm /XD App_Data logs /NFL /NDL /NJH /NJS | Out-Null
Remove-Item $tmp -Recurse -Force
Log "Đã chép bản mới."

# 5. Chạy lại và kiểm tra
Remove-Item (Join-Path $SiteDir "app_offline.htm") -Force
Start-WebAppPool -Name $AppPool
$ok = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Seconds 3
    try { if ((Invoke-WebRequest $HealthUrl -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200) { $ok = $true; break } } catch { }
}

if ($ok) {
    Log "CẬP NHẬT THÀNH CÔNG." 'Green'
    Get-ChildItem $releases -Directory | Sort-Object CreationTime -Descending | Select-Object -Skip $KeepReleases | Remove-Item -Recurse -Force
    exit 0
}

# 6. Quay lại bản cũ
Log "Kiểm tra /health thất bại — QUAY LẠI BẢN CŨ." 'Red'
if (Test-Path $prev) {
    Stop-WebAppPool -Name $AppPool -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    robocopy $prev $SiteDir /MIR /XF appsettings.Production.json /XD App_Data logs /NFL /NDL /NJH /NJS | Out-Null
    Start-WebAppPool -Name $AppPool
    Log "Đã quay lại bản cũ. Xem nhật ký lỗi trong $DataDir\logs và stdout của IIS." 'Yellow'
}
exit 1
