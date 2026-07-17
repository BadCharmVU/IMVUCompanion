# Run BEFORE git push / release / gist — confirms GitHub is connected in one shot.
param(
    [string]$Repo = "BadCharmVU/IMVUCompanion",
    [string[]]$RequiredScopes = @("repo", "gist")
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GitHub-Common.ps1")

$gh = Get-GhExe
if (-not $gh) {
    throw "GitHub CLI (gh) not found. Install: winget install GitHub.cli"
}

function Test-GhAuth {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $out = & $gh auth status 2>&1 | Out-String
    $ok = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $prev
    return @{ Ok = $ok; Output = $out }
}

function Ensure-GhAuth {
    $status = Test-GhAuth
    if ($status.Ok) { return $status.Output }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host "  CONNECT TO GITHUB BEFORE CONTINUING" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Release needs GitHub in one uninterrupted run:"
    Write-Host "  - git push + tag"
    Write-Host "  - GitHub Release + installer upload"
    Write-Host "  - public gist (version.json)"
    Write-Host ""
    Write-Host "In a terminal, run:"
    Write-Host "  gh auth login" -ForegroundColor Cyan
    Write-Host "    -> GitHub.com, HTTPS, login via browser"
    Write-Host "  gh auth setup-git" -ForegroundColor Cyan
    Write-Host "    -> so git push uses the same login"
    Write-Host ""
    Write-Host "Token scopes required: repo, gist"
    Write-Host "  https://github.com/settings/tokens/new"
    Write-Host ""

    $choice = Read-Host "Type 'login' to run gh auth login now, or press Enter after you connected"
    if ($choice -eq "login") {
        & $gh auth login
        if ($LASTEXITCODE -ne 0) { throw "gh auth login failed." }
        Write-Host ""
        Write-Host "Tip: also run  gh auth setup-git  so git push works."
        Read-Host "Press Enter when gh auth setup-git is done (or skip if git push already works)"
    }

    $status = Test-GhAuth
    if (-not $status.Ok) {
        throw "Still not logged in to GitHub. Run: gh auth login"
    }
    return $status.Output
}

function Test-GitRemote {
    param([string]$ProjectRoot)
    $git = Get-GitExe
    if (-not $git) {
        Write-Warning "git.exe not found - skipping git remote check."
        return
    }

    Push-Location $ProjectRoot
    try {
        $prev = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & $git remote get-url origin 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "No git remote 'origin'. Add: git remote add origin https://github.com/$Repo.git"
        }

        & $git ls-remote --heads origin 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "git cannot reach origin yet." -ForegroundColor Yellow
            Write-Host "Run: gh auth setup-git" -ForegroundColor Cyan
            Read-Host "Press Enter after git can push to origin"
            & $git ls-remote --heads origin 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "git still cannot access origin. Run: gh auth setup-git" }
        }
    }
    finally {
        Pop-Location
        $ErrorActionPreference = $prev
    }
}

Write-Host "==> GitHub preflight ($Repo)"

$authText = Ensure-GhAuth
Write-Host $authText.Trim()

foreach ($scope in $RequiredScopes) {
    if ($authText -notmatch "\b$scope\b") {
        Write-Warning "Token may be missing scope '$scope'. Re-login: gh auth login -s $scope"
    }
}

$login = & $gh api user --jq .login 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($login)) {
    throw "Could not read GitHub user. Run: gh auth login"
}
Write-Host "==> Logged in as: $login"

$repoInfo = & $gh api "repos/$Repo" --jq '{private: .private, visibility: .visibility, default_branch: .default_branch}' 2>$null
if ($LASTEXITCODE -ne 0) { throw "Cannot access repo $Repo. Check login and repo name." }
Write-Host "==> Repo: $Repo ($repoInfo)"

if ($repoInfo -match '"private"\s*:\s*true') {
    Write-Host ""
    Write-Host "NOTE: Repo is PRIVATE - release download URLs return 404 for users without access." -ForegroundColor Yellow
    Write-Host "      Update checks via gist still work; installer links need a public host or public releases repo."
    Write-Host ""
}

$root = Split-Path $PSScriptRoot -Parent
Test-GitRemote -ProjectRoot $root

Write-Host "==> GitHub preflight OK - safe to push, release, and update gist."