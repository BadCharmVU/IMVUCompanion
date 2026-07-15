# Run once after: gh auth login
# Creates GitHub Release v0.8.0 and updates the public update gist.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$installer = Join-Path $root "release-build\IMVUCompanion-Setup-v0.8.0.exe"
if (-not (Test-Path $installer)) {
    $installer = Join-Path $root "release\IMVUCompanion-Setup-v0.8.0-final.exe"
}
if (-not (Test-Path $installer)) {
    throw "Installer not found. Run .\scripts\Publish-Release.ps1 first."
}

Write-Host "==> Creating GitHub release v0.8.0"
gh release create v0.8.0 --repo BadCharmVU/IMVUCompanion --title "v0.8.0" --notes @"
v0.8.0

- Cleaner activity log (JOIN, Sent, Skipped, Whisper, Trigger, Bot Started/Stopped)
- uid-based join memory per session; fixed joiner name bleed
- Session stats bar, Session Stats button, and !stats command
- Paused session timer when bot is stopped
- Dev workflow: Run-Dev.ps1, Clean-Stale.ps1, DEV-RUN.txt

Download **IMVUCompanion-Setup-v0.8.0.exe** below (ignore auto-generated Source code zips).
"@ "$installer"

Write-Host "==> Updating public version gist"
& (Join-Path $PSScriptRoot "Update-VersionGist.ps1")

Write-Host "==> Done. Testers: https://github.com/BadCharmVU/IMVUCompanion/releases/tag/v0.8.0"