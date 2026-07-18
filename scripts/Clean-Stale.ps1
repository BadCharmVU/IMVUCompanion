# Removes stale build clutter. Safe anytime.
# KEEPS:
#   bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe  (local dev — Run-Dev.ps1)
#   release\IMVUCompanion-Setup-v*.exe                         (shipped installers only)
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

# Debug builds — never used for local testing
Remove-IfExists (Join-Path $root "bin\Debug")

# Publish is release-only; not the day-to-day test exe
Remove-IfExists (Join-Path $root "bin\Release\net8.0-windows10.0.19041.0\win-x64")

# All publish outputs (plain + timestamped)
Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq "publish" -or $_.Name -match '^publish\.' } |
    ForEach-Object { Remove-IfExists $_.FullName }

# Duplicate release output from locked-file workarounds
Remove-IfExists (Join-Path $root "release-build")
Remove-IfExists (Join-Path $root "release-v071")
Remove-IfExists (Join-Path $root "release2")

# Stray logs / temp build noise at repo root
foreach ($f in @(
    "publish-log.txt", "build-log.txt", "iscc-log.txt",
    "build-err.txt", "build-out.txt"
)) {
    Remove-IfExists (Join-Path $root $f)
}

# Incomplete / locked installer stubs (keep real full-size setups)
Get-ChildItem (Join-Path $root "release") -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -like "IMVUCompanion-Setup-*.exe" -and
        ($_.Length -lt 20MB -or $_.Name -match '\.old|\.locked|\.stale')
    } |
    ForEach-Object { Remove-IfExists $_.FullName }

Write-Host ""
if ($removed.Count -eq 0) {
    Write-Host "Nothing stale to remove."
} else {
    Write-Host "Removed $($removed.Count) item(s)."
}

Write-Host ""
Write-Host "KEEP - local daily test (scripts\Run-Dev.ps1):"
Write-Host "  $root\bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe"
Write-Host "KEEP - installers for testers:"
Write-Host "  $root\release\"
if (Test-Path (Join-Path $root "release")) {
    Get-ChildItem (Join-Path $root "release") -Filter "*.exe" -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
}
