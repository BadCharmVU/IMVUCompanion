function Get-GhExe {
    @(
        "C:\Program Files\GitHub CLI\gh.exe",
        "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Get-GitExe {
    @(
        "C:\Program Files\Git\bin\git.exe",
        "C:\Program Files (x86)\Git\bin\git.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Get-ProjectVersion {
    param([string]$ProjectRoot)
    $csproj = Get-Content (Join-Path $ProjectRoot "IMVUCompanion.csproj") -Raw
    $m = [regex]::Match($csproj, '<Version>([^<]+)</Version>')
    if (-not $m.Success) { throw "Could not read <Version> from IMVUCompanion.csproj" }
    return $m.Groups[1].Value.Trim()
}

function Set-ProjectVersion {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$Version
    )
    $csprojPath = Join-Path $ProjectRoot "IMVUCompanion.csproj"
    $csproj = Get-Content $csprojPath -Raw
    $four = if ($Version -match '^\d+\.\d+\.\d+$') { "$Version.0" } else { $Version }
    $csproj = [regex]::Replace($csproj, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
    $csproj = [regex]::Replace($csproj, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$four</AssemblyVersion>")
    $csproj = [regex]::Replace($csproj, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$four</FileVersion>")
    [System.IO.File]::WriteAllText($csprojPath, $csproj)
    Write-Host "==> csproj version set to $Version"
}

# If csproj still matches the latest git tag, bump the last number so a ship cannot reuse it.
function Ensure-ReleaseVersion {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [string]$GitExe
    )
    $current = Get-ProjectVersion -ProjectRoot $ProjectRoot
    $lastTag = $null
    if ($GitExe) {
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $lastTag = (& $GitExe -C $ProjectRoot describe --tags --abbrev=0 2>$null)
        $ErrorActionPreference = $prevEap
        if ($lastTag) { $lastTag = "$lastTag".Trim().TrimStart('v', 'V') }
    }
    if (-not $lastTag -or $current -ne $lastTag) {
        Write-Host "==> Release version: $current"
        return $current
    }

    $parts = $current.Split('.')
    if ($parts.Length -lt 2) { throw "Cannot bump version '$current'" }
    $last = $parts.Length - 1
    $n = 0
    [int]::TryParse($parts[$last], [ref]$n) | Out-Null
    $parts[$last] = [string]($n + 1)
    $next = $parts -join '.'
    Set-ProjectVersion -ProjectRoot $ProjectRoot -Version $next
    Write-Host "==> csproj was still $current (last tag). Bumped to $next"
    return $next
}

function Find-ReleaseInstaller {
    param(
        [string]$ProjectRoot,
        [string]$Version
    )
    $name = "IMVUCompanion-Setup-v$Version.exe"
    @(
        (Join-Path $ProjectRoot "release\$name"),
        (Join-Path $ProjectRoot "release-build\$name")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Get-FileSha256Lower {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "File not found for hash: $Path" }
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    return $hash.ToLowerInvariant()
}

# Writes version.json with required sha256 of the Setup exe. Hard requirement for the update channel.
function Write-VersionManifest {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$InstallerPath,
        [string]$ReleaseNotes = ""
    )
    if (-not (Test-Path -LiteralPath $InstallerPath)) {
        throw "Installer missing: $InstallerPath"
    }
    $sha = Get-FileSha256Lower -Path $InstallerPath
    if ($sha.Length -ne 64) { throw "Invalid SHA-256 for installer: $sha" }

    $downloadUrl = "https://github.com/BadCharmVU/IMVUCompanion/releases/download/v$Version/IMVUCompanion-Setup-v$Version.exe"
    if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
        $ReleaseNotes = "v$Version"
    }

    $manifestPath = Join-Path $ProjectRoot "version.json"
    $obj = [ordered]@{
        version      = $Version
        downloadUrl  = $downloadUrl
        sha256       = $sha
        releaseNotes = $ReleaseNotes
    }
    $json = $obj | ConvertTo-Json -Depth 5
    # UTF-8 without BOM so gist clients parse cleanly
    [System.IO.File]::WriteAllText($manifestPath, $json + "`n")
    Write-Host "==> version.json written (sha256=$sha)"
    return $manifestPath
}