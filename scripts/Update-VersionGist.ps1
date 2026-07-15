# Sync version.json to the public update gist (run after editing version.json on release)
param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"
$gistId = "d510193765f2062f315d65de91bbceec"
$manifest = Join-Path $ProjectRoot "version.json"
if (-not (Test-Path $manifest)) { throw "Missing $manifest" }

Write-Host "==> Updating public gist $gistId"
gh gist edit $gistId -f "version.json=$manifest"
Write-Host "==> Public URL: https://gist.githubusercontent.com/BadCharmVU/$gistId/raw/version.json"