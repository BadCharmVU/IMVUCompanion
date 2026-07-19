$ErrorActionPreference = "Continue"
$root = "C:\Users\serve\ansel\IMVUCompanion"
Set-Location $root

# Remove accidental junk from broken batch echo lines
$junk = @("Clean", "Copy", "Ensure", "ISCC", "Killing", "git", "scripts\_ship094.cmd", "ship-log.txt")
foreach ($j in $junk) {
    $p = Join-Path $root $j
    if (Test-Path $p) {
        Remove-Item $p -Force -ErrorAction SilentlyContinue
        Write-Host "removed $j"
    }
}

git add -A
$status = git status --porcelain
if ($status) {
    git commit -m "chore: remove accidental ship junk files"
    git push origin HEAD
}

# Tag
git rev-parse v0.9.4 2>$null
if ($LASTEXITCODE -ne 0) {
    git tag v0.9.4
    Write-Host "created tag v0.9.4"
} else {
    Write-Host "tag v0.9.4 exists"
}
git push origin v0.9.4

# Release notes (functionality only)
$notes = @"
## IMVU Companion v0.9.4

### What's new
- **Bot Settings:** add, edit, and organize !commands by category, with search and paging
- New commands and categories are **saved and kept after restart** (same idea as welcome messages)
- The same !command can have a **different reply for each language**
- Welcome messages and bot replies stay **language-aware**
- Improved Add Command flow (categories and validation before save)

### In development (not connected yet)
- **AI Settings** and **AI Providers** are not wired up yet — still in development; only part of the UI is present for now.

### Install
Download **IMVUCompanion-Setup-v0.9.4.exe** below. Your custom messages and commands stay in `%LOCALAPPDATA%\IMVUCompanion` and are not overwritten by Setup.
"@

$installer = Join-Path $root "release\IMVUCompanion-Setup-v0.9.4.exe"
if (-not (Test-Path $installer)) {
    throw "Missing installer: $installer"
}

gh release view v0.9.4 --repo BadCharmVU/IMVUCompanion 2>$null
if ($LASTEXITCODE -eq 0) {
    gh release upload v0.9.4 $installer --repo BadCharmVU/IMVUCompanion --clobber
} else {
    gh release create v0.9.4 --repo BadCharmVU/IMVUCompanion --title "v0.9.4" --notes $notes $installer
}

# Gist
& (Join-Path $root "scripts\Update-VersionGist.ps1")

# Cleanup
& (Join-Path $root "scripts\Clean-Stale.ps1")
if (Test-Path (Join-Path $root "release-build")) {
    Remove-Item (Join-Path $root "release-build") -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem $root -Directory | Where-Object { $_.Name -match '^publish' } | ForEach-Object {
    Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "removed $($_.Name)"
}

dotnet build (Join-Path $root "IMVUCompanion.csproj") -c Release --nologo

Write-Host "DONE v0.9.4"
gh release view v0.9.4 --repo BadCharmVU/IMVUCompanion
