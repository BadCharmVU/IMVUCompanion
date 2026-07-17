param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-Location $ProjectRoot

$csproj = Get-Content (Join-Path $ProjectRoot "IMVUCompanion.csproj") -Raw
$appVersion = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $appVersion) { throw "Could not read <Version> from IMVUCompanion.csproj" }
$setupName = "IMVUCompanion-Setup-v$appVersion.exe"
Write-Host "==> Release version: v$appVersion"

Write-Host "==> Building icon.ico from icon.png"
& (Join-Path $PSScriptRoot "Build-Icon.ps1") -ProjectRoot $ProjectRoot

Write-Host "==> Publishing self-contained win-x64 app"
$publishDir = Join-Path $ProjectRoot "publish"
if (Test-Path $publishDir) {
    try {
        Remove-Item $publishDir -Recurse -Force
    } catch {
        $publishDir = Join-Path $ProjectRoot ("publish.{0:yyyyMMddHHmmss}" -f (Get-Date))
        Write-Host "Publish folder locked - using $publishDir"
    }
}

dotnet publish "$ProjectRoot\IMVUCompanion.csproj" -c Release -r win-x64 --self-contained true `
    -o $publishDir `
    /p:PublishReadyToRun=true `
    /p:DebugType=None `
    /p:DebugSymbols=false

if (-not (Test-Path (Join-Path $publishDir "IMVUCompanion.exe"))) {
    throw "Publish output not found: $publishDir"
}
Write-Host "==> Published to $publishDir"

if ($SkipInstaller) { return }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Inno Setup not found. Install: winget install JRSoftware.InnoSetup"
    Write-Host "Then run: `"${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe`" `"$ProjectRoot\installer\IMVUCompanion.iss`""
    return
}

$releaseDir = Join-Path $ProjectRoot "release"
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$publishRel = (Resolve-Path $publishDir).Path
$publishRel = $publishRel.Substring($ProjectRoot.Length).TrimStart('\')
$publishDefine = "..\$publishRel"
$setup = Join-Path $releaseDir $setupName
if (Test-Path $setup) {
    try {
        Remove-Item $setup -Force -ErrorAction Stop
    } catch {
        $stale = Join-Path $releaseDir ("{0}.locked.{1:yyyyMMddHHmmss}" -f $setupName, (Get-Date))
        try {
            Rename-Item $setup $stale -Force -ErrorAction Stop
            Write-Host "Installer locked - moved aside to $stale"
        } catch {
            Write-Warning "Could not remove old installer (locked). ISCC may overwrite or fail."
        }
    }
}
$iss = Join-Path $ProjectRoot "installer\IMVUCompanion.iss"
& $iscc "/O$releaseDir" "/DPublishDir=$publishDefine" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
$setup = Join-Path $releaseDir $setupName
if (Test-Path $setup) {
    Write-Host "==> Installer ready: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
} else {
    throw "Installer build failed - missing $setup"
}