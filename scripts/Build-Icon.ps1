param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent)
)

$pngPath = Join-Path $ProjectRoot "icon.png"
$icoPath = Join-Path $ProjectRoot "icon.ico"
$makeIcon = Join-Path $ProjectRoot "tools\MakeIcon\MakeIcon.csproj"

if (-not (Test-Path $pngPath)) {
    Write-Error "Missing icon.png at $pngPath"
    exit 1
}

dotnet run --project $makeIcon -- $pngPath $icoPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }