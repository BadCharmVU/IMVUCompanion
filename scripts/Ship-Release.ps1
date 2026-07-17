# One-shot release: preflight GitHub -> build installer -> push/tag -> release + gist
param(
    [string]$CommitMessage,
    [switch]$SkipBuild,
    [switch]$SkipPush
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

. (Join-Path $PSScriptRoot "GitHub-Common.ps1")

Write-Host ""
Write-Host "Ship-Release: connect GitHub FIRST, then everything runs without stopping." -ForegroundColor Cyan
Write-Host ""

& (Join-Path $PSScriptRoot "Preflight-GitHub.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$version = Get-ProjectVersion -ProjectRoot $root
$tag = "v$version"

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "Publish-Release.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Publish-Release failed." }
}

$installer = Find-ReleaseInstaller -ProjectRoot $root -Version $version
if (-not $installer) { throw "Installer missing for v$version. Run Publish-Release.ps1." }
Write-Host "==> Installer ready: $installer"

if (-not $SkipPush) {
    $git = Get-GitExe
    if (-not $git) { throw "git.exe not found." }

    if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
        $CommitMessage = "Release $tag"
    }

    $status = & $git status --porcelain
    if ($status) {
        Write-Host "==> git commit"
        & $git add -A
        & $git commit -m $CommitMessage
        if ($LASTEXITCODE -ne 0) { throw "git commit failed." }
    } else {
        Write-Host "==> No local changes to commit"
    }

    Write-Host "==> git push"
    & $git push origin HEAD
    if ($LASTEXITCODE -ne 0) { throw "git push failed. Run: gh auth setup-git" }

    & $git rev-parse $tag 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "==> git tag $tag"
        & $git tag $tag
        if ($LASTEXITCODE -ne 0) { throw "git tag failed." }
    } else {
        Write-Host "==> Tag $tag already exists locally"
    }

    Write-Host "==> git push tag"
    & $git push origin $tag
    if ($LASTEXITCODE -ne 0) { throw "git push tag failed." }
}

& (Join-Path $PSScriptRoot "Finish-Release.ps1") -Version $version -SkipPreflight
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "==> Ship-Release complete: $tag"