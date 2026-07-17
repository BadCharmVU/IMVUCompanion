# Back-compat wrapper — use Ship-Release.ps1 or Finish-Release.ps1 for new releases.
& (Join-Path $PSScriptRoot "Finish-Release.ps1") -Version "0.8.0"
exit $LASTEXITCODE