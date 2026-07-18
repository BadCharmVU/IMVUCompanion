@echo off
cd /d C:\Users\serve\ansel\IMVUCompanion
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Ship-Release.ps1" -CommitMessage "Release v0.9.2 LocalAppData config and update UI patch versions"
exit /b %ERRORLEVEL%
