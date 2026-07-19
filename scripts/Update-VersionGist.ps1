# Sync version.json to the public update gist (run after writing version.json with sha256)
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

$json = Get-Content $manifest -Raw | ConvertFrom-Json
if (-not $json.version) { throw "version.json missing version" }
if (-not $json.downloadUrl) { throw "version.json missing downloadUrl" }
if (-not "$($json.downloadUrl)".StartsWith("https://")) {
    throw "version.json downloadUrl must start with https://"
}
$sha = "$($json.sha256)".Trim().ToLowerInvariant()
if ($sha.Length -ne 64 -or $sha -notmatch '^[0-9a-f]{64}$') {
    throw "version.json missing or invalid sha256 (required 64 lowercase hex). Refuse to publish update channel."
}

Write-Host "==> Updating public gist $gistId (v$($json.version) sha256=$sha)"
& $gh gist edit $gistId --filename version.json $manifest
if ($LASTEXITCODE -ne 0) { throw "gh gist edit failed" }
Write-Host "==> Public URL: https://gist.githubusercontent.com/BadCharmVU/$gistId/raw/version.json"
