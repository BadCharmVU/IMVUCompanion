$ErrorActionPreference = "Continue"
$root = "C:\Users\serve\ansel\IMVUCompanion"
Set-Location $root
$log = Join-Path $env:TEMP "imvu-ship095.log"
function L($m) { Add-Content $log $m; Write-Host $m }
Set-Content $log "start $(Get-Date)"

Get-Process IMVUCompanion -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 2

L "1 Publish-Release"
& "$root\scripts\Publish-Release.ps1" *>> $log 2>&1
if (-not (Test-Path "$root\release\IMVUCompanion-Setup-v0.9.5.exe")) {
    L "FAIL no installer"
    exit 1
}
$len = (Get-Item "$root\release\IMVUCompanion-Setup-v0.9.5.exe").Length
L "installer size=$len"
if ($len -lt 40MB) { L "FAIL installer too small"; exit 1 }

L "2 git commit"
git -C $root add -A
git -C $root status --short *>> $log
git -C $root commit -m "Release v0.9.5 secure auto-update and Bot Settings UI" *>> $log 2>&1
git -C $root push origin HEAD *>> $log 2>&1

L "3 tag"
git -C $root tag -f v0.9.5 *>> $log 2>&1
git -C $root push origin v0.9.5 --force *>> $log 2>&1

L "4 Finish-Release"
& "$root\scripts\Finish-Release.ps1" -Version "0.9.5" -SkipPreflight *>> $log 2>&1

L "5 Clean"
& "$root\scripts\Clean-Stale.ps1" *>> $log 2>&1
Get-ChildItem $root -Directory | Where-Object { $_.Name -match '^publish' -or $_.Name -eq 'release-build' } |
    ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue; L "rm $($_.Name)" }

Remove-Item "$root\scripts\_ship095.ps1" -Force -ErrorAction SilentlyContinue
if (Test-Path "$root\scripts\_ship095.ps1") { git -C $root rm -f scripts/_ship095.ps1 2>$null; git -C $root commit -m "chore: drop temp ship script" 2>$null; git -C $root push origin HEAD 2>$null }

L "DONE"
Get-Content "$root\version.json" *>> $log
gh release view v0.9.5 --repo BadCharmVU/IMVUCompanion *>> $log 2>&1
exit 0
