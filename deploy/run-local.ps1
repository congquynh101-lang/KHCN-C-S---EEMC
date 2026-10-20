# Chạy thử bản web ngay trên máy này: mở http://localhost:5080
# Dữ liệu lưu tại %LOCALAPPDATA%\EEMC\WorkManagerWeb (không nằm trong OneDrive, tránh xung đột đồng bộ)
$root = Split-Path -Parent $PSScriptRoot
$data = Join-Path $env:LOCALAPPDATA "EEMC\WorkManagerWeb"
New-Item -ItemType Directory -Force $data | Out-Null
$dotnet = if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" } else { "dotnet" }
Write-Host "Mở trình duyệt: http://localhost:5080  (Ctrl+C để dừng)" -ForegroundColor Cyan
Start-Process "http://localhost:5080"
& $dotnet run --project (Join-Path $root "src\EEMC.WM.Web") -c Release --no-launch-profile --urls http://localhost:5080 "--EEMC:DataDir=$data"
