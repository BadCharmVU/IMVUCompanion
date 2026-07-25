$ErrorActionPreference = "Stop"
$root = "C:\Users\serve\ansel\IMVUCompanion"
$log = "C:\Users\serve\ansel\do097.log"
function L($m){ Add-Content $log $m; Write-Host $m }
Set-Content $log "begin $(Get-Date)"

Set-Location $root
Get-Process IMVUCompanion -EA SilentlyContinue | Stop-Process -Force -EA SilentlyContinue
Start-Sleep 1

$pub = Join-Path $root "publish097"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force -EA SilentlyContinue }
L "dotnet publish"
& dotnet publish "$root\IMVUCompanion.csproj" -c Release -r win-x64 --self-contained true -o $pub /p:PublishReadyToRun=true /p:DebugType=None /p:DebugSymbols=false *>> $log 2>&1
if (-not (Test-Path "$pub\IMVUCompanion.exe")) { L "FAIL publish"; exit 1 }
L "publish ok"

$iscc = @(
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { L "FAIL no iscc"; exit 1 }

$rel = Join-Path $root "release"
New-Item -ItemType Directory -Force -Path $rel | Out-Null
L "iscc"
& $iscc "/O$rel" "/DPublishDir=..\publish097" "/DAppVersion=0.9.7" "$root\installer\IMVUCompanion.iss" *>> $log 2>&1
$setup = Join-Path $rel "IMVUCompanion-Setup-v0.9.7.exe"
if (-not (Test-Path $setup)) { L "FAIL no setup"; exit 1 }
L ("size=" + (Get-Item $setup).Length)

L "git"
git -C $root add -A
git -C $root status --short *>> $log
git -C $root commit -m "Release v0.9.7 Bot Settings list focus and delete polish" *>> $log 2>&1
git -C $root push origin HEAD *>> $log 2>&1
git -C $root tag -f v0.9.7 *>> $log 2>&1
git -C $root push origin v0.9.7 --force *>> $log 2>&1

L "finish"
& powershell -NoProfile -ExecutionPolicy Bypass -File "$root\scripts\Finish-Release.ps1" -Version "0.9.7" -SkipPreflight *>> $log 2>&1

L "clean"
Remove-Item $pub -Recurse -Force -EA SilentlyContinue
Get-ChildItem $root -Directory | Where-Object { $_.Name -match '^publish' } | ForEach-Object { Remove-Item $_.FullName -Recurse -Force -EA SilentlyContinue }
Remove-Item "$root\scripts\_ship097.ps1","$root\scripts\_ship097.cmd","$root\scripts\_do097.ps1" -Force -EA SilentlyContinue
if (Test-Path "$root\release\IMVUCompanion-Setup-v0.9.6.exe") {
  Remove-Item "$root\release\IMVUCompanion-Setup-v0.9.6.exe" -Force -EA SilentlyContinue
}

git -C $root add -A
git -C $root commit -m "chore: v0.9.7 sha256 cleanup" *>> $log 2>&1
git -C $root push origin HEAD *>> $log 2>&1

L "DONE"
Get-Content "$root\version.json" *>> $log
exit 0
