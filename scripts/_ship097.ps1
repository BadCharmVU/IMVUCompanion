$ErrorActionPreference = "Continue"
$root = "C:\Users\serve\ansel\IMVUCompanion"
Set-Location $root
$log = Join-Path $env:TEMP "imvu-ship097.log"
function L($m) { Add-Content $log $m; Write-Host $m }
Set-Content $log "start"

Get-Process IMVUCompanion -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 2

L "Publish"
& "$root\scripts\Publish-Release.ps1" *>> $log 2>&1
$setup = "$root\release\IMVUCompanion-Setup-v0.9.7.exe"
if (-not (Test-Path $setup)) { L "FAIL no setup"; exit 1 }
if ((Get-Item $setup).Length -lt 40MB) { L "FAIL small"; exit 1 }
L ("size=" + (Get-Item $setup).Length)

L "git"
git -C $root add -A
git -C $root commit -m "Release v0.9.7 Bot Settings list focus and delete polish" *>> $log 2>&1
git -C $root push origin HEAD *>> $log 2>&1
git -C $root tag -f v0.9.7 *>> $log 2>&1
git -C $root push origin v0.9.7 --force *>> $log 2>&1

L "Finish"
& "$root\scripts\Finish-Release.ps1" -Version "0.9.7" -SkipPreflight *>> $log 2>&1

L "Clean"
& "$root\scripts\Clean-Stale.ps1" *>> $log 2>&1
Get-ChildItem $root -Directory | Where-Object { $_.Name -match '^publish' } | ForEach-Object {
    Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}
Remove-Item "$root\scripts\_ship097.ps1" -Force -ErrorAction SilentlyContinue
git -C $root add -A
git -C $root commit -m "chore: version.json sha256 for v0.9.7" *>> $log 2>&1
git -C $root push origin HEAD *>> $log 2>&1

L "DONE"
Get-Content "$root\version.json" *>> $log
gh release view v0.9.7 --repo BadCharmVU/IMVUCompanion *>> $log 2>&1
exit 0
