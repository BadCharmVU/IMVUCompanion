# Sync version.json to the public update gist (run after editing version.json on release)
param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"
$gh = @(
    "C:\Program Files\GitHub CLI\gh.exe",
    "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $gh) { throw "gh.exe not found." }
$gistId = "d510193765f2062f315d65de91bbceec"
$manifest = Join-Path $ProjectRoot "version.json"
if (-not (Test-Path $manifest)) { throw "Missing $manifest" }

Write-Host "==> Updating public gist $gistId"
& $gh gist edit $gistId --filename version.json $manifest
Write-Host "==> Public URL: https://gist.githubusercontent.com/BadCharmVU/$gistId/raw/version.json"