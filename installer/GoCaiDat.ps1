# Gỡ cài đặt EEMC KHCN Work Manager. Mặc định GIỮ LẠI dữ liệu công việc và các bản sao lưu.
param(
    [switch]$Silent,       # Không hỏi
    [switch]$DeleteData    # Xóa luôn dữ liệu (chỉ khi dùng -Silent; khi chạy thường sẽ được hỏi)
)

$ErrorActionPreference = 'Stop'
$AppName    = 'EEMC KHCN Work Manager'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\EEMC WorkManager'
$DataDir    = Join-Path $env:LOCALAPPDATA 'EEMC\WorkManager'
$StartMenu  = Join-Path ([Environment]::GetFolderPath('Programs')) 'EEMC KHCN Work Manager'
$Desktop    = [Environment]::GetFolderPath('Desktop')
$Startup    = [Environment]::GetFolderPath('Startup')
$UninstKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\EEMC.KHCN.WorkManager'

try {
    Write-Host ""
    Write-Host "=== GỠ CÀI ĐẶT $AppName ===" -ForegroundColor Cyan
    if (-not $Silent) {
        $ans = Read-Host "Gỡ cài đặt phần mềm? (C = Có / K = Không) [K]"
        if ($ans -notmatch '^[CcYy]') { exit 0 }
    }

    Get-Process -Name 'EEMC.WorkManager' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { $_ | Stop-Process -Force; $_ | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue }

    foreach ($lnk in (Join-Path $Desktop "$AppName.lnk"), (Join-Path $Startup "$AppName.lnk")) {
        if (Test-Path $lnk) { Remove-Item $lnk -Force }
    }
    if (Test-Path $StartMenu) { Remove-Item $StartMenu -Recurse -Force }
    if (Test-Path $UninstKey) { Remove-Item $UninstKey -Recurse -Force }

    $removeData = $DeleteData.IsPresent
    if (-not $Silent -and (Test-Path $DataDir)) {
        Write-Host ""
        Write-Host "Dữ liệu công việc đang nằm tại: $DataDir" -ForegroundColor Yellow
        $ans = Read-Host "XÓA LUÔN dữ liệu công việc? Không thể hoàn tác (gõ XOA để xóa, Enter để giữ lại)"
        $removeData = ($ans -ceq 'XOA')
    }
    if ($removeData -and (Test-Path $DataDir)) { Remove-Item $DataDir -Recurse -Force; Write-Host "Đã xóa dữ liệu." }
    else { Write-Host "Đã GIỮ LẠI dữ liệu tại $DataDir (cài lại sẽ dùng tiếp)." }

    # Xóa thư mục chương trình (kể cả chính tệp gỡ cài đặt) sau khi script kết thúc
    if (Test-Path $InstallDir) {
        Get-ChildItem $InstallDir -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notin 'GoCaiDat.bat' } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        $cleanup = "cd /d `"%TEMP%`" & for /l %i in (1,1,10) do (if exist `"$InstallDir`" (timeout /t 1 /nobreak >nul & rmdir /s /q `"$InstallDir`" 2>nul))"
        Start-Process -WindowStyle Hidden -WorkingDirectory $env:TEMP -FilePath cmd.exe -ArgumentList "/c $cleanup"
    }
    Write-Host ""
    Write-Host "ĐÃ GỠ CÀI ĐẶT." -ForegroundColor Green
    if (-not $Silent) { Read-Host "Nhấn Enter để đóng" }
    exit 0
}
catch {
    Write-Host "GỠ CÀI ĐẶT KHÔNG THÀNH CÔNG: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $Silent) { Read-Host "Nhấn Enter để đóng" }
    exit 1
}
