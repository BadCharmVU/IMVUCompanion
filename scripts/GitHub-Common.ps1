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