# LOCAL DEVELOPMENT - run this after every code change.
# Builds Release and starts the ONE dev exe (not Debug, not publish).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

& (Join-Path $PSScriptRoot "Clean-Stale.ps1")

Stop-Process -Name IMVUCompanion -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host ""
Write-Host "=== IMVU Companion - local dev build (Release) ===" -ForegroundColor Cyan
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $root "bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe"
if (-not (Test-Path $exe)) { throw "Dev exe not found: $exe" }

$info = Get-Item $exe
Write-Host ""
Write-Host "LOCAL DEV EXE (use only this for testing):" -ForegroundColor Green
Write-Host "  $($info.FullName)"
Write-Host "  Built: $($info.LastWriteTime)"
Write-Host ""
Write-Host 'For installers / GitHub: .\scripts\Publish-Release.ps1 (only when releasing)' -ForegroundColor DarkGray
Write-Host ""

Start-Process $exe