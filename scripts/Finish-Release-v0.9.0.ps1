# Back-compat wrapper — prefer Ship-Release.ps1 for full one-shot.
& (Join-Path $PSScriptRoot "Finish-Release.ps1") -Version "0.9.0"
exit $LASTEXITCODE
