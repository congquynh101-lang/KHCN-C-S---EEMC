# Cài đặt EEMC KHCN Work Manager cho người dùng hiện tại (không cần quyền quản trị).
# Chạy bằng tệp "CaiDat.bat" đi kèm.
param(
    [switch]$Silent,          # Không hỏi, không mở phần mềm sau khi cài (dùng cho kiểm thử/triển khai hàng loạt)
    [switch]$AutoStart        # Tự chạy khi đăng nhập Windows (khi dùng -Silent)
)

$ErrorActionPreference = 'Stop'
$AppName    = 'EEMC KHCN Work Manager'
$ExeName    = 'EEMC.WorkManager.exe'
$Version    = '1.1.0'
$Src        = Split-Path -Parent $MyInvocation.MyCommand.Path
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\EEMC WorkManager'
$StartMenu  = Join-Path ([Environment]::GetFolderPath('Programs')) 'EEMC KHCN Work Manager'
$Desktop    = [Environment]::GetFolderPath('Desktop')
$Startup    = [Environment]::GetFolderPath('Startup')
$UninstKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\EEMC.KHCN.WorkManager'

function Say($msg, $color = 'Gray') { Write-Host $msg -ForegroundColor $color }

try {
    Say ""
    Say "=== CÀI ĐẶT $AppName $Version ===" 'Cyan'
    Say ""

    $srcExe = Join-Path $Src $ExeName
    if (-not (Test-Path $srcExe)) { throw "Không tìm thấy $ExeName cạnh tệp cài đặt. Hãy giải nén toàn bộ tệp ZIP trước khi cài." }

    if ([Environment]::Is64BitOperatingSystem -eq $false) { throw "Phần mềm yêu cầu Windows 64-bit." }

    # Đóng phần mềm đang chạy (khi cài đè / nâng cấp). Dữ liệu đã được lưu sau mỗi thao tác.
    $running = Get-Process -Name 'EEMC.WorkManager' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) {
        Say "Đang đóng phiên bản đang chạy để cập nhật..." 'Yellow'
        # Nút đóng chỉ thu xuống khay hệ thống, nên cần dừng hẳn tiến trình (dữ liệu đã ghi sau mỗi thao tác)
        $running | Where-Object { -not $_.HasExited } | Stop-Process -Force
        $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    }

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Say "Sao chép chương trình vào: $InstallDir"
    $dst = Join-Path $InstallDir $ExeName
    for ($i = 1; ; $i++) {
        try { Copy-Item $srcExe $dst -Force; break }
        catch {
            if ($i -ge 20) { throw "Không ghi được $dst (tệp đang bị khóa). Hãy thoát phần mềm ở khay hệ thống rồi cài lại." }
            Start-Sleep -Milliseconds 500
        }
    }
    foreach ($f in 'HUONG_DAN_SU_DUNG.md', 'GoCaiDat.ps1', 'GoCaiDat.bat') {
        $p = Join-Path $Src $f
        if (Test-Path $p) { Copy-Item $p (Join-Path $InstallDir $f) -Force }
    }
    Unblock-File -Path (Join-Path $InstallDir $ExeName) -ErrorAction SilentlyContinue

    $exe = Join-Path $InstallDir $ExeName
    $shell = New-Object -ComObject WScript.Shell
    function New-Shortcut($path, $target, $desc, $arguments = '') {
        $s = $shell.CreateShortcut($path)
        $s.TargetPath = $target
        $s.Arguments = $arguments
        $s.WorkingDirectory = $InstallDir
        $s.Description = $desc
        $s.IconLocation = "$exe,0"
        $s.Save()
    }

    New-Item -ItemType Directory -Force -Path $StartMenu | Out-Null
    New-Shortcut (Join-Path $StartMenu "$AppName.lnk") $exe 'Trợ lý quản lý công việc KH&CN và ĐMST'
    New-Shortcut (Join-Path $StartMenu 'Go cai dat EEMC KHCN Work Manager.lnk') (Join-Path $InstallDir 'GoCaiDat.bat') 'Gỡ cài đặt'
    New-Shortcut (Join-Path $Desktop "$AppName.lnk") $exe 'Trợ lý quản lý công việc KH&CN và ĐMST'
    Say "Đã tạo lối tắt trên Desktop và Start Menu."

    # Tự chạy khi đăng nhập để nhắc việc
    $autoLink = Join-Path $Startup "$AppName.lnk"
    $wantAuto = $AutoStart.IsPresent
    if (-not $Silent) {
        $ans = Read-Host "Tự khởi động phần mềm khi mở máy để không bỏ lỡ nhắc việc? (C = Có / K = Không) [C]"
        $wantAuto = ($ans -eq '' -or $ans -match '^[CcYy]')
    }
    if ($wantAuto) { New-Shortcut $autoLink $exe 'Tự chạy EEMC KHCN Work Manager'; Say "Đã bật tự khởi động cùng Windows." }
    elseif (Test-Path $autoLink) { Remove-Item $autoLink -Force }

    # Đăng ký vào "Apps & features" của Windows để có thể gỡ cài đặt
    New-Item -Path $UninstKey -Force | Out-Null
    $size = [int]((Get-ChildItem $InstallDir -Recurse | Measure-Object Length -Sum).Sum / 1KB)
    $props = @{
        DisplayName     = $AppName
        DisplayVersion  = $Version
        Publisher       = 'EEMC – Thư ký KH&CN'
        DisplayIcon     = $exe
        InstallLocation = $InstallDir
        UninstallString = "`"$InstallDir\GoCaiDat.bat`""
        NoModify        = 1
        NoRepair        = 1
        EstimatedSize   = $size
    }
    foreach ($k in $props.Keys) {
        $type = if ($props[$k] -is [int]) { 'DWord' } else { 'String' }
        New-ItemProperty -Path $UninstKey -Name $k -Value $props[$k] -PropertyType $type -Force | Out-Null
    }

    Say ""
    Say "CÀI ĐẶT THÀNH CÔNG." 'Green'
    Say "Dữ liệu sẽ được lưu tại: $env:LOCALAPPDATA\EEMC\WorkManager"
    Say "Bản sao lưu tự động: Documents\EEMC WorkManager\SaoLuu"
    Say ""
    if (-not $Silent) {
        Start-Process $exe
        Read-Host "Nhấn Enter để đóng cửa sổ này"
    }
    exit 0
}
catch {
    Say ""
    Say "CÀI ĐẶT KHÔNG THÀNH CÔNG: $($_.Exception.Message)" 'Red'
    if (-not $Silent) { Read-Host "Nhấn Enter để đóng" }
    exit 1
}
