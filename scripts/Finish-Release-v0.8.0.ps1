param([string]$Token)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$gh = @(
    "C:\Program Files\GitHub CLI\gh.exe",
    "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $gh) { throw "gh.exe not found. Install GitHub CLI." }

function Test-GhLoggedIn {
    $null = & $gh auth status 2>&1
    return $LASTEXITCODE -eq 0
}

if (-not (Test-GhLoggedIn)) {
    if ([string]::IsNullOrWhiteSpace($Token)) {
        Write-Host "GitHub token required. Scopes: repo, gist"
        Write-Host "Create at: https://github.com/settings/tokens/new"
        $secure = Read-Host "Paste token" -AsSecureString
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try { $Token = ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)).Trim() }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
    }
    if ([string]::IsNullOrWhiteSpace($Token)) { throw "No token provided." }
    $env:GH_TOKEN = $Token.Trim()
}

$releaseNotes = "v0.8.0`n`n" +
    "- Cleaner activity log (JOIN, Sent, Skipped, Whisper, Trigger, Bot Started/Stopped)`n" +
    "- uid-based join memory per session; fixed joiner name bleed`n" +
    "- Session stats bar, Session Stats button, and !stats command`n" +
    "- Paused session timer when bot is stopped`n" +
    "- Dev workflow: Run-Dev.ps1, Clean-Stale.ps1, DEV-RUN.txt`n`n" +
    "Download IMVUCompanion-Setup-v0.8.0.exe below (ignore auto-generated Source code zips)."

try {
    $installer = @(
        (Join-Path $root "release\IMVUCompanion-Setup-v0.8.0.exe"),
        (Join-Path $root "release-build\IMVUCompanion-Setup-v0.8.0.exe"),
        (Join-Path $root "release\IMVUCompanion-Setup-v0.8.0-final.exe")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $installer) { throw "Installer not found. Run Publish-Release.ps1 first." }

    Write-Host "==> Installer: $installer"

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $gh release view v0.8.0 --repo BadCharmVU/IMVUCompanion 2>$null | Out-Null
    $releaseExists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $prevEap

    if ($releaseExists) {
        Write-Host "==> Release exists, uploading asset"
        & $gh release upload v0.8.0 $installer --repo BadCharmVU/IMVUCompanion --clobber
    } else {
        Write-Host "==> Creating release v0.8.0"
        & $gh release create v0.8.0 --repo BadCharmVU/IMVUCompanion --title "v0.8.0" --notes $releaseNotes $installer
    }
    if ($LASTEXITCODE -ne 0) { throw "GitHub release failed." }

    Write-Host "==> Updating gist"
    & (Join-Path $PSScriptRoot "Update-VersionGist.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Gist update failed." }

    Write-Host "==> Done: https://github.com/BadCharmVU/IMVUCompanion/releases/tag/v0.8.0"
}
finally {
    Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue
}