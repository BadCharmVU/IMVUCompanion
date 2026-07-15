# Removes stale build folders that are NOT used for local dev.
# Safe to run anytime. Keeps: bin\Release\...\IMVUCompanion.exe, release\, publish\ (current).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$removed = @()

function Remove-IfExists([string]$path) {
    if (-not (Test-Path $path)) { return }
    try {
        Remove-Item $path -Recurse -Force
        $script:removed += $path
    } catch {
        Write-Warning "Could not remove $path : $_"
    }
}

# Debug builds — never used for local testing
Remove-IfExists (Join-Path $root "bin\Debug")

# Old dotnet publish outputs (not the active dev exe)
Remove-IfExists (Join-Path $root "bin\Release\net8.0-windows10.0.19041.0\win-x64")
# Timestamped publish folders only (e.g. publish.20260714194533) - keep plain publish\
Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^publish\.\d' } |
    ForEach-Object { Remove-IfExists $_.FullName }

# Duplicate old release folder
Remove-IfExists (Join-Path $root "release-v071")
Remove-IfExists (Join-Path $root "release2")

# Stray logs
foreach ($f in @("publish-log.txt", "build-log.txt", "iscc-log.txt")) {
    $p = Join-Path $root $f
    if (Test-Path $p) {
        Remove-Item $p -Force
        $removed += $p
    }
}

if ($removed.Count -eq 0) {
    Write-Host "Nothing stale to remove."
} else {
    Write-Host "Removed:"
    $removed | ForEach-Object { Write-Host "  $_" }
}