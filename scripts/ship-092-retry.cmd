@echo off
cd /d C:\Users\serve\ansel\IMVUCompanion
if exist "release\IMVUCompanion-Setup-v0.9.2.exe" (
  ren "release\IMVUCompanion-Setup-v0.9.2.exe" "IMVUCompanion-Setup-v0.9.2.exe.locked.%RANDOM%" 2>nul
  move /Y "release\IMVUCompanion-Setup-v0.9.2.exe" "release\IMVUCompanion-Setup-v0.9.2.exe.locked.%RANDOM%" 2>nul
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Process | Where-Object { $_.Path -like '*IMVUCompanion*' } | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Ship-Release.ps1" -CommitMessage "Release v0.9.2 LocalAppData config and update UI patch versions"
exit /b %ERRORLEVEL%
