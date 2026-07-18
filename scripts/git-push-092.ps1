$ErrorActionPreference = "Stop"
Set-Location "C:\Users\serve\ansel\IMVUCompanion"
. ".\scripts\GitHub-Common.ps1"
$git = Get-GitExe
if (-not $git) { throw "git not found" }

$status = & $git status --porcelain
if ($status) {
    Write-Host "==> git commit"
    & $git add -A
    & $git commit -m "Release v0.9.2 LocalAppData config and update UI patch versions"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed" }
} else {
    Write-Host "==> No local changes to commit"
}

Write-Host "==> git push"
& $git push origin HEAD
if ($LASTEXITCODE -ne 0) { throw "git push failed" }

& $git rev-parse "v0.9.2" 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "==> git tag v0.9.2"
    & $git tag "v0.9.2"
    if ($LASTEXITCODE -ne 0) { throw "git tag failed" }
} else {
    Write-Host "==> Tag v0.9.2 already exists"
}

Write-Host "==> git push tag"
& $git push origin "v0.9.2"
if ($LASTEXITCODE -ne 0) { throw "git push tag failed" }
