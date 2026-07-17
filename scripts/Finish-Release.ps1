param(
    [string]$Version,
    [string]$ReleaseNotes,
    [switch]$SkipPreflight
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

. (Join-Path $PSScriptRoot "GitHub-Common.ps1")

$gh = Get-GhExe
if (-not $gh) { throw "gh.exe not found. Install GitHub CLI." }

if (-not $SkipPreflight) {
    & (Join-Path $PSScriptRoot "Preflight-GitHub.ps1")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion -ProjectRoot $root
}

$tag = "v$Version"
$manifest = Join-Path $root "version.json"
if (Test-Path $manifest) {
    $json = Get-Content $manifest -Raw | ConvertFrom-Json
    if ($json.version -and "$($json.version)" -ne $Version) {
        Write-Warning "version.json has $($json.version) but csproj has $Version - align before shipping."
    }
    if ([string]::IsNullOrWhiteSpace($ReleaseNotes) -and $json.releaseNotes) {
        $ReleaseNotes = "$($json.releaseNotes)".Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "$tag`n`nSee version.json for release notes."
}

try {
    $installer = Find-ReleaseInstaller -ProjectRoot $root -Version $Version
    if (-not $installer) { throw "Installer not found for v$Version. Run Publish-Release.ps1 first." }

    Write-Host "==> Installer: $installer"

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $gh release view $tag --repo BadCharmVU/IMVUCompanion 2>$null | Out-Null
    $releaseExists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $prevEap

    if ($releaseExists) {
        Write-Host "==> Release exists, uploading asset"
        & $gh release upload $tag $installer --repo BadCharmVU/IMVUCompanion --clobber
    } else {
        Write-Host "==> Creating release $tag"
        & $gh release create $tag --repo BadCharmVU/IMVUCompanion --title $tag --notes $ReleaseNotes $installer
    }
    if ($LASTEXITCODE -ne 0) { throw "GitHub release failed." }

    Write-Host "==> Updating gist"
    & (Join-Path $PSScriptRoot "Update-VersionGist.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Gist update failed." }

    Write-Host "==> Done: https://github.com/BadCharmVU/IMVUCompanion/releases/tag/$tag"
}
finally {
    Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue
}