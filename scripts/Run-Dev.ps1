# Always build and run the latest dev copy from bin\Release
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Stop-Process -Name IMVUCompanion -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

dotnet build -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $root "bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe"
Write-Host "Starting: $exe"
Start-Process $exe