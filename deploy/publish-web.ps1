# Đóng gói bản web để triển khai (chạy trên máy phát triển hoặc trong CI)
# Kết quả: artifacts\web\  và  artifacts\EEMC-WorkManager-Web-<phiên bản>.zip
param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $Version = (Select-Xml -Path "src\EEMC.WM.Web\EEMC.WM.Web.csproj" -XPath "//Version").Node.InnerText
    if (-not $Version) { $Version = "1.0.0" }
}
$out = Join-Path $root "artifacts\web"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Write-Host "Chạy kiểm thử..." -ForegroundColor Cyan
dotnet test tests\EEMC.WM.Tests -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Kiểm thử thất bại — dừng đóng gói." }

Write-Host "Đóng gói bản web $Version..." -ForegroundColor Cyan
dotnet publish src\EEMC.WM.Web -c $Configuration -o $out -p:Version=$Version -p:EnvironmentName=Production
if ($LASTEXITCODE -ne 0) { throw "Đóng gói thất bại." }

# Không bao giờ mang dữ liệu thử nghiệm theo bản phát hành
Remove-Item (Join-Path $out "App_Data") -Recurse -Force -ErrorAction SilentlyContinue

$zip = Join-Path $root "artifacts\EEMC-WorkManager-Web-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Xong: $zip" -ForegroundColor Green
