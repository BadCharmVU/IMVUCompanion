# LOCAL DEVELOPMENT - run this after every code change.
# Builds Release into THE ONLY test location and starts it.
# Path: bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$devDir = Join-Path $root "bin\Release\net8.0-windows10.0.19041.0"
$exe = Join-Path $devDir "IMVUCompanion.exe"

& (Join-Path $PSScriptRoot "Clean-Stale.ps1")

Stop-Process -Name IMVUCompanion -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Write-Host ""
Write-Host "=== IMVU Companion - DEV build (single location) ===" -ForegroundColor Cyan
Write-Host "Output: $devDir"
Write-Host ""

# Framework build only — never -r win-x64 here (that creates bin\...\win-x64 or publish\)
dotnet build "$root\IMVUCompanion.csproj" -c Release --no-incremental
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Test-Path $exe)) { throw "Dev exe not found after build: $exe" }

$info = Get-Item $exe
$ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
Write-Host ""
Write-Host "USE ONLY THIS EXE:" -ForegroundColor Green
Write-Host "  $($info.FullName)"
Write-Host "  Built:    $($info.LastWriteTime)"
Write-Host "  FileVer:  $($ver.FileVersion)"
Write-Host "  Product:  $($ver.ProductVersion)"
Write-Host ""
Write-Host "Config (survives rebuild): %LOCALAPPDATA%\IMVUCompanion\" -ForegroundColor DarkGray
Write-Host "Ship installer:            .\scripts\Ship-Release.ps1" -ForegroundColor DarkGray
Write-Host ""

Start-Process -FilePath $exe -WorkingDirectory $devDir
