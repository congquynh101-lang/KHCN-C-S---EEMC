# Cài đặt EEMC Work Manager bản WEB lên một máy Windows (máy chủ hoặc máy tính để luôn bật)
# Chạy bằng "CaiDat-Web.bat" (sẽ hỏi quyền quản trị). Không cần cài .NET.
#
# Sau khi cài:
#   - Phần mềm chạy nền như dịch vụ Windows "EEMC Work Manager Web", tự khởi động cùng máy.
#   - Mở trên máy này: http://localhost:5080
#   - Mở từ máy/điện thoại khác cùng mạng: http://<tên-máy-hoặc-IP>:5080 (nếu đồng ý mở tường lửa)
param(
    [int]$Port = 5080,
    [switch]$Silent,
    [switch]$OpenFirewall
)
$ErrorActionPreference = 'Stop'
$ServiceName = 'EEMCWorkManagerWeb'
$DisplayName = 'EEMC Work Manager Web'
$Version     = '2.0.0'
$Src         = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'app'
$InstallDir  = Join-Path $env:ProgramFiles 'EEMC WorkManager Web'
$DataDir     = Join-Path $env:ProgramData 'EEMC\WorkManagerWeb'
$FwRule      = 'EEMC Work Manager Web'

function Say($m, $c = 'Gray') { Write-Host $m -ForegroundColor $c }

try {
    Say ""
    Say "=== CÀI ĐẶT $DisplayName $Version ===" 'Cyan'

    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { throw "Cần chạy với quyền quản trị (nhấp phải CaiDat-Web.bat → Run as administrator)." }
    if (-not (Test-Path (Join-Path $Src 'EEMC.WM.Web.exe'))) { throw "Không thấy thư mục 'app'. Hãy giải nén TOÀN BỘ tệp ZIP trước khi cài." }
    if (-not [Environment]::Is64BitOperatingSystem) { throw "Yêu cầu Windows 64-bit." }

    # Cổng đã bị chương trình khác dùng?
    $busy = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($busy -and -not $existing) { throw "Cổng $Port đang được chương trình khác sử dụng. Chạy lại với tham số -Port <số khác>, ví dụ: CaiDat-Web.bat -Port 5090" }

    # Cập nhật: dừng dịch vụ cũ (dữ liệu được giữ nguyên)
    if ($existing) {
        Say "Đang dừng phiên bản đang chạy để cập nhật..." 'Yellow'
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        Start-Sleep -Seconds 2
    }

    New-Item -ItemType Directory -Force $InstallDir, $DataDir | Out-Null
    Say "Sao chép chương trình vào: $InstallDir"
    robocopy $Src $InstallDir /MIR /XF appsettings.Production.json /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Sao chép thất bại (mã $LASTEXITCODE)." }

    # Cấu hình riêng của máy này (không bị ghi đè khi cập nhật)
    $cfg = Join-Path $InstallDir 'appsettings.Production.json'
    @{ Urls = "http://0.0.0.0:$Port"; EEMC = @{ DataDir = $DataDir } } | ConvertTo-Json | Set-Content $cfg -Encoding UTF8

    # Dịch vụ chạy bằng tài khoản hạn chế LocalService, chỉ được ghi vào thư mục dữ liệu
    icacls $DataDir /grant "NT AUTHORITY\LocalService:(OI)(CI)M" | Out-Null
    $exe = Join-Path $InstallDir 'EEMC.WM.Web.exe'
    if (-not $existing) {
        New-Service -Name $ServiceName -DisplayName $DisplayName -BinaryPathName "`"$exe`"" -StartupType Automatic `
            -Description 'Trợ lý quản lý công việc KH&CN và ĐMST – EEMC (bản web)' | Out-Null
        sc.exe config $ServiceName obj= "NT AUTHORITY\LocalService" | Out-Null
    }
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', $null, 'Machine') | Out-Null

    # Tường lửa: cho máy khác trong mạng nội bộ truy cập
    $open = $OpenFirewall.IsPresent
    if (-not $Silent) {
        $ans = Read-Host "Cho phép máy tính/điện thoại KHÁC trong mạng nội bộ truy cập (mở cổng $Port trên tường lửa, chỉ mạng Domain/Private)? (C/K) [C]"
        $open = ($ans -eq '' -or $ans -match '^[CcYy]')
    }
    Get-NetFirewallRule -DisplayName $FwRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    if ($open) {
        New-NetFirewallRule -DisplayName $FwRule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Domain, Private | Out-Null
        Say "Đã mở cổng $Port cho mạng nội bộ (Domain/Private)."
    }

    Say "Khởi động dịch vụ..."
    Start-Service $ServiceName
    $ok = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 2
        try { if ((Invoke-WebRequest "http://localhost:$Port/health" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200) { $ok = $true; break } } catch { }
    }
    if (-not $ok) { throw "Dịch vụ không phản hồi. Xem nhật ký lỗi tại $DataDir\logs" }

    $ips = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' }).IPAddress
    Say ""
    Say "CÀI ĐẶT THÀNH CÔNG." 'Green'
    Say "  Trên máy này:            http://localhost:$Port"
    if ($open) {
        Say "  Từ máy khác cùng mạng:   http://$($env:COMPUTERNAME):$Port"
        foreach ($ip in $ips) { Say "                           http://${ip}:$Port" }
    }
    Say "  Dữ liệu và sao lưu:      $DataDir"
    Say "  Lần đầu mở sẽ hiện trang tạo tài khoản quản trị."
    Say ""
    Say "Lưu ý: kết nối dạng http (chưa mã hóa) chỉ nên dùng trong mạng nội bộ. Dùng thật lâu dài nên nhờ CNTT cấu hình HTTPS (xem TRIEN_KHAI_WEB.md)." 'Yellow'
    if (-not $Silent) {
        Start-Process "http://localhost:$Port"
        Read-Host "Nhấn Enter để đóng"
    }
    exit 0
}
catch {
    Say ""
    Say "CÀI ĐẶT KHÔNG THÀNH CÔNG: $($_.Exception.Message)" 'Red'
    if (-not $Silent) { Read-Host "Nhấn Enter để đóng" }
    exit 1
}
