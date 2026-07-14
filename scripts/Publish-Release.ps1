param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent),
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-Location $ProjectRoot

Write-Host "==> Building icon.ico from icon.png"
& (Join-Path $PSScriptRoot "Build-Icon.ps1") -ProjectRoot $ProjectRoot

Write-Host "==> Publishing self-contained win-x64 app"
$publishDir = Join-Path $ProjectRoot "publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish "$ProjectRoot\IMVUCompanion.csproj" -c Release -r win-x64 --self-contained true `
    /p:PublishReadyToRun=true `
    /p:DebugType=None `
    /p:DebugSymbols=false

$built = Join-Path $ProjectRoot "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
if (-not (Test-Path $built)) { throw "Publish output not found: $built" }

Copy-Item $built $publishDir -Recurse -Force
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
& $iscc (Join-Path $ProjectRoot "installer\IMVUCompanion.iss")
$setup = Join-Path $releaseDir "IMVUCompanion-Setup-v0.7.0.exe"
if (Test-Path $setup) {
    Write-Host "==> Installer ready: $setup"
} else {
    throw "Installer build failed"
}