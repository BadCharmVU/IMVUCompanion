# Removes stale build clutter. Safe anytime.
# KEEPS:
#   bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe  (THE only local test exe)
#   release\IMVUCompanion-Setup-v*.exe                         (shipped installers only, optional)
#   source, scripts, installer\, obj\ (rebuild ok)
$ErrorActionPreference = "Continue"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$removed = @()

function Remove-IfExists([string]$path) {
    if (-not (Test-Path $path)) { return }
    try {
        Remove-Item $path -Recurse -Force -ErrorAction Stop
        $script:removed += $path
        Write-Host "  removed $path"
    } catch {
        Write-Warning "Could not remove $path : $_"
    }
}

Write-Host "==> Cleaning stale folders under $root"
Write-Host "    KEEP ONLY: bin\Release\net8.0-windows10.0.19041.0\  (daily test)"
Write-Host ""

# Debug builds — never used for local testing
Remove-IfExists (Join-Path $root "bin\Debug")

# RID publish under bin (dotnet publish -r win-x64) — NOT the daily test path
Remove-IfExists (Join-Path $root "bin\Release\net8.0-windows10.0.19041.0\win-x64")

# All publish / scratch / locked-installer workarounds
Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -eq "publish" -or
        $_.Name -match '^publish\.' -or
        $_.Name -match '^release-' -or
        $_.Name -eq "rfresh" -or
        $_.Name -eq "release-ok" -or
        $_.Name -eq "release-fresh" -or
        $_.Name -eq "release-build" -or
        $_.Name -eq "release2" -or
        $_.Name -eq "release-v071"
    } |
    ForEach-Object { Remove-IfExists $_.FullName }

# Stray logs / temp build noise at repo root
foreach ($f in @(
    "publish-log.txt", "build-log.txt", "iscc-log.txt",
    "build-err.txt", "build-out.txt", "ship-log.txt"
)) {
    Remove-IfExists (Join-Path $root $f)
}

# One-off ship helper scripts (should never stay in scripts\)
Get-ChildItem (Join-Path $root "scripts") -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -match '^ship-0' -or
        $_.Name -match '^git-push-0' -or
        $_.Name -eq "run-ship.cmd" -or
        $_.Name -eq "iscc-once.cmd" -or
        $_.Name -match '^Finish-Release-v' -or
        $_.Name -match '^Ship-v' -or
        $_.Name -match '^Finish-v'
    } |
    ForEach-Object { Remove-IfExists $_.FullName }

# Incomplete / locked installer stubs in release\ (keep full-size setups only)
if (Test-Path (Join-Path $root "release")) {
    Get-ChildItem (Join-Path $root "release") -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like "IMVUCompanion-Setup-*.exe" -and
            ($_.Length -lt 20MB -or $_.Name -match '\.old|\.locked|\.stale')
        } |
        ForEach-Object { Remove-IfExists $_.FullName }

    # Keep only the current csproj version installer (drop older Setup exes)
    $curVer = $null
    try {
        $csprojText = Get-Content (Join-Path $root "IMVUCompanion.csproj") -Raw
        $m = [regex]::Match($csprojText, '<Version>([^<]+)</Version>')
        if ($m.Success) { $curVer = $m.Groups[1].Value }
    } catch { }
    if ($curVer) {
        $keepName = "IMVUCompanion-Setup-v$curVer.exe"
        Get-ChildItem (Join-Path $root "release") -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -like "IMVUCompanion-Setup-*.exe" -and
                $_.Name -ne $keepName
            } |
            ForEach-Object { Remove-IfExists $_.FullName }
    }
}

# MakeIcon tool build output (source only under tools\MakeIcon)
Remove-IfExists (Join-Path $root "tools\MakeIcon\bin")
Remove-IfExists (Join-Path $root "tools\MakeIcon\obj")

# One-off reverse-engineering dumps (if recreated locally)
Remove-IfExists (Join-Path $root "tools\imvu-js")

# Stale messages/commands next to the OLD relative-path locations (config is AppData now)
$devDir = Join-Path $root "bin\Release\net8.0-windows10.0.19041.0"
foreach ($f in @("messages.json", "commands.json", "ai_settings.json", "ui_layout.json")) {
    # Leave them if present — harmless leftovers; optional delete to avoid confusion
    $p = Join-Path $devDir $f
    if (Test-Path $p) {
        try {
            Remove-Item $p -Force -ErrorAction Stop
            $script:removed += $p
            Write-Host "  removed leftover config next to exe (real config is %LOCALAPPDATA%\IMVUCompanion): $f"
        } catch {
            Write-Warning "Could not remove $p : $_"
        }
    }
}

Write-Host ""
if ($removed.Count -eq 0) {
    Write-Host "Nothing stale to remove."
} else {
    Write-Host "Removed $($removed.Count) item(s)."
}

Write-Host ""
Write-Host "YOUR ONLY LOCAL TEST EXE:" -ForegroundColor Green
Write-Host "  $root\bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe"
Write-Host "Rebuild/run:  .\scripts\Run-Dev.ps1"
Write-Host "User config:  %LOCALAPPDATA%\IMVUCompanion\  (messages.json, commands.json)"
Write-Host "Installers (optional):  $root\release\"
if (Test-Path (Join-Path $root "release")) {
    Get-ChildItem (Join-Path $root "release") -Filter "*.exe" -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
}
