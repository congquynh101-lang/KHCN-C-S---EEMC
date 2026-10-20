# Dong goi EEMC Work Manager Web cho NAS Synology khong co Docker (linux-arm, ARM 32-bit, vi du DS215j).
# Ket qua: BanCaiDat\nas-linux-arm gom app.part00.. (moi phan duoi 9 MB, tai len duoc qua File Station),
# app.sha256, install.sh, start.sh. Huong dan: docs/TRIEN_KHAI_SYNOLOGY.md muc 11.
param([string]$Out = (Join-Path $PSScriptRoot '..\..\BanCaiDat\nas-linux-arm'))
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$pub = Join-Path $env:TEMP 'eemc-linux-arm'
$tgz = Join-Path $env:TEMP 'eemc-app-linux-arm.tar.gz'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish (Join-Path $root 'src\EEMC.WM.Web\EEMC.WM.Web.csproj') -c Release -r linux-arm --self-contained true -o $pub --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish loi' }

New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem $Out -Filter 'app.part*' | Remove-Item
tar -czf $tgz -C $pub .
if ($LASTEXITCODE -ne 0) { throw 'tar loi' }

$bytes = [IO.File]::ReadAllBytes($tgz)
$chunk = 9000000
for ($i = 0; $i * $chunk -lt $bytes.Length; $i++) {
    $len = [Math]::Min($chunk, $bytes.Length - $i * $chunk)
    $part = New-Object byte[] $len
    [Array]::Copy($bytes, $i * $chunk, $part, 0, $len)
    [IO.File]::WriteAllBytes((Join-Path $Out ('app.part{0:D2}' -f $i)), $part)
}
$sha = (Get-FileHash $tgz -Algorithm SHA256).Hash.ToLower()
[IO.File]::WriteAllText((Join-Path $Out 'app.sha256'), "$sha  app.tar.gz`n")
Copy-Item (Join-Path $PSScriptRoot 'install.sh'), (Join-Path $PSScriptRoot 'start.sh') $Out -Force
Remove-Item $tgz
"Xong: {0} phan, {1:N1} MB, sha256 {2}" -f $i, ($bytes.Length / 1MB), $sha
"Thu muc: $((Resolve-Path $Out).Path)"
