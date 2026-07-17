$ErrorActionPreference = "Stop"
$root = "C:\Users\serve\ansel\IMVUCompanion"
Set-Location $root
. "$PSScriptRoot\GitHub-Common.ps1"

$git = Get-GitExe
$gh = Get-GhExe
if (-not $git) { throw "git not found" }
if (-not $gh) { throw "gh not found" }

$version = "0.9.0"
$tag = "v$version"
$installer = Find-ReleaseInstaller -ProjectRoot $root -Version $version
if (-not $installer) { throw "Installer missing" }
Write-Host "Installer: $installer size=$((Get-Item $installer).Length)"

# Ensure path in PATH for this session
$env:Path = "C:\Program Files\Git\bin;C:\Program Files\GitHub CLI;" + $env:Path

Write-Host "==> git status"
& $git status --short

Write-Host "==> git add/commit"
& $git add -A
$st = & $git status --porcelain
if ($st) {
    & $git commit -m "Release v0.9.0: proactive whisper, room-aware bot lifecycle, UI layout polish"
    if ($LASTEXITCODE -ne 0) { throw "commit failed" }
} else {
    Write-Host "No changes to commit"
}

Write-Host "==> git push"
& $git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw "push failed" }

Write-Host "==> tag $tag"
& $git rev-parse $tag 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    & $git tag $tag
} else {
    Write-Host "Tag exists locally"
}
& $git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "push tag failed" }

$notes = @"
v0.9.0

- Proactive join whispers via silent IMVU chat API path (handleWhisperAttempt + whisper bar send)
- Room-aware bot: start only with active room UI; pause on leave without reset; resume on re-enter
- Graceful Exit/X: stop bot, leave room (navigate home), then quit
- UI: remember window size/splitters/monitor; center on last monitor; thin scrollbars; Activity Log label
- Whisper/Trigger log colors swapped

Download IMVUCompanion-Setup-v0.9.0.exe below (ignore auto-generated Source code zips).
"@

Write-Host "==> GitHub release"
& $gh release view $tag --repo BadCharmVU/IMVUCompanion 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    & $gh release upload $tag $installer --repo BadCharmVU/IMVUCompanion --clobber
} else {
    & $gh release create $tag --repo BadCharmVU/IMVUCompanion --title $tag --notes $notes $installer
}
if ($LASTEXITCODE -ne 0) { throw "release failed" }

Write-Host "==> gist"
& "$PSScriptRoot\Update-VersionGist.ps1"
if ($LASTEXITCODE -ne 0) { throw "gist failed" }

Write-Host "==> Done https://github.com/BadCharmVU/IMVUCompanion/releases/tag/$tag"
