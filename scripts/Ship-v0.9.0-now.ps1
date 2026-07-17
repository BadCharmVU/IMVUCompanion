$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
& "$PSScriptRoot\Ship-Release.ps1" -CommitMessage "Release v0.9.0: proactive whisper, room-aware bot lifecycle, UI layout polish"
exit $LASTEXITCODE
