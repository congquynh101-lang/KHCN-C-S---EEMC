# Gỡ cài đặt EEMC Work Manager bản WEB. Mặc định GIỮ LẠI dữ liệu.
param([switch]$Silent, [switch]$DeleteData)
$ErrorActionPreference = 'Stop'
$ServiceName = 'EEMCWorkManagerWeb'
$InstallDir  = Join-Path $env:ProgramFiles 'EEMC WorkManager Web'
$DataDir     = Join-Path $env:ProgramData 'EEMC\WorkManagerWeb'
$FwRule      = 'EEMC Work Manager Web'

try {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { throw "Cần chạy với quyền quản trị." }
    Write-Host "=== GỠ CÀI ĐẶT EEMC Work Manager Web ===" -ForegroundColor Cyan
    if (-not $Silent) {
        $ans = Read-Host "Gỡ cài đặt? (C/K) [K]"
        if ($ans -notmatch '^[CcYy]') { exit 0 }
    }
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        sc.exe delete $ServiceName | Out-Null
    }
    Get-NetFirewallRule -DisplayName $FwRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Start-Sleep -Seconds 2
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }

    $remove = $DeleteData.IsPresent
    if (-not $Silent -and (Test-Path $DataDir)) {
        Write-Host "Dữ liệu (công việc, tệp đính kèm, sao lưu) đang ở: $DataDir" -ForegroundColor Yellow
        $ans = Read-Host "XÓA LUÔN dữ liệu? Không thể hoàn tác (gõ XOA để xóa, Enter để giữ lại)"
        $remove = ($ans -ceq 'XOA')
    }
    if ($remove -and (Test-Path $DataDir)) { Remove-Item $DataDir -Recurse -Force; Write-Host "Đã xóa dữ liệu." }
    else { Write-Host "Đã GIỮ LẠI dữ liệu tại $DataDir (cài lại sẽ dùng tiếp)." }
    Write-Host "ĐÃ GỠ CÀI ĐẶT." -ForegroundColor Green
    if (-not $Silent) { Read-Host "Nhấn Enter để đóng" }
}
catch {
    Write-Host "GỠ CÀI ĐẶT KHÔNG THÀNH CÔNG: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $Silent) { Read-Host "Nhấn Enter để đóng" }
    exit 1
}
